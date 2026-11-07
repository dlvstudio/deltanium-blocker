using Deltanium.Blocker.Core;
using Xunit;
using FluentAssertions;
using deltanium_blocker.Tests.Helpers;

namespace deltanium_blocker.Tests.Core;

public class NodeStateTests
{
    [Fact]
    public void AddBlock_FirstBlock_ShouldSetHeightToZero()
    {
        // Arrange
        var state = new NodeState(TestDataBuilder.CreateTempBlockStore());
        var block = TestDataBuilder.CreateBlock(height: 0);

        // Act
        state.AddBlock(block);

        // Assert
        state.Height.Should().Be(0);
        state.LatestBlockId.Should().Be(block.Id);
    }

    [Fact]
    public void AddBlock_MultipleBlocks_ShouldIncrementHeight()
    {
        // Arrange
        var state = new NodeState(TestDataBuilder.CreateTempBlockStore());
        var block1 = TestDataBuilder.CreateBlock(height: 0);
        var block2 = TestDataBuilder.CreateBlock(height: 1, previousId: block1.Id);
        var block3 = TestDataBuilder.CreateBlock(height: 2, previousId: block2.Id);

        // Act
        state.AddBlock(block1);
        state.AddBlock(block2);
        state.AddBlock(block3);

        // Assert
        state.Height.Should().Be(2);
        state.LatestBlockId.Should().Be(block3.Id);
    }

    [Fact]
    public void LatestBlockId_EmptyChain_ShouldReturnEmptyString()
    {
        // Arrange
        var state = new NodeState(TestDataBuilder.CreateTempBlockStore());

        // Act & Assert
        state.LatestBlockId.Should().BeEmpty();
    }

    [Fact]
    public void LatestBlockId_WithBlocks_ShouldReturnLastBlockId()
    {
        // Arrange
        var state = new NodeState(TestDataBuilder.CreateTempBlockStore());
        var block1 = TestDataBuilder.CreateBlock(height: 0);
        var block2 = TestDataBuilder.CreateBlock(height: 1, previousId: block1.Id);

        // Act
        state.AddBlock(block1);
        state.AddBlock(block2);

        // Assert
        state.LatestBlockId.Should().Be(block2.Id);
    }

    [Fact]
    public void Height_EmptyChain_ShouldReturnMinusOne()
    {
        // Arrange
        var state = new NodeState(TestDataBuilder.CreateTempBlockStore());

        // Act & Assert
        state.Height.Should().Be(-1);
    }

    [Fact]
    public void Height_WithBlocks_ShouldReturnLastHeight()
    {
        // Arrange
        var state = new NodeState(TestDataBuilder.CreateTempBlockStore());
        var block1 = TestDataBuilder.CreateBlock(height: 0);
        var block2 = TestDataBuilder.CreateBlock(height: 1, previousId: block1.Id);

        // Act
        state.AddBlock(block1);
        state.AddBlock(block2);

        // Assert
        state.Height.Should().Be(1);
    }

    [Fact]
    public void Chain_ShouldReturnReadOnlyList()
    {
        // Arrange
        var state = new NodeState(TestDataBuilder.CreateTempBlockStore());
        var block1 = TestDataBuilder.CreateBlock(height: 0);
        var block2 = TestDataBuilder.CreateBlock(height: 1, previousId: block1.Id);

        // Act
        state.AddBlock(block1);
        state.AddBlock(block2);

        // Assert
        state.Chain.Should().HaveCount(2);
        state.Chain[0].Id.Should().Be(block1.Id);
        state.Chain[1].Id.Should().Be(block2.Id);
    }
}







