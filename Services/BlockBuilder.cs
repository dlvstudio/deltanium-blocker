using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Deltanium.Blocker.Core;
using NBitcoin;
using CoreBlock = Deltanium.Blocker.Core.Block;

namespace Deltanium.Blocker.Services
{
    public class BlockBuilder
    {
        private readonly NodeConfig _config;
        private readonly TransactionPool _pool;
        private readonly NodeState _state;
        private readonly ILogger<BlockBuilder> _logger;

        public BlockBuilder(NodeConfig config, TransactionPool pool, NodeState state, ILogger<BlockBuilder> logger)
        {
            _config = config;
            _pool = pool;
            _state = state;
            _logger = logger;
        }

        public CoreBlock BuildBlock(CentralAuthorization auth)
        {
            var txs = _pool.Snapshot()
                .OrderByDescending(t => t.Fee)
                .ThenBy(t => t.Timestamp)
                .Take(100) // simple cap
                .ToList();

            var previous = _state.LatestBlockId;
            var height = _state.Height + 1;
            var merkle = CoreBlock.CalculateMerkleRoot(txs);

            var block = new CoreBlock
            {
                PreviousId = previous,
                Height = height,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Transactions = txs,
                MerkleRoot = merkle,
                CreatorPublicKey = _config.NodePublicKey,
                CentralAuthorizationSignature = auth.Signature,
                CentralApiPublicKey = !string.IsNullOrWhiteSpace(auth.CentralPublicKey) ? auth.CentralPublicKey : (_config.CentralApiPublicKey ?? string.Empty),
                Version = string.IsNullOrWhiteSpace(_config.BlockVersion) ? "1.0.0" : _config.BlockVersion.Trim()
            };

            var blockId = CoreBlock.Hash($"{block.PreviousId}:{block.Height}:{block.Timestamp}:{block.MerkleRoot}:{block.CreatorPublicKey}");
            block = block with
            {
                Id = blockId,
                CreatorSignature = Sign(blockId)
            };

            _logger.LogInformation($"Built block {block.Id} at height {block.Height} with {block.Transactions.Count} transactions.");

            return block;
        }

        private string Sign(string payload)
        {
            try
            {
                var key = new Key(Convert.FromHexString(_config.NodePrivateKey));
                using var sha256 = SHA256.Create();
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var sig = key.Sign(new uint256(hashBytes));
                var der = sig.ToDER();
                return Convert.ToBase64String(der);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sign block payload.");
                throw;
            }
        }
    }

    public record CentralAuthorization(string Signature, string CentralPublicKey);
}












