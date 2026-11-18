using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text.Json;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Services;
using Deltanium.Blocker.Services.Utils;
using Deltanium.Blocker.Core.Models;
using Deltanium.Blocker.Services.Ledger;

namespace Deltanium.Blocker.Controllers
{
    [ApiController]
    [Route("api/tx")]
    public class TxController : ControllerBase
    {
        private readonly TransactionPool _txPool;
        private readonly DecisionClient _decisionClient;
        private readonly NodeConfig _nodeConfig;
        private readonly ILogger<TxController> _logger;
        private readonly TransactionValidationService _validationService;
        private readonly MetricsService _metrics;
        private readonly BalanceCheckService? _balances;

        public TxController(
            TransactionPool txPool,
            DecisionClient decisionClient,
            NodeConfig nodeConfig,
            ILogger<TxController> logger,
            TransactionValidationService validationService,
            MetricsService metrics,
            BalanceCheckService? balances = null)
        {
            _balances = balances;
            _txPool = txPool;
            _decisionClient = decisionClient;
            _nodeConfig = nodeConfig;
            _logger = logger;
            _validationService = validationService;
            _metrics = metrics;
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitTransaction([FromBody] BlockerTxRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Id))
            {
                _logger.LogWarning("Received an invalid transaction request: ID is missing or empty.");
                _metrics.IncrementInvalid();
                return BadRequest(new { error = "Invalid tx body" });
            }

            _logger.LogInformation("Received tx {TxId} type={TxType} from={From}", req.Id, req.Type, req.From);

            var decision = await _decisionClient.RequestDecisionAsync(req.Id, _nodeConfig.NodePublicKey);
            if (decision == null)
            {
                // Dev fallback: accept tx without Central Decision API (e.g. when Central is not running)
                var useLocalDecision = string.Equals(Environment.GetEnvironmentVariable("USE_LOCAL_DECISION"), "true", StringComparison.OrdinalIgnoreCase);
                if (useLocalDecision)
                {
                    _logger.LogInformation("Decision API unavailable; USE_LOCAL_DECISION=true, accepting tx {TxId} for local dev.", req.Id);
                }
                else
                {
                    _logger.LogWarning("Decision unavailable for tx {TxId}. Returning 503. Set USE_LOCAL_DECISION=true to accept without Central API.", req.Id);
                    _metrics.IncrementRejected();
                    return StatusCode(503);
                }
            }
            else if (!_validationService.VerifyDecision(decision, req.Id, _nodeConfig.NodePublicKey))
            {
                _metrics.IncrementRejected();
                return StatusCode(403);
            }

            if (!Enum.TryParse<TransactionType>(req.Type, ignoreCase: true, out var txType))
            {
                _logger.LogWarning($"Received transaction {req.Id} with invalid type: {req.Type}.");
                _metrics.IncrementInvalid();
                return BadRequest(new { error = "Invalid tx type" });
            }

            if (txType == TransactionType.StorageOperation)
            {
                if (!_validationService.ValidateStorageOperation(req, out var error))
                {
                    _logger.LogWarning($"Invalid StorageOperation tx {req.Id}: {error}. Returning 400.");
                    _metrics.IncrementInvalid();
                    return BadRequest(new { error });
                }
            }
            else if (txType == TransactionType.TopUp)
            {
                if (!_validationService.ValidateTopUp(req, out var error))
                {
                    _logger.LogWarning("Invalid TopUp tx {TxId}: {Error}. Returning 400.", req.Id, error);
                    _metrics.IncrementInvalid();
                    return BadRequest(new { error });
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(req.SignPayload) ||
                    !SignatureVerifier.Verify(req.From, req.SignPayload, req.Signature))
                {
                    _logger.LogWarning($"Invalid tx signature for tx {req.Id}. Returning 400.");
                    _metrics.IncrementInvalid();
                    return BadRequest(new { error = "Invalid tx signature" });
                }
            }

            if (!TryCollectVersions(req, txType, decision, _nodeConfig.NodePrivateKey, out var versions, out var versionError))
            {
                _logger.LogWarning("Tx {TxId} rejected: {Error}", req.Id, versionError);
                _metrics.IncrementInvalid();
                return BadRequest(new { error = versionError });
            }

