# ReadStorm（Avalonia）重构与阅读器一体化设计

> 目标：以 C# + Avalonia 跨平台重写 SoNovel 的下载能力，并在第二阶段演进为“下载 + 阅读”一体化应用。  
> 当前阶段：先确保**可落地、可迭代、跨平台优先**。

---

## 1. 目标与边界

### 1.1 总体目标

1. **第一步（重构）**：把现有 Java 下载器能力迁移到 .NET 体系，功能对齐并增强可维护性。  
2. **第二步（阅读器）**：在同一应用中新增本地阅读能力，支持 TXT 加载、阅读进度、书架管理。  
3. 保持跨平台（Windows/macOS/Linux）优先，不因短期实现牺牲长期架构。

### 1.2 非目标（当前阶段）

- 不追求一次性做完整阅读生态（云同步、社区、推荐系统等）
- 不在第一阶段支持所有小说格式编辑能力（先聚焦下载 + TXT/EPUB阅读）

---

## 2. 参考现有 SoNovel 的能力映射

| 原系统能力（Java） | ReadStorm（C# + Avalonia）映射 |
|---|---|
| 配置读取 `config.ini` | `IAppConfigService`（JSON/YAML）+ 热重载（`IOptionsMonitor` 或自定义 watcher） |
| 多书源规则 `rule-*.json` | `RuleProvider`（同构 JSON 规则，保持可迁移） |
| 搜索/目录/正文解析器 | `SearchService` / `TocService` / `ChapterService` |
| 并发下载与重试 | `DownloadOrchestrator` + `Polly`（重试/熔断/超时） |
| 内容过滤与格式化 | `ContentPipeline`（Filter -> Normalize -> Convert） |
| EPUB/TXT/HTML/PDF 导出 | `ExportService`（策略模式） |
| CLI 交互入口 | Avalonia UI（MVVM）+ 可选 CLI Host（后续） |

关键原则：**规则优先、流程分层、导出解耦、UI 与核心能力分离**。

---

## 3. 推荐技术栈（跨平台）

### 3.1 基础栈

- .NET：`net10.0`
- UI：Avalonia 11（MVVM）
- 依赖注入：`Microsoft.Extensions.DependencyInjection`
- 日志：`Serilog`
- 配置：`Microsoft.Extensions.Configuration`（JSON + 环境变量）
- HTTP：`HttpClientFactory`
- HTML 解析：`AngleSharp` 或 `HtmlAgilityPack`
- JS 执行（用于规则中的 JS 片段）：`Jint`
- 重试与弹性：`Polly`
- 本地存储：
  - 轻量元数据：`SQLite`（推荐 `sqlite-net` / `EF Core Sqlite`）
  - 文件缓存：本地目录（章节缓存、封面缓存）

### 3.2 为什么这样选

- Avalonia 原生跨平台，桌面体验比 MAUI 在 Linux 侧更稳。  
- `HttpClientFactory + Polly` 是 .NET 下成熟的下载/爬取组合。  
- `Jint` 允许保留“规则中带 JS”这一关键扩展能力，便于迁移现有规则资产。  
- SQLite 可同时服务下载元信息和阅读器书架状态，减少后续迁移成本。

---

## 4. 目标架构（可落地）

### 4.1 分层架构

1. **Presentation（Avalonia）**
   - 页面：搜索页、下载任务页、书源管理页、设置页、阅读页（第二阶段）
   - ViewModel：只负责状态与命令，不写业务解析逻辑

2. **Application（用例编排层）**
   - UseCase：`SearchBooks`、`DownloadBook`、`ExportBook`、`OpenBook`
   - 负责事务边界、进度汇总、取消控制（`CancellationToken`）

3. **Domain（核心模型与规则）**
   - 实体：`Book`、`Chapter`、`SearchResult`、`Rule`
   - 规则：章节过滤、标题去重、格式转换策略

4. **Infrastructure（外部资源访问）**
   - 站点抓取：HTTP、HTML 解析、JS 规则执行
   - 存储：SQLite、文件系统
   - 导出：TXT/EPUB/PDF/HTML

### 4.2 模块划分（建议 Solution 结构）

- `ReadStorm.Desktop`（Avalonia 启动与 UI）
- `ReadStorm.Application`（用例与编排）
- `ReadStorm.Domain`（实体/值对象/规则接口）
- `ReadStorm.Infrastructure`（网络、解析、导出、存储实现）
- `ReadStorm.Rules`（书源 JSON 规则）
- `ReadStorm.Tests`（单元 + 集成 + 规则回归测试）

