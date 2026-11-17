using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Services;
using FluentAssertions;
using Moq;
using Moq.Protected;
using NBitcoin;
using Xunit;

namespace deltanium_blocker.Tests.Services
{
    public class TransactionRelayClientTests
    {
        private static (TransactionRelayClient Client, Func<HttpRequestMessage?> Captured) Create(Key key, HttpStatusCode status)
        {
            HttpRequestMessage? captured = null;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
                .ReturnsAsync(new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.OK ? "[]" : "no") });
            var client = new TransactionRelayClient("http://localhost:5002", key);
            typeof(TransactionRelayClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(client, new HttpClient(handler.Object));
            return (client, () => captured);
        }

        [Fact]
        public async Task Pull_is_a_POST_signed_with_the_node_key()
        {
            var key = new Key();
            var (client, captured) = Create(key, HttpStatusCode.OK);

            (await client.PullTransactionsAsync(10)).Should().BeEmpty();

            var req = captured()!;
            req.Method.Should().Be(HttpMethod.Post);
            req.Headers.Contains("X-Internal-Token").Should().BeFalse();
            req.Headers.GetValues("X-User-PubKey").Single().Should().Be(key.PubKey.ToHex());
            var ts = req.Headers.GetValues("X-Timestamp").Single();
            var message = NetworkScope.Scope(TransactionRelayClient.SignedMessage("POST", req.RequestUri!.PathAndQuery, ts, ""));
            var sig = Convert.FromBase64String(req.Headers.GetValues("X-Signature").Single());
            key.PubKey.Verify(new uint256(SHA256.HashData(Encoding.UTF8.GetBytes(message))), sig).Should().BeTrue();
        }

        [Fact]
        public async Task Refused_pull_returns_empty()
        {
            var (client, _) = Create(new Key(), HttpStatusCode.Forbidden);
            (await client.PullTransactionsAsync()).Should().BeEmpty();
        }
    }
}
