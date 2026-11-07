using System.Text.Json;
using Deltanium.Blocker.Core.Models;
using Deltanium.Blocker.Services.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace deltanium_blocker.Tests.Services;

public class TransactionValidationServiceTests
{
    private static BlockerTxRequest CreateRequestWithData(string dataJson)
    {
        var dataDoc = JsonDocument.Parse(dataJson);
        var dataElement = dataDoc.RootElement.Clone();
        dataDoc.Dispose();
        return new BlockerTxRequest(
            Id: "tx-test-1",
            Type: "StorageOperation",
            From: "024aa6c7f0bdd2fc4a9ccc4ce71b79e834457adae7ccc4b1fd61c9022c2a9da2d0",
            To: "03699b56070f41068ca1166169547a4bd05022c41e184069352163dfe21553a14c",
            Data: dataElement,
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Fee: "0.0000001000",
            Signature: "sig",
            SignPayload: "hash"
        );
    }

    [Fact]
    public void ValidateStorageOperation_WhenDataMissingContract_ReturnsFalse()
    {
        var logger = new Mock<ILogger<TransactionValidationService>>();
        var service = new TransactionValidationService(logger.Object);
        var req = CreateRequestWithData("""{ "contractId": "c1", "fileId": "f1" }""");

        var result = service.ValidateStorageOperation(req, out var error);

        result.Should().BeFalse();
        error.Should().Be("Missing contract in StorageOperation data");
    }

    [Fact]
    public void ValidateStorageOperation_Envelope_WhenContractMissingHash_ReturnsFalse()
    {
        var logger = new Mock<ILogger<TransactionValidationService>>();
        var service = new TransactionValidationService(logger.Object);
        var dataJson = """
        {
          "contract": {
            "appSignature": "s1",
            "storageNodeSignature": "s2",
            "content": {
              "appPublicKey": "024aa6c7f0bdd2fc4a9ccc4ce71b79e834457adae7ccc4b1fd61c9022c2a9da2d0",
              "storageNodePublicKey": "03699b56070f41068ca1166169547a4bd05022c41e184069352163dfe21553a14c"
            }
          }
        }
        """;
        var req = CreateRequestWithData(dataJson);

        var result = service.ValidateStorageOperation(req, out var error);

        result.Should().BeFalse();
        error.Should().Be("Envelope contract must have hash, appSignature, and storageNodeSignature");
    }

    [Fact]
    public void ValidateStorageOperation_Envelope_WhenContentMissingAppPublicKey_ReturnsFalse()
    {
        var logger = new Mock<ILogger<TransactionValidationService>>();
        var service = new TransactionValidationService(logger.Object);
        var dataJson = """
        {
          "contract": {
            "hash": "abc123",
            "appSignature": "s1",
            "storageNodeSignature": "s2",
            "content": {
              "storageNodePublicKey": "03699b56070f41068ca1166169547a4bd05022c41e184069352163dfe21553a14c"
            }
          }
        }
        """;
        var req = CreateRequestWithData(dataJson);

        var result = service.ValidateStorageOperation(req, out var error);

        result.Should().BeFalse();
        error.Should().Be("Contract content must have appPublicKey and storageNodePublicKey");
    }

    [Fact]
    public void ValidateStorageOperation_Envelope_WhenContentMissingStorageNodePublicKey_ReturnsFalse()
    {
        var logger = new Mock<ILogger<TransactionValidationService>>();
        var service = new TransactionValidationService(logger.Object);
        var dataJson = """
        {
          "contract": {
            "hash": "abc123",
            "appSignature": "s1",
            "storageNodeSignature": "s2",
            "content": {
              "appPublicKey": "024aa6c7f0bdd2fc4a9ccc4ce71b79e834457adae7ccc4b1fd61c9022c2a9da2d0"
            }
          }
        }
        """;
        var req = CreateRequestWithData(dataJson);

        var result = service.ValidateStorageOperation(req, out var error);

        result.Should().BeFalse();
        error.Should().Be("Contract content must have appPublicKey and storageNodePublicKey");
    }

    [Fact]
    public void ValidateStorageOperation_Envelope_WhenContentHasOnlyRequiredFields_DoesNotFailOnMissingStatusOrDates()
    {
        var logger = new Mock<ILogger<TransactionValidationService>>();
        var service = new TransactionValidationService(logger.Object);
        var dataJson = """
        {
          "contract": {
            "hash": "abc123",
            "appSignature": "invalid-sig-will-fail-later",
            "storageNodeSignature": "invalid-sig",
            "content": {
              "appPublicKey": "024aa6c7f0bdd2fc4a9ccc4ce71b79e834457adae7ccc4b1fd61c9022c2a9da2d0",
              "storageNodePublicKey": "03699b56070f41068ca1166169547a4bd05022c41e184069352163dfe21553a14c"
            }
          }
        }
        """;
        var req = CreateRequestWithData(dataJson);

        var result = service.ValidateStorageOperation(req, out var error);

        result.Should().BeFalse();
        error.Should().Be("Invalid contract app signature");
    }

    [Fact]
    public void ValidateStorageOperation_Envelope_WhenContentHasDynamicFields_OnlyRequiresAppAndStorageNodeKeys()
    {
        var logger = new Mock<ILogger<TransactionValidationService>>();
        var service = new TransactionValidationService(logger.Object);
        var dataJson = """
        {
          "contract": {
            "hash": "def456",
            "appSignature": "s1",
            "storageNodeSignature": "s2",
            "content": {
              "appPublicKey": "024aa6c7f0bdd2fc4a9ccc4ce71b79e834457adae7ccc4b1fd61c9022c2a9da2d0",
              "storageNodePublicKey": "03699b56070f41068ca1166169547a4bd05022c41e184069352163dfe21553a14c",
              "customField": "any",
              "anotherDynamic": 42
            }
          }
        }
        """;
        var req = CreateRequestWithData(dataJson);

        var result = service.ValidateStorageOperation(req, out var error);

        result.Should().BeFalse();
        error.Should().Be("Invalid contract app signature");
    }

    [Fact]
    public void ValidateStorageOperation_WhenDataIsNull_ReturnsFalse()
    {
        var logger = new Mock<ILogger<TransactionValidationService>>();
        var service = new TransactionValidationService(logger.Object);
        var req = new BlockerTxRequest(
            "tx-null",
            "StorageOperation",
            "from",
            "to",
            default,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            "0",
            "sig",
            "payload"
        );

        var result = service.ValidateStorageOperation(req, out var error);

        result.Should().BeFalse();
        error.Should().Be("Missing StorageOperation data");
    }
}
