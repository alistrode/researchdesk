---
name: researchdesk
description: 运行、调试与维护 ResearchDesk 本地多平台调研工作台时使用；覆盖启动、健康检查、Ollama 与 SearXNG/YouTube 配置、检索通道排障与密钥存储位置。
---

# ResearchDesk

## 何时使用

- 需要在本机启动、重启或停止 ResearchDesk（默认 http://127.0.0.1:5182）。
- 检索通道报「未配置 / 查询失败 / 无匹配」，或某平台没有结果。
- 需要配置 SearXNG、Brave、Tavily、YouTube Data API 密钥。
- 需要从源码构建，或重新生成自包含免安装分发包。

## 关键事实

| 项 | 值 |
|---|---|
| 监听地址 | `http://127.0.0.1:5182`（可用 `RESEARCHDESK_PORT` 改端口） |
| 运行时 | Windows x64；分发包自带 .NET 10 运行时，无需安装 .NET |
| 外部依赖 | 本机 Ollama（`http://127.0.0.1:11434`）；SearXNG / YouTube API 可选 |
| 检索通道 | SearXNG → Truegle（免费无 Key）→ Brave → Tavily → DuckDuckGo |
| 密钥存储 | Data Protection + DPAPI，`%LOCALAPPDATA%\ResearchDesk\keys` |
| 调研记录 | `%LOCALAPPDATA%\ResearchDesk\sessions` |

## 常用命令与等效文件索引

| 用途 | 命令 | 等效代码 / 脚本 |
|---|---|---|
| 启动（自动选用分发包） | `.\scripts\start.ps1` | `scripts/start.ps1` → `scripts/launch.ps1` |
| 健康检查 | `Invoke-RestMethod http://127.0.0.1:5182/api/health` | `src/Program.cs` 的 `/api/health` 端点 |
| 查看各通道状态 | `Invoke-RestMethod http://127.0.0.1:5182/api/status` | `src/Program.cs` → `SourceSearch.Status` |
| 查看检索设置（不回传密钥） | `Invoke-RestMethod http://127.0.0.1:5182/api/search-settings` | `src/SearchSettings.cs` → `SearchSettingsStore.Public` |
| 列出调研会话 | `Invoke-RestMethod http://127.0.0.1:5182/api/sessions` | `src/Program.cs` → `SessionStore.List` |
| 从源码运行 | `dotnet run --project src\ResearchDesk.csproj -c Release` | `src/ResearchDesk.csproj` |
| 重新生成自包含包 | `dotnet publish src\ResearchDesk.csproj -c Release -r win-x64 --self-contained true -o publish` | `src/ResearchDesk.csproj` |
| Truegle 免费检索实现 | — | `src/FreeSearch.cs` |
| Purili 备用检索实现 | — | `src/PuriliSearch.cs` |
| 改端口 | `$env:RESEARCHDESK_PORT='5183'; .\scripts\start.ps1` | `src/Program.cs` 顶部端口解析 |
| 停止进程 | `Get-Process ResearchDesk -ErrorAction SilentlyContinue \| Stop-Process -Force` | — |

## 环境变量

| 变量 | 默认值 | 说明 |
|---|---|---|
| `RESEARCHDESK_PORT` | `5182` | 1–65535，否则回退默认 |
| `RESEARCHDESK_OLLAMA_URL` | `http://127.0.0.1:11434` | 仅回环地址 |
| `RESEARCHDESK_SEARXNG_URL` | 空 | 如 `http://127.0.0.1:8080` |
| `YOUTUBE_DATA_API_KEY` | 空 | 可选，缺省回退公开网页检索 |

环境变量只在进程启动时读取；已在运行就先关进程再重启。

## 排障顺序

1. `launch.ps1` 先探测 `/api/health`，返回 `product=ResearchDesk` 就复用已运行实例。
2. 报 `executable not found` 说明 `publish\ResearchDesk.exe` 缺失——解压 Release 的 `researchdesk-v*-win-x64.zip` 覆盖。
3. 网页论坛类结果常为空：公开索引不等于平台全量内容，小红书/抖音/知乎/登录态内容本就不保证可搜。
4. DuckDuckGo 触发挑战会进入两分钟冷却，期间不会重复冲击同一受限入口。
5. SearXNG 需在其 `settings.yml` 打开 `json` 格式，并**重启 ResearchDesk** 才生效。

## 目录说明

- `src/` — 全部源码
- `scripts/` — 启动脚本
- `README.md` — 产品说明（含各通道细节）
- Release 附件 `researchdesk-v*-win-x64.zip` — 自包含免安装分发包

## v1.3.1 配置进度与恢复

模型配置实现见 `src/ModelSetup.cs`，面板见 `src/wwwroot/app.js`。`GET /api/setup/state` 始终保留失败状态；`POST /api/setup/ensure-model` 启动后台配置，重复点击运行中任务不重复启动。进度流断开后页面仍每三秒轮询。Ollama 官方便携包由目标主机自行下载，默认模型使用完整官方 manifest。Windows curl 使用 `--ssl-revoke-best-effort` 处理吊销服务离线，禁止关闭证书或域名校验。

构建完整包：`dotnet publish src/ResearchDesk.csproj -c Release -r win-x64 --self-contained true -o publish`；完整分发文件夹保留 `publish`、`assets`、`Start.cmd`、`start.ps1`、`launch.ps1`、README、MODELS 和 VERSION。替换应用前保留旧运行包，用户会话与密钥位于 LOCALAPPDATA，禁止随发行包上传。

报告预算：`src/Program.cs` 的 `WriteReportBody` 使用 4096 token 和 8192 上下文，正文要求 600 汉字以内；思考型模型的推理计入输出预算，必须用真实报告检查，不能以 tags/health 或仅有来源列表判定成功。
