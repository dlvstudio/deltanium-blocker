using System.Text.Json;
using Xunit;
using FluentAssertions;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Services;
using deltanium_blocker.Tests.Helpers;

namespace deltanium_blocker.Tests.Core;

public class NodeConfigTests
{
    [Fact]
    public void LoadDefaults_NoNodeInfoFile_ShouldReturnDefaults()
    {
        // Arrange
        using var helper = new TestFileHelper();
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(helper.TempDirectory);

            // Act
            var config = NodeConfig.LoadDefaults();

            // Assert
            config.NodePublicKey.Should().Be("NODE_PUBKEY_PLACEHOLDER");
            config.NodePrivateKey.Should().Be("NODE_PRIVKEY_PLACEHOLDER");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void LoadDefaults_WithNodeInfoFile_ShouldLoadKeys()
    {
        // Arrange
        using var helper = new TestFileHelper();
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(helper.TempDirectory);

            var nodeInfo = new NodeInfo
            {
                PublicKey = "test-public-key",
                PrivateKey = "test-private-key",
                Endpoint = "http://test-endpoint:5001",
                ApiResponse = JsonSerializer.Deserialize<JsonElement>("{}")
            };

            var json = JsonSerializer.Serialize(nodeInfo);
            File.WriteAllText("node_info.json", json);

            // Act
            var config = NodeConfig.LoadDefaults();

            // Assert
            config.NodePublicKey.Should().Be("test-public-key");
            config.NodePrivateKey.Should().Be("test-private-key");
            config.NodeEndpoint.Should().Be("http://test-endpoint:5001");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void LoadDefaults_InvalidNodeInfoFile_ShouldReturnDefaults()
    {
        // Arrange
        using var helper = new TestFileHelper();
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(helper.TempDirectory);
            File.WriteAllText("node_info.json", "invalid json {");

            // Act
            var config = NodeConfig.LoadDefaults();

            // Assert
            config.NodePublicKey.Should().Be("NODE_PUBKEY_PLACEHOLDER");
            config.NodePrivateKey.Should().Be("NODE_PRIVKEY_PLACEHOLDER");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void LoadDefaults_NodeInfoFileWithEmptyPublicKey_ShouldReturnDefaults()
    {
        // Arrange
        using var helper = new TestFileHelper();
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(helper.TempDirectory);

            var nodeInfo = new NodeInfo
            {
                PublicKey = string.Empty,
                PrivateKey = "test-private-key",
                ApiResponse = JsonSerializer.Deserialize<JsonElement>("{}")
            };

            var json = JsonSerializer.Serialize(nodeInfo);
            File.WriteAllText("node_info.json", json);

            // Act
            var config = NodeConfig.LoadDefaults();

            // Assert
            config.NodePublicKey.Should().Be("NODE_PUBKEY_PLACEHOLDER");
            config.NodePrivateKey.Should().Be("NODE_PRIVKEY_PLACEHOLDER");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }
}

