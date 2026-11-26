using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deltanium.Blocker.Core.Models;
using Deltanium.Blocker.Services.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NBitcoin;
using Xunit;

namespace deltanium_blocker.Tests.Services;

public class StorageTermValidationTests
{
    private const string Terms = "[{\"fileId\":\"f1\",\"status\":\"active\",\"paidUntil\":1800000000,\"deleteAfter\":1803196800,\"blocks\":[\"b0\"]}]";

    private static string Sign(Key key, string message) =>
        Convert.ToBase64String(key.Sign(new uint256(SHA256.HashData(Encoding.UTF8.GetBytes(message)))).ToDER());

    private static BlockerTxRequest Req(Key signer, string from, string terms, string? payload = null, string id = "st-1", long ts = 1_800_000_000)
    {
        payload ??= TransactionValidationService.StorageTermSignPayload(id, ts, terms);
        var data = JsonDocument.Parse(JsonSerializer.Serialize(new { terms, count = 1 })).RootElement;
        return new BlockerTxRequest(id, "StorageTerm", from, "", data, ts, "0.0000000000", Sign(signer, payload), payload, null);
    }

    [Fact]
    public void Central_signed_terms_are_accepted()
    {
        var central = new Key();
        var sut = new TransactionValidationService(NullLogger<TransactionValidationService>.Instance, central.PubKey.ToHex());
        sut.ValidateStorageTerm(Req(central, central.PubKey.ToHex(), Terms), out var error).Should().BeTrue(error);
    }

    [Fact]
    public void Terms_not_from_Central_or_altered_after_signing_are_refused()
    {
        var central = new Key();
        var other = new Key();
        var sut = new TransactionValidationService(NullLogger<TransactionValidationService>.Instance, central.PubKey.ToHex());

        sut.ValidateStorageTerm(Req(other, other.PubKey.ToHex(), Terms), out _).Should().BeFalse();       // not Central
        sut.ValidateStorageTerm(Req(other, central.PubKey.ToHex(), Terms), out _).Should().BeFalse();     // forged signature
        var signedPayload = TransactionValidationService.StorageTermSignPayload("st-1", 1_800_000_000, Terms);
        var altered = Terms.Replace("1803196800", "1903196800");                                          // later deletion
        sut.ValidateStorageTerm(Req(central, central.PubKey.ToHex(), altered, signedPayload), out var e).Should().BeFalse();
        e.Should().Contain("does not match");
        sut.ValidateStorageTerm(Req(central, central.PubKey.ToHex(), "[{\"fileId\":\"f1\"}]"), out _).Should().BeFalse();
    }
}
