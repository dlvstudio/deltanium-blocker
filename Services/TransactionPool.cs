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

        public int Count => _pool.Count;

        public TransactionPool(IMempoolStore? store = null, ILogger<TransactionPool>? logger = null, string? mempoolFilePath = null)
        {
            _store = store ?? new JsonMempoolStore(mempoolFilePath);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            LoadMempool();
        }

        // Backward-compatible ctor used by older call sites / tests
        public TransactionPool(string? mempoolFilePath, ILogger<TransactionPool>? logger)
            : this(new JsonMempoolStore(mempoolFilePath), logger)
        {
        }

        public bool TryAdd(Transaction tx)
        {
            if (!ValidateTx(tx)) return false;
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
