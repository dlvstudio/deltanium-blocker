using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deltanium.Blocker.Core
{
    /// <summary>
    /// Blocker's representation of a StorageContract.
    /// Used during StorageOperation transaction validation.
    /// Both parties sign ContractHash (SHA-256 of canonical JSON); MessageToSign is legacy.
    /// </summary>
    public record StorageContract(
        string ContractId,
        string ContractType, // "TimeFixed" or "OpenEnded"
        string AppPublicKey,
        string StorageNodePublicKey,
        long StartDateUnix,
        long? EndDateUnix,
        long TotalFileSize,
        decimal TotalFee,
        List<string> FileIds,
        string MessageToSign,
        string AppSignature,
        string StorageNodeSignature,
        long CreatedAtUnix,
        string Status = "Active",
        string ContractHash = ""
    )
    {
        public bool IsValidNow()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            if (Status != "Active")
                return false;
            
            if (now < StartDateUnix)
                return false;
            
            if (ContractType == "TimeFixed" && EndDateUnix.HasValue && now > EndDateUnix)
                return false;
            
            if (ContractType == "OpenEnded" && now > StartDateUnix + (365 * 24 * 60 * 60))
                return false;
            
            return true;
        }

        public bool CoversFileId(string fileId)
        {
            return FileIds.Contains(fileId);
        }

        public bool CoversFileSize(long fileSizeBytes)
        {
            return fileSizeBytes <= TotalFileSize;
        }

        /// <summary>Options for canonical JSON: compact, no indentation. Must match Store.</summary>
        private static readonly JsonSerializerOptions CanonicalJsonOptions = new() { WriteIndented = false };

        /// <summary>
        /// Build canonical JSON string (for hashing). Used by ComputeContractHash; exposed for debug logging.
        /// </summary>
        public static string BuildCanonicalJson(StorageContract c)
        {
            var feeString = c.TotalFee.ToString("F10", CultureInfo.InvariantCulture);
            var fileIds = c.FileIds?.ToList() ?? new List<string>();
            fileIds.Sort(StringComparer.Ordinal);
            var dict = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["appPublicKey"] = c.AppPublicKey ?? "",
                ["contractId"] = c.ContractId ?? "",
                ["contractType"] = c.ContractType ?? "",
                ["createdAtUnix"] = c.CreatedAtUnix,
                ["fileIds"] = fileIds,
                ["startDateUnix"] = c.StartDateUnix,
                ["status"] = c.Status ?? "Active",
                ["storageNodePublicKey"] = c.StorageNodePublicKey ?? "",
                ["totalFee"] = feeString,
                ["totalFileSize"] = c.TotalFileSize
            };
            if (c.EndDateUnix.HasValue)
                dict["endDateUnix"] = c.EndDateUnix.Value;
            return JsonSerializer.Serialize(dict, CanonicalJsonOptions);
        }

        /// <summary>
        /// Compute contract hash: SHA-256(utf8(canonicalJson)) as lowercase hex.
        /// Must match Store and App implementation (canonical JSON, alphabetical keys, totalFee F10, fileIds sorted).
        /// </summary>
        public static string ComputeContractHash(StorageContract c)
        {
            var json = BuildCanonicalJson(c);
            var bytes = Encoding.UTF8.GetBytes(json);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
