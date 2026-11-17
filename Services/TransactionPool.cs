using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Data;

namespace Deltanium.Blocker.Services
{
    public class TransactionPool
    {
        private readonly ConcurrentDictionary<string, Transaction> _pool = new();
        private readonly IMempoolStore _store;
        private readonly object _lock = new();
        private readonly ILogger<TransactionPool> _logger;

        // Value txs held until a ledger anchor lets the Blocker check the sender's balance (TOKENOMICS_V2 §11 phase B).
        // Never put in blocks; kept in their own file so a restart does not lose them.
        private readonly ConcurrentDictionary<string, (Transaction Tx, DateTime Since)> _deferred = new();
        private readonly IMempoolStore _deferredStore;

        public int Count => _pool.Count;
        public int DeferredCount => _deferred.Count;

        /// <summary>Set when a tx is held; the node then produces a block so Central anchors the ledger again.</summary>
        public bool DeferredAwaitingAnchor { get; set; }

        public TransactionPool(IMempoolStore? store = null, ILogger<TransactionPool>? logger = null, string? mempoolFilePath = null,
            string? deferredFilePath = null)
        {
            _store = store ?? new JsonMempoolStore(mempoolFilePath);
            _deferredStore = new JsonMempoolStore(deferredFilePath ??
                (mempoolFilePath != null ? mempoolFilePath + ".deferred" : "mempool_deferred.json"));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            LoadMempool();
            LoadDeferred();
        }

        // Backward-compatible ctor used by older call sites / tests
        public TransactionPool(string? mempoolFilePath, ILogger<TransactionPool>? logger)
            : this(new JsonMempoolStore(mempoolFilePath), logger, mempoolFilePath)
        {
        }

        public bool TryAdd(Transaction tx)
        {
            if (!ValidateTx(tx)) return false;
            if (_deferred.ContainsKey(tx.Id)) return false;
            var added = _pool.TryAdd(tx.Id, tx);
            if (added)
            {
                SaveMempool();
                _logger.LogInformation("[MEMPOOL] Added tx {TxId}, pool size={Count}", tx.Id, _pool.Count);
            }
            return added;
        }

        public IReadOnlyCollection<Transaction> Snapshot() => _pool.Values.ToList();

        public void Remove(IEnumerable<string> txIds)
        {
            var removedAny = false;
            foreach (var id in txIds)
            {
                removedAny = _pool.TryRemove(id, out _) || removedAny;
            }
            if (removedAny) SaveMempool();
        }

        /// <summary>Hold a value tx out of blocks until it can be checked. False if the id is already known.</summary>
        public bool TryDefer(Transaction tx)
        {
            if (!ValidateTx(tx) || _pool.ContainsKey(tx.Id)) return false;
            if (!_deferred.TryAdd(tx.Id, (tx, DateTime.UtcNow))) return false;
            DeferredAwaitingAnchor = true;
            SaveDeferred();
            _logger.LogInformation("[MEMPOOL] Holding value tx {TxId} until a ledger anchor covers it, held={Count}", tx.Id, _deferred.Count);
            return true;
        }

        public IReadOnlyCollection<(Transaction Tx, DateTime Since)> Deferred() => _deferred.Values.ToList();

        public IReadOnlyCollection<Transaction> DeferredSnapshot() => _deferred.Values.Select(d => d.Tx).ToList();

        /// <summary>Move a held tx into the mempool so the next block includes it.</summary>
        public bool PromoteDeferred(string txId)
        {
            if (!_deferred.TryRemove(txId, out var d)) return false;
            SaveDeferred();
            return TryAdd(d.Tx);
        }

        public void DropDeferred(string txId)
        {
            if (_deferred.TryRemove(txId, out _)) SaveDeferred();
        }

        private void LoadDeferred()
        {
            try
            {
                foreach (var tx in _deferredStore.LoadAll())
                    if (!_pool.ContainsKey(tx.Id)) _deferred.TryAdd(tx.Id, (tx, DateTime.UtcNow));
                DeferredAwaitingAnchor = !_deferred.IsEmpty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MEMPOOL] Error loading held txs: {ErrorMessage}", ex.Message);
            }
        }

        private void SaveDeferred()
        {
            lock (_lock)
            {
                try { _deferredStore.SaveAll(_deferred.Values.Select(d => d.Tx).ToList()); }
                catch (Exception ex) { _logger.LogError(ex, "[MEMPOOL] Error saving held txs: {ErrorMessage}", ex.Message); }
            }
        }

        private bool ValidateTx(Transaction tx)
        {
            if (string.IsNullOrWhiteSpace(tx.Id)) return false;
            if (string.IsNullOrWhiteSpace(tx.From)) return false;
            if (tx.Timestamp <= 0) return false;
            return true;
        }

        private void LoadMempool()
        {
            try
            {
                foreach (var tx in _store.LoadAll())
                    _pool.TryAdd(tx.Id, tx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MEMPOOL] Error loading mempool: {ErrorMessage}", ex.Message);
            }
        }

        private void SaveMempool()
        {
            lock (_lock)
            {
                try
                {
                    _store.SaveAll(_pool.Values.ToList());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[MEMPOOL] Error saving mempool: {ErrorMessage}", ex.Message);
                }
            }
        }
    }
}
