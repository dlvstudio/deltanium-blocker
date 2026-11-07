using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Deltanium.Blocker.Core;
using Microsoft.EntityFrameworkCore;

namespace Deltanium.Blocker.Data
{
    public class MySqlBlockStore : IBlockStore
    {
        private readonly IDbContextFactory<BlockerDbContext> _factory;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        public MySqlBlockStore(IDbContextFactory<BlockerDbContext> factory)
        {
            _factory = factory;
        }

        public List<Block> LoadAll()
        {
            using var db = _factory.CreateDbContext();
            var records = db.Blocks.AsNoTracking().OrderBy(b => b.Height).ToList();
            var blocks = new List<Block>();
            foreach (var record in records)
            {
                var block = JsonSerializer.Deserialize<Block>(record.BlockJson);
                if (block != null) blocks.Add(block);
            }
            return blocks;
        }

        public void SaveAll(IReadOnlyList<Block> blocks)
        {
            using var db = _factory.CreateDbContext();
            db.Blocks.RemoveRange(db.Blocks);
            foreach (var block in blocks)
            {
                db.Blocks.Add(new BlockRecord
                {
                    Id = block.Id,
                    Height = block.Height,
                    BlockJson = JsonSerializer.Serialize(block, JsonOptions),
                    SavedAt = DateTime.UtcNow
                });
            }
            db.SaveChanges();
        }
    }

    public class MySqlMempoolStore : IMempoolStore
    {
        private readonly IDbContextFactory<BlockerDbContext> _factory;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        public MySqlMempoolStore(IDbContextFactory<BlockerDbContext> factory)
        {
            _factory = factory;
        }

        public List<Transaction> LoadAll()
        {
            using var db = _factory.CreateDbContext();
            var records = db.MempoolTransactions.AsNoTracking().ToList();
            var txs = new List<Transaction>();
            foreach (var record in records)
            {
                var tx = JsonSerializer.Deserialize<Transaction>(record.TxJson);
                if (tx != null) txs.Add(tx);
            }
            return txs;
        }

        public void SaveAll(IReadOnlyCollection<Transaction> txs)
        {
            using var db = _factory.CreateDbContext();
            db.MempoolTransactions.RemoveRange(db.MempoolTransactions);
            foreach (var tx in txs)
            {
                db.MempoolTransactions.Add(new MempoolTxRecord
                {
                    Id = tx.Id,
                    TxJson = JsonSerializer.Serialize(tx, JsonOptions),
                    SavedAt = DateTime.UtcNow
                });
            }
            db.SaveChanges();
        }
    }
}
