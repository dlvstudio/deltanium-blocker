using System.Text.Json;
using Deltanium.Blocker.Data;
using deltanium_blocker.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace deltanium_blocker.Tests.Integration;

public class RealMysqlBlockStoreIntegrationTests : IDisposable
{
    private readonly string? _cs;
    private readonly IDbContextFactory<BlockerDbContext>? _factory;
    private readonly MySqlBlockStore? _blocks;
    private readonly MySqlMempoolStore? _mempool;
    private readonly string _tag;
    private readonly List<string> _blockIds = new();
    private readonly List<string> _txIds = new();

    public RealMysqlBlockStoreIntegrationTests()
    {
        _cs = TryLoadCs();
        _tag = $"it_{Guid.NewGuid():N}"[..12];
        if (_cs == null) return;

        var options = new DbContextOptionsBuilder<BlockerDbContext>()
            .UseMySql(_cs, new MySqlServerVersion(new Version(8, 4, 0)))
            .Options;
        _factory = new SimpleFactory(options);
        using (var db = _factory.CreateDbContext())
            db.Database.EnsureCreated();
        _blocks = new MySqlBlockStore(_factory);
        _mempool = new MySqlMempoolStore(_factory);
    }

    private static string? TryLoadCs()
    {
        var env = Environment.GetEnvironmentVariable("DELTANIUM_MYSQL_CS");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".microsoft", "usersecrets", "deltanium-blocker-local-secrets", "secrets.json");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.TryGetProperty("ConnectionStrings:Default", out var v) ? v.GetString() : null;
    }

    public void Dispose()
    {
        if (_factory == null) return;
        try
        {
            using var db = _factory.CreateDbContext();
            if (_blockIds.Count > 0)
                db.Blocks.RemoveRange(db.Blocks.Where(b => _blockIds.Contains(b.Id)));
            if (_txIds.Count > 0)
                db.MempoolTransactions.RemoveRange(db.MempoolTransactions.Where(t => _txIds.Contains(t.Id)));
            db.SaveChanges();
        }
        catch { /* best effort */ }
    }

    [Fact]
    public void RealDb_SaveAndLoad_BlocksAndMempool()
    {
        Assert.True(_blocks != null && _mempool != null, "Missing blocker MySQL secrets");

        // Don't wipe production chain: append via direct records then LoadAll includes them.
        // Use SaveAll carefully — it replaces ALL blocks. So use DB append for IT safety.
        using (var db = _factory!.CreateDbContext())
        {
            var h1 = 8_000_000 + Math.Abs(_tag.GetHashCode() % 50_000);
            var genesis = TestDataBuilder.CreateBlock(id: $"{_tag}_genesis", height: h1);
            var next = TestDataBuilder.CreateBlock(
                id: $"{_tag}_b1",
                previousId: genesis.Id,
                height: h1 + 1,
                transactions: new() { TestDataBuilder.CreateTransaction() });

            db.Blocks.Add(new BlockRecord
            {
                Id = genesis.Id,
                Height = genesis.Height,
                BlockJson = JsonSerializer.Serialize(genesis),
                SavedAt = DateTime.UtcNow
            });
            db.Blocks.Add(new BlockRecord
            {
                Id = next.Id,
                Height = next.Height,
                BlockJson = JsonSerializer.Serialize(next),
                SavedAt = DateTime.UtcNow
            });
            _blockIds.Add(genesis.Id);
            _blockIds.Add(next.Id);
            db.SaveChanges();
        }

        var loaded = _blocks!.LoadAll();
        loaded.Should().Contain(b => b.Id == $"{_tag}_genesis");
        loaded.Should().Contain(b => b.Id == $"{_tag}_b1");

        var tx = TestDataBuilder.CreateTransaction();
        // SaveAll replaces mempool — snapshot then restore
        var existing = _mempool!.LoadAll();
        try
        {
            _mempool.SaveAll(existing.Concat(new[] { tx }).ToList());
            _txIds.Add(tx.Id);
            _mempool.LoadAll().Should().Contain(t => t.Id == tx.Id);
        }
        finally
        {
            _mempool.SaveAll(existing);
        }
    }

    private sealed class SimpleFactory : IDbContextFactory<BlockerDbContext>
    {
        private readonly DbContextOptions<BlockerDbContext> _options;
        public SimpleFactory(DbContextOptions<BlockerDbContext> options) => _options = options;
        public BlockerDbContext CreateDbContext() => new(_options);
    }
}
