using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deltanium.Blocker.Core.Models
{
    public record BlockerTxRequest(
        string Id,
        string Type,
        string From,
        string? To,
        JsonElement Data,
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long Timestamp,
        string Fee,
        string Signature,
        string SignPayload
    );
}