---

## 5. 第一阶段：下载器重构（MVP）

### 5.1 功能范围（必须）

- 多书源搜索（单源/聚合）
- 书籍目录获取（支持分页目录）
- 并发下载章节 + 失败重试
- 内容清洗（广告/乱码/标题重复）
- 导出 TXT、EPUB（PDF 可列为 M1.5）
- 基础设置页（线程数、间隔、代理、导出格式、下载路径）

### 5.2 UI 最小集合

- 搜索页：输入关键词 -> 列表展示 -> 选择书籍
- 下载页：章节选择（全本/区间/最新N章）-> 任务进度
- 设置页：配置项编辑与保存
- 日志抽屉：显示失败章节与错误原因

### 5.3 核心流程

1. UI 发起搜索 -> Application 层调用 `SearchService`
2. 解析结果后用户选择下载策略 -> `DownloadOrchestrator`
3. 并发抓取章节 + `Polly` 重试
4. 内容进入 `ContentPipeline`
5. 写入章节缓存并导出
6. 记录任务结果到 SQLite（便于后续阅读器接管）

---

## 6. 第二阶段：阅读器能力（与下载一体化）

### 6.1 能力目标

- 本地 TXT（优先）加载与分页阅读
- 书架（最近阅读、已下载）
- 阅读进度、书签、主题（亮/暗、字体、行间距）
- 从“下载记录”一键加入书架

### 6.2 数据模型扩展

建议新增表：

- `books`：书籍元信息（含来源、封面、路径）
- `downloads`：下载任务与状态
- `reading_progress`：当前章节/位置/时间戳
- `bookmarks`：书签与摘录（后续）

### 6.3 阅读内核建议

- TXT：先做“按章节索引 + 按段渲染”，避免一次性加载超大文本
- EPUB：第二优先（可复用导出后的结构）
- PDF：只做外部打开或只读内嵌展示（不建议先做复杂注释）

---

## 7. 跨平台可行性与困难点

### 7.1 跨平台可行部分（高把握）

- Avalonia UI（Win/macOS/Linux）
- HTTP 抓取与解析
- SQLite 本地数据
- TXT/EPUB 导出
- 基础阅读器（TXT）

### 7.2 可能存在困难的组件（需要预案）

1. **PDF 导出字体一致性**
   - 困难：不同系统字体可用性不同，中文字体渲染差异明显
   - 建议：应用内置可分发字体（注意许可证），并允许用户自定义字体路径

2. **规则中的 JS 兼容性**
   - 困难：Java 侧 JS 运行时与 .NET `Jint` 语义细节可能有差异
   - 建议：先定义“规则 JS 子集”，并建立规则兼容测试集

3. **Linux 桌面环境差异**
   - 困难：字体、输入法、文件对话框在不同发行版体验不同
   - 建议：先支持 Ubuntu LTS + Debian 作为主验证目标

4. **网页反爬策略变化**
   - 困难：部分书源受限流/Cloudflare/地域IP影响
   - 建议：代理配置、请求频率控制、失败分级提示（可恢复/需换源/需换IP）

---

## 8. 里程碑计划（建议）

### M0：脚手架与底座（1~2周）

- 建立 Solution 分层
- 接入 DI、日志、配置、SQLite
- 打通设置页 + 本地配置保存

### M1：下载闭环（2~4周）

- 搜索/目录/下载/重试/导出 TXT+EPUB
- 下载任务 UI 与进度
- MVP 可发布（跨平台自测）

### M1.5：稳定性强化（1~2周）

- 规则兼容测试
- 错误分类与日志完善
- PDF 导出（可选）

### M2：阅读器一期（2~4周）

- 书架 + TXT 阅读 + 进度 + 书签
- 下载记录与书架打通

### M3：一体化体验优化（持续）

- 任务调度优化、阅读主题、EPUB阅读支持
- 插件化规则管理、自动更新规则

---

## 9. 第一版工程目录（建议）

```text
ReadStorm/
  src/
    ReadStorm.Desktop/
    ReadStorm.Application/
    ReadStorm.Domain/
    ReadStorm.Infrastructure/
    ReadStorm.Rules/
  tests/
    ReadStorm.Tests/
  docs/
    architecture/
    rules/
  assets/
  build/
```

---

## 10. 风险控制与验收标准

### 10.1 验收标准（第一阶段）

