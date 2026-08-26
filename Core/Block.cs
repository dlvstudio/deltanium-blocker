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












