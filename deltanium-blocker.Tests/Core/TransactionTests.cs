using Deltanium.Blocker.Core;
using Xunit;
using FluentAssertions;
using deltanium_blocker.Tests.Helpers;

namespace deltanium_blocker.Tests.Core;

public class TransactionTests
{
    [Fact]
    public void Transaction_DefaultValues_ShouldBeValid()
    {
        // Arrange & Act
        var tx = TestDataBuilder.CreateTransaction();

        // Assert
        tx.Id.Should().NotBeEmpty();
        tx.Type.Should().Be(TransactionType.UserRegistration);
        tx.From.Should().NotBeEmpty();
        tx.Timestamp.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Transaction_WithData_ShouldPreserveData()
    {
        // Arrange
        var data = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", 123 }
        };

        // Act
        var tx = TestDataBuilder.CreateTransaction(data: data);

        // Assert
        tx.Data.Should().HaveCount(2);
        tx.Data["key1"].Should().Be("value1");
        tx.Data["key2"].Should().Be(123);
    }

    [Fact]
    public void Transaction_DifferentTypes_ShouldHaveCorrectType()
    {
        // Arrange & Act
        var tx1 = TestDataBuilder.CreateTransaction(TransactionType.UserRegistration);
        var tx2 = TestDataBuilder.CreateTransaction(TransactionType.Follow);
        var tx3 = TestDataBuilder.CreateTransaction(TransactionType.CreatePost);

        // Assert
        tx1.Type.Should().Be(TransactionType.UserRegistration);
        tx2.Type.Should().Be(TransactionType.Follow);
        tx3.Type.Should().Be(TransactionType.CreatePost);
    }
}







