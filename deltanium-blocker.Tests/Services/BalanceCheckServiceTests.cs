using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Services;
using Deltanium.Blocker.Services.Ledger;
using deltanium_blocker.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace deltanium_blocker.Tests.Services;

/// <summary>TOKENOMICS_V2 §11 phase B: value txs checked against the BalancesRoot anchored in the chain.</summary>
public class BalanceCheckServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private static readonly DateTime AnchorAt = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly long AfterAnchorMs = new DateTimeOffset(AnchorAt.AddMinutes(5)).ToUnixTimeMilliseconds();

    private sealed class FakeClient : ILedgerProofClient
    {
        public List<AnchorDto> Anchors = new();
        public Dictionary<string, BalanceProofDto> Proofs = new();
        public Task<List<AnchorDto>> GetAnchorsAsync() => Task.FromResult(Anchors);
        public Dictionary<long, List<LedgerEntryDto>> Entries = new();
        public Task<BalanceProofDto?> GetBalanceProofAsync(long seq, string account) =>
            Task.FromResult(Proofs.TryGetValue(account, out var p) ? p : null);
        public Task<List<LedgerEntryDto>?> GetAnchorEntriesAsync(long seq) =>
            Task.FromResult(Entries.TryGetValue(seq, out var e) ? e : null);
    }

    private static List<MerkleStep> Path(List<string> leaves, int index)
    {
        var path = new List<MerkleStep>();
        var level = leaves.ToList();
        while (level.Count > 1)
        {
            var sib = index ^ 1;
            if (sib < level.Count) path.Add(new MerkleStep { Hash = level[sib], Side = sib < index ? "L" : "R" });
            var next = new List<string>();
            for (var i = 0; i < level.Count; i += 2)
                next.Add(i + 1 < level.Count ? LedgerMerkle.Sha256Hex(level[i] + level[i + 1]) : level[i]);
            level = next;
            index /= 2;
        }
        return path;
    }

    /// <summary>Chain with one anchor block committing balances; Central fake serving matching proofs.</summary>
    private static (BalanceCheckService Svc, FakeClient Client, NodeState State, TransactionPool Pool) Setup(
        Dictionary<string, decimal> balances, string mode = "enforce")
    {
        var accounts = balances.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
        var leaves = accounts.Select(kv => LedgerMerkle.BalanceLeaf(kv.Key, kv.Value)).ToList();
        var root = LedgerMerkle.Root(leaves);
        var state = new NodeState(TestDataBuilder.CreateTempBlockStore());
        state.AddBlock(TestDataBuilder.CreateBlock(height: 0));
        state.AddBlock(TestDataBuilder.CreateBlock(height: 1) with { LedgerAnchorSeq = 1, LedgerRoot = "lr", BalancesRoot = root });
        var client = new FakeClient();
        client.Anchors.Add(new AnchorDto { Seq = 1, UpToInclusive = AnchorAt, LedgerRoot = "lr", BalancesRoot = root });
        for (var i = 0; i < accounts.Count; i++)
            client.Proofs[accounts[i].Key] = new BalanceProofDto
            {
                Seq = 1, Account = accounts[i].Key, Balance = accounts[i].Value, Included = true,
                Leaf = leaves[i], Path = Path(leaves, i), Root = root
            };
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        return (new BalanceCheckService(client, state, pool, mode), client, state, pool);
    }

    private static BalanceCheckService.ValueTxAuth Auth(decimal amount, long atMs) => new(amount, atMs, "POST", "/api/wallet/transfer", "1", "{}");

    private static Transaction ValueTx(string id, string from, decimal amount, long atMs, string sig = "s-other") => new()
    {
        Id = id, Type = TransactionType.Transfer, From = from, To = Bob, Signature = sig,
        Data = new Dictionary<string, object>
        {
            ["amount"] = JsonDocument.Parse("\"" + amount.ToString("F10", System.Globalization.CultureInfo.InvariantCulture) + "\"").RootElement,
            ["ledgerAtMs"] = JsonDocument.Parse(atMs.ToString()).RootElement
        }
    };

    [Fact]
    public async Task Proven_balance_covers_the_move()
    {
        var (svc, _, _, _) = Setup(new() { [Alice] = 1000, [Bob] = 5, ["carol"] = 7 });
        var (v, detail) = await svc.CheckAsync("t1", Alice, "sig1", Auth(800, AfterAnchorMs));
        v.Should().Be(BalanceVerdict.Ok, detail);
    }

    [Fact]
    public async Task Earlier_on_chain_spend_since_the_anchor_is_subtracted()
    {
        var (svc, _, _, pool) = Setup(new() { [Alice] = 1000, [Bob] = 5 });
        pool.TryAdd(ValueTx("t0", Alice, 600, AfterAnchorMs - 1000)).Should().BeTrue();
        var (v, _) = await svc.CheckAsync("t1", Alice, "sig1", Auth(500, AfterAnchorMs));
        v.Should().Be(BalanceVerdict.Unverifiable, "the snapshot alone does not cover it and no later anchor exists yet");
        svc.Defers(v).Should().BeTrue();
    }

    [Fact]
    public async Task Forged_balance_in_proof_is_invalid()
    {
        var (svc, client, _, _) = Setup(new() { [Alice] = 10, [Bob] = 5 });
        var p = client.Proofs[Alice];
        p.Balance = 1_000_000;
        p.Leaf = LedgerMerkle.BalanceLeaf(Alice, p.Balance); // self-consistent leaf but not in the anchored root
        var (v, _) = await svc.CheckAsync("t1", Alice, "sig1", Auth(500, AfterAnchorMs));
        v.Should().Be(BalanceVerdict.Invalid);
    }

    [Fact]
    public async Task Account_missing_from_snapshot_has_zero_balance()
    {
        var (svc, client, _, _) = Setup(new() { [Bob] = 5 });
        client.Proofs[Alice] = new BalanceProofDto { Seq = 1, Account = Alice, Included = false };
        (await svc.CheckAsync("t1", Alice, "sig1", Auth(1, AfterAnchorMs))).Verdict.Should().Be(BalanceVerdict.Unverifiable,
            "a top-up after the snapshot is possible until a later anchor shows otherwise");
    }

    // ---- replay of anchored rows after the snapshot (top-up after the last anchor) ----

    private static readonly DateTime MoveAt = AnchorAt.AddMinutes(5);

    private static LedgerEntryDto Entry(string id, string? from, string? to, decimal amount, string kind, string? refId, DateTime at) =>
        new() { Id = id, FromPublicKey = from, ToPublicKey = to, Amount = amount, Kind = kind, RefId = refId, CreatedAt = at };

    /// <summary>Anchor 2 commits these rows; it is added to Central's list and to the chain.</summary>
    private static void AddAnchor2(FakeClient client, NodeState state, List<LedgerEntryDto> rows, string? chainLedgerRoot = null)
    {
        var root = LedgerMerkle.Root(rows.Select(LedgerMerkle.LedgerLeaf));
        client.Anchors.Add(new AnchorDto { Seq = 2, UpToInclusive = rows[^1].CreatedAt, LedgerRoot = root, BalancesRoot = "br2" });
        client.Entries[2] = rows;
        state.AddBlock(TestDataBuilder.CreateBlock(height: 2) with { LedgerAnchorSeq = 2, LedgerRoot = chainLedgerRoot ?? root, BalancesRoot = "br2" });
    }

    private static long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();

    [Fact]
    public async Task Top_up_after_the_snapshot_is_counted_once_a_later_anchor_covers_the_move()
    {
        var (svc, client, state, _) = Setup(new() { [Bob] = 5 });
        client.Proofs[Alice] = new BalanceProofDto { Seq = 1, Account = Alice, Included = false };
        (await svc.CheckAsync("t1", Alice, "sig1", Auth(800, Ms(MoveAt)))).Verdict.Should().Be(BalanceVerdict.Unverifiable);

        AddAnchor2(client, state, new()
        {
            Entry("e1", null, Alice, 1000, "topup", "order-1", AnchorAt.AddMinutes(1)),
            Entry("e2", Alice, Bob, 800, "transfer", "t1", MoveAt),
        });
        var (v, detail) = await svc.CheckAsync("t1", Alice, "sig1", Auth(800, Ms(MoveAt)));
        v.Should().Be(BalanceVerdict.Ok, detail);
    }

    [Fact]
    public async Task Replay_still_finds_an_overspend()
    {
        var (svc, client, state, _) = Setup(new() { [Bob] = 5 });
        client.Proofs[Alice] = new BalanceProofDto { Seq = 1, Account = Alice, Included = false };
        AddAnchor2(client, state, new()
        {
            Entry("e1", null, Alice, 100, "topup", "order-1", AnchorAt.AddMinutes(1)),
            Entry("e2", Alice, Bob, 800, "transfer", "t1", MoveAt),
        });
        (await svc.CheckAsync("t1", Alice, "sig1", Auth(800, Ms(MoveAt)))).Verdict.Should().Be(BalanceVerdict.Insufficient);
    }

    [Fact]
    public async Task On_chain_spend_missing_from_Centrals_rows_is_still_subtracted()
    {
        var (svc, client, state, pool) = Setup(new() { [Bob] = 5 });
        client.Proofs[Alice] = new BalanceProofDto { Seq = 1, Account = Alice, Included = false };
        pool.TryAdd(ValueTx("t0", Alice, 600, Ms(AnchorAt.AddMinutes(2)))).Should().BeTrue(); // not in Central's rows
        AddAnchor2(client, state, new()
        {
            Entry("e1", null, Alice, 1000, "topup", "order-1", AnchorAt.AddMinutes(1)),
            Entry("e2", Alice, Bob, 800, "transfer", "t1", MoveAt),
        });
        (await svc.CheckAsync("t1", Alice, "sig1", Auth(800, Ms(MoveAt)))).Verdict.Should().Be(BalanceVerdict.Insufficient);
    }

    [Fact]
    public async Task Rows_that_do_not_hash_to_the_chains_LedgerRoot_are_not_trusted()
    {
        var (svc, client, state, _) = Setup(new() { [Bob] = 5 });
        client.Proofs[Alice] = new BalanceProofDto { Seq = 1, Account = Alice, Included = false };
        AddAnchor2(client, state, new()
        {
            Entry("e1", null, Alice, 1000, "topup", "order-1", AnchorAt.AddMinutes(1)),
            Entry("e2", Alice, Bob, 800, "transfer", "t1", MoveAt),
        }, chainLedgerRoot: "something-else");
        (await svc.CheckAsync("t1", Alice, "sig1", Auth(800, Ms(MoveAt)))).Verdict.Should().Be(BalanceVerdict.Unverifiable);
    }

    // ---- deferred mempool ----

    private static Transaction HeldTx(decimal amount) => ValueTx("t1", Alice, amount, Ms(MoveAt), sig: "sig1");

    [Fact]
    public async Task Held_tx_joins_the_mempool_once_an_anchor_proves_the_balance()
    {
        var (svc, client, state, pool) = Setup(new() { [Bob] = 5 });
        client.Proofs[Alice] = new BalanceProofDto { Seq = 1, Account = Alice, Included = false };
        pool.TryDefer(HeldTx(800)).Should().BeTrue();
        pool.Count.Should().Be(0, "held txs never go into blocks");
        pool.DeferredAwaitingAnchor.Should().BeTrue();
        pool.TryAdd(HeldTx(800)).Should().BeFalse("same id is already held");

        (await svc.ProcessDeferredAsync()).Should().Be((0, 0, 1));

        AddAnchor2(client, state, new()
        {
            Entry("e1", null, Alice, 1000, "topup", "order-1", AnchorAt.AddMinutes(1)),
            Entry("e2", Alice, Bob, 800, "transfer", "t1", MoveAt),
        });
        (await svc.ProcessDeferredAsync()).Should().Be((1, 0, 0));
        pool.DeferredCount.Should().Be(0);
        pool.Snapshot().Select(t => t.Id).Should().Equal("t1");
    }

    [Fact]
    public async Task Held_tx_is_dropped_when_the_anchor_shows_an_overspend_or_it_waits_too_long()
    {
        var (svc, client, state, pool) = Setup(new() { [Bob] = 5 });
        client.Proofs[Alice] = new BalanceProofDto { Seq = 1, Account = Alice, Included = false };
        pool.TryDefer(HeldTx(800)).Should().BeTrue();
        (await svc.ProcessDeferredAsync(DateTime.UtcNow + svc.MaxHold + TimeSpan.FromMinutes(1))).Should().Be((0, 1, 0));
        pool.DeferredCount.Should().Be(0);

        pool.TryDefer(HeldTx(800)).Should().BeTrue();
        AddAnchor2(client, state, new()
        {
            Entry("e1", null, Alice, 100, "topup", "order-1", AnchorAt.AddMinutes(1)),
            Entry("e2", Alice, Bob, 800, "transfer", "t1", MoveAt),
        });
        (await svc.ProcessDeferredAsync()).Should().Be((0, 1, 0));
        pool.Count.Should().Be(0);
    }

    [Fact]
    public void Held_txs_survive_a_restart()
    {
        var path = TestDataBuilder.CreateTempMempoolPath();
        var pool = new TransactionPool(path, TestDataBuilder.CreateLogger<TransactionPool>());
        pool.TryDefer(HeldTx(1)).Should().BeTrue();
        var reloaded = new TransactionPool(path, TestDataBuilder.CreateLogger<TransactionPool>());
        reloaded.DeferredCount.Should().Be(1);
        reloaded.Count.Should().Be(0);
        reloaded.DeferredAwaitingAnchor.Should().BeTrue();
    }

    [Fact]
    public async Task Move_before_any_anchored_snapshot_is_unverifiable_and_rejected_only_in_enforce()
    {
        var (svc, _, _, _) = Setup(new() { [Alice] = 1000 });
        var before = new DateTimeOffset(AnchorAt.AddMinutes(-1)).ToUnixTimeMilliseconds();
        var (v, _) = await svc.CheckAsync("t1", Alice, "sig1", Auth(1, before));
        v.Should().Be(BalanceVerdict.Unverifiable);
        svc.Accepts(v).Should().BeFalse();
        new BalanceCheckService(new FakeClient(), new NodeState(TestDataBuilder.CreateTempBlockStore()),
            new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>()), "warn")
            .Accepts(v).Should().BeTrue();
    }

    [Fact]
    public async Task Reused_user_signature_is_a_replay()
    {
        var (svc, _, _, pool) = Setup(new() { [Alice] = 1000 });
        pool.TryAdd(ValueTx("t0", Alice, 1, AfterAnchorMs - 10, sig: "same")).Should().BeTrue();
        (await svc.CheckAsync("t1", Alice, "same", Auth(1, AfterAnchorMs))).Verdict.Should().Be(BalanceVerdict.Invalid);
    }

    private static JsonElement Data(decimal amount, string body, string path = "/api/wallet/transfer") => JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        amount = amount.ToString("F10", System.Globalization.CultureInfo.InvariantCulture),
        ledgerAtMs = AfterAnchorMs,
        auth = new { method = "POST", path, timestamp = "1700000000", body }
    })).RootElement;

    [Fact]
    public void Carried_request_must_match_amount_recipient_and_sign_payload()
    {
        var body = "{\"toPublicKey\":\"bob\",\"amount\":25}";
        var payload = BalanceCheckService.SignedMessage("POST", "/api/wallet/transfer", "1700000000", body);

        BalanceCheckService.TryParseAuth(TransactionType.Transfer, Bob, payload, Data(25, body), out var auth, out var err).Should().BeTrue(err);
        auth!.Amount.Should().Be(25);

        BalanceCheckService.TryParseAuth(TransactionType.Transfer, Bob, payload, Data(2500, body), out _, out _).Should().BeFalse();
        BalanceCheckService.TryParseAuth(TransactionType.Transfer, "mallory", payload, Data(25, body), out _, out _).Should().BeFalse();
        BalanceCheckService.TryParseAuth(TransactionType.Transfer, Bob, payload + "x", Data(25, body), out _, out _).Should().BeFalse();
        BalanceCheckService.TryParseAuth(TransactionType.Donate, Bob, payload, Data(25, body), out _, out _)
            .Should().BeFalse("a tip must carry a POST /api/tip request");
    }
}
