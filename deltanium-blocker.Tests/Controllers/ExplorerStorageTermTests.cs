using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Deltanium.Blocker.Controllers;
using Deltanium.Blocker.Core;
using Deltanium.Blocker.Services;
using deltanium_blocker.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace deltanium_blocker.Tests.Controllers;

public class ExplorerStorageTermTests
{
    private static Transaction Term(string id, string terms) => new()
    {
        Id = id,
        Type = TransactionType.StorageTerm,
        From = "central",
        Timestamp = 1_800_000_000,
        Data = new Dictionary<string, object> { ["terms"] = terms, ["count"] = 1 },
    };

    private static ExplorerController Controller()
    {
        var state = new NodeState(TestDataBuilder.CreateTempBlockStore());
        state.AddBlock(new Block { Id = "b1", Height = 1, Transactions = new() { Term("st-1", "[{\"fileId\":\"aaaa1111bbbb\",\"status\":\"active\"}]") } });
        state.AddBlock(new Block { Id = "b2", Height = 2, Transactions = new() { Term("st-2", "[{\"fileId\":\"cccc2222dddd\",\"status\":\"ended\"}]") } });
        state.AddBlock(new Block { Id = "b3", Height = 3, Transactions = new() { Term("st-3", "[{\"fileId\":\"aaaa1111bbbb\",\"status\":\"stopped\"}]") } });
        return new ExplorerController(state, new TransactionPool(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mempool-" + System.Guid.NewGuid().ToString("N") + ".json"), NullLogger<TransactionPool>.Instance), new NodeConfig());
    }

    private static JsonElement Json(IActionResult r) =>
        JsonSerializer.SerializeToElement(((OkObjectResult)r).Value);

    [Fact]
    public void A_file_id_finds_every_StorageTerm_that_mentions_it()
    {
        var d = Json(Controller().SearchTxs(q: "aaaa1111bbbb"));
        d.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("Id").GetString()).Should().Equal("st-3", "st-1");
        Json(Controller().SearchTxs(q: "aaaa")).GetProperty("total").GetInt32().Should().Be(0); // short needles: ids only
    }

    [Fact]
    public void Storage_term_feed_returns_blocks_above_a_height_oldest_first()
    {
        var d = Json(Controller().StorageTerms(afterHeight: 1));
        d.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("Id").GetString()).Should().Equal("st-2", "st-3");
    }
}
