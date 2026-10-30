using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

record SearchSettings(string SearxUrl = "", string BraveKey = "", string TavilyKey = "", string YouTubeKey = "");
record SearchSettingsUpdate(string? SearxUrl, string? BraveKey, string? TavilyKey, string? YouTubeKey, bool ClearBrave = false, bool ClearTavily = false, bool ClearYouTube = false);
sealed class SearchSettingsStore
{
    private readonly object _gate = new();
    private readonly IDataProtector _protector;
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResearchDesk", "search-settings.json");
    private SearchSettings _saved = new();
    public SearchSettingsStore(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ResearchDesk.SearchSettings.v1");
        if (File.Exists(_path)) try { _saved = JsonSerializer.Deserialize<SearchSettings>(_protector.Unprotect(File.ReadAllText(_path))) ?? new(); } catch { }
    }
    public SearchSettings Current
    {
        get { lock (_gate) return new(_saved.SearxUrl.Length > 0 ? _saved.SearxUrl : Environment.GetEnvironmentVariable("RESEARCHDESK_SEARXNG_URL") ?? "",
            _saved.BraveKey.Length > 0 ? _saved.BraveKey : Environment.GetEnvironmentVariable("BRAVE_SEARCH_API_KEY") ?? "",
            _saved.TavilyKey.Length > 0 ? _saved.TavilyKey : Environment.GetEnvironmentVariable("TAVILY_API_KEY") ?? "",
            _saved.YouTubeKey.Length > 0 ? _saved.YouTubeKey : Environment.GetEnvironmentVariable("YOUTUBE_DATA_API_KEY") ?? ""); }
    }
    public object Public() { var s = Current; return new { searxUrl = s.SearxUrl, braveConfigured = s.BraveKey.Length > 0, tavilyConfigured = s.TavilyKey.Length > 0, youtubeConfigured = s.YouTubeKey.Length > 0 }; }
    public string? Update(SearchSettingsUpdate update)
    {
        var url = update.SearxUrl?.Trim();
        if (!string.IsNullOrEmpty(url) && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host is not ("localhost" or "127.0.0.1") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)) return "SearXNG 地址需为本机 http://127.0.0.1:端口 或 http://localhost:端口。";
        if (new[] { update.BraveKey, update.TavilyKey, update.YouTubeKey }.Any(x => x?.Length > 512 || (x?.Any(char.IsControl) ?? false))) return "密钥格式无效。";
        lock (_gate)
        {
            string Secret(string? incoming, string old, bool clear) => clear ? "" : string.IsNullOrWhiteSpace(incoming) ? old : incoming.Trim();
            var next = new SearchSettings(url ?? _saved.SearxUrl, Secret(update.BraveKey, _saved.BraveKey, update.ClearBrave), Secret(update.TavilyKey, _saved.TavilyKey, update.ClearTavily), Secret(update.YouTubeKey, _saved.YouTubeKey, update.ClearYouTube));
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", _protector.Protect(JsonSerializer.Serialize(next)));
            File.Move(_path + ".tmp", _path, true);
            _saved = next;
        }
        return null;
    }
}
