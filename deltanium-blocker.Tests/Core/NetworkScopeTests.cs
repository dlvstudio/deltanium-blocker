using System;
using System.Security.Cryptography;
using System.Text;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Services;
using FluentAssertions;
using NBitcoin;
using Xunit;

namespace Deltanium.Blocker.Tests.Core;

[Collection("NetworkScope")]
public class NetworkScopeTests : IDisposable
{
    public void Dispose() => NetworkScope.Id = "";

    private static string SignRaw(Key key, string message)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(key.Sign(new uint256(hash)).ToDER());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("mainnet", "")]
    [InlineData(" Deltanium-Test ", "deltanium-test")]
    public void Id_Normalizes(string? raw, string expected)
    {
        NetworkScope.Id = raw!;
        NetworkScope.Id.Should().Be(expected);
    }

    [Fact]
    public void Id_RejectsBadValues()
    {
        var act = () => NetworkScope.Id = "test net\n";
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Mainnet_MessageUnchanged_OldSignaturesStillVerify()
    {
        NetworkScope.Id = "";
        var key = new Key();
        var sig = SignRaw(key, "POST/api/tip1700000000abc");
        SignatureVerifier.Verify(key.PubKey.ToHex(), "POST/api/tip1700000000abc", sig).Should().BeTrue();
    }

    [Fact]
    public void SignatureFromOneNetwork_FailsOnTheOther()
    {
        var key = new Key();
        const string msg = "POST/api/wallet/transfer1700000000abc";

        var mainSig = SignRaw(key, msg);
        var testSig = SignRaw(key, "deltanium-test\n" + msg);

        NetworkScope.Id = "deltanium-test";
        SignatureVerifier.Verify(key.PubKey.ToHex(), msg, testSig).Should().BeTrue();
        SignatureVerifier.Verify(key.PubKey.ToHex(), msg, mainSig).Should().BeFalse();

        NetworkScope.Id = "";
        SignatureVerifier.Verify(key.PubKey.ToHex(), msg, mainSig).Should().BeTrue();
        SignatureVerifier.Verify(key.PubKey.ToHex(), msg, testSig).Should().BeFalse();
    }
}

[CollectionDefinition("NetworkScope", DisableParallelization = true)]
public class NetworkScopeCollection { }
