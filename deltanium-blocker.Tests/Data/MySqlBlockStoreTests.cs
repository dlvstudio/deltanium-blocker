using Deltanium.Blocker.Data;
using deltanium_blocker.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace deltanium_blocker.Tests.Data;

public class MySqlBlockStoreTests : IDisposable
{
    private readonly InMemoryBlockerDbFactory _factory;
    private readonly MySqlBlockStore _blocks;
    private readonly MySqlMempoolStore _mempool;

    public MySqlBlockStoreTests()
    {
        _factory = new InMemoryBlockerDbFactory();
        _blocks = new MySqlBlockStore(_factory);
        _mempool = new MySqlMempoolStore(_factory);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void BlockStore_SaveAll_LoadAll_RoundTrips()
    {
        var genesis = TestDataBuilder.CreateGenesisBlock();
        var next = TestDataBuilder.CreateBlock(
            previousId: genesis.Id,
            height: 1,
            transactions: new() { TestDataBuilder.CreateTransaction() });

        _blocks.SaveAll(new[] { genesis, next });

        var loaded = _blocks.LoadAll();
        loaded.Should().HaveCount(2);
        loaded[0].Id.Should().Be(genesis.Id);
        loaded[1].Height.Should().Be(1);
        loaded[1].Transactions.Should().HaveCount(1);
    }

    [Fact]
    public void BlockStore_SaveAll_ReplacesExisting()
    {
        var a = TestDataBuilder.CreateGenesisBlock();
        _blocks.SaveAll(new[] { a });

        var b = TestDataBuilder.CreateBlock(height: 0, id: "replacement");
        _blocks.SaveAll(new[] { b });

        var loaded = _blocks.LoadAll();
        loaded.Should().ContainSingle();
        loaded[0].Id.Should().Be("replacement");
    }

    [Fact]
    public void MempoolStore_SaveAll_LoadAll_RoundTrips()
    {
        var tx1 = TestDataBuilder.CreateTransaction();
        var tx2 = TestDataBuilder.CreateTransaction();

        _mempool.SaveAll(new[] { tx1, tx2 });

        var loaded = _mempool.LoadAll();
        loaded.Should().HaveCount(2);
        loaded.Select(t => t.Id).Should().BeEquivalentTo(new[] { tx1.Id, tx2.Id });
    }

    [Fact]
    public void MempoolStore_SaveAll_ClearsPrevious()
    {
        _mempool.SaveAll(new[] { TestDataBuilder.CreateTransaction() });
        _mempool.SaveAll(Array.Empty<Deltanium.Blocker.Core.Transaction>());

        _mempool.LoadAll().Should().BeEmpty();
    }
}
