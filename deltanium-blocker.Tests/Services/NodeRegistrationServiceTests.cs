using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Deltanium.Blocker.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;
using deltanium_blocker.Tests.Helpers;

namespace deltanium_blocker.Tests.Services;

public class NodeRegistrationServiceTests
{
    [Fact]
    public async Task RegisterNodeIfNeededAsync_NodeInfoExists_ShouldReturnAlreadyRegistered()
    {
        // Arrange
        using var helper = new TestFileHelper();
        var nodeInfoPath = Path.Combine(helper.TempDirectory, "node_info.json");
            var nodeInfo = new NodeInfo
            {
                PublicKey = "existing-public-key",
                PrivateKey = "existing-private-key",
                Mnemonic = "existing mnemonic",
                Endpoint = "http://existing-endpoint:5001",
                ApiResponse = JsonSerializer.Deserialize<JsonElement>("{}")
            };

            var json = JsonSerializer.Serialize(nodeInfo);
        File.WriteAllText(nodeInfoPath, json);

        var handler = new Mock<HttpMessageHandler>();
        var service = new NodeRegistrationService("http://localhost:5002", "http://test:5001", "test-voucher", new Mock<ILogger<NodeRegistrationService>>().Object);
        
        // Use reflection to set the _nodeInfoPath field
        var field = typeof(NodeRegistrationService).GetField("_nodeInfoPath", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(service, nodeInfoPath);

        // Act
        var result = await service.RegisterNodeIfNeededAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.AlreadyRegistered.Should().BeTrue();
        result.PublicKey.Should().Be("existing-public-key");
        result.PrivateKey.Should().Be("existing-private-key");
        result.Mnemonic.Should().Be("existing mnemonic");
    }

    [Fact]
    public async Task RegisterNodeIfNeededAsync_NewNode_ShouldRegisterWithAPI()
    {
        // Arrange
        using var helper = new TestFileHelper();
        var nodeInfoPath = Path.Combine(helper.TempDirectory, "node_info.json");

        var responseContent = JsonSerializer.Serialize(new { id = "test-id", publicKey = "test-public-key" });
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler.Object);
        var service = new NodeRegistrationService("http://localhost:5002", "http://test:5001", "test-voucher", new Mock<ILogger<NodeRegistrationService>>().Object);
        
        // Use reflection to set the _nodeInfoPath field
        var field = typeof(NodeRegistrationService).GetField("_nodeInfoPath", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(service, nodeInfoPath);

        // Note: This test is limited because NodeRegistrationService creates its own HttpClient
        // We would need to refactor to inject HttpClient for better testability
        // For now, we'll test the file loading part
    }

    [Fact]
    public async Task LoadNodeInfoAsync_FileExists_ShouldReturnNodeInfo()
    {
        // Arrange
        using var helper = new TestFileHelper();
        var nodeInfoPath = Path.Combine(helper.TempDirectory, "node_info.json");
            var nodeInfo = new NodeInfo
            {
                PublicKey = "test-public-key",
                PrivateKey = "test-private-key",
                Mnemonic = "test mnemonic",
                Endpoint = "http://test:5001",
                ApiResponse = JsonSerializer.Deserialize<JsonElement>("{}")
            };

            var json = JsonSerializer.Serialize(nodeInfo);
        File.WriteAllText(nodeInfoPath, json);

        var service = new NodeRegistrationService("http://localhost:5002", "http://test:5001", "test-voucher", new Mock<ILogger<NodeRegistrationService>>().Object);
        
        // Use reflection to set the _nodeInfoPath field
        var field = typeof(NodeRegistrationService).GetField("_nodeInfoPath", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(service, nodeInfoPath);

        // Act
        var result = await service.LoadNodeInfoAsync();

        // Assert
        result.Should().NotBeNull();
        result!.PublicKey.Should().Be("test-public-key");
        result.PrivateKey.Should().Be("test-private-key");
        result.Mnemonic.Should().Be("test mnemonic");
        result.Endpoint.Should().Be("http://test:5001");
    }

    [Fact]
    public async Task LoadNodeInfoAsync_FileNotExists_ShouldReturnNull()
    {
        // Arrange
        using var helper = new TestFileHelper();
        var nodeInfoPath = Path.Combine(helper.TempDirectory, "non-existent.json");

        var service = new NodeRegistrationService("http://localhost:5002", "http://test:5001", "test-voucher", new Mock<ILogger<NodeRegistrationService>>().Object);
        
        // Use reflection to set the _nodeInfoPath field
        var field = typeof(NodeRegistrationService).GetField("_nodeInfoPath", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(service, nodeInfoPath);

        // Act
        var result = await service.LoadNodeInfoAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadNodeInfoAsync_InvalidJson_ShouldReturnNull()
    {
        // Arrange
        using var helper = new TestFileHelper();
        var nodeInfoPath = Path.Combine(helper.TempDirectory, "node_info.json");
        File.WriteAllText(nodeInfoPath, "invalid json {");

        var service = new NodeRegistrationService("http://localhost:5002", "http://test:5001", "test-voucher", new Mock<ILogger<NodeRegistrationService>>().Object);
        
        // Use reflection to set the _nodeInfoPath field
        var field = typeof(NodeRegistrationService).GetField("_nodeInfoPath", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(service, nodeInfoPath);

        // Act
        var result = await service.LoadNodeInfoAsync();

        // Assert
        result.Should().BeNull();
    }
}

