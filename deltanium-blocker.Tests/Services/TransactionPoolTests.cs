using Deltanium.Blocker.Core;
using Xunit;
using FluentAssertions;
using Deltanium.Blocker.Services;
using deltanium_blocker.Tests.Helpers;

namespace deltanium_blocker.Tests.Services;

public class TransactionPoolTests
{
    [Fact]
    public void TryAdd_ValidTransaction_ShouldReturnTrue()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var tx = TestDataBuilder.CreateTransaction();

        // Act
        var result = pool.TryAdd(tx);

        // Assert
        result.Should().BeTrue();
        pool.Count.Should().Be(1);
    }

    [Fact]
    public void TryAdd_InvalidTransaction_ShouldReturnFalse()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var tx = TestDataBuilder.CreateTransaction(from: string.Empty);

        // Act
        var result = pool.TryAdd(tx);

        // Assert
        result.Should().BeFalse();
        pool.Count.Should().Be(0);
    }

    [Fact]
    public void TryAdd_DuplicateTransaction_ShouldReturnFalse()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var tx = TestDataBuilder.CreateTransaction();

        // Act
        var result1 = pool.TryAdd(tx);
        var result2 = pool.TryAdd(tx);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeFalse();
        pool.Count.Should().Be(1);
    }

    [Fact]
    public void TryAdd_TransactionWithEmptyId_ShouldReturnFalse()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var tx = TestDataBuilder.CreateTransaction();
        // Create a transaction with empty ID by using reflection or creating a new one
        var invalidTx = tx with { Id = string.Empty };

        // Act
        var result = pool.TryAdd(invalidTx);

        // Assert
        result.Should().BeFalse();
        pool.Count.Should().Be(0);
    }

    [Fact]
    public void TryAdd_TransactionWithEmptyFrom_ShouldReturnFalse()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var tx = TestDataBuilder.CreateTransaction(from: string.Empty);

        // Act
        var result = pool.TryAdd(tx);

        // Assert
        result.Should().BeFalse();
        pool.Count.Should().Be(0);
    }

    [Fact]
    public void TryAdd_TransactionWithInvalidTimestamp_ShouldReturnFalse()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var tx = TestDataBuilder.CreateTransaction(timestamp: 0);

        // Act
        var result = pool.TryAdd(tx);

        // Assert
        result.Should().BeFalse();
        pool.Count.Should().Be(0);
    }

    [Fact]
    public void Snapshot_EmptyPool_ShouldReturnEmptyCollection()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());

        // Act
        var snapshot = pool.Snapshot();

        // Assert
        snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Snapshot_WithTransactions_ShouldReturnAllTransactions()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var tx1 = TestDataBuilder.CreateTransaction();
        var tx2 = TestDataBuilder.CreateTransaction();
        var tx3 = TestDataBuilder.CreateTransaction();

        pool.TryAdd(tx1);
        pool.TryAdd(tx2);
        pool.TryAdd(tx3);

        // Act
        var snapshot = pool.Snapshot();

        // Assert
        snapshot.Should().HaveCount(3);
        snapshot.Should().Contain(tx1);
        snapshot.Should().Contain(tx2);
        snapshot.Should().Contain(tx3);
    }

    [Fact]
    public void Snapshot_ShouldReturnReadOnlyCollection()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var tx = TestDataBuilder.CreateTransaction();
        pool.TryAdd(tx);

        // Act
        var snapshot = pool.Snapshot();

        // Assert
        snapshot.Should().BeAssignableTo<IReadOnlyCollection<Transaction>>();
    }

    [Fact]
    public void Remove_ExistingTransactions_ShouldRemoveThem()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var tx1 = TestDataBuilder.CreateTransaction();
        var tx2 = TestDataBuilder.CreateTransaction();
        var tx3 = TestDataBuilder.CreateTransaction();

        pool.TryAdd(tx1);
        pool.TryAdd(tx2);
        pool.TryAdd(tx3);

        // Act
        pool.Remove(new[] { tx1.Id, tx2.Id });

        // Assert
        pool.Count.Should().Be(1);
        pool.Snapshot().Should().Contain(tx3);
        pool.Snapshot().Should().NotContain(tx1);
        pool.Snapshot().Should().NotContain(tx2);
    }

    [Fact]
    public void Remove_NonExistentTransactions_ShouldNotThrow()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var tx = TestDataBuilder.CreateTransaction();
        pool.TryAdd(tx);

        // Act
        var action = () => pool.Remove(new[] { "non-existent-id-1", "non-existent-id-2" });

        // Assert
        action.Should().NotThrow();
        pool.Count.Should().Be(1);
    }

    [Fact]
    public void Count_ShouldReflectPoolSize()
    {
        // Arrange
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());

        // Act & Assert
        pool.Count.Should().Be(0);

        var tx1 = TestDataBuilder.CreateTransaction();
        pool.TryAdd(tx1);
        pool.Count.Should().Be(1);

        var tx2 = TestDataBuilder.CreateTransaction();
        pool.TryAdd(tx2);
        pool.Count.Should().Be(2);

        pool.Remove(new[] { tx1.Id });
        pool.Count.Should().Be(1);
    }
}







