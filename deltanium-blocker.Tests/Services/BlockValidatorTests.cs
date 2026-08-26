using Deltanium.Blocker.Core;
using Xunit;
using FluentAssertions;
using Deltanium.Blocker.Services;
using deltanium_blocker.Tests.Helpers;

namespace deltanium_blocker.Tests.Services;

public class BlockValidatorTests
{
    [Fact]
    public void Validate_ValidBlock_ShouldReturnTrue()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var validator = new BlockValidator(config, TestDataBuilder.CreateLogger<BlockValidator>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());

        var tx = TestDataBuilder.CreateTransaction();
        var block = TestDataBuilder.CreateBlock(
            height: 1,
            previousId: state.LatestBlockId,
            transactions: new List<Transaction> { tx }
        );

        // Act
        var result = validator.Validate(block, state, out var reason);

        // Assert
        result.Should().BeTrue();
        reason.Should().BeEmpty();
    }

    [Fact]
    public void Validate_BlockWithEmptyId_ShouldReturnFalse()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var validator = new BlockValidator(config, TestDataBuilder.CreateLogger<BlockValidator>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());

        var block = TestDataBuilder.CreateBlock(
            id: string.Empty,
            height: 1,
            previousId: state.LatestBlockId
        );

        // Act
        var result = validator.Validate(block, state, out var reason);

        // Assert
        result.Should().BeFalse();
        reason.Should().Be("Missing block id");
    }

    [Fact]
    public void Validate_BlockWithWrongHeight_ShouldReturnFalse()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var validator = new BlockValidator(config, TestDataBuilder.CreateLogger<BlockValidator>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());

        var block = TestDataBuilder.CreateBlock(
            height: 5, // Wrong height, should be 1
            previousId: state.LatestBlockId
        );

        // Act
        var result = validator.Validate(block, state, out var reason);

        // Assert
        result.Should().BeFalse();
        reason.Should().Be("Unexpected block height");
    }

    [Fact]
    public void Validate_BlockWithInvalidMerkleRoot_ShouldReturnFalse()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var validator = new BlockValidator(config, TestDataBuilder.CreateLogger<BlockValidator>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());

        var tx = TestDataBuilder.CreateTransaction();
        var block = TestDataBuilder.CreateBlock(
            height: 1,
            previousId: state.LatestBlockId,
            transactions: new List<Transaction> { tx },
            merkleRoot: "invalid-merkle-root"
        );

        // Act
        var result = validator.Validate(block, state, out var reason);

        // Assert
        result.Should().BeFalse();
        reason.Should().Be("Invalid merkle root");
    }

    [Fact]
    public void Validate_BlockWithInvalidTransactionTimestamp_ShouldReturnFalse()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var validator = new BlockValidator(config, TestDataBuilder.CreateLogger<BlockValidator>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());

        var tx = TestDataBuilder.CreateTransaction(timestamp: 0); // Invalid timestamp
        var block = TestDataBuilder.CreateBlock(
            height: 1,
            previousId: state.LatestBlockId,
            transactions: new List<Transaction> { tx }
        );

        // Act
        var result = validator.Validate(block, state, out var reason);

        // Assert
        result.Should().BeFalse();
        reason.Should().Be("Transaction timestamp invalid");
    }

    [Fact]
    public void Validate_GenesisBlock_ShouldReturnTrue()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var validator = new BlockValidator(config, TestDataBuilder.CreateLogger<BlockValidator>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());

        var genesisBlock = TestDataBuilder.CreateGenesisBlock();

        // Act
        var result = validator.Validate(genesisBlock, state, out var reason);

        // Assert
        result.Should().BeTrue();
        reason.Should().BeEmpty();
    }

    [Fact]
    public void Validate_BlockWithEmptyTransactions_ShouldReturnTrue()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var validator = new BlockValidator(config, TestDataBuilder.CreateLogger<BlockValidator>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());

        var block = TestDataBuilder.CreateBlock(
            height: 1,
            previousId: state.LatestBlockId,
            transactions: new List<Transaction>()
        );

        // Act
        var result = validator.Validate(block, state, out var reason);

        // Assert
        result.Should().BeTrue();
        reason.Should().BeEmpty();
    }

    [Fact]
    public void Validate_BlockWithMultipleTransactions_ShouldValidateAll()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var validator = new BlockValidator(config, TestDataBuilder.CreateLogger<BlockValidator>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());

        var tx1 = TestDataBuilder.CreateTransaction();
        var tx2 = TestDataBuilder.CreateTransaction();
        var tx3 = TestDataBuilder.CreateTransaction();
        var block = TestDataBuilder.CreateBlock(
            height: 1,
            previousId: state.LatestBlockId,
            transactions: new List<Transaction> { tx1, tx2, tx3 }
        );

        // Act
        var result = validator.Validate(block, state, out var reason);

        // Assert
        result.Should().BeTrue();
        reason.Should().BeEmpty();
    }
}







