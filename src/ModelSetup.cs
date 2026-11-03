using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;

// ==================== 模型一键配置（自动注入） ====================
static class ModelSetup
{
    public sealed class Status
    {
        public string phase { get; set; } = "check";
        public string status { get; set; } = "";
        public long completed { get; set; }
        public long total { get; set; }
        public string? error { get; set; }
        public bool running { get; set; }
        public bool ready { get; set; }
        public bool ollamaRunning { get; set; }
        public bool ollamaInstalled { get; set; }
        public string model { get; set; } = "";
        public string[] installed { get; set; } = Array.Empty<string>();
    }

    sealed class Runner
    {
        public readonly object Gate = new();
        public Status Current = new();
        public string Endpoint = "";
        public string Model = "";
        readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };

        Uri U(string p) => new(Endpoint.TrimEnd('/') + p);

        public string Snap() { lock (Gate) return System.Text.Json.JsonSerializer.Serialize(Current); }
        public Status Get() { lock (Gate) return Current; }

        public async Task<bool> Ping()
        {
            try { using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(3)); using var r = await http.GetAsync(U("/api/version"), ct.Token); return r.IsSuccessStatusCode; } catch { return false; }
        }

        public async Task<string[]> Tags()
        {
            try
            {
                using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var r = await http.GetAsync(U("/api/tags"), ct.Token);
                if (!r.IsSuccessStatusCode) return Array.Empty<string>();
                using var d = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                return d.RootElement.GetProperty("models").EnumerateArray()
                    .Select(x => x.GetProperty("name").GetString() ?? "")
                    .Where(s => s.Length > 0).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        static string BaseName(string n) { var i = n.IndexOf(':'); return i < 0 ? n : n.Substring(0, i); }

        public static string? FindOllama()
        {
            var cands = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
                @"E:\ruanj\Ollama\ollama.exe"
            };
            foreach (var c in cands) { try { if (File.Exists(c)) return c; } catch { } }
            return null;
        }

        public async Task<Status> Snapshot()
        {
            var up = await Ping();
            var tags = up ? await Tags() : Array.Empty<string>();
            var installed = FindOllama() is not null;
            lock (Gate)
            {
                Current.ollamaRunning = up;
                Current.ollamaInstalled = installed;
                Current.installed = tags;
                Current.model = Model;
                if (!Current.running)
                {
                    Current.ready = tags.Any(t => BaseName(t) == BaseName(Model));
                    if (Current.ready) Current.phase = "done";
                    if (Current.ready) { Current.status = "模型已就绪"; Current.error = null; }
                }
                return Current;
            }
        }

        public void Start()
        {
            lock (Gate)
            {
                if (Current.running || Current.ready) return;
                Current = new Status { running = true, phase = "check", status = "检测环境", model = Model };
            }
            _ = Task.Run(RunAsync);
        }

        void Set(string phase, string status, long total = 0)
        {
            lock (Gate)
            {
                Current.phase = phase;
                Current.status = status;
                if (total > 0) { Current.total = total; Current.completed = 0; }
                else if (total == 0) { Current.total = 0; Current.completed = 0; }
            }
        }

        // curl keeps certificate/hostname validation. Windows best-effort revocation
        // tolerates an unreachable revocation responder, never a revoked certificate.
        async Task Download(string url, string target, string label, long expected = 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var partial = target + ".partial";
            var headers = target + ".headers";
            Set("download", label, expected);
            var psi = new ProcessStartInfo(OperatingSystem.IsWindows() ? "curl.exe" : "curl") {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { "--fail", "--location", "--silent", "--show-error",
                "--connect-timeout", "20", "--max-time", "7200", "--speed-limit", "1024",
                "--speed-time", "60", "--retry", "3", "--retry-delay", "2",
                "--dump-header", headers, "--output", partial }) psi.ArgumentList.Add(arg);
            if (OperatingSystem.IsWindows()) psi.ArgumentList.Add("--ssl-revoke-best-effort");
            psi.ArgumentList.Add(url);
            using var proc = Process.Start(psi) ?? throw new Exception("无法启动安全下载通道");
            var output = proc.StandardOutput.ReadToEndAsync();
            var error = proc.StandardError.ReadToEndAsync();
            try
            {
                while (!proc.HasExited)
                {
                    long bytes = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                    long total = expected;
                    try
                    {
                        var matches = System.Text.RegularExpressions.Regex.Matches(
                            await File.ReadAllTextAsync(headers), @"(?im)^content-length:\s*(\d+)");
                        if (total == 0 && matches.Count > 0)
                            long.TryParse(matches[^1].Groups[1].Value, out total);
                    } catch (IOException) { }
                    lock (Gate) { Current.completed = bytes; Current.total = total; }
                    await Task.Delay(300);
                }
                await proc.WaitForExitAsync();
                await output;
                var detail = (await error).Trim();
                if (proc.ExitCode != 0) throw new Exception(label + "失败：" + detail);
                var actual = new FileInfo(partial).Length;
                if (actual == 0 || (expected > 0 && actual != expected))
                    throw new Exception(label + "未完整下载，请重试");
                File.Move(partial, target, true);
                lock (Gate) { Current.completed = actual; if (Current.total == 0) Current.total = actual; }
            }
            finally
            {
                if (!proc.HasExited) { proc.Kill(true); await proc.WaitForExitAsync(); }
                try { File.Delete(headers); } catch { }
            }
        }

        async Task EnsureOllamaInstalled()
        {
            if (FindOllama() is not null) return;
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResearchDesk", "downloads");
            var archive = Path.Combine(cache, "ollama-windows-amd64.zip");
            // Official portable build needs neither elevation nor an installer dialog.
            await Download("https://github.com/ollama/ollama/releases/latest/download/ollama-windows-amd64.zip",
                archive, "下载 Ollama 官方运行包");
            Set("install-ollama", "正在解压 Ollama 官方运行包");
            var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama");
            Directory.CreateDirectory(target);
            ZipFile.ExtractToDirectory(archive, target, true);
            if (FindOllama() is null) throw new Exception("运行包中没有 ollama.exe");
            File.Delete(archive);
        }

        // ---------- 通道一：Ollama 官方 /api/pull ----------
        async Task PullViaApi()
        {
            Set("pull", "下载模型 " + Model);
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, U("/api/pull"))
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { model = Model, stream = true })
            };
            using var pullClient = new HttpClient { Timeout = TimeSpan.FromHours(2) };
            using var res = await pullClient.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) throw new Exception("HTTP " + (int)res.StatusCode);

            using var stream = await res.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(90))) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var d = System.Text.Json.JsonDocument.Parse(line);
                    var root = d.RootElement;
                    if (root.TryGetProperty("error", out var e)) throw new Exception(e.GetString() ?? "未知错误");
                    lock (Gate)
                    {
                        if (root.TryGetProperty("status", out var st)) Current.status = st.GetString() ?? Current.status;
                        if (root.TryGetProperty("total", out var t) && t.TryGetInt64(out var tv) && tv > 0) Current.total = tv;
                        if (root.TryGetProperty("completed", out var c) && c.TryGetInt64(out var cv)) Current.completed = cv;
                        Current.phase = "pull";
                    }
                }
                catch (System.Text.Json.JsonException) { }
            }
        }

        // Preserve the entire official manifest, including projector/template/config.
        // Importing only the largest GGUF silently breaks multimodal models.
        async Task ImportFromRegistry()
        {
            var parts = Model.Split(':', 2);
            var name = parts[0]; var tag = parts.Length > 1 ? parts[1] : "latest";
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$") ||
                !System.Text.RegularExpressions.Regex.IsMatch(tag, @"^[a-zA-Z0-9_.-]+$"))
                throw new Exception("不支持的模型名称");
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResearchDesk", "downloads");
            var manifestFile = Path.Combine(cache, "model-manifest.json");
            var registry = "https://registry.ollama.ai/v2/library/" + name;
            await Download(registry + "/manifests/" + tag, manifestFile, "获取模型清单 " + Model);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestFile));
            var store = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
            if (string.IsNullOrWhiteSpace(store)) store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
            var blobs = Path.Combine(store, "blobs"); Directory.CreateDirectory(blobs);
            var layers = manifest.RootElement.GetProperty("layers").EnumerateArray().ToList();
            layers.Add(manifest.RootElement.GetProperty("config"));
            foreach (var layer in layers)
            {
                var digest = layer.GetProperty("digest").GetString()!;
                if (!System.Text.RegularExpressions.Regex.IsMatch(digest, @"^sha256:[a-f0-9]{64}$"))
                    throw new Exception("模型清单包含无效层标识");
                var size = layer.GetProperty("size").GetInt64();
                var file = Path.Combine(blobs, digest.Replace(':', '-'));
                if (File.Exists(file) && new FileInfo(file).Length == size) continue;
                await Download(registry + "/blobs/" + digest, file, "下载模型组件 " + Model, size);
            }
            Set("verify", "安装完整模型清单");
            var destination = Path.Combine(store, "manifests", "registry.ollama.ai", "library", name, tag);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination + ".partial", await File.ReadAllBytesAsync(manifestFile));
            File.Move(destination + ".partial", destination, true);
        }

        async Task RunAsync()
        {
            try
            {
                Set("check", "检测环境");
                if (!await Ping() && FindOllama() is null) await EnsureOllamaInstalled();

                if (!await Ping())
                {
                    Set("start-ollama", "启动 Ollama");
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(FindOllama()!) { UseShellExecute = false, CreateNoWindow = true };
                        psi.ArgumentList.Add("serve");
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch { }

                    var up = false;
                    for (var i = 0; i < 120; i++) { await Task.Delay(500); if (await Ping()) { up = true; break; } }
                    if (!up) throw new Exception("Ollama 启动超时（60 秒内未响应 /api/version）");
                }

                Set("pull", "检查模型");
                var tags = await Tags();
                if (tags.Any(t => BaseName(t) == BaseName(Model)))
                {
                    lock (Gate) { Current.ready = true; Current.running = false; Current.phase = "done"; Current.status = "模型已就绪"; }
                    return;
                }

                string? firstError = null;
                try { await PullViaApi(); }
                catch (Exception ex) { firstError = ex.Message; }

                var afterPull = await Tags();
                if (!afterPull.Any(t => BaseName(t) == BaseName(Model)))
                {
                    // 官方通道失败时自动改走本地导入通道（常见于代理 / fake-ip DNS 环境）
                    try { await ImportFromRegistry(); }
                    catch (Exception ex2)
                    {
                        var msg = firstError is null ? ex2.Message : ("官方通道：" + firstError + "；本地导入通道：" + ex2.Message);
                        throw new Exception(msg);
                    }
                }

                Set("verify", "校验模型");
                var after = await Tags();
                if (!after.Any(t => BaseName(t) == BaseName(Model))) throw new Exception("下载结束但模型未出现在 /api/tags 中");
                lock (Gate) { Current.installed = after; Current.ready = true; Current.running = false; Current.phase = "done"; Current.status = "模型已就绪"; }
            }
            catch (Exception ex)
            {
                lock (Gate) { Current.running = false; Current.phase = "error"; Current.status = "配置失败，请重试"; Current.error = ex.Message; }
            }
        }
    }

    public static void Bind(WebApplication app, string endpoint, string model)
    {
        var runner = new Runner { Endpoint = endpoint, Model = model };

        app.MapGet("/api/setup/state", async () => await runner.Snapshot());

        app.MapPost("/api/setup/ensure-model", async () =>
        {
            await runner.Snapshot();
            runner.Start();
            return Results.Accepted("/api/setup/state", new { ok = true, model });
        });

        app.MapGet("/api/setup/progress", async (HttpContext ctx) =>
        {
            ctx.Response.Headers["Content-Type"] = "text/event-stream; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            for (var i = 0; i < 6000; i++)
            {
                await ctx.Response.WriteAsync("data: " + runner.Snap() + "\n\n");
                await ctx.Response.Body.FlushAsync();
                var s = runner.Get();
                if (!s.running && (s.phase == "done" || s.phase == "error")) break;
                try { await Task.Delay(300, ctx.RequestAborted); } catch { break; }
            }
        });
    }
}
