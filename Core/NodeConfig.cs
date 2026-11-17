using System;
using System.IO;
using System.Text.Json;
using Deltanium.Blocker.Services;

namespace Deltanium.Blocker.Core
{
    public class NodeConfig
    {
        public string NodePublicKey { get; init; } = "NODE_PUBKEY_PLACEHOLDER";
        public string NodePrivateKey { get; init; } = "NODE_PRIVKEY_PLACEHOLDER";
        public Uri CentralEndpoint { get; init; } = new Uri("https://deltanium.com");
        /// <summary>Trusted Central API public key for verifying decision and block-slot signatures. Set via CENTRAL_API_PUBLIC_KEY env or fetch from GET decision-key.</summary>
        public string CentralApiPublicKey { get; init; } = string.Empty;
        /// <summary>Trusted Super Admin public key for TopUp dual-control. Set via SUPER_ADMIN_PUBLIC_KEY.</summary>
        public string SuperAdminPublicKey { get; init; } = string.Empty;
        public string NodeEndpoint { get; init; } = "http://localhost:5003";
        public TimeSpan BlockCreationInterval { get; init; } = TimeSpan.FromSeconds(2);
        public TimeSpan ValidationPollInterval { get; init; } = TimeSpan.FromSeconds(5);
        /// <summary>Block version written into new blocks (e.g. "1.0.0"). Set via BlockVersion in appsettings.</summary>
        public string BlockVersion { get; init; } = "1.0.0";

        public NodeConfig With(
            string? nodePublicKey = null,
            string? nodePrivateKey = null,
            Uri? centralEndpoint = null,
            string? centralApiPublicKey = null,
            string? superAdminPublicKey = null,
            string? nodeEndpoint = null,
            TimeSpan? blockCreationInterval = null,
            TimeSpan? validationPollInterval = null,
            string? blockVersion = null) => new()
        {
            NodePublicKey = nodePublicKey ?? NodePublicKey,
            NodePrivateKey = nodePrivateKey ?? NodePrivateKey,
            CentralEndpoint = centralEndpoint ?? CentralEndpoint,
            CentralApiPublicKey = centralApiPublicKey ?? CentralApiPublicKey,
            SuperAdminPublicKey = superAdminPublicKey ?? SuperAdminPublicKey,
            NodeEndpoint = nodeEndpoint ?? NodeEndpoint,
            BlockCreationInterval = blockCreationInterval ?? BlockCreationInterval,
            ValidationPollInterval = validationPollInterval ?? ValidationPollInterval,
            BlockVersion = blockVersion ?? BlockVersion
        };

        /// <summary>Public key of <paramref name="privateKeyHex"/>; throws if it is not the registered node key.</summary>
        public static string CheckNodeKey(string privateKeyHex, string registeredPublicKey)
        {
            string derived;
            try { derived = new NBitcoin.Key(Convert.FromHexString(privateKeyHex)).PubKey.ToHex(); }
            catch (Exception) { throw new InvalidOperationException("NODE_PRIVATE_KEY is not a 32-byte hex private key"); }
            var registered = string.IsNullOrWhiteSpace(registeredPublicKey) || registeredPublicKey == "NODE_PUBKEY_PLACEHOLDER"
                ? null : registeredPublicKey.Trim();
            if (registered != null && !string.Equals(derived, registered, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"NODE_PRIVATE_KEY belongs to {derived}, but node_info.json registers {registered}");
            return derived;
        }

        public static NodeConfig LoadDefaults()
        {
            // Always try node_info.json for keys first. NODE_ENDPOINT / CENTRAL_* only overlay.
            NodeConfig config = new NodeConfig();
            var nodeInfoPath = "node_info.json";
            if (File.Exists(nodeInfoPath))
            {
                try
                {
                    var json = File.ReadAllText(nodeInfoPath);
                    var nodeInfo = JsonSerializer.Deserialize<Services.NodeInfo>(json);
                    if (nodeInfo != null && !string.IsNullOrEmpty(nodeInfo.PublicKey))
                    {
                        config = config.With(
                            nodePublicKey: nodeInfo.PublicKey,
                            nodePrivateKey: nodeInfo.PrivateKey,
                            centralApiPublicKey: nodeInfo.CentralApiPublicKey ?? string.Empty,
                            nodeEndpoint: nodeInfo.Endpoint ?? "http://localhost:5003");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CONFIG] Error loading node_info.json: {ex.Message}");
                }
            }

            // Node signing key: NODE_PRIVATE_KEY (DPAPI value in %ProgramData%\Deltanium\<site>\env.dpapi.json,
            // decrypted at startup) wins over node_info.json, so no plaintext key has to stay on disk.
            var envNodeKey = Environment.GetEnvironmentVariable("NODE_PRIVATE_KEY");
            if (!string.IsNullOrWhiteSpace(envNodeKey))
                config = config.With(nodePublicKey: CheckNodeKey(envNodeKey.Trim(), config.NodePublicKey), nodePrivateKey: envNodeKey.Trim());

            var envEndpoint = Environment.GetEnvironmentVariable("NODE_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(envEndpoint))
                config = config.With(nodeEndpoint: envEndpoint.Trim());

            var centralPubKey = Environment.GetEnvironmentVariable("CENTRAL_API_PUBLIC_KEY");
            if (!string.IsNullOrWhiteSpace(centralPubKey))
                config = config.With(centralApiPublicKey: centralPubKey.Trim());

            var centralApiUrl = Environment.GetEnvironmentVariable("CENTRAL_API_URL");
            if (!string.IsNullOrWhiteSpace(centralApiUrl))
            {
                var uri = Uri.TryCreate(centralApiUrl.Trim(), UriKind.Absolute, out var u) &&
                          (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
                    ? u
                    : null;
                if (uri != null)
                    config = config.With(centralEndpoint: uri);
            }

            var superAdminPk = Environment.GetEnvironmentVariable("SUPER_ADMIN_PUBLIC_KEY");
            if (!string.IsNullOrWhiteSpace(superAdminPk))
                config = config.With(superAdminPublicKey: superAdminPk.Trim());

            return config;
        }
    }
}
