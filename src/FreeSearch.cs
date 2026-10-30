using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

sealed partial class SourceSearch
{
    private static readonly SemaphoreSlim FreeSearchGate = new(1, 1);
    private static DateTimeOffset _freeNextRequest = DateTimeOffset.MinValue;
    private static DateTimeOffset _freeLastSuccess = DateTimeOffset.MinValue;
    private static DateTimeOffset _freeBackoffUntil = DateTimeOffset.MinValue;
    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, List<Evidence> Results)> FreeCache = new();
    private static Evidence CopyFree(Evidence x) => new() { Title = x.Title, Url = x.Url, Snippet = x.Snippet, Platform = x.Platform, Type = x.Type, PublishedAt = x.PublishedAt, Metric = x.Metric };
    private async Task<List<Evidence>> FreeSearch(ResearchSession s, string q, string platform, int max, int days, CancellationToken ct)
    {
        var primary = await TruegleSearch(s, q, platform, 20, days, ct);
        if (primary.Any(x => { Score(x, [q], days); return x.MatchScore >= 18 && (days == 0 || IsInWindow(x.PublishedAt, days)); })) return primary;
        var backup = await PuriliSearch(s, q, platform, ct);
        return primary.Concat(backup).ToList();
    }
    private async Task<List<Evidence>> TruegleSearch(ResearchSession s, string q, string platform, int max, int days, CancellationToken ct)
    {
        var query = BuildQuery(q, platform);
        if (query.Length > 300) query = query[..300];
        var key = platform + ":" + days + ":" + query;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(90));
        try { await FreeSearchGate.WaitAsync(budget.Token); }
        catch (OperationCanceledException) { Attempt(s, platform, "Truegle 免费 API", "failed", 0, "免费搜索队列等待超时"); return []; }
        try
        {
            if (FreeCache.TryGetValue(key, out var cached) && cached.At > DateTimeOffset.UtcNow.AddMinutes(-10))
            {
                var results = cached.Results.Select(CopyFree).Take(Math.Clamp(max, 1, 20)).ToList();
                Attempt(s, platform, "Truegle 免费 API", results.Count > 0 ? "results" : "empty", results.Count, "十分钟查询缓存；来源保留 Truegle 归属");
                return results;
            }
            if (_freeBackoffUntil > DateTimeOffset.UtcNow) { Attempt(s, platform, "Truegle 免费 API", "blocked", 0, "服务限流冷却期；不重复请求"); return []; }
            var delay = _freeNextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, budget.Token);
            _freeNextRequest = DateTimeOffset.UtcNow.AddMilliseconds(2200);
            using var timeout = SourceTimeout(budget.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.truegle.info/api/search");
            request.Headers.Add("X-Truegle-Client", "ResearchDesk");
            request.Content = JsonContent.Create(new { query, mode = "blue-pill" });
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 429)
                {
                    var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1);
                    _freeBackoffUntil = DateTimeOffset.UtcNow + (retry > TimeSpan.Zero ? retry : TimeSpan.FromMinutes(1));
                }
                Attempt(s, platform, "Truegle 免费 API", (int)response.StatusCode is 403 or 429 ? "blocked" : "failed", 0, $"HTTP {(int)response.StatusCode}；无需密钥，不重试访问限制");
                return [];
            }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (!doc.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True || !doc.RootElement.TryGetProperty("results", out var rows) || rows.ValueKind != JsonValueKind.Array)
            { Attempt(s, platform, "Truegle 免费 API", "invalid", 0, "返回结构无效；未当作成功"); return []; }
            string Strip(string value) => Clean(Regex.Replace(System.Net.WebUtility.HtmlDecode(value), @"[\u200B-\u200F\u2060\uFEFF]|<[^>]+>", " "));
            var raw = rows.EnumerateArray().Select(x => new Evidence { Title = Strip(Get(x, "title")), Url = Get(x, "url"), Snippet = Strip(Get(x, "snippet")), PublishedAt = Get(x, "date"), Platform = PlatformCatalog.Items[platform], Type = "Truegle 免费搜索 API · 公开索引", Metric = "Truegle / " + Get(x, "source") }).ToList();
            var accepted = raw.Where(x => x.Title.Length > 0 && Uri.TryCreate(x.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && MatchesPlatformDomain(uri, platform)).ToList();
            _freeLastSuccess = DateTimeOffset.UtcNow;
            if (accepted.Count > 0 || raw.Count == 0)
            {
                if (FreeCache.Count > 256) foreach (var entry in FreeCache.Where(x => x.Value.At < DateTimeOffset.UtcNow.AddMinutes(-10)).ToArray()) FreeCache.TryRemove(entry.Key, out _);
                FreeCache[key] = (DateTimeOffset.UtcNow, accepted.Select(CopyFree).ToList());
            }
            var result = accepted.Take(Math.Clamp(max, 1, 20)).ToList();
            Attempt(s, platform, "Truegle 免费 API", result.Count > 0 ? "results" : raw.Count > 0 ? "invalid" : "empty", result.Count, result.Count > 0 ? "免密钥公开 API；域名已核验；按 Truegle 要求标注来源" : raw.Count > 0 ? "返回链接不属于所选平台，已拒收" : "查询成功，没有匹配的公开索引");
            return result;
        }
        catch (Exception ex) { Attempt(s, platform, "Truegle 免费 API", "failed", 0, "免费搜索异常：" + ex.GetType().Name); return []; }
        finally { FreeSearchGate.Release(); }
    }
}
