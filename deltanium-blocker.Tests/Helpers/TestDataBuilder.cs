using Deltanium.Blocker.Core;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Transaction = Deltanium.Blocker.Core.Transaction;
using Block = Deltanium.Blocker.Core.Block;

namespace deltanium_blocker.Tests.Helpers;

public static class TestDataBuilder
{
    /// <summary>Creates a mock logger for use in tests (e.g. TransactionPool, BlockBuilder, NodeHost).</summary>
    public static ILogger<T> CreateLogger<T>() => new Mock<ILogger<T>>().Object;

    /// <summary>Unique temp path for mempool so tests don't share state (TransactionPool loads from file).</summary>
    public static string CreateTempMempoolPath() => Path.Combine(Path.GetTempPath(), "test_mempool_" + Guid.NewGuid().ToString("N") + ".json");

    /// <summary>Unique temp path for blocks so tests don't share state (NodeState loads from file).</summary>
    public static string CreateTempBlocksPath() => Path.Combine(Path.GetTempPath(), "test_blocks_" + Guid.NewGuid().ToString("N") + ".json");

    public static Transaction CreateTransaction(
        TransactionType type = TransactionType.UserRegistration,
        string? from = null,
        string? to = null,
        Dictionary<string, object>? data = null,
        long? timestamp = null,
        long fee = 0,
        string? signature = null)
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var hdRoot = mnemonic.DeriveExtKey();
        var defaultFrom = hdRoot.PrivateKey.PubKey.ToHex();

        // If timestamp is null, use current time. If explicitly set (including 0), use that value.
        var finalTimestamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        return new Transaction
        {
            Type = type,
            From = from ?? defaultFrom,
            To = to,
            Data = data ?? new Dictionary<string, object>(),
            Timestamp = finalTimestamp,
            Fee = fee,
            Signature = signature ?? "test-signature"
        };
    }

    public static Block CreateBlock(
        string? id = null,
        string? previousId = null,
        long height = 0,
        long timestamp = 0,
        List<Transaction>? transactions = null,
        string? merkleRoot = null,
        string? creatorPublicKey = null,
        string? centralAuthorizationSignature = null,
        string? creatorSignature = null)
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var hdRoot = mnemonic.DeriveExtKey();
        var defaultCreator = hdRoot.PrivateKey.PubKey.ToHex();

        var txs = transactions ?? new List<Transaction>();
        var calculatedMerkle = merkleRoot ?? Block.CalculateMerkleRoot(txs);
        var prevId = previousId ?? string.Empty;
        var ts = timestamp > 0 ? timestamp : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var creatorPk = creatorPublicKey ?? defaultCreator;

        var blockId = id ?? Block.Hash($"{prevId}:{height}:{ts}:{calculatedMerkle}:{creatorPk}");

        return new Block
        {
            Id = blockId,
            PreviousId = prevId,
            Height = height,
            Timestamp = ts,
            Transactions = txs,
            MerkleRoot = calculatedMerkle,
            CreatorPublicKey = creatorPk,
            CentralAuthorizationSignature = centralAuthorizationSignature ?? "central-auth-sig",
            CentralApiPublicKey = string.Empty,
            CreatorSignature = creatorSignature ?? string.Empty,
            Version = "1.0.0"
        };
    }

    public static Block CreateGenesisBlock()
    {
        return new Block
        {
            Id = "genesis",
            PreviousId = string.Empty,
            Height = 0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Transactions = new List<Transaction>(),
            MerkleRoot = string.Empty,
            CreatorPublicKey = "genesis",
            CentralAuthorizationSignature = string.Empty,
            CentralApiPublicKey = string.Empty,
            CreatorSignature = string.Empty,
            Version = "1.0.0"
        };
    }

    public static NodeConfig CreateNodeConfig(
        string? nodePublicKey = null,
        string? nodePrivateKey = null,
        string? centralEndpoint = null,
        string? nodeEndpoint = null)
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var hdRoot = mnemonic.DeriveExtKey();
        var defaultPublicKey = hdRoot.PrivateKey.PubKey.ToHex();
        var defaultPrivateKey = hdRoot.PrivateKey.ToHex();

        return new NodeConfig
        {
            NodePublicKey = nodePublicKey ?? defaultPublicKey,
            NodePrivateKey = nodePrivateKey ?? defaultPrivateKey,
            CentralEndpoint = centralEndpoint != null ? new Uri(centralEndpoint) : new Uri("http://localhost:5002"),
            NodeEndpoint = nodeEndpoint ?? "http://localhost:5001"
        };
    }
}

