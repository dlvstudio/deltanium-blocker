using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Deltanium.Blocker.Core;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace Deltanium.Blocker.Services.Ledger
{
    public enum BalanceVerdict { Ok, Insufficient, Unverifiable, Invalid }

    /// <summary>
    /// TOKENOMICS_V2 §11 phase B: a Blocker checks a user value move (Transfer / Donate) itself.
    /// 1. The user signed exactly this amount and recipient (their HTTP request travels inside the tx).
    /// 2. Their balance at the latest anchor before the move, proven by a Merkle path to the BalancesRoot
    ///    already committed in this Blocker's own chain, minus their other on-chain value moves since that anchor,
    ///    covers the amount.
    ///    If the snapshot alone does not cover it (e.g. a top-up after it), the anchored ledger rows after the snapshot
    ///    are replayed up to the move; until an anchor covers the move the tx is Unverifiable.
    /// Mode (LEDGER_BALANCE_CHECK / Ledger:BalanceCheck): off | warn (default, log only) | enforce (reject; hold
    /// Unverifiable txs in the deferred mempool until a later anchor decides them).
    /// </summary>
    public class BalanceCheckService
    {
        public static readonly HashSet<TransactionType> ValueTypes = new() { TransactionType.Transfer, TransactionType.Donate };

        private readonly ILedgerProofClient _client;
        private readonly NodeState _state;
        private readonly TransactionPool _pool;
        private readonly ILogger<BalanceCheckService>? _log;

        public string Mode { get; }

        public BalanceCheckService(ILedgerProofClient client, NodeState state, TransactionPool pool, string? mode = null,
            ILogger<BalanceCheckService>? log = null)
        {
            _client = client;
            _state = state;
            _pool = pool;
            _log = log;
            Mode = (mode ?? Environment.GetEnvironmentVariable("LEDGER_BALANCE_CHECK") ?? "warn").Trim().ToLowerInvariant();
        }

        public record ValueTxAuth(decimal Amount, long LedgerAtMs, string Method, string Path, string Timestamp, string Body);

        /// <summary>Parse and check the user-signed request carried by a value tx. Signature itself is checked by the caller.</summary>
        public static bool TryParseAuth(TransactionType type, string? to, string signPayload, JsonElement data, out ValueTxAuth? auth, out string error)
        {
            auth = null;
            error = "";
            if (data.ValueKind != JsonValueKind.Object) { error = "value tx requires data"; return false; }
            if (!data.TryGetProperty("amount", out var amtEl) || !TryDecimal(amtEl, out var amount) || amount <= 0)
            { error = "data.amount must be positive"; return false; }
            if (!data.TryGetProperty("ledgerAtMs", out var atEl) || !TryLong(atEl, out var ledgerAt) || ledgerAt <= 0)
            { error = "data.ledgerAtMs required"; return false; }
            if (!data.TryGetProperty("auth", out var a) || a.ValueKind != JsonValueKind.Object)
            { error = "data.auth (user-signed request) required"; return false; }
            var method = Str(a, "method");
            var path = Str(a, "path");
            var ts = Str(a, "timestamp");
            var body = Str(a, "body");
            var expectedPath = type == TransactionType.Transfer ? "/api/wallet/transfer" : "/api/tip";
            if (!string.Equals(method, "POST", StringComparison.Ordinal) || !path.StartsWith(expectedPath, StringComparison.Ordinal))
            { error = $"auth must be POST {expectedPath}"; return false; }
            if (!string.Equals(signPayload, SignedMessage(method, path, ts, body), StringComparison.Ordinal))
            { error = "signPayload does not match the carried request"; return false; }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var r = doc.RootElement;
                var bodyTo = r.TryGetProperty("toPublicKey", out var t) ? t.GetString() : null;
                if (!SameKey(bodyTo, to)) { error = "signed recipient differs from tx.to"; return false; }
                if (!r.TryGetProperty("amount", out var ba) || !TryDecimal(ba, out var bodyAmount) || bodyAmount != amount)
                { error = "signed amount differs from data.amount"; return false; }
            }
            catch (JsonException) { error = "auth.body is not JSON"; return false; }

            auth = new ValueTxAuth(amount, ledgerAt, method, path, ts, body);
            return true;
        }

        /// <summary>Same as Central's SignatureAuthHelper.BuildSignedMessage for POST.</summary>
        public static string SignedMessage(string method, string pathAndQuery, string timestamp, string body) =>
            $"{method}{pathAndQuery}{timestamp}{LedgerMerkle.Sha256Hex(body ?? "")}";

        /// <summary>How many anchor windows after the last pre-move snapshot a check may replay.</summary>
        public const int MaxReplayWindows = 50;

        public async Task<(BalanceVerdict Verdict, string Detail)> CheckAsync(string txId, string from, string signature, ValueTxAuth auth)
        {
            var onChain = ValueTxs().ToList();
            if (onChain.Any(v => v.Id != txId && v.Signature == signature && !string.IsNullOrEmpty(signature)))
                return (BalanceVerdict.Invalid, "user signature already used by another value tx (replay)");

            var anchored = _state.Chain.Where(b => b.LedgerAnchorSeq > 0 && !string.IsNullOrEmpty(b.BalancesRoot))
                .GroupBy(b => b.LedgerAnchorSeq).ToDictionary(g => g.Key, g => g.First());
            if (anchored.Count == 0) return (BalanceVerdict.Unverifiable, "no ledger anchor in chain yet");

            List<AnchorDto> anchors;
            try { anchors = await _client.GetAnchorsAsync(); }
            catch (Exception ex) { return (BalanceVerdict.Unverifiable, "Central anchors unavailable: " + ex.Message); }

            // Only anchors whose roots this Blocker committed in its own chain count.
            var inChain = anchors
                .Where(a => anchored.TryGetValue(a.Seq, out var b) &&
                            string.Equals(b.BalancesRoot, a.BalancesRoot, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(b.LedgerRoot, a.LedgerRoot, StringComparison.OrdinalIgnoreCase))
                .GroupBy(a => a.Seq).ToDictionary(g => g.Key, g => g.First());

            var anchor = inChain.Values.Where(a => a.UpToUnixMs < auth.LedgerAtMs).OrderByDescending(a => a.Seq).FirstOrDefault();

            // 1. Balance at the latest snapshot before the move (zero when the move predates every anchor).
            decimal balance = 0;
            if (anchor != null)
            {
                BalanceProofDto? proof;
                try { proof = await _client.GetBalanceProofAsync(anchor.Seq, from); }
                catch (Exception ex) { return (BalanceVerdict.Unverifiable, "balance proof unavailable: " + ex.Message); }
                if (proof == null) return (BalanceVerdict.Unverifiable, "balance proof unavailable");

                var root = anchored[anchor.Seq].BalancesRoot;
                if (proof.Included)
                {
                    if (!SameKey(proof.Account, from)) return (BalanceVerdict.Invalid, "proof is for another account");
                    if (proof.Leaf != LedgerMerkle.BalanceLeaf(proof.Account, proof.Balance))
                        return (BalanceVerdict.Invalid, "proof leaf does not encode the stated balance");
                    if (!string.Equals(LedgerMerkle.RootFromPath(proof.Leaf, proof.Path), root, StringComparison.OrdinalIgnoreCase))
                        return (BalanceVerdict.Invalid, "proof does not reach the BalancesRoot in this chain");
                    balance = proof.Balance;
                }

                var upTo = anchor.UpToUnixMs;
                var spentSince = onChain
                    .Where(v => v.Id != txId && SameKey(v.From, from) && v.LedgerAtMs >= upTo && v.LedgerAtMs <= auth.LedgerAtMs)
                    .Sum(v => v.Amount);
                if (balance - spentSince >= auth.Amount)
                    return (BalanceVerdict.Ok, $"anchor {anchor.Seq}: balance {balance}, spent since {spentSince}");
            }

            // 2. Not covered by the snapshot alone (e.g. topped up after it): replay the anchored ledger rows
            //    after that snapshot up to the move itself. Each window must hash to the LedgerRoot in this chain.
            return await ReplayToMoveAsync(txId, from, auth, anchor, balance, inChain, onChain);
        }

        private async Task<(BalanceVerdict, string)> ReplayToMoveAsync(string txId, string from, ValueTxAuth auth,
            AnchorDto? start, decimal startBalance, Dictionary<long, AnchorDto> inChain, List<OnChainValue> onChain)
        {
            var startSeq = start?.Seq ?? 0;
            var startMs = start?.UpToUnixMs ?? 0;
            var label = start == null ? "ledger start" : $"anchor {start.Seq}";
            var lastSeq = inChain.Keys.DefaultIfEmpty(0).Max();
            var balance = startBalance;
            var refIds = new HashSet<string>(StringComparer.Ordinal);

            for (var seq = startSeq + 1; seq <= lastSeq && seq <= startSeq + MaxReplayWindows; seq++)
            {
                if (!inChain.TryGetValue(seq, out var a))
                    return (BalanceVerdict.Unverifiable, $"anchor {seq} is not committed in this chain");
                List<LedgerEntryDto>? entries;
                try { entries = await _client.GetAnchorEntriesAsync(seq); }
                catch (Exception ex) { return (BalanceVerdict.Unverifiable, $"entries of anchor {seq} unavailable: {ex.Message}"); }
                if (entries == null) return (BalanceVerdict.Unverifiable, $"entries of anchor {seq} unavailable");
                if (!string.Equals(LedgerMerkle.Root(entries.Select(LedgerMerkle.LedgerLeaf)), a.LedgerRoot, StringComparison.OrdinalIgnoreCase))
                    return (BalanceVerdict.Unverifiable, $"entries of anchor {seq} do not hash to its LedgerRoot");

                foreach (var e in entries)
                {
                    if (e.RefId == txId && SameKey(e.FromPublicKey, from))
                    {
                        // Value moves Blocker saw on chain but Central's ledger left out still count as spent.
                        var missing = onChain
                            .Where(v => v.Id != txId && SameKey(v.From, from) && v.LedgerAtMs >= startMs &&
                                        v.LedgerAtMs <= auth.LedgerAtMs && !refIds.Contains(v.Id))
                            .Sum(v => v.Amount);
                        var available = balance - missing;
                        return available >= auth.Amount
                            ? (BalanceVerdict.Ok, $"{label} + anchored rows to anchor {seq}: balance {available} before the move")
                            : (BalanceVerdict.Insufficient, $"{label} + anchored rows to anchor {seq}: balance {available} < {auth.Amount}");
                    }
                    if (!string.IsNullOrEmpty(e.RefId)) refIds.Add(e.RefId);
                    if (SameKey(e.FromPublicKey, from)) balance -= e.Amount;
                    if (SameKey(e.ToPublicKey, from)) balance += e.Amount;
                }
            }

            return start == null
                ? (BalanceVerdict.Unverifiable, "no anchored snapshot before this move and the move is not anchored yet")
                : (BalanceVerdict.Unverifiable, $"{label}: balance {startBalance} short; waiting for an anchor covering the move");
        }

        /// <summary>Re-run the check for a tx held in the deferred mempool (its signature was checked on submit).</summary>
        public Task<(BalanceVerdict Verdict, string Detail)> RecheckAsync(Core.Transaction tx)
        {
            if (!tx.Data.TryGetValue("amount", out var a) || !TryDecimal(a, out var amount) ||
                !tx.Data.TryGetValue("ledgerAtMs", out var l) || !TryLong(l, out var at))
                return Task.FromResult((BalanceVerdict.Invalid, "held tx lost its amount / ledgerAtMs"));
            return CheckAsync(tx.Id, tx.From, tx.Signature, new ValueTxAuth(amount, at, "POST", "", "", ""));
        }

        /// <summary>In enforce mode an unverifiable value tx is held until an anchor lets it be checked.</summary>
        public bool Defers(BalanceVerdict verdict) => Mode == "enforce" && verdict == BalanceVerdict.Unverifiable;

        /// <summary>How long a held tx may wait (LEDGER_DEFER_MINUTES, default 30).</summary>
        public TimeSpan MaxHold { get; init; } = TimeSpan.FromMinutes(
            int.TryParse(Environment.GetEnvironmentVariable("LEDGER_DEFER_MINUTES"), out var m) && m > 0 ? m : 30);

        /// <summary>
        /// Re-check held value txs: Ok ones join the mempool for the next block, Insufficient / Invalid ones are
        /// dropped, unverifiable ones wait until <see cref="MaxHold"/>.
        /// </summary>
        public async Task<(int Promoted, int Dropped, int Waiting)> ProcessDeferredAsync(DateTime? now = null)
        {
            var t = now ?? DateTime.UtcNow;
            int promoted = 0, dropped = 0, waiting = 0;
            if (_pool.DeferredCount == 0) return (0, 0, 0);
            _client.ForgetAnchors();
            foreach (var (tx, since) in _pool.Deferred())
            {
                var (verdict, detail) = await RecheckAsync(tx);
                if (verdict == BalanceVerdict.Ok)
                {
                    _pool.PromoteDeferred(tx.Id);
                    promoted++;
                    _log?.LogInformation("Held value tx {TxId} verified: {Detail}", tx.Id, detail);
                }
                else if (verdict != BalanceVerdict.Unverifiable || t - since > MaxHold)
                {
                    _pool.DropDeferred(tx.Id);
                    dropped++;
                    _log?.LogWarning("Held value tx {TxId} dropped: {Verdict} {Detail}", tx.Id, verdict, detail);
                }
                else waiting++;
            }
            return (promoted, dropped, waiting);
        }

        /// <summary>Whether the verdict lets the tx in under the current mode.</summary>
        public bool Accepts(BalanceVerdict verdict) => Mode switch
        {
            "off" => true,
            "enforce" => verdict == BalanceVerdict.Ok,
            _ => true // warn
        };

        private record OnChainValue(string Id, string From, string Signature, decimal Amount, long LedgerAtMs);

        private IEnumerable<OnChainValue> ValueTxs()
        {
            var txs = _state.Chain.SelectMany(b => b.Transactions).Concat(_pool.Snapshot()).Concat(_pool.DeferredSnapshot());
            foreach (var tx in txs)
            {
                if (!ValueTypes.Contains(tx.Type)) continue;
                if (!tx.Data.TryGetValue("amount", out var a) || !TryDecimal(a, out var amount)) continue;
                long at = 0;
                if (tx.Data.TryGetValue("ledgerAtMs", out var l)) TryLong(l, out at);
                yield return new OnChainValue(tx.Id, tx.From, tx.Signature, amount, at);
            }
        }

        public static bool SameKey(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            try { return new PubKey(a.Trim()) == new PubKey(b.Trim()); }
            catch { return false; }
        }

        private static string Str(JsonElement o, string name) =>
            o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static bool TryDecimal(object? v, out decimal d)
        {
            d = 0;
            return v switch
            {
                JsonElement { ValueKind: JsonValueKind.Number } e => e.TryGetDecimal(out d),
                JsonElement { ValueKind: JsonValueKind.String } e => decimal.TryParse(e.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d),
                decimal m => (d = m) == m,
                string s => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d),
                double x => (d = (decimal)x) == d,
                long x => (d = x) == d,
                int x => (d = x) == d,
                _ => false
            };
        }

        private static bool TryLong(object? v, out long l)
        {
            l = 0;
            return v switch
            {
                JsonElement { ValueKind: JsonValueKind.Number } e => e.TryGetInt64(out l),
                JsonElement { ValueKind: JsonValueKind.String } e => long.TryParse(e.GetString(), out l),
                long x => (l = x) == x,
                int x => (l = x) == x,
                string s => long.TryParse(s, out l),
                _ => false
            };
        }
    }
}
