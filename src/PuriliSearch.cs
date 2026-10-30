using System.Collections.Concurrent;
using System.Text.Json;

sealed partial class SourceSearch
{
    private static readonly SemaphoreSlim PuriliGate = new(1, 1);
    private static DateTimeOffset _puriliNext, _puriliBlocked;
    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, List<Evidence> Rows)> PuriliCache = new();
    private async Task<List<Evidence>> PuriliSearch(ResearchSession s, string q, string platform, CancellationToken ct)
    {
        var query = BuildQuery(q, platform);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(60));
        try { await PuriliGate.WaitAsync(budget.Token); }
        catch (OperationCanceledException) { Attempt(s, platform, "Purili 免费 API", "failed", 0, "备用搜索队列超时"); return []; }
        try
        {
            if (PuriliCache.TryGetValue(query, out var cached) && cached.At > DateTimeOffset.UtcNow.AddMinutes(-10)) return Finish(cached.Rows.Select(CopyFree).ToList(), "十分钟缓存");
            if (_puriliBlocked > DateTimeOffset.UtcNow) { Attempt(s, platform, "Purili 免费 API", "blocked", 0, "备用接口冷却期"); return []; }
            var delay = _puriliNext - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, budget.Token);
            _puriliNext = DateTimeOffset.UtcNow.AddMilliseconds(2200);
            using var timeout = SourceTimeout(budget.Token);
            using var response = await http.GetAsync("https://puri.li/api/search?q=" + Uri.EscapeDataString(query), timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode is 403 or 429) _puriliBlocked = DateTimeOffset.UtcNow.AddMinutes(2);
                Attempt(s, platform, "Purili 免费 API", (int)response.StatusCode is 403 or 429 ? "blocked" : "failed", 0, "HTTP " + (int)response.StatusCode);
                return [];
            }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) { Attempt(s, platform, "Purili 免费 API", "invalid", 0, "返回结构无效"); return []; }
            var rows = results.EnumerateArray().Select(x => new Evidence { Title = Clean(Get(x, "title")), Url = Get(x, "url"), Snippet = Clean(Get(x, "description")), Platform = PlatformCatalog.Items[platform], Type = "Purili 免费搜索 API · 公开索引", Metric = "Purili" }).Where(x => x.Title.Length > 0 && Uri.TryCreate(x.Url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && MatchesPlatformDomain(u, platform)).Take(20).ToList();
            if (PuriliCache.Count >= 256) PuriliCache.Clear();
            PuriliCache[query] = (DateTimeOffset.UtcNow, rows.Select(CopyFree).ToList());
            return Finish(rows, "免密钥备用 API；域名已核验");
        }
        catch (Exception ex) { Attempt(s, platform, "Purili 免费 API", "failed", 0, ex.GetType().Name); return []; }
        finally { PuriliGate.Release(); }
        List<Evidence> Finish(List<Evidence> rows, string detail) { Attempt(s, platform, "Purili 免费 API", rows.Count > 0 ? "results" : "empty", rows.Count, detail); return rows; }
    }
}
