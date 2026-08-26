using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Deltanium.Blocker.Core;

namespace Deltanium.Blocker.Services
{
    public class TransactionPool
    {
        private readonly ConcurrentDictionary<string, Transaction> _pool = new();
        private readonly string _mempoolFilePath;
        private readonly object _lock = new();
        private readonly ILogger<TransactionPool> _logger;

        public int Count => _pool.Count;

        public TransactionPool(string? mempoolFilePath = null, ILogger<TransactionPool>? logger = null)
        {
            _mempoolFilePath = mempoolFilePath ?? "mempool.json";
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            LoadMempool();
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
                if (!File.Exists(_mempoolFilePath))
                {
                    File.WriteAllText(_mempoolFilePath, "[]");
                    return;
                }

                var json = File.ReadAllText(_mempoolFilePath);
                var txs = JsonSerializer.Deserialize<List<Transaction>>(json);
                if (txs == null) return;
                foreach (var tx in txs)
                {
                    _pool.TryAdd(tx.Id, tx);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MEMPOOL] Error loading mempool.json: {ErrorMessage}", ex.Message);
            }
        }

        private void SaveMempool()
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_pool.Values.ToList(), new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(_mempoolFilePath, json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[MEMPOOL] Error saving mempool.json: {ErrorMessage}", ex.Message);
                }
            }
        }
    }
}












