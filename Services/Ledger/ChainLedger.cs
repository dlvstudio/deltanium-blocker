using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deltanium.Blocker.Core;

namespace Deltanium.Blocker.Services.Ledger
{
    /// <summary>
    /// D balances computed from the chain alone (TOKENOMICS_V2 §11, plan phase 1 "shadow").
    ///
    /// Central publishes every wallet ledger row on chain in LedgerRows txs, one anchor window at a time (split in
    /// parts when large). This replays them in anchor order, like a Bitcoin node replays blocks, and checks itself:
    /// the rows of a window must hash to the LedgerRoot that the anchor committed in a block header, and the balances
    /// after them must hash to that anchor's BalancesRoot. Every check is recorded, so "the chain matches Central's
    /// ledger, account by account" can be read off <see cref="Checks"/>.
    /// </summary>
    public sealed class ChainLedger
    {
        public const string RowsType = nameof(TransactionType.LedgerRows);

        /// <summary>One ledger row exactly as Central hashes it (amount F10, time to the microsecond, UTC).</summary>
        public sealed record Row(
            [property: JsonPropertyName("id")] string Id,
            [property: JsonPropertyName("from")] string? From,
            [property: JsonPropertyName("to")] string? To,
            [property: JsonPropertyName("amount")] string Amount,
            [property: JsonPropertyName("kind")] string Kind,
            [property: JsonPropertyName("refId")] string? RefId,
            [property: JsonPropertyName("at")] string At);

        public sealed class Check
        {
            public long Seq { get; init; }
            public int Rows { get; init; }
            public long AppliedAtHeight { get; init; }
            /// <summary>null until the anchor's block header is in the chain.</summary>
            public bool? LedgerRootOk { get; set; }
            public bool? BalancesRootOk { get; set; }
            public string RootAfter { get; init; } = "";
        }

        private readonly Dictionary<string, decimal> _balances = new(StringComparer.Ordinal);
        private readonly Dictionary<long, (string LedgerRoot, string BalancesRoot)> _anchors = new();
        private readonly Dictionary<long, SortedDictionary<int, List<Row>>> _parts = new();
        private readonly Dictionary<long, int> _partCount = new();
        private readonly Dictionary<long, string> _windowLedgerRoot = new();
        private readonly List<Check> _checks = new();

        public long AppliedSeq { get; private set; }
        public long ProcessedHeight { get; private set; } = -1;
        public IReadOnlyDictionary<string, decimal> Balances => _balances;
        public IReadOnlyList<Check> Checks => _checks;
        /// <summary>Windows seen on chain but not applied yet (a part or an earlier window is missing).</summary>
        public IEnumerable<long> WaitingSeqs => _parts.Keys.Where(s => s > AppliedSeq).OrderBy(s => s);

        public string StateRoot => Root(_balances);

        public decimal BalanceOf(string account) => _balances.TryGetValue(account, out var b) ? b : 0m;

        public static string Root(IReadOnlyDictionary<string, decimal> balances) =>
            LedgerMerkle.Root(balances.Where(kv => kv.Value != 0m).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => LedgerMerkle.BalanceLeaf(kv.Key, kv.Value)));

        public static string RowLeaf(Row r) => LedgerMerkle.Sha256Hex(string.Join("|",
            r.Id, r.From ?? "", r.To ?? "", r.Amount, r.Kind, r.RefId ?? "", r.At));

        public static List<Row> ParseRows(string json) =>
            JsonSerializer.Deserialize<List<Row>>(json) ?? new List<Row>();

        /// <summary>Apply every block after <see cref="ProcessedHeight"/>.</summary>
        public void Sync(IReadOnlyList<Block> chain)
        {
            foreach (var b in chain.Where(b => b.Height > ProcessedHeight).OrderBy(b => b.Height)) Apply(b);
        }

