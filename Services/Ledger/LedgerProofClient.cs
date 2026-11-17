using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Deltanium.Blocker.Services.Ledger
{
    public interface ILedgerProofClient
    {
        Task<List<AnchorDto>> GetAnchorsAsync();
        Task<BalanceProofDto?> GetBalanceProofAsync(long seq, string account);
        /// <summary>Ledger rows in anchor <paramref name="seq"/>'s window, in Central's order; null if unavailable.</summary>
        Task<List<LedgerEntryDto>?> GetAnchorEntriesAsync(long seq);
        /// <summary>Drop cached anchors, e.g. right after a block brought a new one.</summary>
        void ForgetAnchors() { }
    }

    /// <summary>Reads public anchor data from Central. Nothing here is trusted until checked against the chain.</summary>
    public class LedgerProofClient : ILedgerProofClient
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
        private readonly HttpClient _http;
        private readonly string _api;
        private List<AnchorDto>? _cache;
        private DateTime _cacheAt;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, List<LedgerEntryDto>> _entries = new();

        public LedgerProofClient(Uri centralEndpoint, HttpClient? http = null)
        {
            _api = centralEndpoint.ToString().TrimEnd('/');
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        public async Task<List<AnchorDto>> GetAnchorsAsync()
        {
            if (_cache != null && DateTime.UtcNow - _cacheAt < TimeSpan.FromSeconds(30)) return _cache;
            var json = await _http.GetStringAsync($"{_api}/api/wallet/anchors?limit=500");
            _cache = JsonSerializer.Deserialize<List<AnchorDto>>(json, Json) ?? new();
            _cacheAt = DateTime.UtcNow;
            return _cache;
        }

        public async Task<BalanceProofDto?> GetBalanceProofAsync(long seq, string account)
        {
            using var resp = await _http.GetAsync($"{_api}/api/wallet/anchors/{seq}/balance-proof/{Uri.EscapeDataString(account)}");
            if (!resp.IsSuccessStatusCode) return null;
            return JsonSerializer.Deserialize<BalanceProofDto>(await resp.Content.ReadAsStringAsync(), Json);
        }

        public async Task<List<LedgerEntryDto>?> GetAnchorEntriesAsync(long seq)
        {
            if (_entries.TryGetValue(seq, out var cached)) return cached;
            using var resp = await _http.GetAsync($"{_api}/api/wallet/anchors/{seq}/entries");
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("entries", out var arr)) return null;
            var list = arr.Deserialize<List<LedgerEntryDto>>(Json) ?? new();
            // An anchor's window never changes; the caller still checks it against the LedgerRoot in the chain.
            _entries[seq] = list;
            return list;
        }

        public void ForgetAnchors() => _cache = null;
    }
}
