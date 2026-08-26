using System.Text.Json;
using System.Text.Json.Serialization;
using Deltanium.Blocker.Core.Models;
using FluentAssertions;
using Xunit;

namespace deltanium_blocker.Tests
{
    public class BlockerTxRequestTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        [Fact]
        public void Deserialize_WithFeeString_Succeeds()
        {
            var json = """
            {
              "id": "tx123",
              "type": "StorageOperation",
              "from": "pubkeyA",
              "to": "pubkeyB",
              "data": {},
              "timestamp": 1771257770,
              "fee": "0.1629410932",
              "signature": "sig",
              "signPayload": "payload"
            }
            """;

            var result = JsonSerializer.Deserialize<BlockerTxRequest>(json, Options);

            Assert.NotNull(result);
            Assert.Equal("0.1629410932", result!.Fee);
        }

        [Fact]
        public void Deserialize_WithTimestampAsString_Succeeds()
        {
            var json = """
            {
              "id": "tx456",
              "type": "StorageOperation",
              "from": "pubkeyA",
              "to": "pubkeyB",
              "data": {},
              "timestamp": "1771257770",
              "fee": "0.0000001000",
              "signature": "sig",
              "signPayload": "payload"
            }
            """;

            var result = JsonSerializer.Deserialize<BlockerTxRequest>(json, Options);

            result.Should().NotBeNull();
            result!.Timestamp.Should().Be(1771257770L);
            result.Fee.Should().Be("0.0000001000");
        }
    }
}
