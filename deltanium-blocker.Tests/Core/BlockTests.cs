using Deltanium.Blocker.Core;
using Xunit;
using FluentAssertions;
using deltanium_blocker.Tests.Helpers;

namespace deltanium_blocker.Tests.Core;

public class BlockTests
{
    [Fact]
    public void CalculateMerkleRoot_EmptyTransactions_ShouldReturnEmptyString()
    {
        // Arrange
        var transactions = new List<Transaction>();

        // Act
        var result = Block.CalculateMerkleRoot(transactions);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void CalculateMerkleRoot_SingleTransaction_ShouldReturnHash()
    {
        // Arrange
        var tx = TestDataBuilder.CreateTransaction();
        var transactions = new List<Transaction> { tx };

        // Act
        var result = Block.CalculateMerkleRoot(transactions);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().HaveLength(64); // SHA256 hex string length
    }

    [Fact]
    public void CalculateMerkleRoot_MultipleTransactions_ShouldCalculateCorrectly()
    {
        // Arrange
        var tx1 = TestDataBuilder.CreateTransaction();
        var tx2 = TestDataBuilder.CreateTransaction();
        var tx3 = TestDataBuilder.CreateTransaction();
        var transactions = new List<Transaction> { tx1, tx2, tx3 };

        // Act
        var result = Block.CalculateMerkleRoot(transactions);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().HaveLength(64);
    }

    [Fact]
    public void CalculateMerkleRoot_OddNumberOfTransactions_ShouldHandleCorrectly()
    {
        // Arrange
        var transactions = new List<Transaction>
        {
            TestDataBuilder.CreateTransaction(),
            TestDataBuilder.CreateTransaction(),
            TestDataBuilder.CreateTransaction()
        };

        // Act
        var result = Block.CalculateMerkleRoot(transactions);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().HaveLength(64);
    }

    [Fact]
    public void CalculateMerkleRoot_SameTransactions_ShouldReturnSameMerkleRoot()
    {
        // Arrange
        var tx1 = TestDataBuilder.CreateTransaction();
        var tx2 = TestDataBuilder.CreateTransaction();
        var transactions1 = new List<Transaction> { tx1, tx2 };
        var transactions2 = new List<Transaction> { tx1, tx2 };

        // Act
        var result1 = Block.CalculateMerkleRoot(transactions1);
        var result2 = Block.CalculateMerkleRoot(transactions2);

        // Assert
        result1.Should().Be(result2);
    }

    [Fact]
    public void CalculateMerkleRoot_DifferentTransactions_ShouldReturnDifferentMerkleRoot()
    {
        // Arrange
        var tx1 = TestDataBuilder.CreateTransaction();
        var tx2 = TestDataBuilder.CreateTransaction();
        var tx3 = TestDataBuilder.CreateTransaction();
        var transactions1 = new List<Transaction> { tx1, tx2 };
        var transactions2 = new List<Transaction> { tx1, tx3 };

        // Act
        var result1 = Block.CalculateMerkleRoot(transactions1);
        var result2 = Block.CalculateMerkleRoot(transactions2);

        // Assert
        result1.Should().NotBe(result2);
    }

    [Fact]
    public void Hash_ValidInput_ShouldReturnHexString()
    {
        // Arrange
        var input = "test-input";

        // Act
        var result = Block.Hash(input);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().HaveLength(64);
        result.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Hash_DifferentInputs_ShouldReturnDifferentHashes()
    {
        // Arrange
        var input1 = "test-input-1";
        var input2 = "test-input-2";

        // Act
        var result1 = Block.Hash(input1);
        var result2 = Block.Hash(input2);

        // Assert
        result1.Should().NotBe(result2);
    }

    [Fact]
    public void Hash_EmptyInput_ShouldReturnHash()
    {
        // Arrange
        var input = string.Empty;

        // Act
        var result = Block.Hash(input);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().HaveLength(64);
    }

    [Fact]
    public void Hash_SameInput_ShouldReturnSameHash()
    {
        // Arrange
        var input = "test-input";

        // Act
        var result1 = Block.Hash(input);
        var result2 = Block.Hash(input);

        // Assert
        result1.Should().Be(result2);
    }
}







