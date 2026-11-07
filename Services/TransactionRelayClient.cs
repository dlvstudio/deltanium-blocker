using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Deltanium.Blocker.Core;

namespace Deltanium.Blocker.Services
{
    public class TransactionRelayClient
    {
        private readonly string _apiUrl;
        private readonly string? _internalApiToken;
        private readonly HttpClient _httpClient = new();

        public TransactionRelayClient(string apiUrl, string? internalApiToken = null)
        {
            _apiUrl = apiUrl.TrimEnd('/');
            _internalApiToken = internalApiToken
                ?? Environment.GetEnvironmentVariable("INTERNAL_API_TOKEN");
        }

        public async Task<List<Transaction>> PullTransactionsAsync(string blockerPubKey, int limit = 50)
        {
            var url = $"{_apiUrl}/api/tx/pull?blockerPubKey={Uri.EscapeDataString(blockerPubKey)}&limit={limit}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(_internalApiToken))
                request.Headers.TryAddWithoutValidation("X-Internal-Token", _internalApiToken);
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
