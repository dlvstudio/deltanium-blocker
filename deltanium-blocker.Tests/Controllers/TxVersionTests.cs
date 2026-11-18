using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Deltanium.Blocker.Controllers;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Core.Models;
using Deltanium.Blocker.Services;
using FluentAssertions;
using NBitcoin;
using Xunit;

namespace Deltanium.Blocker.Tests.Controllers;

public class TxVersionTests
{
    private readonly Key _central = new();
    private readonly Key _store = new();
    private readonly Key _blocker = new();

    private BlockerTxRequest Req(string id, VersionAttestation? origin = null, string? to = null) =>
        new(id, "StorageOperation", "02aa", to ?? _store.PubKey.ToHex(), JsonDocument.Parse("{}").RootElement, 1, "0", "sig", "payload", null, origin);

    private DecisionResponse Decision(string txId, VersionAttestation? version = null) => new()
    {
        CentralPublicKey = _central.PubKey.ToHex(),
        Version = version ?? VersionAttestation.Create(txId, VersionAttestation.Api, "1.0.0", "4bfdc36", _central.ToHex())
    };

    [Fact]
    public void Storage_tx_gets_signed_api_store_and_blocker_versions()
    {
        var origin = VersionAttestation.Create("tx1", VersionAttestation.Store, "1.0.0", "0900f4c", _store.ToHex());
        TxController.TryCollectVersions(Req("tx1", origin), TransactionType.StorageOperation, Decision("tx1"), _blocker.ToHex(), out var v, out var err)
            .Should().BeTrue(err);
        v.Select(x => x.Service).Should().Equal("api", "store", "blocker");
        v.Should().OnlyContain(x => x.IsValidFor("tx1"));
        v.Last().PublicKey.Should().Be(_blocker.PubKey.ToHex());
    }

    [Fact]
    public void Storage_tx_without_store_version_is_refused()
    {
        TxController.TryCollectVersions(Req("tx1"), TransactionType.StorageOperation, Decision("tx1"), _blocker.ToHex(), out _, out var err)
            .Should().BeFalse();
        err.Should().Contain("Store");
    }

    [Fact]
    public void Store_version_signed_by_another_key_or_for_another_tx_is_refused()
    {
        var other = VersionAttestation.Create("tx1", VersionAttestation.Store, "1.0.0", "x", new Key().ToHex());
        TxController.TryCollectVersions(Req("tx1", other), TransactionType.StorageOperation, Decision("tx1"), _blocker.ToHex(), out _, out _).Should().BeFalse();
        var replayed = VersionAttestation.Create("tx0", VersionAttestation.Store, "1.0.0", "x", _store.ToHex());
        TxController.TryCollectVersions(Req("tx1", replayed), TransactionType.StorageOperation, Decision("tx1"), _blocker.ToHex(), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Decision_without_or_with_forged_api_version_is_refused()
    {
        var d = Decision("tx1");
        d.Version = null;
        TxController.TryCollectVersions(Req("tx1"), TransactionType.Follow, d, _blocker.ToHex(), out _, out _).Should().BeFalse();
        var forged = VersionAttestation.Create("tx1", VersionAttestation.Api, "9.9.9", "x", new Key().ToHex()) with { PublicKey = _central.PubKey.ToHex() };
        TxController.TryCollectVersions(Req("tx1"), TransactionType.Follow, Decision("tx1", forged), _blocker.ToHex(), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void App_tx_gets_api_and_blocker_versions()
    {
        TxController.TryCollectVersions(Req("tx2"), TransactionType.Follow, Decision("tx2"), _blocker.ToHex(), out var v, out var err)
            .Should().BeTrue(err);
        v.Select(x => x.Service).Should().Equal("api", "blocker");
    }

    [Fact]
    public void Tampered_version_in_a_block_tx_is_detected()
    {
        var a = VersionAttestation.Create("tx3", VersionAttestation.Blocker, "1.0.0", "abc", _blocker.ToHex());
        a.IsValidFor("tx3").Should().BeTrue();
        (a with { Commit = "def" }).IsValidFor("tx3").Should().BeFalse();
    }

    [Fact]
    public void Store_origin_json_binds_to_the_request()
    {
        var o = VersionAttestation.Create("tx9", VersionAttestation.Store, "1.0.0", "0900f4c", _store.ToHex());
        var json = $"{{\"id\":\"tx9\",\"type\":\"StorageOperation\",\"from\":\"02aa\",\"to\":\"{_store.PubKey.ToHex()}\",\"data\":{{}},\"timestamp\":\"1\",\"fee\":\"0\",\"signature\":\"s\",\"signPayload\":\"p\"," +
                   $"\"origin\":{{\"service\":\"store\",\"version\":\"1.0.0\",\"commit\":\"0900f4c\",\"publicKey\":\"{o.PublicKey}\",\"signature\":\"{o.Signature}\"}}}}";
        var req = JsonSerializer.Deserialize<BlockerTxRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        req.Origin.Should().Be(o);
        req.Origin!.IsValidFor("tx9").Should().BeTrue();
    }
}
