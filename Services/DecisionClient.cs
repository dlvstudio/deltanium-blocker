using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Deltanium.Blocker.Services
{
    public class DecisionClient
    {
        private readonly string _apiUrl;
        private readonly HttpClient _httpClient = new();
        private readonly ILogger<DecisionClient> _logger;

        public DecisionClient(string apiUrl, ILogger<DecisionClient> logger)
        {
            _apiUrl = apiUrl.TrimEnd('/');
            _logger = logger;
        }

        public async Task<DecisionResponse?> RequestDecisionAsync(string txId, string blockerPubKey)
        {
            var body = JsonSerializer.Serialize(new { txId, blockerPubKey });
            var resp = await _httpClient.PostAsync($"{_apiUrl}/api/blockcreatornode/decision",
                new StringContent(body, Encoding.UTF8, "application/json"));

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to request decision for tx {txId}. Status code: {resp.StatusCode}");
                return null;
            }
            try
            {
                var json = await resp.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize<DecisionResponse>(json, options);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, $"Failed to deserialize decision response for tx {txId}.");
                return null;
            }
        }
    }

    public class DecisionResponse
    {
        public DecisionPayload Decision { get; set; } = new DecisionPayload();
        public string Signature { get; set; } = string.Empty;
        public string CentralPublicKey { get; set; } = string.Empty;
    }

    public class DecisionPayload
    {
        public string TxId { get; set; } = string.Empty;
        public string BlockerPubKey { get; set; } = string.Empty;
        public bool Selected { get; set; }
        public long IssuedAt { get; set; }
        public long ExpiresAt { get; set; }
        public string Nonce { get; set; } = string.Empty;
    }
}
