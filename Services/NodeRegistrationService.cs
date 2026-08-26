using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NBitcoin;

namespace Deltanium.Blocker.Services
{
    public class NodeRegistrationService
    {
        private readonly string _apiUrl;
        private readonly string _endpoint;
        private readonly string _voucher;
        private readonly string _nodeInfoPath = "node_info.json";
        private readonly ILogger<NodeRegistrationService> _logger;

        public NodeRegistrationService(string apiUrl, string endpoint, string voucher, ILogger<NodeRegistrationService> logger)
        {
            _apiUrl = apiUrl;
            _endpoint = endpoint;
            _voucher = voucher;
            _logger = logger;
        }

        public async Task<NodeRegistrationResult> RegisterNodeIfNeededAsync()
        {
            if (File.Exists(_nodeInfoPath))
            {
                // Load existing node info
                var existingInfo = await LoadNodeInfoAsync();
                if (existingInfo != null)
                {
                    _logger.LogInformation("[REGISTRATION] Node already registered. Loading existing credentials...");
                    return new NodeRegistrationResult
                    {
                        Success = true,
                        AlreadyRegistered = true,
                        PublicKey = existingInfo.PublicKey,
                        PrivateKey = existingInfo.PrivateKey,
                        Mnemonic = existingInfo.Mnemonic
                    };
                }
            }

            // Generate mnemonic and key pair
            _logger.LogInformation("[REGISTRATION] Generating new key pair...");
            var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
            var hdRoot = mnemonic.DeriveExtKey();
            var privateKey = hdRoot.PrivateKey;
            var publicKey = privateKey.PubKey;
            var publicKeyHex = publicKey.ToHex();

            var payload = new
            {
                endpoint = _endpoint,
                voucher = _voucher,
                publicKey = publicKeyHex
            };

            _logger.LogInformation($"[REGISTRATION] Registering with Central API at {_apiUrl}...");
            using var client = new HttpClient();
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{_apiUrl}/api/blockcreatornode/register", content);

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

                // Save mnemonic, privateKey, publicKey, and node info
                var nodeInfo = new NodeInfo
                {
                    Mnemonic = mnemonic.ToString(),
                    PrivateKey = privateKey.ToHex(),
                    PublicKey = publicKeyHex,
                    Endpoint = _endpoint,
                    ApiResponse = apiResponse
                };

                await File.WriteAllTextAsync(_nodeInfoPath, JsonSerializer.Serialize(nodeInfo, new JsonSerializerOptions { WriteIndented = true }));
                _logger.LogInformation("[REGISTRATION] Block Creator node registered successfully!");
                _logger.LogInformation($"[REGISTRATION] Public Key: {publicKeyHex.Substring(0, Math.Min(20, publicKeyHex.Length))}...");
                _logger.LogInformation($"[REGISTRATION] Node info saved to {_nodeInfoPath}");

                return new NodeRegistrationResult
                {
                    Success = true,
                    AlreadyRegistered = false,
                    PublicKey = publicKeyHex,
                    PrivateKey = privateKey.ToHex(),
                    Mnemonic = mnemonic.ToString()
                };
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"[REGISTRATION] Node registration failed: {(string.IsNullOrWhiteSpace(error) ? "<empty response>" : error)}");
                _logger.LogError($"[REGISTRATION] Status: {(int)response.StatusCode} {response.ReasonPhrase}");
                return new NodeRegistrationResult
                {
                    Success = false,
                    ErrorMessage = string.IsNullOrWhiteSpace(error)
                        ? $"{(int)response.StatusCode} {response.ReasonPhrase}"
                        : error
                };
            }
        }

        public async Task<NodeInfo?> LoadNodeInfoAsync()
        {
            if (!File.Exists(_nodeInfoPath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(_nodeInfoPath);
                return JsonSerializer.Deserialize<NodeInfo>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[REGISTRATION] Error loading node info: {ErrorMessage}", ex.Message);
                return null;
            }
        }

        /// <summary>Persist Central API public key to node_info.json so it survives restarts.</summary>
        public async Task SaveCentralApiPublicKeyAsync(string centralApiPublicKey)
        {
            if (string.IsNullOrWhiteSpace(centralApiPublicKey) || !File.Exists(_nodeInfoPath))
                return;
            try
            {
                var existing = await LoadNodeInfoAsync();
                if (existing == null)
                    return;
                existing.CentralApiPublicKey = centralApiPublicKey.Trim();
                await File.WriteAllTextAsync(_nodeInfoPath, JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true }));
                _logger.LogInformation("[REGISTRATION] Saved Central API public key to {Path}", _nodeInfoPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[REGISTRATION] Failed to save Central API public key: {Message}", ex.Message);
            }
        }
    }

    public class NodeInfo
    {
        public string Mnemonic { get; set; } = string.Empty;
        public string PrivateKey { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public JsonElement ApiResponse { get; set; }
        /// <summary>Trusted Central API public key (persisted so it survives restarts).</summary>
        public string CentralApiPublicKey { get; set; } = string.Empty;
    }

    public class NodeRegistrationResult
    {
        public bool Success { get; set; }
        public bool AlreadyRegistered { get; set; }
        public string? PublicKey { get; set; }
        public string? PrivateKey { get; set; }
        public string? Mnemonic { get; set; }
        public string? ErrorMessage { get; set; }
    }
}











