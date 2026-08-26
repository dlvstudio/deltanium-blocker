using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text.Json;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Services;
using Deltanium.Blocker.Services.Utils;
using Deltanium.Blocker.Core.Models;

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

        public TxController(
            TransactionPool txPool,
            DecisionClient decisionClient,
            NodeConfig nodeConfig,
            ILogger<TxController> logger,
            TransactionValidationService validationService,
            MetricsService metrics)
        {
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
                Signature = req.Signature
            };

            if (_txPool.TryAdd(tx))
            {
                _logger.LogInformation($"Accepted transaction {tx.Id} of type {tx.Type}.");
                _metrics.IncrementAccepted();
                return Ok(new { status = "accepted", txId = tx.Id });
            }

            _logger.LogWarning($"Received duplicate transaction {tx.Id}. Returning 409 Conflict.");
            _metrics.IncrementRejected();
            return Conflict(new { error = "Duplicate tx" });
        }
    }
}