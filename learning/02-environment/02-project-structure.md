# 2.2 项目结构解析

[← 上一章：开发环境配置](01-dev-environment-setup.md) | [返回首页](../README.md) | [下一章：编译与运行 →](03-build-and-run.md)

---

## 解决方案全景

ReadStorm 采用标准的 .NET 解决方案结构，解决方案文件 `ReadStorm.slnx` 定义了所有项目的组织关系。

```
ReadStorm/
├── ReadStorm.slnx                    ← 解决方案文件
├── README.md                         ← 项目说明
├── RELEASE_NOTES.md                  ← 发布说明（版本真源）
├── LICENSE                           ← MIT 开源协议
│
├── src/                              ← 源代码目录
│   ├── ReadStorm.Domain/             ← 领域层（核心模型）
│   ├── ReadStorm.Application/        ← 应用层（业务接口）
│   ├── ReadStorm.Infrastructure/     ← 基础设施层（具体实现）
│   ├── ReadStorm.Desktop/            ← 桌面端 UI
│   └── ReadStorm.Android/            ← Android 端 UI
│
├── tests/                            ← 测试目录
│   ├── ReadStorm.Tests/              ← xUnit 测试项目
│   └── TESTING_PRINCIPLE.md          ← 测试原则说明
│
├── scripts/                          ← 脚本工具
│   └── android-dev-oneclick.ps1      ← Android 一键开发脚本
│
├── Android/                          ← Android 适配文档
│   ├── 方案对比与选型.md
│   ├── 实施方案.md
│   └── ...
│
├── pureopus/                         ← 纯原生 Kotlin Android 实现
│   └── app/src/main/java/
│
├── learning/                         ← 学习教程（本手册）
│   └── ...
│
└── .github/                          ← CI/CD 配置
    └── workflows/
        └── release.yml
```

---

## 清洁架构分层

ReadStorm 严格遵循清洁架构（Clean Architecture）原则，依赖关系 **只能从外到内**：

```
┌─────────────────────────────────────────┐
│           UI 层 (Desktop / Android)      │
│  Views, ViewModels, 平台特定代码          │
├─────────────────────────────────────────┤
│         基础设施层 (Infrastructure)       │
│  SQLite, HTTP, 文件IO, 规则解析           │
├─────────────────────────────────────────┤
│           应用层 (Application)            │
│  用例接口, 服务抽象                       │
├─────────────────────────────────────────┤
│            领域层 (Domain)               │
│  实体模型, 枚举, 值对象                   │
└─────────────────────────────────────────┘
          ↑ 依赖方向（从外到内）
```

### 层间依赖规则

| 层 | 可以依赖 | 不可以依赖 |
|----|----------|-----------|
| Domain | 无外部依赖 | 任何其他层 |
| Application | Domain | Infrastructure, UI |
| Infrastructure | Domain, Application | UI |
| Desktop/Android | Domain, Application, Infrastructure | - |

---

## 各层详解

### Domain 层 - 领域模型

**职责**：定义业务的核心数据结构，不包含任何逻辑实现。

```
src/ReadStorm.Domain/
├── Models/
│   ├── BookEntity.cs              ← 图书实体
│   ├── BookRecord.cs              ← 图书记录
│   ├── ChapterEntity.cs           ← 章节实体
│   ├── ChapterStatus.cs           ← 章节状态
│   ├── SearchResult.cs            ← 搜索结果
│   ├── DownloadTask.cs            ← 下载任务
│   ├── DownloadTaskStatus.cs      ← 下载状态
│   ├── BookSourceRule.cs          ← 书源规则
│   ├── AppSettings.cs             ← 应用设置
│   ├── ReadingStateEntity.cs      ← 阅读状态
│   └── ...
└── ReadStorm.Domain.csproj
```

**关键特征**：零外部依赖，只使用 .NET 基础类型。

### Application 层 - 业务接口

**职责**：定义系统能做什么（接口），但不关心怎么做（实现）。

