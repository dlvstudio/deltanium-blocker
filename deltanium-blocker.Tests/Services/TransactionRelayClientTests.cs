using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Deltanium.Blocker.Services;
using FluentAssertions;
using Moq;
using Moq.Protected;
using Xunit;

namespace deltanium_blocker.Tests.Services
{
    public class TransactionRelayClientTests
    {
        [Fact]
        public async Task PullTransactionsAsync_SendsInternalTokenHeader()
        {
            HttpRequestMessage? captured = null;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });

            var client = new TransactionRelayClient("http://localhost:5002", "blocker-secret");
            var httpField = typeof(TransactionRelayClient)
                .GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
            httpField.Should().NotBeNull();
            httpField!.SetValue(client, new HttpClient(handler.Object));

            var result = await client.PullTransactionsAsync("pk", 10);

            result.Should().BeEmpty();
            captured.Should().NotBeNull();
            captured!.RequestUri!.ToString().Should().Contain("/api/tx/pull");
            captured.Headers.GetValues("X-Internal-Token").Should().ContainSingle("blocker-secret");
        }

        [Fact]
        public async Task PullTransactionsAsync_Unauthorized_ReturnsEmpty()
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var client = new TransactionRelayClient("http://localhost:5002", "bad");
            var httpField = typeof(TransactionRelayClient)
                .GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
            httpField!.SetValue(client, new HttpClient(handler.Object));

            var result = await client.PullTransactionsAsync("pk");
            result.Should().BeEmpty();
        }
    }
}
