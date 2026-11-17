using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deltanium.Blocker.Core.Models;
using Deltanium.Blocker.Services;
using Deltanium.Blocker.Services.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NBitcoin;
using Xunit;

namespace deltanium_blocker.Tests.Services;

public class TopUpValidationTests
{
    private static (Key key, string pub) NewKey()
    {
        var k = new Key();
        return (k, k.PubKey.ToHex());
    }

    private static string Sign(Key key, string message)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(key.Sign(new uint256(hash)).ToDER());
    }

    [Fact]
    public void ValidateTopUp_BothSignatures_ReturnsTrue()
    {
        var (centralKey, centralPub) = NewKey();
        var (saKey, saPub) = NewKey();
        var buyer = NewKey().pub;
        var orderId = Guid.NewGuid().ToString("N");
        var signPayload =
            $"{{\"id\":\"{orderId}\",\"type\":\"TopUp\",\"from\":\"{centralPub}\",\"to\":\"{buyer}\",\"data\":{{\"orderId\":\"{orderId}\",\"dCredited\":10}},\"timestamp\":1700000000,\"fee\":\"0.0000000000\"}}";

        var data = JsonDocument.Parse($"{{\"orderId\":\"{orderId}\",\"dCredited\":10}}").RootElement;
        var req = new BlockerTxRequest(
            orderId,
            "TopUp",
            centralPub,
            buyer,
            data,
            1700000000,
            "0.0000000000",
            Sign(centralKey, signPayload),
            signPayload,
            Sign(saKey, signPayload));

        var sut = new TransactionValidationService(NullLogger<TransactionValidationService>.Instance, centralPub, saPub);
        sut.ValidateTopUp(req, out var error).Should().BeTrue(error);
    }

    [Fact]
    public void ValidateTopUp_WrongFrom_ReturnsFalse()
    {
        var (centralKey, centralPub) = NewKey();
        var (saKey, saPub) = NewKey();
        var (otherKey, otherPub) = NewKey();
        var orderId = Guid.NewGuid().ToString("N");
        var signPayload = "payload";
        var data = JsonDocument.Parse($"{{\"orderId\":\"{orderId}\",\"dCredited\":1}}").RootElement;
        var req = new BlockerTxRequest(
            orderId, "TopUp", otherPub, NewKey().pub, data, 1, "0",
            Sign(otherKey, signPayload), signPayload, Sign(saKey, signPayload));

        var sut = new TransactionValidationService(NullLogger<TransactionValidationService>.Instance, centralPub, saPub);
        sut.ValidateTopUp(req, out var error).Should().BeFalse();
        error.Should().Contain("Central");
    }
}
