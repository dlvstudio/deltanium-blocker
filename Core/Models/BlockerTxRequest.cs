using System.Text.Json;
using System.Text.Json.Serialization;
using Deltanium.Blocker.Core;

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
        string SignPayload,
        string? SuperAdminSignature = null,
        /// <summary>Signed version of the Store that submitted this tx (Stores only).</summary>
        VersionAttestation? Origin = null
    );
}