```
src/ReadStorm.Application/
├── Abstractions/
│   ├── ISearchBooksUseCase.cs     ← 搜索用例
│   ├── IDownloadBookUseCase.cs    ← 下载用例
│   ├── IBookRepository.cs         ← 数据仓库
│   ├── IBookshelfUseCase.cs       ← 书架用例
│   ├── IRuleCatalogUseCase.cs     ← 规则目录
│   ├── ISourceDiagnosticUseCase.cs ← 源诊断
│   └── ...
├── Services/
│   └── ReaderAutoPrefetchPolicy.cs ← 预读策略
└── ReadStorm.Application.csproj
```

### Infrastructure 层 - 具体实现

**职责**：实现 Application 层定义的所有接口。

```
src/ReadStorm.Infrastructure/
├── Services/
│   ├── SqliteBookRepository.cs         ← SQLite 数据存储
│   ├── RuleBasedSearchBooksUseCase.cs  ← 基于规则的搜索
│   ├── RuleBasedDownloadBookUseCase.cs ← 基于规则的下载
│   ├── HybridSearchBooksUseCase.cs     ← 混合搜索（聚合）
│   ├── EpubExporter.cs                 ← EPUB 导出
│   ├── RuleFileLoader.cs               ← 规则文件加载
│   ├── WorkDirectoryManager.cs         ← 工作目录管理
│   ├── AppLogger.cs                    ← 日志服务
│   └── ...
├── rules/                              ← 内置书源规则（JSON）
└── ReadStorm.Infrastructure.csproj
```

### UI 层 - 桌面端

```
src/ReadStorm.Desktop/
├── Program.cs                     ← 桌面端入口
├── App.axaml / App.axaml.cs       ← 应用配置
├── Views/
│   ├── MainWindow.axaml           ← 主窗口
│   ├── SearchView.axaml           ← 搜索页面
│   ├── BookshelfView.axaml        ← 书架页面
│   ├── ReaderView.axaml           ← 阅读器页面
│   ├── SettingsView.axaml         ← 设置页面
│   └── ...
├── ViewModels/
│   ├── MainWindowViewModel.cs     ← 主窗口 ViewModel
│   ├── SearchDownloadViewModel.cs ← 搜索下载
│   ├── BookshelfViewModel.cs      ← 书架
│   ├── ReaderViewModel.cs         ← 阅读器
│   └── ...
├── Styles/                        ← 样式文件
├── Converters/                    ← 数据转换器
└── ReadStorm.Desktop.csproj
```

> 💡 关于清洁架构的深入解析，参见 [4.1 清洁架构详解](../04-architecture/01-clean-architecture.md)

---

## 项目引用关系

```xml
<!-- ReadStorm.slnx 解决方案结构 -->
<Solution>
  <Folder Name="/src/">
    <Project Path="src/ReadStorm.Domain/ReadStorm.Domain.csproj" />
    <Project Path="src/ReadStorm.Application/ReadStorm.Application.csproj" />
    <Project Path="src/ReadStorm.Infrastructure/ReadStorm.Infrastructure.csproj" />
    <Project Path="src/ReadStorm.Desktop/ReadStorm.Desktop.csproj" />
    <Project Path="src/ReadStorm.Android/ReadStorm.Android.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/ReadStorm.Tests/ReadStorm.Tests.csproj" />
  </Folder>
</Solution>
```

引用链：

```
Desktop/Android → Infrastructure → Application → Domain
Tests → Infrastructure, Application, Domain
```

---

## 小结

- ReadStorm 采用清洁架构的四层分层设计
- 依赖方向严格从外向内
- Domain 层零依赖，可独立复用
- Infrastructure 负责所有外部交互
- 两个 UI 项目（Desktop/Android）共享所有内层代码

---

[← 上一章：开发环境配置](01-dev-environment-setup.md) | [返回首页](../README.md) | [下一章：编译与运行 →](03-build-and-run.md)
