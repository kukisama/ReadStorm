# ReadStorm 技术指南

> 本文档面向开发者与贡献者，详细介绍 ReadStorm 的技术背景、架构设计、核心组件及运行机制。

---

## 目录

- [1. 技术背景](#1-技术背景)
- [2. 技术栈](#2-技术栈)
- [3. 分层架构](#3-分层架构)
- [4. 领域模型](#4-领域模型)
- [5. 用例接口](#5-用例接口)
- [6. 基础设施实现](#6-基础设施实现)
- [7. 桌面 UI 层](#7-桌面-ui-层)
- [8. 依赖注入](#8-依赖注入)
- [9. 书源规则系统](#9-书源规则系统)
- [10. 核心流程](#10-核心流程)
  - [10.1 搜索流程](#101-搜索流程)
  - [10.2 下载流程](#102-下载流程)
  - [10.3 阅读流程](#103-阅读流程)
  - [10.4 书源编辑流程](#104-书源编辑流程)
- [11. 数据存储](#11-数据存储)
- [12. 构建与测试](#12-构建与测试)
- [13. 目录结构](#13-目录结构)

---

## 1. 技术背景

ReadStorm 是一款跨平台桌面小说阅读器，目标是让用户通过统一界面搜索、下载和阅读来自多个在线书源的小说内容。

项目面临的核心技术挑战：

- **多书源适配**：不同网站的 HTML 结构各异，需要灵活的内容提取方案
- **跨平台 UI**：需要在 Windows、Linux、macOS 上提供一致的原生体验
- **离线阅读**：下载的内容需要持久化存储，支持断点续传
- **可扩展性**：用户应能自定义书源，而无需修改代码

ReadStorm 通过 **规则驱动的 CSS 选择器提取** + **清洁架构分层** 解决这些挑战。

---

## 2. 技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| **.NET** | 10.0 | 运行时框架 |
| **C#** | 最新版本 | 开发语言 |
| **Avalonia UI** | 11.3.11 | 跨平台桌面 UI 框架 |
| **Semi.Avalonia** | 11.2.1 | Semi Design 风格主题 |
| **CommunityToolkit.Mvvm** | 8.2.1 | MVVM 基础库（ObservableObject、RelayCommand） |
| **AngleSharp** | 1.1.2 | HTML 解析与 CSS 选择器查询 |
| **Microsoft.Data.Sqlite** | 9.0.2 | SQLite 数据库驱动 |
| **Microsoft.Extensions.DependencyInjection** | 10.0.0 | 依赖注入容器 |
| **xUnit** | 2.9.3 | 单元测试框架 |

---

## 3. 分层架构

ReadStorm 采用 **清洁架构（Clean Architecture）** 四层设计，依赖方向严格从外层指向内层：

```mermaid
graph TB
    subgraph Desktop["Desktop 层（Avalonia UI）"]
        Views["Views<br/>MainWindow"]
        ViewModels["ViewModels<br/>SearchDownload / Bookshelf / Reader"]
    end

    subgraph Infrastructure["Infrastructure 层（基础设施）"]
        Services["Services<br/>SQLite / HTTP / AngleSharp"]
        Rules["Rules<br/>JSON 书源规则文件"]
    end

    subgraph Application["Application 层（应用接口）"]
        UseCases["Use Case 接口<br/>ISearchBooksUseCase / IDownloadBookUseCase"]
    end

    subgraph Domain["Domain 层（领域模型）"]
        Models["Models<br/>BookEntity / ChapterEntity / DownloadTask"]
    end

    Views --> ViewModels
    ViewModels --> UseCases
    Services --> UseCases
    Services --> Models
    UseCases --> Models

    style Desktop fill:#e3f2fd,stroke:#1565c0
    style Infrastructure fill:#fff3e0,stroke:#ef6c00
    style Application fill:#e8f5e9,stroke:#2e7d32
    style Domain fill:#fce4ec,stroke:#c62828
```

### 层间职责

| 层 | 职责 | 依赖 |
|----|------|------|
| **Domain** | 定义核心业务模型、枚举和值对象 | 无外部依赖 |
| **Application** | 定义用例接口（`ISearchBooksUseCase` 等） | → Domain |
| **Infrastructure** | 实现用例接口，处理 HTTP、HTML 解析、数据库 | → Application, Domain |
| **Desktop** | 提供 Avalonia UI 和 ViewModel | → Application, Infrastructure |

### 依赖关系图

```mermaid
graph LR
    Desktop --> Application
    Desktop --> Infrastructure
    Infrastructure --> Application
    Infrastructure --> Domain
    Application --> Domain
```

---

## 4. 领域模型

### 核心实体

```mermaid
classDiagram
    class BookEntity {
        +int Id
        +string Title
        +string Author
        +string SourceId
        +string TocUrl
        +int TotalChapters
        +int DoneChapters
        +int ReadChapterIndex
        +string CoverUrl
        +byte[] CoverBlob
        +double ProgressPercent
        +bool IsComplete
    }

    class ChapterEntity {
        +int Id
        +int BookId
        +int IndexNo
        +string Title
        +string? Content
        +ChapterStatus Status
        +string SourceId
        +string SourceUrl
        +string? Error
    }

    class DownloadTask {
        +string BookTitle
        +string Author
        +DownloadMode Mode
        +DownloadTaskStatus Status
        +double ProgressPercent
        +int TotalChapterCount
        +int CurrentChapterIndex
        +TransitionTo(status)
        +ResetForRetry()
        +ResetForResume()
    }

    class SearchResult {
        +string Title
        +string Author
        +string Latest
        +string CoverUrl
        +string TocUrl
        +string SourceId
    }

    class FullBookSourceRule {
        +int Id
        +string Name
        +string Url
        +SearchConfig Search
        +TocConfig Toc
        +ChapterConfig Chapter
        +BookInfoConfig Book
    }

    BookEntity "1" --> "*" ChapterEntity : 包含
    DownloadTask --> BookEntity : 关联
    SearchResult --> FullBookSourceRule : 来源
```

### 下载状态机

`DownloadTask` 使用严格的状态机管理下载生命周期：

```mermaid
stateDiagram-v2
    [*] --> Queued : 加入队列
    Queued --> Downloading : 开始下载
    Downloading --> Succeeded : 全部完成
    Downloading --> Failed : 发生错误
    Downloading --> Cancelled : 用户取消
    Downloading --> Paused : 用户暂停
    Paused --> Queued : 恢复下载
    Failed --> Queued : 重试
    Succeeded --> [*]
    Cancelled --> [*]
```

### 章节状态

```mermaid
stateDiagram-v2
    [*] --> Pending : 初始状态
    Pending --> Downloading : 开始获取
    Downloading --> Done : 内容获取成功
    Downloading --> Failed : 获取失败
    Failed --> Pending : 重试
```

---

## 5. 用例接口

Application 层定义了所有业务操作的接口，供 Infrastructure 层实现、Desktop 层调用。

| 接口 | 职责 | 关键方法 |
|------|------|----------|
| `ISearchBooksUseCase` | 搜索小说 | `ExecuteAsync(keyword, sourceId?, ct)` |
| `IDownloadBookUseCase` | 下载管理 | `QueueAsync(...)`, `CheckNewChaptersAsync(...)`, `FetchChapterFromSourceAsync(...)` |
| `IBookRepository` | 书籍与章节持久化 | `UpsertBookAsync(...)`, `GetChaptersAsync(...)`, `UpdateReadProgressAsync(...)` |
| `IAppSettingsUseCase` | 应用设置读写 | `LoadAsync(ct)`, `SaveAsync(settings, ct)` |
| `IRuleCatalogUseCase` | 书源目录 | `GetAllAsync(ct)` → `IReadOnlyList<BookSourceRule>` |
| `ICoverUseCase` | 封面管理 | `RefreshCoverAsync(...)`, `ApplyCoverCandidateAsync(...)` |
| `ISourceDiagnosticUseCase` | 书源诊断 | `DiagnoseAsync(sourceId, keyword, ct)` |
| `ISourceHealthCheckUseCase` | 书源可达性检测 | `CheckAllAsync(sources, ct)` |
| `IBookshelfUseCase` | 书架管理 | `GetAllAsync(ct)`, `AddAsync(...)`, `RemoveAsync(...)` |
| `IRuleEditorUseCase` | 书源规则编辑 | `LoadAsync(id)`, `SaveAsync(rule)`, `TestSearchAsync(...)` |

---

## 6. 基础设施实现

### 6.1 HTML 解析（AngleSharp）

使用 AngleSharp 库通过 CSS 选择器从 HTML 页面中提取结构化数据。这是 ReadStorm 书源规则系统的核心。

```mermaid
flowchart LR
    HTML["原始 HTML"] --> Parser["AngleSharp 解析器"]
    Rule["CSS 选择器规则"] --> Parser
    Parser --> Data["结构化数据<br/>（书名/作者/章节列表/正文）"]
```

### 6.2 数据持久化（SQLite）

`SqliteBookRepository` 使用 WAL 模式实现线程安全的读写：

- **books 表**：书籍元数据、阅读进度、封面数据
- **chapters 表**：章节标题、内容文本、下载状态
- 写操作通过 `SemaphoreSlim` 保证串行化

### 6.3 HTTP 通信

`RuleHttpHelper` 提供统一的 HTTP 客户端管理：

- 自定义 User-Agent 模拟浏览器请求
- 支持 GET/POST 两种请求方式
- 带超时和重试机制

### 6.4 文件导出

| 导出器 | 格式 | 说明 |
|--------|------|------|
| 内置 TXT 导出 | `.txt` | 纯文本，章节标题 + 正文 |
| `EpubExporter` | `.epub` | 标准 EPUB 格式，含目录结构 |

### 6.5 辅助服务

| 服务 | 职责 |
|------|------|
| `RuleFileLoader` | 加载和规范化 JSON 规则文件 |
| `RulePathResolver` | 解析内置规则与用户规则目录 |
| `WorkDirectoryManager` | 管理应用数据目录路径 |
| `SourceDownloadQueue` | FIFO 下载队列，支持并发控制 |
| `AppLogger` | 文件日志记录 |

---

## 7. 桌面 UI 层

### 7.1 MVVM 架构

Desktop 层采用 MVVM 模式，通过 `CommunityToolkit.Mvvm` 实现数据绑定和命令：

```mermaid
graph TB
    subgraph Views
        MainWindow["MainWindow.axaml"]
    end

    subgraph ViewModels
        MainVM["MainWindowViewModel"]
        SearchVM["SearchDownloadViewModel"]
        BookshelfVM["BookshelfViewModel"]
        ReaderVM["ReaderViewModel"]
        DiagVM["DiagnosticViewModel"]
        RuleVM["RuleEditorViewModel"]
        SettingsVM["SettingsViewModel"]
    end

    subgraph UseCases["用例接口"]
        ISearch["ISearchBooksUseCase"]
        IDownload["IDownloadBookUseCase"]
        IBook["IBookRepository"]
        IRuleCat["IRuleCatalogUseCase"]
    end

    MainWindow --> MainVM
    MainVM --> SearchVM
    MainVM --> BookshelfVM
    MainVM --> ReaderVM
    MainVM --> DiagVM
    MainVM --> RuleVM
    MainVM --> SettingsVM

    SearchVM --> ISearch
    SearchVM --> IDownload
    BookshelfVM --> IBook
    ReaderVM --> IBook
    DiagVM --> IRuleCat

    style Views fill:#e3f2fd
    style ViewModels fill:#e8f5e9
    style UseCases fill:#fff3e0
```

### 7.2 懒加载模式

`MainWindowViewModel` 采用延迟初始化策略：子 ViewModel 仅在用户首次切换到对应页签时才创建，降低启动开销。

### 7.3 页签功能

| 页签 | ViewModel | 功能 |
|------|-----------|------|
| 搜索下载 | `SearchDownloadViewModel` | 书源搜索、下载队列管理 |
| 书架 | `BookshelfViewModel` | 已下载书籍管理，网格/列表视图 |
| 阅读器 | `ReaderViewModel` | 小说阅读，目录导航，主题切换 |
| 书源诊断 | `DiagnosticViewModel` | 书源健康检查与诊断 |
| 书源编辑 | `RuleEditorViewModel` | 创建/编辑/测试书源规则 |
| 设置 | `SettingsViewModel` | 应用配置 |

---

## 8. 依赖注入

在 `App.axaml.cs` 中通过 `Microsoft.Extensions.DependencyInjection` 完成服务注册：

```csharp
private static ServiceProvider ConfigureServices()
{
    var services = new ServiceCollection();

    // 用例实现（单例，保持状态）
    services.AddSingleton<ISearchBooksUseCase, HybridSearchBooksUseCase>();
    services.AddSingleton<IDownloadBookUseCase, RuleBasedDownloadBookUseCase>();
    services.AddSingleton<IBookRepository, SqliteBookRepository>();
    services.AddSingleton<IAppSettingsUseCase, JsonFileAppSettingsUseCase>();
    services.AddSingleton<IRuleCatalogUseCase, EmbeddedRuleCatalogUseCase>();
    services.AddSingleton<ICoverUseCase>(sp => sp.GetRequiredService<CoverService>());
    services.AddSingleton<ISourceDiagnosticUseCase, RuleBasedSourceDiagnosticUseCase>();
    services.AddSingleton<ISourceHealthCheckUseCase, FastSourceHealthCheckUseCase>();
    services.AddSingleton<IBookshelfUseCase, JsonFileBookshelfUseCase>();
    services.AddSingleton<IRuleEditorUseCase, FileBasedRuleEditorUseCase>();

    // ViewModel（瞬态，每次创建新实例）
    services.AddTransient<MainWindowViewModel>();

    return services.BuildServiceProvider();
}
```

### 生命周期策略

- **Singleton**：有状态服务（数据库连接池、下载队列、设置缓存）
- **Transient**：ViewModel（每个窗口创建独立实例）

---

## 9. 书源规则系统

### 9.1 规则结构

每个书源通过一个 JSON 文件描述其 HTML 结构，ReadStorm 使用 CSS 选择器从页面中提取数据。

```mermaid
graph TB
    subgraph RuleFile["rule-N.json"]
        Meta["元信息<br/>id / name / url / language"]
        Search["搜索配置<br/>url / method / CSS 选择器"]
        Book["书籍信息配置<br/>书名 / 作者 / 简介 / 封面"]
        Toc["目录配置<br/>章节链接选择器 / 偏移 / 排序"]
        Chapter["章节配置<br/>标题 / 正文选择器 / 过滤规则"]
    end

    style RuleFile fill:#fff8e1,stroke:#f9a825
```

### 9.2 规则 JSON 示例

```json
{
  "id": 1,
  "url": "http://example.com/",
  "name": "示例书源",
  "type": "html",
  "language": "zh_CN",

  "search": {
    "url": "http://example.com/search",
    "method": "post",
    "data": "{searchkey: %s}",
    "result": "#results > table > tr",
    "bookName": "td.title > a",
    "author": "td:nth-of-type(3)",
    "latestChapter": "td.latest > a"
  },

  "book": {
    "bookName": "meta[property='og:novel:book_name']",
    "author": "meta[property='og:novel:author']",
    "intro": "meta[property='og:description']",
    "coverUrl": "meta[property='og:image']"
  },

  "toc": {
    "item": "#chapter-list > dd > a",
    "offset": 0,
    "desc": false
  },

  "chapter": {
    "title": ".chapter-title > h1",
    "content": "#chapter-content",
    "paragraphTag": "<br>+",
    "filterTxt": "广告文本正则",
    "filterTag": "div script"
  }
}
```

### 9.3 规则加载优先级

```mermaid
flowchart TD
    Start["加载规则 rule-N"] --> UserDir{"用户目录存在<br/>rule-N.json？"}
    UserDir -- 是 --> LoadUser["加载用户版本"]
    UserDir -- 否 --> LoadEmbed["加载内置版本"]
    LoadUser --> Normalize["规范化选择器"]
    LoadEmbed --> Normalize
    Normalize --> Ready["规则就绪"]
```

- **内置规则**：`src/ReadStorm.Infrastructure/rules/` 下的 20 个 JSON 文件，编译时嵌入程序集
- **用户规则**：`{工作目录}/rules/` 下的同名文件，优先级高于内置规则
- 用户可通过规则编辑器修改后保存到用户目录，也可重置为内置默认版本

---

## 10. 核心流程

### 10.1 搜索流程

```mermaid
sequenceDiagram
    actor User as 用户
    participant VM as SearchDownloadViewModel
    participant Search as HybridSearchBooksUseCase
    participant Loader as RuleFileLoader
    participant HTTP as HttpClient
    participant Parser as AngleSharp

    User->>VM: 输入关键词，点击搜索
    VM->>Search: ExecuteAsync(keyword, sourceId)
    Search->>Loader: 加载目标书源规则
    Loader-->>Search: FullBookSourceRule

    loop 每个书源
        Search->>HTTP: 发送搜索请求 (GET/POST)
        HTTP-->>Search: HTML 响应
        Search->>Parser: 使用 CSS 选择器提取结果
        Parser-->>Search: 书名 / 作者 / 最新章节
    end

    Search-->>VM: List<SearchResult>
    VM-->>User: 显示搜索结果列表
```

### 10.2 下载流程

```mermaid
sequenceDiagram
    actor User as 用户
    participant VM as SearchDownloadViewModel
    participant DL as RuleBasedDownloadBookUseCase
    participant Queue as SourceDownloadQueue
    participant Repo as SqliteBookRepository
    participant HTTP as HttpClient
    participant Parser as AngleSharp

    User->>VM: 双击搜索结果
    VM->>DL: QueueAsync(task, searchResult, mode)
    DL->>Queue: 加入下载队列

    Note over Queue: 队列按 FIFO 顺序处理

    Queue->>DL: 取出任务开始下载
    DL->>DL: 加载书源规则
    DL->>HTTP: 请求目录页
    HTTP-->>DL: 目录 HTML
    DL->>Parser: 提取章节链接列表
    Parser-->>DL: List<(title, url)>
    DL->>Repo: InsertChaptersAsync (Pending)

    loop 每个章节
        DL->>HTTP: 请求章节页面
        HTTP-->>DL: 章节 HTML
        DL->>Parser: 提取正文内容
        Parser-->>DL: 章节文本
        DL->>Repo: UpdateChapterAsync (Done)
        DL->>VM: 更新进度百分比
    end

    DL->>VM: 下载完成
    VM-->>User: 显示下载成功
```

### 10.3 阅读流程

```mermaid
sequenceDiagram
    actor User as 用户
    participant Shelf as BookshelfViewModel
    participant Main as MainWindowViewModel
    participant Reader as ReaderViewModel
    participant Repo as SqliteBookRepository

    User->>Shelf: 双击书籍
    Shelf->>Main: OpenDbBookAndSwitchToReaderAsync(book)
    Main->>Reader: 切换到阅读器页签
    Reader->>Repo: GetChaptersAsync(bookId)
    Repo-->>Reader: List<ChapterEntity>
    Reader->>Reader: 定位到上次阅读位置
    Reader-->>User: 显示章节内容

    User->>Reader: 点击"下一章"
    Reader->>Repo: 加载下一章内容
    Reader->>Repo: UpdateReadProgressAsync(bookId, index)
    Repo-->>Reader: 章节文本
    Reader-->>User: 显示下一章内容
```

### 10.4 书源编辑流程

```mermaid
flowchart TD
    Start["打开书源编辑器"] --> LoadList["加载所有规则列表"]
    LoadList --> Select["选择一个规则"]
    Select --> Edit["编辑 CSS 选择器 / URL"]
    Edit --> Test{"测试类型"}

    Test -- 搜索测试 --> TestSearch["TestSearchAsync<br/>发送请求 + 提取结果"]
    Test -- 目录测试 --> TestToc["TestTocAsync<br/>提取章节列表"]
    Test -- 章节测试 --> TestChapter["TestChapterAsync<br/>提取正文内容"]

    TestSearch --> Preview["预览提取结果"]
    TestToc --> Preview
    TestChapter --> Preview

    Preview --> Decide{"是否满意？"}
    Decide -- 是 --> Save["SaveAsync<br/>保存到用户目录"]
    Decide -- 否 --> Edit
    Save --> Done["规则已更新"]
```

---

## 11. 数据存储

### 11.1 工作目录

应用数据统一存放在用户专属目录中：

| 平台 | 路径 |
|------|------|
| Windows | `%APPDATA%\ReadStorm\` |
| Linux | `~/.readstorm/` |
| macOS | `~/.readstorm/` |

### 11.2 目录结构

```
{工作目录}/
├── ReadStorm.db          # SQLite 数据库
├── settings.json         # 应用设置
├── rules/                # 用户自定义规则（可选）
│   └── rule-N.json
└── logs/                 # 运行日志
    └── download-YYYY-MM-DD.log
```

### 11.3 数据库表结构

```mermaid
erDiagram
    BOOKS {
        int id PK
        string title
        string author
        string source_id
        string toc_url
        int total_chapters
        int done_chapters
        int read_chapter_index
        string read_chapter_title
        datetime read_at
        string cover_url
        string cover_image
        blob cover_blob
    }

    CHAPTERS {
        int id PK
        int book_id FK
        int index_no
        string title
        string content
        int status
        string source_id
        string source_url
        string error
        datetime updated_at
    }

    BOOKS ||--o{ CHAPTERS : "包含"
```

---

## 12. 构建与测试

### 12.1 构建

```bash
# 还原依赖并编译
dotnet build ReadStorm.slnx

# 发布（以 Windows x64 为例）
dotnet publish src/ReadStorm.Desktop -c Release -r win-x64
```

### 12.2 测试

```bash
# 运行全部测试
dotnet test ReadStorm.slnx

# 仅运行特定测试类
dotnet test ReadStorm.slnx --filter "FullyQualifiedName~EnhancedFeatureTests"
```

### 12.3 测试原则

项目遵循以下测试原则（详见 `tests/TESTING_PRINCIPLE.md`）：

- 测试调用**真实主项目代码**，通过 `ProjectReference` 引用
- 测试发现与执行自动化，测试用例由开发者手动编写
- 覆盖：状态机逻辑、持久化操作、规则解析、下载管线
- 不覆盖：外部 API 变化、平台特定 UI、性能指标

---

## 13. 目录结构

```
ReadStorm/
├── ReadStorm.slnx                           # 解决方案文件
├── README.md                                # 项目简介
├── LICENSE                                  # MIT 许可证
├── RELEASE_NOTES.md                         # 发行说明
│
├── src/
│   ├── ReadStorm.Domain/                    # 领域层
│   │   └── Models/
│   │       ├── BookEntity.cs                #   书籍实体
│   │       ├── ChapterEntity.cs             #   章节实体
│   │       ├── DownloadTask.cs              #   下载任务状态机
│   │       ├── SearchResult.cs              #   搜索结果
│   │       ├── BookSourceRule.cs            #   书源元数据
│   │       ├── FullBookSourceRule.cs         #   完整书源规则
│   │       ├── AppSettings.cs               #   应用设置
│   │       └── ...                          #   枚举与值对象
│   │
│   ├── ReadStorm.Application/               # 应用层
│   │   └── Abstractions/
│   │       ├── ISearchBooksUseCase.cs       #   搜索用例接口
│   │       ├── IDownloadBookUseCase.cs      #   下载用例接口
│   │       ├── IBookRepository.cs           #   数据仓储接口
│   │       └── ...                          #   其他用例接口
│   │
│   ├── ReadStorm.Infrastructure/            # 基础设施层
│   │   ├── Services/
│   │   │   ├── SqliteBookRepository.cs      #   SQLite 仓储实现
│   │   │   ├── RuleBasedDownloadBookUseCase.cs  # 下载引擎
│   │   │   ├── HybridSearchBooksUseCase.cs  #   混合搜索实现
│   │   │   ├── EpubExporter.cs              #   EPUB 导出
│   │   │   └── ...                          #   其他服务实现
│   │   └── rules/                           #   内置书源规则
│   │       ├── rule-1.json ~ rule-20.json
│   │       └── rule-template.json
│   │
│   └── ReadStorm.Desktop/                   # 桌面 UI 层
│       ├── App.axaml.cs                     #   DI 配置与启动
│       ├── Views/
│       │   └── MainWindow.axaml.cs          #   主窗口
│       └── ViewModels/
│           ├── MainWindowViewModel.cs       #   根 ViewModel
│           ├── SearchDownloadViewModel.cs   #   搜索下载
│           ├── BookshelfViewModel.cs        #   书架
│           ├── ReaderViewModel.cs           #   阅读器
│           └── ...                          #   其他 ViewModel
│
├── tests/
│   └── ReadStorm.Tests/                     # 测试项目
│       ├── DownloadPipelineIntegrationTests.cs  # 集成测试
│       └── EnhancedFeatureTests.cs          # 功能测试
│
└── docs/
    └── TechnicalGuide.md                    # 本文档
```
