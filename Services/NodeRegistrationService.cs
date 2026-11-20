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

            // Key: NODE_PRIVATE_KEY (DPAPI env, set up front by the operator) if present, so the key is never
            // written to disk; otherwise generate one (stored in node_info.json until moved to DPAPI).
            Key privateKey;
            Mnemonic? mnemonic = null;
            var envKey = Environment.GetEnvironmentVariable("NODE_PRIVATE_KEY")?.Trim();
            if (!string.IsNullOrEmpty(envKey))
            {
                _logger.LogInformation("[REGISTRATION] Registering with the key from NODE_PRIVATE_KEY (DPAPI env)...");
                privateKey = new Key(Convert.FromHexString(envKey));
            }
            else
            {
                _logger.LogInformation("[REGISTRATION] Generating new key pair...");
                mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
                privateKey = mnemonic.DeriveExtKey().PrivateKey;
            }
            var keyFromEnv = mnemonic == null;
            var publicKey = privateKey.PubKey;
            var publicKeyHex = publicKey.ToHex();

            var tcpEndpoint = new[] { "NODE_TCP_ENDPOINT", "Dltp__PublicEndpoint" }
                .Select(Environment.GetEnvironmentVariable).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
            // A Block Creator on a home computer may have no HTTPS at all, only its DLTP listener: then do not
            // register the local listen URL as its public endpoint.
            var endpoint = tcpEndpoint != null && Uri.TryCreate(_endpoint, UriKind.Absolute, out var local) && local.IsLoopback
                ? "" : _endpoint;
            var payload = new
            {
                endpoint,
                // Optional DLTP endpoint (tcps://host:port), served by the DLTP gateway in front of this node.
                // or the built-in listener's address (Dltp__PublicEndpoint, e.g. tcps://auto:7333 on a home computer).
                tcpEndpoint,
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
                    Mnemonic = keyFromEnv ? string.Empty : mnemonic!.ToString(),
                    PrivateKey = keyFromEnv ? string.Empty : privateKey.ToHex(),
                    PublicKey = publicKeyHex,
                    Endpoint = _endpoint,
                    ApiResponse = apiResponse
                };

                await File.WriteAllTextAsync(_nodeInfoPath, JsonSerializer.Serialize(nodeInfo, new JsonSerializerOptions { WriteIndented = true }));
                _logger.LogInformation("[REGISTRATION] Block Creator node registered successfully!");
                _logger.LogInformation($"[REGISTRATION] Public Key: {publicKeyHex.Substring(0, Math.Min(20, publicKeyHex.Length))}...");
                _logger.LogInformation($"[REGISTRATION] Node info saved to {_nodeInfoPath}");
                if (!keyFromEnv)
                    _logger.LogWarning("[REGISTRATION] node_info.json now holds the node key in plaintext. Rotate it into DPAPI with Scripts/Rotate-BlockerNodeKey.ps1.");

                return new NodeRegistrationResult
                {
                    Success = true,
                    AlreadyRegistered = false,
                    PublicKey = publicKeyHex,
                    PrivateKey = privateKey.ToHex(),
                    Mnemonic = mnemonic?.ToString()
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











