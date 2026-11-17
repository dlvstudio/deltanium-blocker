using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Deltanium.Blocker.Services.Ledger
{
    /// <summary>Same construction as Central's ledger anchors (TOKENOMICS_V2 §11).</summary>
    public static class LedgerMerkle
    {
        public static string Sha256Hex(string s) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

        public static string BalanceLeaf(string account, decimal balance) =>
            Sha256Hex($"{account}|{balance.ToString("F10", CultureInfo.InvariantCulture)}");

        public static string Root(IEnumerable<string> leaves)
        {
            var level = leaves.ToList();
            if (level.Count == 0) return Sha256Hex("");
            while (level.Count > 1)
            {
                var next = new List<string>();
                for (var i = 0; i < level.Count; i += 2)
                    next.Add(i + 1 < level.Count ? Sha256Hex(level[i] + level[i + 1]) : level[i]);
                level = next;
            }
            return level[0];
        }

        /// <summary>Must match Central's EconomicsService.LedgerLeaf.</summary>
        public static string LedgerLeaf(LedgerEntryDto e) => Sha256Hex(string.Join("|",
            e.Id, e.FromPublicKey ?? "", e.ToPublicKey ?? "",
            e.Amount.ToString("F10", CultureInfo.InvariantCulture), e.Kind, e.RefId ?? "",
            e.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture)));

        public static string RootFromPath(string leaf, IEnumerable<MerkleStep> path) =>
            path.Aggregate(leaf, (cur, s) => s.Side == "L" ? Sha256Hex(s.Hash + cur) : Sha256Hex(cur + s.Hash));
    }

    public class MerkleStep
    {
        public string Hash { get; set; } = "";
        public string Side { get; set; } = "R";
    }

    public class BalanceProofDto
    {
        public long Seq { get; set; }
        public string Account { get; set; } = "";
        public decimal Balance { get; set; }
        public bool Included { get; set; }
        public string Leaf { get; set; } = "";
        public List<MerkleStep> Path { get; set; } = new();
        public string Root { get; set; } = "";
    }

    /// <summary>One published wallet ledger row (GET /api/wallet/anchors/{seq}/entries).</summary>
    public class LedgerEntryDto
    {
        public string Id { get; set; } = "";
        public string? FromPublicKey { get; set; }
        public string? ToPublicKey { get; set; }
        public decimal Amount { get; set; }
        public string Kind { get; set; } = "";
        public string? RefId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AnchorDto
    {
        public long Seq { get; set; }
        public DateTime UpToInclusive { get; set; }
        public string LedgerRoot { get; set; } = "";
        public string BalancesRoot { get; set; } = "";

        public long UpToUnixMs => new DateTimeOffset(
            UpToInclusive.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(UpToInclusive, DateTimeKind.Utc) : UpToInclusive.ToUniversalTime())
            .ToUnixTimeMilliseconds();
    }
}
