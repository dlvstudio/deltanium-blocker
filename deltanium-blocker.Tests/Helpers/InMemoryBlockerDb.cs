using Deltanium.Blocker.Data;
using Microsoft.EntityFrameworkCore;

namespace deltanium_blocker.Tests.Helpers;

public sealed class InMemoryBlockerDbFactory : IDbContextFactory<BlockerDbContext>, IDisposable
{
    private readonly DbContextOptions<BlockerDbContext> _options;

    public InMemoryBlockerDbFactory(string? name = null)
    {
        _options = new DbContextOptionsBuilder<BlockerDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .Options;

        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public BlockerDbContext CreateDbContext() => new(_options);

    public void Dispose()
    {
        // InMemory provider has nothing to dispose beyond contexts created by callers.
    }
}
