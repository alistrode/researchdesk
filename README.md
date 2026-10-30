# ResearchDesk · 本地多平台调研工作台

一个独立的本地优先研究工具，按证据来源组织跨平台检索，保留原始 URL、平台、时间和摘录，再交给本机 Ollama 归纳；会话保存在 `%LOCALAPPDATA%\ResearchDesk\sessions`。

## 启动

Windows；源码开发需要 .NET 10 SDK，发布包无需安装 .NET Runtime：

```powershell
.\start.ps1
```

浏览器地址：<http://127.0.0.1:5182>。默认 Ollama 地址 `http://127.0.0.1:11434`，可用 `RESEARCHDESK_OLLAMA_URL` 更改（仅回环地址）。本服务只绑定 `127.0.0.1`。

## 数据源与边界

- **GitHub**：使用官方 REST API 检索公开仓库；保留仓库、描述、星标、更新时间和链接。
- **学术**：使用 arXiv Atom API 与 Crossref REST API；论文元数据和摘要可追溯到原始记录。
- **通用网页与社媒**：默认使用 Truegle 免费搜索 API（无需账户或密钥）；可选配置本机 [SearXNG](https://github.com/searxng/searxng) JSON Search API，作为聚合多个搜索引擎的本地主通道。SearXNG 设置 `RESEARCHDESK_SEARXNG_URL`（如 `http://127.0.0.1:8080`）后按平台域名执行 `site:` 查询，无结果时回退到 DuckDuckGo。结果链接会按所选平台的域名核对。上游搜索服务会收到查询词。小红书、抖音、知乎和 X 的登录内容、推荐流及受限页面不保证可搜。
- **YouTube**：可配置官方 YouTube Data API v3 Key（`YOUTUBE_DATA_API_KEY`）；没有 Key 时可经 SearXNG 搜索公开网页，功能和配额以 Google 官方 API 为准。
- **用户材料**：支持粘贴公开链接及摘录，明确标注为手动导入，不伪装成自动抓取结果。

系统不会绕过登录、验证码、付费墙或平台访问限制。没有 SearXNG 或 YouTube Key 时，界面会显示其未连接/未配置。原始搜索查询会发送给配置的搜索服务（Brave/Tavily 的使用可能消耗账户额度）；调研摘录发送给本机 Ollama 整理报告，不调用云模型 API。

### 配置本地网页搜索

默认 Truegle 免费搜索 API 无需 API Key；客户端标识为 ResearchDesk，查询间隔至少 2.2 秒，缓存十分钟，HTTP 429 进入冷却。若需要把更多引擎统一到本地控制面，按 [SearXNG 官方安装文档](https://docs.searxng.org/admin/installation.html)在本机启动实例，并将端口只绑定到回环地址（例如 `127.0.0.1:8080`）。在 SearXNG 的 `settings.yml` 中启用 JSON 搜索格式，然后**重启 ResearchDesk**：

```yaml
search:
  formats:
    - html
    - json
```

PowerShell：

```powershell
$env:RESEARCHDESK_SEARXNG_URL = 'http://127.0.0.1:8080'
$env:YOUTUBE_DATA_API_KEY = '你的 YouTube Data API v3 Key' # 可选
.\start.ps1
```

环境变量只在进程启动时读取。若 ResearchDesk 已经在运行，先关闭现有进程，再用同一终端启动脚本。**不要把密钥提交到项目文件。**社媒公开索引是否有覆盖取决于实例启用的引擎；研究页逐来源显示“未配置”“查询失败”“无匹配”或“有结果”，这些状态彼此区分。

## 工作流

1. 选择近 7/30/90 天或不限时间；系统将长问题按标点拆成短检索角度，避免把整段需求重复塞进每条搜索词。
2. 并行检索 GitHub、arXiv、Crossref，以及网页/社媒公开索引和（可选）YouTube API；未配置 YouTube Key 时仍会搜索公开索引。尽可能把时间范围下推到数据源，再按可解析日期本地过滤。
3. 规范化 URL 后去重。相关度按每个短检索角度分别计算，并提高标题匹配权重；结果再结合新近程度排序。有公开数值信号时才作有限加权。GitHub API 遇到限流会尝试公开网页索引回退；当前社交平台经 SearXNG 返回的互动数据通常不可用，因此不会伪造“热度排名”。
4. 本地模型按来源编号总结共识、分歧、趋势和待核实点；报告输入限制为分平台精选证据，低显存设备的生成等待时间最长约 4 分钟。导入材料也作为不可信证据处理，报告结论需回到原始链接核查。
5. 服务诊断明确显示 Ollama、SearXNG 和 YouTube API 可用状态；调研记录、覆盖范围和摘录保存在本地，支持补充人工核验来源后重新生成简报。

## 开源设计参考

- [Stanford STORM](https://github.com/stanford-oval/storm)：多视角研究、先搜集可引用来源，再撰写结构化报告。
- [LangChain Open Deep Research](https://github.com/langchain-ai/open_deep_research)：可替换搜索后端与模型，研究流程应显式编排。
- [SearXNG](https://github.com/searxng/searxng)：自托管元搜索，适合把多个公开搜索引擎放到本地控制面。
- [mvanhorn/last30days-skill](https://github.com/mvanhorn/last30days-skill)：多来源并行检索、时间窗、查询扩展、相关性/新近度/可用互动信号排序、跨来源去重和来源健康诊断。ResearchDesk 参考这些工作流；不复制其代码；按需配置独立搜索 API，不读取浏览器 Cookie 或平台登录态。

本工具借鉴流程与来源溯源，不复制上述项目代码。与 last30days-skill 当前版本相比，本地 MVP 暂不做账号/话题实体消歧、评论/字幕全文抓取、跨 URL 语义聚类、完整平台互动数和趋势库/定时 watchlist；这些需要额外开放数据端点、授权或持久化与对比功能，界面不会声称已经覆盖。先以“有明确边界的公开索引 + 官方 API + 可复核证据”形成可信基线。


## 搜索通道修复（2026-10-09）

- 网页顶部“搜索设置”可保存本机 SearXNG 地址、Brave Search、Tavily 和 YouTube Data API 密钥；下一轮调研立即生效。Windows 使用 Data Protection + DPAPI 加密保存，设置 API 仅返回是否配置，不返回密钥。
- 搜索依次使用已配置的 SearXNG → 默认免费的 Truegle → 已配置的 Brave → Tavily，再尝试 DuckDuckGo。每个平台单独记录通道、结果数量、失败原因；备用通道恢复后会明确显示实际来源。
- YouTube 未配置 Key 或 API 失败时读取公开搜索页的视频标题、简介和原始链接，再回退搜索索引。此模式没有官方 API 的日期精度，不读取字幕、评论或登录内容；遇到同意页、登录页、挑战页会明确记录失败。
- DuckDuckGo 挑战/限流会触发两分钟冷却，停止向同一受限入口重复发送所有平台查询。仅 HTTP 200、挑战 HTML 或所选平台以外的链接均不能冒充检索成功。
- 已接入 Truegle 官方免密钥公开 API：https://truegle.info/developers/ 。2026-10-09 真实 Qwen3 TTS 六平台验收：网页、YouTube、X、知乎、抖音各保留 3 条来源；小红书 API 返回候选，但未通过相关性过滤。六项均有可用检索通道；公开索引不等于平台全部内容。
- 桌面版本已在用户明确要求更新后安装并启动，原有调研记录保留。安装目录为 `E:\xiangmujihua\apps\ResearchDesk\publish`；旧版备份为同级 `publish.backup-free-api-20261009`。



## 免费通道后续完善

- Truegle 候选池扩大到 20 条，再做日期、相关性与每平台数量筛选，避免先截断造成漏查。
- Truegle 没有足够相关候选时，自动调用免密钥 Purili 备用 API；缓存十分钟，访问受限时冷却，不重复冲击。
- 被筛掉的候选保留链接与原因，界面可展开查看；这些候选不用于报告。
- 扩大候选池、备用 API、候选保留及原有密钥保密回归检查均通过。小红书本次 Qwen3 TTS 查询仍无足够相关公开证据。
- 后续完善版保存于 E:\xiangmujihua\.tmp\ResearchDesk-search-complete。已在用户再次明确要求更新后完成桌面安装，DLL 与已测试发布版本一致，免费备用 API 和候选查看已验证生效。

