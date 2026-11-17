using System;
using System.Collections.Generic;
using System.Linq;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Services;
using Microsoft.AspNetCore.Mvc;

namespace Deltanium.Blocker.Controllers
{
    /// <summary>Public read-only chain explorer (confirmed txs in blocks + optional mempool).</summary>
    [ApiController]
    [Route("api/explorer")]
    public class ExplorerController : ControllerBase
    {
        private readonly NodeState _state;
        private readonly TransactionPool _pool;
        private readonly NodeConfig _config;

        public ExplorerController(NodeState state, TransactionPool pool, NodeConfig config)
        {
            _state = state;
            _pool = pool;
            _config = config;
        }

        [HttpGet("status")]
        public IActionResult Status()
        {
            return Ok(new
            {
                blockerPublicKey = _config.NodePublicKey,
                height = _state.Height,
                latestBlockId = _state.LatestBlockId,
                blockCount = _state.Chain.Count,
                mempoolCount = _pool.Count
            });
        }

        /// <summary>
        /// Search confirmed (and optionally pending) transactions.
        /// q matches tx id, from, or to (case-insensitive contains / exact).
        /// </summary>
        [HttpGet("txs")]
        public IActionResult SearchTxs(
            [FromQuery] string? q = null,
            [FromQuery] string? type = null,
            [FromQuery] int limit = 50,
            [FromQuery] int offset = 0,
            [FromQuery] bool includePending = false)
        {
            if (limit <= 0) limit = 50;
            if (limit > 200) limit = 200;
            if (offset < 0) offset = 0;

            IEnumerable<TxRow> rows = EnumerateConfirmed();
            if (includePending)
                rows = rows.Concat(EnumeratePending());

            IEnumerable<TxRow> filtered = rows;
            if (!string.IsNullOrWhiteSpace(type) &&
                !string.Equals(type, "all", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(t =>
                    string.Equals(t.Type, type, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                filtered = filtered.Where(t =>
                    Contains(t.Id, needle) ||
                    Contains(t.From, needle) ||
                    Contains(t.To, needle));
            }

            var list = filtered
                .OrderByDescending(t => t.BlockHeight)
                .ThenByDescending(t => t.Timestamp)
                .ToList();

            var page = list.Skip(offset).Take(limit).Select(t => ToPublic(t)).ToList();
            return Ok(new
            {
                total = list.Count,
                offset,
                limit,
                items = page
            });
        }

        [HttpGet("txs/{txId}")]
        public IActionResult GetTx(string txId)
        {
            if (string.IsNullOrWhiteSpace(txId))
                return BadRequest(new { error = "txId required" });

            var hit = EnumerateConfirmed()
                .Concat(EnumeratePending())
                .FirstOrDefault(t => string.Equals(t.Id, txId, StringComparison.OrdinalIgnoreCase));

            if (hit == null)
                return NotFound(new { error = "Transaction not found" });

            return Ok(ToPublic(hit, includeData: true));
        }

        [HttpGet("blocks")]
        public IActionResult ListBlocks([FromQuery] int limit = 20, [FromQuery] int offset = 0)
        {
            if (limit <= 0) limit = 20;
            if (limit > 100) limit = 100;
            if (offset < 0) offset = 0;

            var blocks = _state.Chain
                .OrderByDescending(b => b.Height)
                .Skip(offset)
                .Take(limit)
                .Select(b => new
                {
                    b.Id,
                    b.Height,
                    b.Timestamp,
                    b.PreviousId,
                    txCount = b.Transactions?.Count ?? 0,
                    b.CreatorPublicKey,
                    b.Version,
                    b.LedgerAnchorSeq,
                    b.LedgerRoot,
                    b.BalancesRoot
                })
                .ToList();

            return Ok(new
            {
                height = _state.Height,
                total = _state.Chain.Count,
                offset,
                limit,
                items = blocks
            });
        }

        private IEnumerable<TxRow> EnumerateConfirmed()
        {
            foreach (var block in _state.Chain)
            {
                if (block.Transactions == null) continue;
                foreach (var tx in block.Transactions)
                {
                    yield return new TxRow(
                        tx.Id,
                        tx.Type.ToString(),
                        tx.From,
                        tx.To,
                        tx.Timestamp,
                        tx.Fee,
                        tx.Signature,
                        tx.Data,
                        block.Height,
                        block.Id,
                        "confirmed");
                }
            }
        }

        private IEnumerable<TxRow> EnumeratePending()
        {
            foreach (var tx in _pool.Snapshot())
            {
                yield return new TxRow(
                    tx.Id,
                    tx.Type.ToString(),
                    tx.From,
                    tx.To,
                    tx.Timestamp,
                    tx.Fee,
                    tx.Signature,
                    tx.Data,
                    -1,
                    null,
                    "pending");
            }
        }

        private static bool Contains(string? hay, string needle) =>
            !string.IsNullOrEmpty(hay) &&
            hay.Contains(needle, StringComparison.OrdinalIgnoreCase);

        private static object ToPublic(TxRow t, bool includeData = false) => new
        {
            t.Id,
            t.Type,
            t.From,
            t.To,
            t.Timestamp,
            fee = t.Fee,
            t.Status,
            blockHeight = t.BlockHeight < 0 ? (long?)null : t.BlockHeight,
            blockId = t.BlockId,
            data = includeData ? t.Data : null
        };

        private sealed record TxRow(
            string Id,
            string Type,
            string From,
            string? To,
            long Timestamp,
            decimal Fee,
            string Signature,
            Dictionary<string, object> Data,
            long BlockHeight,
            string? BlockId,
            string Status);
    }
}
