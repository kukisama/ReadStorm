# ReadStorm 变更记录

> 记录每次功能实现与增强的操作记录。

## 2026-02-15 功能补齐与阅读器一体化

### 1. 下载任务面板增强

- **新增 `Paused` 下载状态**：`DownloadTaskStatus` 枚举增加 `Paused = 6`，状态机支持 `Downloading ↔ Paused` 和 `Paused → Cancelled` 流转。
- **失败重试机制**：`DownloadTask` 新增 `ResetForRetry()` 方法，允许 `Failed`/`Cancelled` 状态的任务重置为 `Queued` 并自增 `RetryCount`。
- **CanRetry / CanCancel 属性**：`DownloadTask` 新增计算属性，UI 按钮可直接绑定控制显示/隐藏。
- **SourceSearchResult 回存**：下载任务保存原始 `SearchResult` 引用，重试时无需重新搜索。
- **任务状态过滤**：ViewModel 新增 `TaskFilterStatus` 属性与 `FilteredDownloadTasks` 集合，UI 提供下拉过滤（全部/Queued/Downloading/Succeeded/Failed/Cancelled/Paused）。
- **重试按钮**：下载任务列表每项显示"重试"按钮（仅 Failed/Cancelled 可见），点击触发 `RetryDownloadCommand`。
- **取消按钮**：每项显示"取消"按钮（仅 Queued/Downloading/Paused 可见），点击触发 `CancelDownloadCommand`。
- **文件变更**：
  - `src/ReadStorm.Domain/Models/DownloadTaskStatus.cs` — 增加 Paused
  - `src/ReadStorm.Domain/Models/DownloadTask.cs` — ResetForRetry, CanRetry, CanCancel, RetryCount, SourceSearchResult
  - `src/ReadStorm.Desktop/ViewModels/MainWindowViewModel.cs` — RetryDownloadCommand, CancelDownloadCommand, TaskFilterStatus
  - `src/ReadStorm.Desktop/Views/MainWindow.axaml` — 下载任务 Tab 增强

### 2. 书源诊断页

- **SourceDiagnosticResult 模型**：Domain 层新增诊断结果模型，包含搜索/目录/章节规则命中状态、HTTP 状态码、搜索结果数、selector 信息、诊断日志行。
- **ISourceDiagnosticUseCase 接口**：Application 层新增书源诊断用例抽象。
- **RuleBasedSourceDiagnosticUseCase 实现**：Infrastructure 层实现，对指定书源执行规则检查（搜索/目录/章节 selector 是否配置）、HTTP 连通性测试、搜索结果 selector 命中测试。
- **诊断 UI Tab**：新增"书源诊断"Tab，提供书源 ID 输入、测试关键字输入、诊断按钮，显示诊断摘要（规则命中/HTTP 状态/搜索结果数/selector 信息）和详细诊断日志（等宽字体，逐行展示）。
- **文件变更**：
  - `src/ReadStorm.Domain/Models/SourceDiagnosticResult.cs` — 新增
  - `src/ReadStorm.Application/Abstractions/ISourceDiagnosticUseCase.cs` — 新增
  - `src/ReadStorm.Infrastructure/Services/RuleBasedSourceDiagnosticUseCase.cs` — 新增
  - `src/ReadStorm.Desktop/ViewModels/MainWindowViewModel.cs` — RunDiagnosticCommand
  - `src/ReadStorm.Desktop/Views/MainWindow.axaml` — 书源诊断 Tab

### 3. 导出与章节策略增强

- **EPUB 导出**：新增 `EpubExporter` 静态类，无外部依赖，基于 `System.IO.Compression` 生成 EPUB 3 格式文件。包含 mimetype、container.xml、content.opf、toc.ncx、toc.xhtml（导航）以及每章 XHTML 文件。
- **导出格式切换**：`RuleBasedDownloadBookUseCase` 根据用户设置 `ExportFormat` 自动选择 TXT 或 EPUB 导出。
- **设置页改为下拉选择**：导出格式从文本输入改为 ComboBox（txt/epub）。
- **文件变更**：
  - `src/ReadStorm.Infrastructure/Services/EpubExporter.cs` — 新增
  - `src/ReadStorm.Infrastructure/Services/RuleBasedDownloadBookUseCase.cs` — 增加 EPUB 分支
  - `src/ReadStorm.Desktop/Views/MainWindow.axaml` — 设置 Tab 导出格式改为下拉

### 4. 阅读器一体化

- **BookRecord / ReadingProgress 模型**：Domain 层新增书架记录和阅读进度模型。
- **IBookshelfUseCase 接口**：Application 层新增书架用例抽象（获取全部、添加、移除、更新进度）。
- **JsonFileBookshelfUseCase 实现**：Infrastructure 层实现，基于 JSON 文件持久化书架数据，原子写入（临时文件 + 重命名）。
- **下载完成自动入架**：下载成功后自动将书籍添加到书架（重复书籍更新文件路径）。
- **书架 Tab**：新增"书架"Tab，展示已下载书籍列表（标题、作者、格式、添加时间、上次阅读章节），每项提供"阅读"和"移除"按钮。
- **阅读器 Tab**：新增"阅读"Tab，支持 TXT 格式书籍的章节解析与展示。功能包含：
  - 目录跳转（ComboBox 选择章节）
  - 上一章/下一章导航
  - 阅读进度自动保存与恢复
  - 章节标题识别（第X章/第X节/第X回/Chapter X 模式）
- **DI 注册**：App.axaml.cs 注册 `ISourceDiagnosticUseCase` 和 `IBookshelfUseCase`。
- **文件变更**：
  - `src/ReadStorm.Domain/Models/BookRecord.cs` — 新增
  - `src/ReadStorm.Application/Abstractions/IBookshelfUseCase.cs` — 新增
  - `src/ReadStorm.Infrastructure/Services/JsonFileBookshelfUseCase.cs` — 新增
  - `src/ReadStorm.Desktop/ViewModels/MainWindowViewModel.cs` — 书架/阅读器逻辑
  - `src/ReadStorm.Desktop/Views/MainWindow.axaml` — 书架 Tab、阅读 Tab
  - `src/ReadStorm.Desktop/App.axaml.cs` — DI 注册

### 5. 测试补充

- 新增 `EnhancedFeatureTests` 测试类，包含 12 个测试用例：
  - 重试机制（CanRetry 状态判断、ResetForRetry 重置、非法状态重试异常）
  - 取消机制（CanCancel 状态判断）
  - 暂停状态流转（Paused → Downloading / Cancelled）
  - EPUB 导出（文件结构验证、内容验证、导航验证）
  - 书架持久化（增/删/查/进度更新/空文件处理）
  - 下载管线 EPUB 导出集成测试
- **文件变更**：
  - `tests/ReadStorm.Tests/EnhancedFeatureTests.cs` — 新增