- 在 Windows/macOS/Linux 至少各一套环境可启动
- 至少 3 个书源下载链路打通
- 单本下载成功率达到可用水平（例如 > 95%，按章节计）
- 导出 TXT/EPUB 可被常见阅读器正常打开

### 10.2 风险控制

- 保留规则驱动，不把站点特例写死到业务代码
- 所有 I/O 操作带取消令牌和超时
- 下载错误分层：网络错误 / 解析错误 / 规则错误 / 导出错误
- 每次新增书源前先补兼容测试样例

---

## 11. 你这个目标的实施建议（结论）

你的“两步走”是正确路线：

1. **先重构下载器**：把可变复杂度最高的“抓取与规则”迁到可维护结构。  
2. **再做阅读器**：直接复用下载成果与元数据，形成一体化体验。  

建议从 M0 + M1 开始，不要一开始就把阅读器细节做重，这样最容易在 4~8 周内拿到第一个可发布版本。

---

## 12. 下一步可直接执行的任务清单

1. 创建 `ReadStorm` solution 与 5 个核心项目（Desktop/Application/Domain/Infrastructure/Tests）
2. 定义第一版领域模型（Book/Chapter/SearchResult/Rule/DownloadTask）
3. 实现 `RuleProvider` 与一个书源的端到端下载样例
4. 搭建最小 Avalonia 界面：搜索 + 下载任务 + 设置
5. 接入 SQLite 保存下载记录
6. 打包 Win/macOS/Linux 首个预览版

> 如果你希望，我下一步可以直接给你输出：
> - `ReadStorm.sln` 的项目结构脚手架清单
> - 第一批 C# 接口定义（可直接开工）
> - M0/M1 每周执行计划（按 2 人小团队节奏）

---

## 13. 当前已落地进度（2026-02-15）

已完成（可编译运行）：

- `ReadStorm.slnx` 分层工程已创建：
   - `src/ReadStorm.Desktop`
   - `src/ReadStorm.Application`
   - `src/ReadStorm.Domain`
   - `src/ReadStorm.Infrastructure`
   - `src/ReadStorm.Rules`
   - `tests/ReadStorm.Tests`
- 项目依赖关系已接通：Desktop -> Application + Infrastructure，Infrastructure -> Application + Domain + Rules
- Avalonia 主界面已替换为 M0 壳：`搜索下载 / 下载任务 / 设置` 三页签
- 已接入 DI 容器（`Microsoft.Extensions.DependencyInjection`）
- 已落第一批领域模型：`SearchResult`、`DownloadTask`、`AppSettings`、`BookSourceRule`
- 已落第一批用例接口：`ISearchBooksUseCase`、`IDownloadBookUseCase`、`IAppSettingsUseCase`、`IRuleCatalogUseCase`
- 已落基础实现（M0）：
   - `InMemoryAppSettingsUseCase`
   - `EmbeddedRuleCatalogUseCase`
- 已添加示例规则文件：`src/ReadStorm.Rules/rules/rule-1.json`、`rule-2.json`
- 已完成一次全量构建验证与测试验证（通过）

下一步（M1 起点）：

1. 继续完善真实 HTTP + 规则解析实现（先支持 1 个书源并逐步扩展）
2. 将 `InMemoryAppSettingsUseCase` 替换为 JSON/SQLite 持久化实现
3. 下载任务引入真实状态机（Queued -> Downloading -> Succeeded/Failed）与进度回报
4. 增加 `RuleCatalog` 管理页（展示书源、搜索支持、可用性）

### 13.1 已新增（2026-02-16）

- `ISearchBooksUseCase` 已支持按 `sourceId` 搜索
- 新增 `RuleBasedSearchBooksUseCase`：可按真实规则文件执行 HTTP 搜索并解析结果
- 新增 `HybridSearchBooksUseCase`：选中书源走真实链路，失败时返回空结果避免误导
- UI 书源切换仍可用，且已与搜索参数联动
- 当前自动化验证：`dotnet build` + `dotnet test` 均通过

### 13.2 已新增（2026-02-16 / 第二轮）

- 设置持久化已从内存实现升级为本地 JSON 文件（用户目录）
- 下载任务已接入状态机：`Queued -> Downloading -> Succeeded/Failed/Cancelled`
- 真实规则搜索兼容增强：
   - 支持分页链接提取（`nextPage`）
   - 支持 `limitPage` 限制
   - 支持相对链接转绝对 URL
   - 支持 `@js:` 前缀选择器安全截断
   - 增加异常分类骨架（网络/规则格式/取消/未知）
- 自动化验证已提升到 6 条测试，全部通过
