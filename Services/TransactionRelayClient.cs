using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Deltanium.Blocker.Core;
using Key = NBitcoin.Key;
using uint256 = NBitcoin.uint256;

namespace Deltanium.Blocker.Services
{
    /// <summary>
    /// Pulls pending transactions from Central. Pulling takes them off the queue, so Central only lets an
    /// active registered Blocker do it: the request is signed with this node's key (X-User-PubKey,
    /// X-Timestamp, X-Signature over METHOD + path+query + timestamp + sha256hex(body), network-scoped).
    /// </summary>
    public class TransactionRelayClient
    {
        private readonly string _apiUrl;
        private readonly Key _nodeKey;
        private readonly HttpClient _httpClient = new();

        public TransactionRelayClient(string apiUrl, Key nodeKey)
        {
            _apiUrl = apiUrl.TrimEnd('/');
            _nodeKey = nodeKey;
        }

        public static string SignedMessage(string method, string pathAndQuery, string timestamp, string body) =>
            method + pathAndQuery + timestamp + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        public async Task<List<Transaction>> PullTransactionsAsync(int limit = 50)
        {
            var pubKey = _nodeKey.PubKey.ToHex();
            var pathAndQuery = $"/api/tx/pull?blockerPubKey={Uri.EscapeDataString(pubKey)}&limit={limit}";
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var message = NetworkScope.Scope(SignedMessage("POST", pathAndQuery, ts, ""));
            var sig = _nodeKey.Sign(new uint256(SHA256.HashData(Encoding.UTF8.GetBytes(message))));

            using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl + pathAndQuery)
            {
                Content = new StringContent("", Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("X-User-PubKey", pubKey);
            request.Headers.TryAddWithoutValidation("X-Timestamp", ts);
            request.Headers.TryAddWithoutValidation("X-Signature", Convert.ToBase64String(sig.ToDER()));
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return new List<Transaction>();
            }

            var json = await response.Content.ReadAsStringAsync();
            try
            {
                var options = new JsonSerializerOptions();
                options.Converters.Add(new JsonStringEnumConverter());
                var txs = JsonSerializer.Deserialize<List<Transaction>>(json, options);
                return txs ?? new List<Transaction>();
            }
            catch
            {
                return new List<Transaction>();
            }
        }
    }
}
