using System;
using Deltanium.Blocker.Core;
using FluentAssertions;
using NBitcoin;
using Xunit;

namespace deltanium_blocker.Tests.Services;

public class NodeKeyTests
{
    [Fact]
    public void Env_key_matching_the_registered_public_key_is_accepted()
    {
        var key = new Key();
        NodeConfig.CheckNodeKey(key.ToHex(), key.PubKey.ToHex()).Should().Be(key.PubKey.ToHex());
        NodeConfig.CheckNodeKey(key.ToHex(), "").Should().Be(key.PubKey.ToHex(), "no registered key yet: derive it");
    }

    [Fact]
    public void Env_key_for_another_node_or_garbage_stops_startup()
    {
        var key = new Key();
        var other = new Key();
        FluentActions.Invoking(() => NodeConfig.CheckNodeKey(other.ToHex(), key.PubKey.ToHex()))
            .Should().Throw<InvalidOperationException>().Which.Message.Should().NotContain(other.ToHex());
        FluentActions.Invoking(() => NodeConfig.CheckNodeKey("zz", key.PubKey.ToHex()))
            .Should().Throw<InvalidOperationException>();
    }
}
