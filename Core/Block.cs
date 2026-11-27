using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Deltanium.Blocker.Core
{
    public record Block
    {
        public string Id { get; init; } = string.Empty;
        public string PreviousId { get; init; } = string.Empty;
        public long Height { get; init; }
        public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public List<Transaction> Transactions { get; init; } = new();
        public string MerkleRoot { get; init; } = string.Empty;
        public string CreatorPublicKey { get; init; } = string.Empty;
        public string CentralAuthorizationSignature { get; init; } = string.Empty;
        public string CentralApiPublicKey { get; init; } = string.Empty;
        public string CreatorSignature { get; init; } = string.Empty;
        /// <summary>Block format / node version that created this block (e.g. "1.0.0").</summary>
        public string Version { get; init; } = "1.0.0";

        /// <summary>TOKENOMICS_V2 §11: Central wallet ledger anchor committed by this block (0/empty = none).</summary>
        public long LedgerAnchorSeq { get; init; }
        public string LedgerRoot { get; init; } = string.Empty;
        public string BalancesRoot { get; init; } = string.Empty;

        /// <summary>Balances replayed from the chain's LedgerRows after this block: last anchor window applied and the
        /// Merkle root of every non-zero balance (empty on blocks made before the chain ledger).</summary>
        public long StateSeq { get; init; }
        public string StateRoot { get; init; } = string.Empty;

        /// <summary>Preimage of the block id. Anchor fields are appended only when present (pre-anchor blocks keep their ids).</summary>
        public static string IdPayload(Block b)
        {
            var s = $"{b.PreviousId}:{b.Height}:{b.Timestamp}:{b.MerkleRoot}:{b.CreatorPublicKey}";
            if (!string.IsNullOrEmpty(b.LedgerRoot))
                s += $":{b.LedgerAnchorSeq}:{b.LedgerRoot}:{b.BalancesRoot}";
            if (!string.IsNullOrEmpty(b.StateRoot))
                s += $":state:{b.StateSeq}:{b.StateRoot}";
            return s;
        }

        public static string CalculateMerkleRoot(IEnumerable<Transaction> txs)
        {
            var hashes = txs.Select(tx => Hash(tx.Id)).ToList();
            if (!hashes.Any()) return string.Empty;
            while (hashes.Count > 1)
            {
                var next = new List<string>();
                for (int i = 0; i < hashes.Count; i += 2)
                {
                    if (i + 1 < hashes.Count)
                        next.Add(Hash(hashes[i] + hashes[i + 1]));
                    else
                        next.Add(hashes[i]);
                }
                hashes = next;
            }
            return hashes[0];
        }

        public static string Hash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}












