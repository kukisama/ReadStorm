# A.1 术语表

[← 上一章：UI 渲染问题](../08-troubleshooting/04-ui-rendering-issues.md) | [返回首页](../README.md) | [下一章：参考链接 →](02-reference-links.md)

---

## 编程语言与平台

| 术语 | 英文 | 解释 |
|------|------|------|
| C# | C Sharp | 微软开发的面向对象编程语言 |
| .NET | dot NET | C# 的运行时平台和开发框架 |
| SDK | Software Development Kit | 软件开发工具包，包含编译器和工具 |
| Runtime | Runtime | 运行时环境，执行编译后的程序 |
| JIT | Just-In-Time | 即时编译，运行时将中间代码编译为机器码 |
| AOT | Ahead-Of-Time | 预编译，提前将代码编译为机器码 |
| IL | Intermediate Language | 中间语言，.NET 编译器生成的字节码 |
| GC | Garbage Collector | 垃圾回收器，自动管理内存 |
| NuGet | NuGet | .NET 的包管理器 |

## UI 框架

| 术语 | 英文 | 解释 |
|------|------|------|
| Avalonia | Avalonia UI | 跨平台 .NET UI 框架 |
| AXAML | Avalonia XAML | Avalonia 的 XML 标记语言 |
| XAML | eXtensible Application Markup Language | 可扩展应用标记语言 |
| Skia | Skia Graphics Library | Google 开源的 2D 图形引擎 |
| WPF | Windows Presentation Foundation | Windows 桌面 UI 框架 |
| MAUI | Multi-platform App UI | .NET 多平台应用 UI 框架 |
| MDI | Material Design Icons | Material Design 图标库 |

## 架构模式

| 术语 | 英文 | 解释 |
|------|------|------|
| MVVM | Model-View-ViewModel | 模型-视图-视图模型架构模式 |
| Clean Architecture | Clean Architecture | 清洁架构，强调依赖方向 |
| DI | Dependency Injection | 依赖注入，解耦组件依赖 |
| IoC | Inversion of Control | 控制反转，DI 的理论基础 |
| Repository | Repository Pattern | 仓库模式，封装数据访问 |
| Use Case | Use Case | 用例，表示一个业务操作 |

## 数据与存储

| 术语 | 英文 | 解释 |
|------|------|------|
| SQLite | SQLite | 嵌入式关系数据库 |
| CRUD | Create Read Update Delete | 增删改查操作 |
| ORM | Object-Relational Mapping | 对象关系映射 |
| ADO.NET | ActiveX Data Objects for .NET | .NET 数据访问技术 |
| JSON | JavaScript Object Notation | 轻量级数据交换格式 |

## 网络与解析

| 术语 | 英文 | 解释 |
|------|------|------|
| HTTP | HyperText Transfer Protocol | 超文本传输协议 |
| HTML | HyperText Markup Language | 超文本标记语言 |
| CSS | Cascading Style Sheets | 层叠样式表 |
| AngleSharp | AngleSharp | .NET 的 HTML/CSS 解析库 |
| DOM | Document Object Model | 文档对象模型 |
| Selector | CSS Selector | CSS 选择器，用于定位元素 |

## 异步编程

| 术语 | 英文 | 解释 |
|------|------|------|
| async/await | async/await | C# 异步编程关键字 |
| Task | Task | 表示异步操作的对象 |
| CancellationToken | Cancellation Token | 取消令牌，用于取消异步操作 |
| CTS | CancellationTokenSource | 取消令牌源 |
| Semaphore | Semaphore | 信号量，控制并发访问 |
| Deadlock | Deadlock | 死锁，线程互相等待 |

## 测试

| 术语 | 英文 | 解释 |
|------|------|------|
| xUnit | xUnit | .NET 单元测试框架 |
| Fact | Fact | xUnit 中的无参数测试 |
| Theory | Theory | xUnit 中的参数化测试 |
| Assert | Assert | 断言，验证测试结果 |
| Mock | Mock | 模拟对象，替代真实依赖 |
| AAA | Arrange-Act-Assert | 测试组织模式 |

## 构建与部署

| 术语 | 英文 | 解释 |
|------|------|------|
| FDD | Framework-Dependent Deployment | 依赖框架部署 |
| SCD | Self-Contained Deployment | 自包含部署 |
| RID | Runtime Identifier | 运行时标识符（如 win-x64） |
| APK | Android Package Kit | Android 安装包格式 |
| CI/CD | Continuous Integration/Deployment | 持续集成/持续部署 |
| GitHub Actions | GitHub Actions | GitHub 自动化工作流平台 |
| ADB | Android Debug Bridge | Android 调试桥接工具 |

## ReadStorm 专用

| 术语 | 英文 | 解释 |
|------|------|------|
| 书源 | Book Source | 图书内容的来源网站 |
| 规则 | Rule | 定义书源解析方式的 JSON 配置 |
| 规则引擎 | Rule Engine | 执行规则解析的通用引擎 |
| pureopus | Pure Opus | ReadStorm 的纯原生 Kotlin Android 实现 |
| 下载队列 | Download Queue | 管理章节下载的任务队列 |
| 聚合搜索 | Aggregate Search | 同时搜索多个书源的功能 |

---

[← 上一章：UI 渲染问题](../08-troubleshooting/04-ui-rendering-issues.md) | [返回首页](../README.md) | [下一章：参考链接 →](02-reference-links.md)
