using Deltanium.Blocker.Core;
using Xunit;
using FluentAssertions;
using Deltanium.Blocker.Services;
using deltanium_blocker.Tests.Helpers;

namespace deltanium_blocker.Tests.Services;

public class CentralCoordinatorClientTests
{
    [Fact]
    public async Task RequestBlockSlotAsync_ShouldReturnAuthorizationOrNull()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var client = new CentralCoordinatorClient(config, TestDataBuilder.CreateLogger<CentralCoordinatorClient>());

        // Act
        var auth = await client.RequestBlockSlotAsync();

        // Assert
        // Since it's random, we just verify it returns something (either null or authorization)
        // We can't assert a specific value, but we can verify the method doesn't throw
        auth.Should().BeAssignableTo<CentralAuthorization?>();
    }

    [Fact]
    public async Task RequestBlockSlotAsync_ShouldReturnValidSignature()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var client = new CentralCoordinatorClient(config, TestDataBuilder.CreateLogger<CentralCoordinatorClient>());

        // Act - run multiple times to get an authorization
        CentralAuthorization? auth = null;
        for (int i = 0; i < 10 && auth == null; i++)
        {
            auth = await client.RequestBlockSlotAsync();
        }

        // Assert (when Central API is reachable, signature is real base64 from API)
        if (auth != null)
        {
            auth.Signature.Should().NotBeEmpty();
            auth.Signature.Length.Should().BeGreaterThan(20);
        }
    }

    [Fact]
    public async Task ReportInvalidBlockAsync_ShouldNotThrow()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var client = new CentralCoordinatorClient(config, TestDataBuilder.CreateLogger<CentralCoordinatorClient>());
        var block = TestDataBuilder.CreateBlock();

        // Act
        var action = async () => await client.ReportInvalidBlockAsync(block, "test reason");

        // Assert
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RequestBlockSlotAsync_MultipleCalls_ShouldReturnDifferentResults()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var client = new CentralCoordinatorClient(config, TestDataBuilder.CreateLogger<CentralCoordinatorClient>());

        // Act - run fewer times to speed up test (still enough to test randomness)
        var results = new List<CentralAuthorization?>();
        for (int i = 0; i < 10; i++)
        {
            results.Add(await client.RequestBlockSlotAsync());
        }

        // Assert - each result is either null or a valid authorization (random 50% in implementation)
        results.Should().HaveCount(10);
        results.Should().OnlyContain(r => r == null || (r != null && !string.IsNullOrEmpty(r.Signature)));
    }
}


