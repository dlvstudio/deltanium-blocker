using System;
using System.Collections.Generic;
using System.Linq;
using Deltanium.Blocker.Core;

namespace Deltanium.Blocker.Security
{
    /// <summary>
    /// Browser origins allowed to call this Blocker (the web app submits Follow / Unfollow txs to /api/tx/submit).
    /// Cors:AllowedOrigins (Cors__AllowedOrigins__N) wins; otherwise this network's own web app.
    /// </summary>
    public static class CorsOrigins
    {
        public static string[] Resolve(IEnumerable<string>? configured, string networkId)
        {
            var list = (configured ?? Array.Empty<string>())
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim().TrimEnd('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (list.Length > 0) return list;
            return networkId.Length == 0
                ? new[] { "https://app.deltanium.com" }
                : new[] { "https://testnet-app.deltanium.com" };
        }

        public static string[] Resolve(IEnumerable<string>? configured) => Resolve(configured, NetworkScope.Id);
    }
}
