using Deltanium.Blocker.Core;
using Xunit;
using FluentAssertions;
using Deltanium.Blocker.Services;
using deltanium_blocker.Tests.Helpers;

namespace deltanium_blocker.Tests.Services;

public class BlockBuilderTests
{
    [Fact]
    public void BuildBlock_WithAuthorization_ShouldCreateBlock()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        // Act
        var block = builder.BuildBlock(auth);

        // Assert
        block.Should().NotBeNull();
        block.Id.Should().NotBeEmpty();
        block.CreatorPublicKey.Should().Be(config.NodePublicKey);
        block.CentralAuthorizationSignature.Should().Be("test-auth-signature");
        block.CreatorSignature.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildBlock_EmptyPool_ShouldCreateEmptyBlock()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        // Act
        var block = builder.BuildBlock(auth);

        // Assert
        block.Transactions.Should().BeEmpty();
        block.MerkleRoot.Should().BeEmpty();
    }

    [Fact]
    public void BuildBlock_WithTransactions_ShouldIncludeTransactions()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        var tx1 = TestDataBuilder.CreateTransaction();
        var tx2 = TestDataBuilder.CreateTransaction();
        pool.TryAdd(tx1);
        pool.TryAdd(tx2);

        // Act
        var block = builder.BuildBlock(auth);

        // Assert
        block.Transactions.Should().HaveCount(2);
        block.Transactions.Should().Contain(tx1);
        block.Transactions.Should().Contain(tx2);
    }

    [Fact]
    public void BuildBlock_ShouldSetPreviousBlockId()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        var genesisBlock = TestDataBuilder.CreateGenesisBlock();
        state.AddBlock(genesisBlock);
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        // Act
        var block = builder.BuildBlock(auth);

        // Assert
        block.PreviousId.Should().Be(genesisBlock.Id);
    }

    [Fact]
    public void BuildBlock_ShouldIncrementHeight()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        var genesisBlock = TestDataBuilder.CreateGenesisBlock();
        state.AddBlock(genesisBlock);
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        // Act
        var block = builder.BuildBlock(auth);

        // Assert
        block.Height.Should().Be(1);
    }

    [Fact]
    public void BuildBlock_ShouldCalculateMerkleRoot()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        var tx1 = TestDataBuilder.CreateTransaction();
        var tx2 = TestDataBuilder.CreateTransaction();
        pool.TryAdd(tx1);
        pool.TryAdd(tx2);

        // Act
        var block = builder.BuildBlock(auth);

        // Assert
        var expectedMerkle = Block.CalculateMerkleRoot(block.Transactions);
        block.MerkleRoot.Should().Be(expectedMerkle);
    }

    [Fact]
    public void BuildBlock_ShouldSetCreatorPublicKey()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        // Act
        var block = builder.BuildBlock(auth);

        // Assert
        block.CreatorPublicKey.Should().Be(config.NodePublicKey);
    }

    [Fact]
    public void BuildBlock_ShouldSetCentralAuthorizationSignature()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        // Act
        var block = builder.BuildBlock(auth);

        // Assert
        block.CentralAuthorizationSignature.Should().Be("test-auth-signature");
    }

    [Fact]
    public void BuildBlock_ShouldGenerateBlockId()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        // Act
        var block = builder.BuildBlock(auth);

        // Assert
        block.Id.Should().NotBeEmpty();
        block.Id.Should().HaveLength(64); // SHA256 hex string
    }

    [Fact]
    public void BuildBlock_ShouldSignBlock()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        // Act
        var block = builder.BuildBlock(auth);

        // Assert (CreatorSignature is ECDSA base64, not mock "signed(...)")
        block.CreatorSignature.Should().NotBeEmpty();
        block.CreatorSignature.Length.Should().BeGreaterThan(20);
    }

    [Fact]
    public void BuildBlock_TransactionsOrderedByFee_ShouldPrioritizeHighFee()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        var tx1 = TestDataBuilder.CreateTransaction(fee: 10);
        var tx2 = TestDataBuilder.CreateTransaction(fee: 100);
        var tx3 = TestDataBuilder.CreateTransaction(fee: 50);
        pool.TryAdd(tx1);
        pool.TryAdd(tx2);
        pool.TryAdd(tx3);

        // Act
        var block = builder.BuildBlock(auth);

        // Assert
        block.Transactions.Should().HaveCount(3);
        block.Transactions[0].Fee.Should().Be(100); // Highest fee first
        block.Transactions[1].Fee.Should().Be(50);
        block.Transactions[2].Fee.Should().Be(10);
    }

    [Fact]
    public void BuildBlock_MoreThan100Transactions_ShouldCapAt100()
    {
        // Arrange
        var config = TestDataBuilder.CreateNodeConfig();
        var pool = new TransactionPool(TestDataBuilder.CreateTempMempoolPath(), TestDataBuilder.CreateLogger<TransactionPool>());
        var state = new NodeState(TestDataBuilder.CreateTempBlocksPath());
        state.AddBlock(TestDataBuilder.CreateGenesisBlock());
        var builder = new BlockBuilder(config, pool, state, TestDataBuilder.CreateLogger<BlockBuilder>());
        var auth = new CentralAuthorization("test-auth-signature", "TEST_CENTRAL_PUBKEY_PLACEHOLDER");

        // Add 150 transactions
        for (int i = 0; i < 150; i++)
        {
            var tx = TestDataBuilder.CreateTransaction(fee: i);
            pool.TryAdd(tx);
        }

        // Act
        var block = builder.BuildBlock(auth);

        // Assert
        block.Transactions.Should().HaveCount(100);
    }
}