        public void Apply(Block block)
        {
            if (block.Height <= ProcessedHeight) return;
            ProcessedHeight = block.Height;
            if (block.LedgerAnchorSeq > 0 && !string.IsNullOrEmpty(block.LedgerRoot) && !_anchors.ContainsKey(block.LedgerAnchorSeq))
            {
                _anchors[block.LedgerAnchorSeq] = (block.LedgerRoot, block.BalancesRoot);
                foreach (var c in _checks.Where(c => c.Seq == block.LedgerAnchorSeq)) Verify(c);
            }
            foreach (var tx in block.Transactions.Where(t => t.Type == TransactionType.LedgerRows))
            {
                if (!TryReadPart(tx, out var seq, out var part, out var parts, out var rows)) continue;
                if (seq <= AppliedSeq) continue; // already applied (a re-publish)
                if (!_parts.TryGetValue(seq, out var got)) _parts[seq] = got = new SortedDictionary<int, List<Row>>();
                if (!got.ContainsKey(part)) got[part] = rows; // first copy wins
                _partCount[seq] = parts;
            }
            while (_parts.TryGetValue(AppliedSeq + 1, out var next) && _partCount.TryGetValue(AppliedSeq + 1, out var n) && next.Count == n)
            {
                var seq = AppliedSeq + 1;
                var rows = next.Values.SelectMany(r => r).ToList();
                foreach (var r in rows) Move(r);
                _windowLedgerRoot[seq] = LedgerMerkle.Root(rows.Select(RowLeaf));
                var check = new Check { Seq = seq, Rows = rows.Count, AppliedAtHeight = block.Height, RootAfter = StateRoot };
                Verify(check);
                _checks.Add(check);
                _parts.Remove(seq);
                AppliedSeq = seq;
            }
        }

        private void Verify(Check c)
        {
            if (!_anchors.TryGetValue(c.Seq, out var a)) return;
            c.LedgerRootOk = _windowLedgerRoot.TryGetValue(c.Seq, out var lr) && string.Equals(lr, a.LedgerRoot, StringComparison.OrdinalIgnoreCase);
            c.BalancesRootOk = string.Equals(c.RootAfter, a.BalancesRoot, StringComparison.OrdinalIgnoreCase);
        }

        private void Move(Row r)
        {
            if (!decimal.TryParse(r.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) return;
            if (!string.IsNullOrEmpty(r.From)) Add(r.From, -amount);
            if (!string.IsNullOrEmpty(r.To)) Add(r.To, amount);
        }

        private void Add(string account, decimal delta)
        {
            var v = (_balances.TryGetValue(account, out var cur) ? cur : 0m) + delta;
            if (v == 0m) _balances.Remove(account); else _balances[account] = v;
        }

        public ChainLedger Clone()
        {
            var c = new ChainLedger { AppliedSeq = AppliedSeq, ProcessedHeight = ProcessedHeight };
            foreach (var kv in _balances) c._balances[kv.Key] = kv.Value;
            foreach (var kv in _anchors) c._anchors[kv.Key] = kv.Value;
            foreach (var kv in _parts) c._parts[kv.Key] = new SortedDictionary<int, List<Row>>(kv.Value);
            foreach (var kv in _partCount) c._partCount[kv.Key] = kv.Value;
            foreach (var kv in _windowLedgerRoot) c._windowLedgerRoot[kv.Key] = kv.Value;
            foreach (var x in _checks) c._checks.Add(new Check
            {
                Seq = x.Seq, Rows = x.Rows, AppliedAtHeight = x.AppliedAtHeight, RootAfter = x.RootAfter,
                LedgerRootOk = x.LedgerRootOk, BalancesRootOk = x.BalancesRootOk
            });
            return c;
        }

        public static bool TryReadPart(Transaction tx, out long seq, out int part, out int parts, out List<Row> rows)
        {
            seq = 0; part = 0; parts = 0; rows = new List<Row>();
            if (!TryLong(tx.Data.GetValueOrDefault("anchorSeq"), out seq) || seq <= 0) return false;
            if (!TryLong(tx.Data.GetValueOrDefault("part"), out var p) || !TryLong(tx.Data.GetValueOrDefault("parts"), out var n)) return false;
            if (n < 1 || p < 0 || p >= n) return false;
            var json = Str(tx.Data.GetValueOrDefault("rows"));
            if (json == null) return false;
            try { rows = ParseRows(json); } catch (JsonException) { return false; }
            part = (int)p; parts = (int)n;
            return true;
        }

        public static string? Str(object? v) => v switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            _ => null
        };

        public static bool TryLong(object? v, out long l)
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

    /// <summary>The node's <see cref="ChainLedger"/>, kept in step with its chain.</summary>
    public sealed class ChainLedgerService
    {
        private readonly NodeState _state;
        private readonly object _gate = new();
        private ChainLedger _ledger = new();

        public ChainLedgerService(NodeState state) => _state = state;

        /// <summary>A copy caught up with the chain (safe to read while blocks keep arriving).</summary>
        public ChainLedger Snapshot()
        {
            lock (_gate)
            {
                _ledger.Sync(_state.Chain);
                return _ledger.Clone();
            }
        }

        /// <summary>State after a candidate block's transactions (for its header and its validation).</summary>
        public (long Seq, string Root) Preview(Block candidate)
        {
            lock (_gate)
            {
                _ledger.Sync(_state.Chain);
                var c = _ledger.Clone();
                c.Apply(candidate);
                return (c.AppliedSeq, c.StateRoot);
            }
        }
    }
}
