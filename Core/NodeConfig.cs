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
        public string NodeEndpoint { get; init; } = "http://localhost:5003";
        public TimeSpan BlockCreationInterval { get; init; } = TimeSpan.FromSeconds(2);
        public TimeSpan ValidationPollInterval { get; init; } = TimeSpan.FromSeconds(5);
        /// <summary>Block version written into new blocks (e.g. "1.0.0"). Set via BlockVersion in appsettings.</summary>
        public string BlockVersion { get; init; } = "1.0.0";

        public static NodeConfig LoadDefaults()
        {
            NodeConfig config;
            var envEndpoint = Environment.GetEnvironmentVariable("NODE_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(envEndpoint))
            {
                config = new NodeConfig { NodeEndpoint = envEndpoint };
            }
            else
            {
                var nodeInfoPath = "node_info.json";
                if (File.Exists(nodeInfoPath))
                {
                    try
                    {
                        var json = File.ReadAllText(nodeInfoPath);
                        var nodeInfo = JsonSerializer.Deserialize<Services.NodeInfo>(json);
                        if (nodeInfo != null && !string.IsNullOrEmpty(nodeInfo.PublicKey))
                        {
                            config = new NodeConfig
                            {
                                NodePublicKey = nodeInfo.PublicKey,
                                NodePrivateKey = nodeInfo.PrivateKey,
                                CentralApiPublicKey = nodeInfo.CentralApiPublicKey ?? string.Empty,
                                NodeEndpoint = nodeInfo.Endpoint ?? "http://localhost:5003"
                            };
                        }
                        else
                            config = new NodeConfig();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CONFIG] Error loading node_info.json: {ex.Message}");
                        config = new NodeConfig();
                    }
                }
                else
                    config = new NodeConfig();
            }

            var centralPubKey = Environment.GetEnvironmentVariable("CENTRAL_API_PUBLIC_KEY");
            var centralApiUrl = Environment.GetEnvironmentVariable("CENTRAL_API_URL");
            if (!string.IsNullOrWhiteSpace(centralPubKey))
            {
                config = new NodeConfig
                {
                    NodePublicKey = config.NodePublicKey,
                    NodePrivateKey = config.NodePrivateKey,
                    CentralEndpoint = config.CentralEndpoint,
                    CentralApiPublicKey = centralPubKey.Trim(),
                    NodeEndpoint = config.NodeEndpoint,
                    BlockCreationInterval = config.BlockCreationInterval,
                    ValidationPollInterval = config.ValidationPollInterval,
                    BlockVersion = config.BlockVersion
                };
            }
            if (!string.IsNullOrWhiteSpace(centralApiUrl))
            {
                var uri = Uri.TryCreate(centralApiUrl.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) ? u : null;
                if (uri != null)
                {
                    config = new NodeConfig
                    {
                        NodePublicKey = config.NodePublicKey,
                        NodePrivateKey = config.NodePrivateKey,
                        CentralEndpoint = uri,
                        CentralApiPublicKey = config.CentralApiPublicKey,
                        NodeEndpoint = config.NodeEndpoint,
                        BlockCreationInterval = config.BlockCreationInterval,
                        ValidationPollInterval = config.ValidationPollInterval,
                        BlockVersion = config.BlockVersion
                    };
                }
            }
            return config;
        }
    }
}


