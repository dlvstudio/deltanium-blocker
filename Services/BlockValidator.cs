using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using Deltanium.Blocker.Core;

namespace Deltanium.Blocker.Services
{
    public class BlockValidator
    {
        private readonly NodeConfig _config;
        private readonly ILogger<BlockValidator> _logger;

        private readonly Ledger.ChainLedgerService? _ledger;

        public BlockValidator(NodeConfig config, ILogger<BlockValidator> logger, Ledger.ChainLedgerService? ledger)
            : this(config, logger)
        {
            _ledger = ledger;
        }

        public BlockValidator(NodeConfig config, ILogger<BlockValidator> logger)
        {
            _config = config;
            _logger = logger;
        }

        public bool Validate(Block block, NodeState state, out string reason)
        {
            // Basic structural checks (MVP placeholder)
            if (string.IsNullOrWhiteSpace(block.Id))
            {
                reason = "Missing block id";
                _logger.LogWarning($"Validation failed for block (ID unknown): {reason}");
                return false;
            }

            if (block.Height != state.Height + 1 && block.Height != 0)
            {
                reason = "Unexpected block height";
                _logger.LogWarning($"Validation failed for block {block.Id}: {reason}. Expected height {state.Height + 1}, got {block.Height}");
                return false;
            }

            var expectedMerkle = Block.CalculateMerkleRoot(block.Transactions);
            if (!string.Equals(expectedMerkle, block.MerkleRoot, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Invalid merkle root";
                _logger.LogWarning($"Validation failed for block {block.Id}: {reason}. Expected {expectedMerkle}, got {block.MerkleRoot}");
                return false;
            }

            if (block.Transactions.Any(tx => tx.Timestamp <= 0))
            {
                reason = "Transaction timestamp invalid";
                _logger.LogWarning($"Validation failed for block {block.Id}: {reason}");
                return false;
            }

            var badVersion = block.Transactions.FirstOrDefault(tx => (tx.Versions ?? new()).Any(v => !v.IsValidFor(tx.Id)));
            if (badVersion != null)
            {
                reason = "Invalid version signature";
                _logger.LogWarning($"Validation failed for block {block.Id}: {reason} on tx {badVersion.Id}");
                return false;
            }

            if (_ledger != null && !string.IsNullOrEmpty(block.StateRoot))
            {
                var (seq, root) = _ledger.Preview(block);
                if (seq != block.StateSeq || !string.Equals(root, block.StateRoot, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "State root mismatch";
                    _logger.LogWarning($"Validation failed for block {block.Id}: {reason}. Expected {seq}:{root}, got {block.StateSeq}:{block.StateRoot}");
                    return false;
                }
            }
            var expectedBlockId = Block.Hash(Block.IdPayload(block));
            if (!string.Equals(expectedBlockId, block.Id, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Block id mismatch";
                _logger.LogWarning($"Validation failed for block: {reason}. Expected {expectedBlockId}, got {block.Id}");
                return false;
            }

            if (block.Height > 0 && !string.IsNullOrWhiteSpace(block.CreatorPublicKey) && !string.IsNullOrWhiteSpace(block.CreatorSignature))
            {
                if (!SignatureVerifier.Verify(block.CreatorPublicKey, block.Id, block.CreatorSignature))
                {
                    reason = "Invalid creator signature";
                    _logger.LogWarning($"Validation failed for block {block.Id}: {reason}");
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }
    }
}