            var hold = false;
            var holdDetail = "";
            // TOKENOMICS_V2 §11 phase B: value moves carry the user's signed request; check it and the balance.
            if (BalanceCheckService.ValueTypes.Contains(txType) && _balances != null && _balances.Mode != "off")
            {
                if (!BalanceCheckService.TryParseAuth(txType, req.To, req.SignPayload, req.Data, out var auth, out var authError))
                {
                    if (_balances.Mode == "enforce")
                    {
                        _logger.LogWarning("Value tx {TxId} rejected: {Error}", req.Id, authError);
                        _metrics.IncrementInvalid();
                        return BadRequest(new { error = authError });
                    }
                    _logger.LogWarning("Value tx {TxId} not verifiable ({Error}); accepted in warn mode", req.Id, authError);
                }
                else
                {
                    var (verdict, detail) = await _balances.CheckAsync(req.Id, req.From, req.Signature, auth!);
                    if (_balances.Defers(verdict))
                    {
                        hold = true;
                        holdDetail = detail;
                    }
                    else if (verdict == BalanceVerdict.Invalid || !_balances.Accepts(verdict))
                    {
                        _logger.LogWarning("Value tx {TxId} rejected: {Verdict} {Detail}", req.Id, verdict, detail);
                        _metrics.IncrementRejected();
                        return verdict == BalanceVerdict.Unverifiable
                            ? StatusCode(503, new { error = detail, verdict = verdict.ToString() })
                            : BadRequest(new { error = detail, verdict = verdict.ToString() });
                    }
                    _logger.LogInformation("Value tx {TxId}: {Verdict} {Detail}", req.Id, verdict, detail);
                }
            }

            Dictionary<string, object> dataDict = new();
            if (req.Data.ValueKind != JsonValueKind.Undefined && req.Data.ValueKind != JsonValueKind.Null)
            {
                try
                {
                    dataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(req.Data.GetRawText()) ?? new();
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, $"Failed to deserialize transaction data for tx {req.Id}. Returning 400.");
                    _metrics.IncrementInvalid();
                    return BadRequest(new { error = $"Invalid transaction data: {ex.Message}" });
                }
            }

            if (!decimal.TryParse(req.Fee, NumberStyles.Any, CultureInfo.InvariantCulture, out var feeDecimal))
            {
                _logger.LogWarning($"Invalid fee format for tx {req.Id}: {req.Fee}. Returning 400.");
                _metrics.IncrementInvalid();
                return BadRequest(new { error = "Invalid fee format" });
            }

            var timestamp = req.Timestamp > 0 ? req.Timestamp : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var tx = new Transaction
            {
                Id = req.Id,
                Type = txType,
                From = req.From,
                To = req.To,
                Data = dataDict,
                Timestamp = timestamp,
                Fee = feeDecimal,
                Signature = req.Signature,
                Versions = versions
            };

            if (hold)
            {
                // Held out of blocks until a ledger anchor covers the move; then promoted or dropped.
                if (_txPool.TryDefer(tx))
                {
                    _logger.LogInformation("Value tx {TxId} held: {Detail}", tx.Id, holdDetail);
                    return Accepted(new { status = "pending", txId = tx.Id, verdict = nameof(BalanceVerdict.Unverifiable), detail = holdDetail });
                }
            }
            else if (_txPool.TryAdd(tx))
            {
                _logger.LogInformation($"Accepted transaction {tx.Id} of type {tx.Type}.");
                _metrics.IncrementAccepted();
                return Ok(new { status = "accepted", txId = tx.Id });
            }

            _logger.LogWarning($"Received duplicate transaction {tx.Id}. Returning 409 Conflict.");
            _metrics.IncrementRejected();
            return Conflict(new { error = "Duplicate tx" });
        }

        /// <summary>
        /// Every tx records which builds handled it, each signed by that component: the API's (from Central's decision),
        /// the submitting Store's (StorageOperation) and this Blocker's own.
        /// </summary>
        public static bool TryCollectVersions(BlockerTxRequest req, TransactionType txType, DecisionResponse? decision,
            string blockerPrivateKeyHex, out List<VersionAttestation> versions, out string error)
        {
            versions = new List<VersionAttestation>();
            error = string.Empty;

            if (decision != null)
            {
                var api = decision.Version;
                if (api == null)
                {
                    error = "Central decision carries no signed API version";
                    return false;
                }
                if (api.Service != VersionAttestation.Api ||
                    !string.Equals(api.PublicKey, decision.CentralPublicKey, StringComparison.OrdinalIgnoreCase) ||
                    !api.IsValidFor(req.Id))
                {
                    error = "Invalid API version signature";
                    return false;
                }
                versions.Add(api);
            }

            if (req.Origin != null || txType == TransactionType.StorageOperation)
            {
                var store = req.Origin;
                if (store == null)
                {
                    error = "StorageOperation must carry the Store's signed version (origin)";
                    return false;
                }
                if (store.Service != VersionAttestation.Store ||
                    !string.Equals(store.PublicKey, req.To, StringComparison.OrdinalIgnoreCase) ||
                    !store.IsValidFor(req.Id))
                {
                    error = "Invalid Store version signature";
                    return false;
                }
                versions.Add(store);
            }

            versions.Add(VersionAttestation.Create(req.Id, VersionAttestation.Blocker, BuildInfo.Version, BuildInfo.Commit, blockerPrivateKeyHex));
            return true;
        }
    }
}
