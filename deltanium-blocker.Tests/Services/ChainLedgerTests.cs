using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Services;
using Deltanium.Blocker.Services.Ledger;
using deltanium_blocker.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace deltanium_blocker.Tests.Services;

public class ChainLedgerTests
{
    private static ChainLedger.Row R(string id, string? from, string? to, string amount, string kind = "topup") =>
        new(id, from, to, amount, kind, null, "2026-11-27T01:02:03.000000Z");

    private static Transaction Rows(long seq, IEnumerable<ChainLedger.Row> rows, int part = 0, int parts = 1) => new()
    {
        Id = $"lr-{seq}-{part}",
        Type = TransactionType.LedgerRows,
        From = "central",
        Timestamp = 1_800_000_000,
        Data = new Dictionary<string, object>
        {
            ["anchorSeq"] = seq, ["part"] = part, ["parts"] = parts, ["rows"] = JsonSerializer.Serialize(rows.ToList()),
        },
    };

    private static string LedgerRoot(IEnumerable<ChainLedger.Row> rows) => LedgerMerkle.Root(rows.Select(ChainLedger.RowLeaf));

    private static string BalancesRoot(params (string, decimal)[] b) => ChainLedger.Root(b.ToDictionary(x => x.Item1, x => x.Item2));

    private static readonly ChainLedger.Row[] W1 = { R("r1", "treasury", "alice", "100.0000000000"), R("r2", "treasury", "bob", "5.0000000000") };
    private static readonly ChainLedger.Row[] W2 = { R("r3", "alice", "escrow:f1", "0.7771000000", "escrow:fund") };

    [Fact]
    public void Rows_replayed_from_the_chain_match_the_anchors_window_by_window()
    {
        var l = new ChainLedger();
        // Block 1 carries anchor 1 in its header; the rows of window 1 arrive in block 2, window 2 in parts.
        l.Apply(new Block { Height = 1, LedgerAnchorSeq = 1, LedgerRoot = LedgerRoot(W1),
            BalancesRoot = BalancesRoot(("treasury", -105m), ("alice", 100m), ("bob", 5m)) });
        l.Apply(new Block { Height = 2, Transactions = new() { Rows(1, W1) } });
        l.AppliedSeq.Should().Be(1);
        l.Checks.Single().BalancesRootOk.Should().BeTrue();
        l.Checks.Single().LedgerRootOk.Should().BeTrue();

        // Window 2's rows arrive before its anchor header: checked once the header shows up.
        l.Apply(new Block { Height = 3, Transactions = new() { Rows(2, W2) } });
        l.Checks.Last().BalancesRootOk.Should().BeNull();
        l.Apply(new Block { Height = 4, LedgerAnchorSeq = 2, LedgerRoot = LedgerRoot(W2),
            BalancesRoot = BalancesRoot(("treasury", -105m), ("alice", 99.2229m), ("bob", 5m), ("escrow:f1", 0.7771m)) });
        l.Checks.Last().BalancesRootOk.Should().BeTrue();
        l.BalanceOf("alice").Should().Be(99.2229m);
        l.StateRoot.Should().Be(BalancesRoot(("treasury", -105m), ("alice", 99.2229m), ("bob", 5m), ("escrow:f1", 0.7771m)));
    }

    [Fact]
    public void A_window_waits_for_all_its_parts_and_for_the_window_before_it()
    {
        var l = new ChainLedger();
        l.Apply(new Block { Height = 1, Transactions = new() { Rows(2, W2) } });           // window 1 missing
        l.AppliedSeq.Should().Be(0);
        l.WaitingSeqs.Should().Equal(2);
        l.Apply(new Block { Height = 2, Transactions = new() { Rows(1, W1.Take(1), 0, 2) } }); // half of window 1
        l.AppliedSeq.Should().Be(0);
        l.Apply(new Block { Height = 3, Transactions = new() { Rows(1, W1.Skip(1), 1, 2), Rows(1, W1.Skip(1), 1, 2) } });
        l.AppliedSeq.Should().Be(2);                                                         // both windows, in order
        l.BalanceOf("bob").Should().Be(5m);
        l.BalanceOf("escrow:f1").Should().Be(0.7771m);
    }

    [Fact]
    public void Altered_rows_show_as_a_mismatch_against_the_anchor()
    {
        var l = new ChainLedger();
        var forged = new[] { R("r1", "treasury", "alice", "1000.0000000000"), W1[1] };
        l.Apply(new Block { Height = 1, LedgerAnchorSeq = 1, LedgerRoot = LedgerRoot(W1),
            BalancesRoot = BalancesRoot(("treasury", -105m), ("alice", 100m), ("bob", 5m)) });
        l.Apply(new Block { Height = 2, Transactions = new() { Rows(1, forged) } });
        l.Checks.Single().LedgerRootOk.Should().BeFalse();
        l.Checks.Single().BalancesRootOk.Should().BeFalse();
    }

    [Fact]
    public void Blocks_commit_the_state_root_and_a_wrong_one_is_rejected()
    {
        var state = new NodeState(TestDataBuilder.CreateTempBlockStore());
        var pool = new TransactionPool(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mp-" + System.Guid.NewGuid().ToString("N") + ".json"),
            NullLogger<TransactionPool>.Instance);
        var ledger = new ChainLedgerService(state);
        var config = TestDataBuilder.CreateNodeConfig();
        var builder = new BlockBuilder(config, pool, state, NullLogger<BlockBuilder>.Instance, ledger);
        var validator = new BlockValidator(config, NullLogger<BlockValidator>.Instance, ledger);

        pool.TryAdd(Rows(1, W1)).Should().BeTrue();
        var block = builder.BuildBlock(new CentralAuthorization("sig", "", 0, null, null));
        block.StateSeq.Should().Be(1);
        block.StateRoot.Should().Be(BalancesRoot(("treasury", -105m), ("alice", 100m), ("bob", 5m)));
        validator.Validate(block, state, out var reason).Should().BeTrue(reason);

        var forged = block with { StateRoot = BalancesRoot(("alice", 1_000_000m)) };
        forged = forged with { Id = Block.Hash(Block.IdPayload(forged)) };
        validator.Validate(forged, state, out reason).Should().BeFalse();
        reason.Should().Be("State root mismatch");
    }
}
