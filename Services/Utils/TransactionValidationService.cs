using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Core.Models;
using Deltanium.Blocker.Services;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Deltanium.Blocker.Services.Utils
{
    public class TransactionValidationService
    {
        private readonly ILogger<TransactionValidationService> _logger;
        private readonly string? _trustedCentralPublicKey;
        private readonly string? _trustedSuperAdminPublicKey;

        public TransactionValidationService(
            ILogger<TransactionValidationService> logger,
            string? trustedCentralPublicKey = null,
            string? trustedSuperAdminPublicKey = null)
        {
            _logger = logger;
            _trustedCentralPublicKey = string.IsNullOrWhiteSpace(trustedCentralPublicKey) ? null : trustedCentralPublicKey.Trim();
            _trustedSuperAdminPublicKey = string.IsNullOrWhiteSpace(trustedSuperAdminPublicKey) ? null : trustedSuperAdminPublicKey.Trim();
        }

        public bool ValidateStorageOperation(BlockerTxRequest req, out string error)
        {
            error = string.Empty;
            if (req.Data.ValueKind == JsonValueKind.Undefined || req.Data.ValueKind == JsonValueKind.Null)
            {
                error = "Missing StorageOperation data";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }

            if (!req.Data.TryGetProperty("contract", out var contractElement))
            {
                error = "Missing contract in StorageOperation data";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }

            // New envelope format: { hash, appSignature, storageNodeSignature, content }
            if (contractElement.TryGetProperty("content", out var contentElement))
            {
                return ValidateEnvelopeContract(req, contractElement, contentElement, out error);
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };
            StorageContract? contract;
            try
            {
                contract = JsonSerializer.Deserialize<StorageContract>(contractElement.GetRawText(), options);
            }
            catch (Exception ex)
            {
                error = $"Invalid contract payload: {ex.Message}";
                _logger.LogError(ex, "Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }

            if (contract == null)
            {
                error = "Invalid contract payload";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }

            if (!string.Equals(req.From, contract.AppPublicKey, StringComparison.OrdinalIgnoreCase))
            {
                error = "StorageOperation from must match contract app public key";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }

            if (!string.Equals(req.To ?? string.Empty, contract.StorageNodePublicKey, StringComparison.OrdinalIgnoreCase))
            {
                error = "StorageOperation to must match contract storage node public key";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }

            if (!contract.IsValidNow())
            {
                error = "Contract is not active or valid now";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }

            // New path: sign on contract hash (V2)
            if (!string.IsNullOrWhiteSpace(contract.ContractHash))
            {
                if (!SignatureVerifier.Verify(contract.AppPublicKey, contract.ContractHash, contract.AppSignature))
                {
                    error = "Invalid contract app signature";
                    _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                    return false;
                }
                return true;
            }

            // Legacy path: sign on message (messageToSign)
            var expectedMessage = BuildContractMessage(contract);
            if (string.IsNullOrWhiteSpace(expectedMessage) ||
                !string.Equals(expectedMessage, contract.MessageToSign, StringComparison.Ordinal))
            {
                error = "Contract message does not match expected format";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }
            if (!SignatureVerifier.Verify(contract.AppPublicKey, contract.MessageToSign, contract.AppSignature))
            {
                error = "Invalid contract app signature";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Validate contract in envelope format: { hash, appSignature, storageNodeSignature, content }.
        /// Hash = SHA256(canonical(content)); both parties sign the hash.
        /// </summary>
        private bool ValidateEnvelopeContract(BlockerTxRequest req, JsonElement contractElement, JsonElement contentElement, out string error)
        {
            error = string.Empty;
            var hash = contractElement.TryGetProperty("hash", out var h) ? h.GetString() : contractElement.TryGetProperty("contractHash", out var ch) ? ch.GetString() : null;
            var appSignature = contractElement.TryGetProperty("appSignature", out var asig) ? asig.GetString() : null;
            var storageNodeSignature = contractElement.TryGetProperty("storageNodeSignature", out var ssig) ? ssig.GetString() : null;
            if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(appSignature) || string.IsNullOrWhiteSpace(storageNodeSignature))
            {
                error = "Envelope contract must have hash, appSignature, and storageNodeSignature";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }
            // Only these two content fields are required for signature verification and from/to matching; rest of content is opaque.
            if (!contentElement.TryGetProperty("appPublicKey", out var apEl) || !contentElement.TryGetProperty("storageNodePublicKey", out var spEl))
            {
                error = "Contract content must have appPublicKey and storageNodePublicKey";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }
            var appPublicKey = apEl.GetString();
            var storageNodePublicKey = spEl.GetString();
            if (string.IsNullOrWhiteSpace(appPublicKey) || string.IsNullOrWhiteSpace(storageNodePublicKey))
            {
                error = "Contract content appPublicKey and storageNodePublicKey must be non-empty";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }
            if (!SignatureVerifier.Verify(appPublicKey, hash, appSignature))
            {
                error = "Invalid contract app signature";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }
            if (!SignatureVerifier.Verify(storageNodePublicKey, hash, storageNodeSignature))
            {
                error = "Invalid contract storage node signature";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }
            if (!string.Equals(req.From, appPublicKey, StringComparison.OrdinalIgnoreCase))
            {
                error = "StorageOperation from must match contract app public key";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }
            if (!string.Equals(req.To ?? string.Empty, storageNodePublicKey, StringComparison.OrdinalIgnoreCase))
            {
                error = "StorageOperation to must match contract storage node public key";
                _logger.LogWarning("Validation failed for tx {TxId}: {Error}", req.Id, error);
                return false;
            }
            // Content is dynamic: Blocker does not validate status, startDateUnix, endDateUnix, contractType, etc.
            // Only envelope (hash + 2 signatures) and content.appPublicKey / content.storageNodePublicKey are required.
            return true;
        }

        /// <summary>Build canonical JSON string from content JsonElement (alphabetical keys). Must match Store serialization.</summary>
        private static string BuildCanonicalJsonFromContent(JsonElement content)
        {
            var props = content.EnumerateObject().OrderBy(p => p.Name).ToList();
            var sb = new StringBuilder();
            sb.Append('{');
            for (var i = 0; i < props.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(props[i].Name).Append("\":").Append(props[i].Value.GetRawText());
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string ComputeSha256Hex(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public string BuildContractMessage(StorageContract contract)
        {
            var feeString = contract.TotalFee.ToString("F10", CultureInfo.InvariantCulture);
            if (string.Equals(contract.ContractType, "TimeFixed", StringComparison.OrdinalIgnoreCase))
            {
                if (!contract.EndDateUnix.HasValue)
                {
                    _logger.LogWarning("BuildContractMessage failed: TimeFixed contract is missing EndDateUnix.");
                    return string.Empty;
                }
                return $"TimeFixed|{contract.StartDateUnix}|{contract.EndDateUnix.Value}|{contract.AppPublicKey}|{contract.StorageNodePublicKey}|{feeString}|{contract.TotalFileSize}";
            }

            if (string.Equals(contract.ContractType, "OpenEnded", StringComparison.OrdinalIgnoreCase))
            {
                return $"OpenEnded|{contract.StartDateUnix}|{contract.AppPublicKey}|{contract.StorageNodePublicKey}|{feeString}|{contract.TotalFileSize}";
            }

            _logger.LogWarning("BuildContractMessage failed: Unknown contract type {ContractType}.", contract.ContractType);
            return string.Empty;
        }

        /// <summary>
        /// TopUp: From must be Central; Central signs SignPayload; Super Admin also signs SignPayload.
        /// Data must include orderId and dCredited. Credit application stays on Central wallet ledger after accept.
        /// </summary>
        public bool ValidateTopUp(BlockerTxRequest req, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(_trustedCentralPublicKey))
            {
                error = "Central public key not configured on blocker";
                return false;
            }
            if (string.IsNullOrWhiteSpace(_trustedSuperAdminPublicKey))
            {
                error = "Super Admin public key not configured on blocker (SUPER_ADMIN_PUBLIC_KEY)";
                return false;
            }
            if (string.IsNullOrWhiteSpace(req.To))
            {
                error = "TopUp requires To (buyer public key)";
                return false;
            }
            if (!string.Equals(req.From?.Trim(), _trustedCentralPublicKey, StringComparison.OrdinalIgnoreCase))
            {
                error = "TopUp From must be Central public key";
                return false;
            }
            if (string.IsNullOrWhiteSpace(req.SignPayload) || string.IsNullOrWhiteSpace(req.Signature))
            {
                error = "TopUp requires SignPayload and Signature (Central)";
                return false;
            }
            if (string.IsNullOrWhiteSpace(req.SuperAdminSignature))
            {
                error = "TopUp requires SuperAdminSignature";
                return false;
            }
            if (req.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                error = "TopUp requires data";
                return false;
            }
            if (!req.Data.TryGetProperty("orderId", out var orderIdEl) ||
                string.IsNullOrWhiteSpace(orderIdEl.GetString()))
            {
                error = "TopUp data.orderId required";
                return false;
            }
            if (!req.Data.TryGetProperty("dCredited", out var creditedEl))
            {
                error = "TopUp data.dCredited required";
                return false;
            }
            if (creditedEl.ValueKind == JsonValueKind.Number)
            {
                if (!creditedEl.TryGetDecimal(out var amt) || amt <= 0)
                {
                    error = "TopUp data.dCredited must be positive";
                    return false;
                }
            }
            else if (!decimal.TryParse(creditedEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amtStr) || amtStr <= 0)
            {
                error = "TopUp data.dCredited must be positive";
                return false;
            }

            if (!string.Equals(req.Id?.Trim(), orderIdEl.GetString()?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                error = "TopUp Id must equal data.orderId";
                return false;
            }

            if (!SignatureVerifier.Verify(_trustedCentralPublicKey, req.SignPayload, req.Signature))
            {
                error = "Invalid Central signature on TopUp";
                return false;
            }
            if (!SignatureVerifier.Verify(_trustedSuperAdminPublicKey, req.SignPayload, req.SuperAdminSignature))
            {
                error = "Invalid Super Admin signature on TopUp";
                return false;
            }

            return true;
        }

        public bool VerifyDecision(DecisionResponse response, string expectedTxId, string blockerPubKey)
        {
            var payload = response.Decision;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now < payload.IssuedAt || now > payload.ExpiresAt)
            {
                _logger.LogWarning("Decision {TxId} rejected: IssuedAt/ExpiresAt out of range. IssuedAt={IssuedAt}, ExpiresAt={ExpiresAt}, Now={Now}", expectedTxId, payload.IssuedAt, payload.ExpiresAt, now);
                return false;
            }
            if (!payload.Selected)
            {
                _logger.LogWarning("Decision {TxId} rejected: Not selected. Selected={Selected}", expectedTxId, payload.Selected);
                return false;
            }
            if (!string.Equals(payload.TxId, expectedTxId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Decision {TxId} rejected: TxId mismatch. Expected={ExpectedTxId}, Got={ActualTxId}", expectedTxId, payload.TxId, expectedTxId);
                return false;
            }
            if (!string.Equals(payload.BlockerPubKey, blockerPubKey, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Decision {TxId} rejected: BlockerPubKey mismatch. Expected={ExpectedPubKey}, Got={ActualPubKey}", expectedTxId, blockerPubKey, payload.BlockerPubKey);
                return false;
            }

            var toSign = $"DECISION|{payload.TxId}|{payload.BlockerPubKey}|{payload.Selected}|{payload.IssuedAt}|{payload.ExpiresAt}|{payload.Nonce}";
            var pubKeyToUse = _trustedCentralPublicKey ?? response.CentralPublicKey;
            if (string.IsNullOrWhiteSpace(pubKeyToUse))
            {
                _logger.LogWarning("Decision {TxId} rejected: No Central public key (set CENTRAL_API_PUBLIC_KEY or ensure API returns centralPublicKey).", expectedTxId);
                return false;
            }
            if (_trustedCentralPublicKey != null && !string.Equals(response.CentralPublicKey, _trustedCentralPublicKey, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Decision {TxId} rejected: Response CentralPublicKey does not match trusted key.", expectedTxId);
                return false;
            }
            if (!SignatureVerifier.Verify(pubKeyToUse, toSign, response.Signature))
            {
                _logger.LogWarning("Decision {TxId} rejected: Invalid Central API signature.", expectedTxId);
                return false;
            }
            return true;
        }
    }
}