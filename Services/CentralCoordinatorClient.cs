using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Deltanium.Blocker.Core;

namespace Deltanium.Blocker.Services
{
    public class CentralCoordinatorClient
    {
        private readonly NodeConfig _config;
        private readonly ILogger<CentralCoordinatorClient> _logger;
        private readonly HttpClient _httpClient = new();

        public CentralCoordinatorClient(NodeConfig config, ILogger<CentralCoordinatorClient> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<CentralAuthorization?> RequestBlockSlotAsync()
        {
            var baseUrl = _config.CentralEndpoint.ToString().TrimEnd('/');
            var requestBody = JsonSerializer.Serialize(new { blockerPubKey = _config.NodePublicKey });
            try
            {
                var resp = await _httpClient.PostAsync($"{baseUrl}/api/blockcreatornode/block-slot",
                    new StringContent(requestBody, Encoding.UTF8, "application/json"));
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[COORDINATOR] Block-slot request failed: {StatusCode}", resp.StatusCode);
                    return null;
                }
                var json = await resp.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var response = JsonSerializer.Deserialize<BlockSlotResponse>(json, options);
                if (response?.Payload == null || string.IsNullOrWhiteSpace(response.Signature))
                {
                    _logger.LogWarning("[COORDINATOR] Invalid block-slot response.");
                    return null;
                }
                var p = response.Payload;
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (now < p.IssuedAt || now > p.ExpiresAt)
                {
                    _logger.LogWarning("[COORDINATOR] Block-slot expired or not yet valid. IssuedAt={IssuedAt}, ExpiresAt={ExpiresAt}", p.IssuedAt, p.ExpiresAt);
                    return null;
                }
                if (!p.Granted)
                {
                    _logger.LogDebug("[COORDINATOR] Block slot not granted for this node.");
                    return null;
                }
                if (!string.Equals(p.BlockerPubKey, _config.NodePublicKey, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("[COORDINATOR] Block-slot BlockerPubKey mismatch.");
                    return null;
                }
                var toSign = $"BLOCK_SLOT|{p.BlockerPubKey}|{p.IssuedAt}|{p.ExpiresAt}|{p.Nonce}|{p.Granted}";
                var pubKeyToUse = string.IsNullOrWhiteSpace(_config.CentralApiPublicKey) ? response.CentralPublicKey : _config.CentralApiPublicKey;
                if (string.IsNullOrWhiteSpace(pubKeyToUse))
                {
                    _logger.LogWarning("[COORDINATOR] No Central public key to verify block-slot signature.");
                    return null;
                }
                if (!string.IsNullOrWhiteSpace(_config.CentralApiPublicKey) && !string.Equals(response.CentralPublicKey, _config.CentralApiPublicKey, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("[COORDINATOR] Block-slot response CentralPublicKey does not match trusted key.");
                    return null;
                }
                if (!SignatureVerifier.Verify(pubKeyToUse, toSign, response.Signature))
                {
                    _logger.LogWarning("[COORDINATOR] Invalid block-slot signature.");
                    return null;
                }
                return new CentralAuthorization(response.Signature, response.CentralPublicKey ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[COORDINATOR] Block-slot request error: {Message}", ex.Message);
                return null;
            }
        }

        public Task ReportInvalidBlockAsync(Block block, string reason)
        {
            _logger.LogWarning($"[COORDINATOR] Report invalid block {block.Id}: {reason}");
            return Task.CompletedTask;
        }

        private class BlockSlotPayload
        {
            public string BlockerPubKey { get; set; } = string.Empty;
            public long IssuedAt { get; set; }
            public long ExpiresAt { get; set; }
            public string Nonce { get; set; } = string.Empty;
            public bool Granted { get; set; }
        }

        private class BlockSlotResponse
        {
            public BlockSlotPayload Payload { get; set; } = new BlockSlotPayload();
            public string Signature { get; set; } = string.Empty;
            [JsonPropertyName("centralPublicKey")]
            public string CentralPublicKey { get; set; } = string.Empty;
        }
    }
}
