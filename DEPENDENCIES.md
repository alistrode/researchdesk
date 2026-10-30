# 依赖清单

## 运行分发包 `researchdesk-v*-win-x64.zip`

开箱即用，不需要安装 .NET、不需要管理员权限、不写注册表。

| 依赖 | 是否必须 | 说明 |
|---|---|---|
| Windows x64 | 必须 | 目标框架 .NET 10.0，运行时 DLL 已随包分发（352 个文件） |
| .NET 10 运行时 | 不需要 | 已框架内嵌（`mscorlib.dll`、`System.Private.CoreLib.dll` 等齐全） |
| 本机 Ollama | 必须 | 默认 `http://127.0.0.1:11434`，用于把检索结果归纳成报告 |
| SearXNG | 可选 | 自托管元搜索，需开启 JSON 格式并绑定回环地址 |
| YouTube Data API Key | 可选 | 缺省时回退公开网页检索，无日期精度 |
| Brave / Tavily Key | 可选 | 在界面设置里填写，走 DPAPI 加密存储 |
| 互联网 | 必须 | 需要访问 GitHub API、arXiv、Crossref、Truegle 等公开数据源 |
| 外部数据库 | 不需要 | 会话存 `%LOCALAPPDATA%\ResearchDesk\sessions` |

## 从源码构建

| 依赖 | 版本 |
|---|---|
| .NET SDK | 10.0 |
| 目标框架 | `net10.0` |
| 程序集依赖 | `Microsoft.NET.Sdk.Web`；`Microsoft.AspNetCore.DataProtection` 来自共享框架，无第三方 NuGet 包 |

## 端口

| 端口 | 用途 |
|---|---|
| 5182 | ResearchDesk HTTP 服务（仅绑定 127.0.0.1，可用 `RESEARCHDESK_PORT` 改） |
| 11434 | 本机 Ollama（外部依赖） |
| 8080 | 可选 SearXNG 默认端口（外部依赖） |

## 环境变量

| 变量 | 默认值 | 约束 |
|---|---|---|
| `RESEARCHDESK_PORT` | `5182` | 1–65535 |
| `RESEARCHDESK_OLLAMA_URL` | `http://127.0.0.1:11434` | 必须是环回地址 |
| `RESEARCHDESK_SEARXNG_URL` | 空 | 如 `http://127.0.0.1:8080` |
| `YOUTUBE_DATA_API_KEY` | 空 | 可选 |

## 随包分发的第三方运行时

运行包里包含微软 .NET 运行时 DLL（`System.*.dll`、`Microsoft.Extensions.*.dll`、`mscordaccore.dll` 等），
来源为 `dotnet publish -r win-x64 --self-contained`，属于运行时必需文件，不可删除。
