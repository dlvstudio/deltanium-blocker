using System;
using Microsoft.EntityFrameworkCore;

namespace Deltanium.Blocker.Data
{
    public class BlockerDbContext : DbContext
    {
        public BlockerDbContext(DbContextOptions<BlockerDbContext> options) : base(options)
        {
        }

        public DbSet<BlockRecord> Blocks => Set<BlockRecord>();
        public DbSet<MempoolTxRecord> MempoolTransactions => Set<MempoolTxRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BlockRecord>(e =>
            {
                e.ToTable("blocks");
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.Height).IsUnique();
                e.Property(x => x.Id).HasMaxLength(128);
                e.Property(x => x.BlockJson).HasColumnType("longtext").IsRequired();
            });

            modelBuilder.Entity<MempoolTxRecord>(e =>
            {
                e.ToTable("mempool_transactions");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasMaxLength(128);
                e.Property(x => x.TxJson).HasColumnType("longtext").IsRequired();
            });
        }
    }

    public class BlockRecord
    {
        public string Id { get; set; } = string.Empty;
        public long Height { get; set; }
        public string BlockJson { get; set; } = "{}";
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    }

    public class MempoolTxRecord
    {
        public string Id { get; set; } = string.Empty;
        public string TxJson { get; set; } = "{}";
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    }
}
