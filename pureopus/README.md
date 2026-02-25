# ReadStorm – Pure Android Native (pureopus)

本目录包含 ReadStorm 的**纯 Android 原生**实现，使用 Kotlin + Android SDK 构建，不依赖任何跨平台框架（如 Avalonia / .NET）。

## 与原项目的对应关系

| 层级 | 原项目（Avalonia / C#） | 本项目（Android Native / Kotlin） |
|------|------------------------|----------------------------------|
| **Domain** | `ReadStorm.Domain/Models/` | `app/.../domain/models/` |
| **Application** | `ReadStorm.Application/Abstractions/` | `app/.../application/abstractions/` |
| **Infrastructure** | `ReadStorm.Infrastructure/Services/` | `app/.../infrastructure/services/` |
| **UI** | `ReadStorm.Android/Views/` (Avalonia AXAML) | `app/.../ui/` (Activities + Fragments + XML) |

## 架构

```
pureopus/
├── app/src/main/
│   ├── java/com/readstorm/app/
│   │   ├── domain/models/         # 数据模型（与 C# Domain 一一对应）
│   │   ├── application/abstractions/  # 用例接口
│   │   ├── infrastructure/services/   # SQLite、JSON 设置、规则加载等
│   │   └── ui/
│   │       ├── activities/        # SplashActivity, MainActivity, ReaderActivity
│   │       ├── fragments/         # 9 个 Fragment 对应原 TabControl 各页面
│   │       ├── adapters/          # RecyclerView 适配器
│   │       └── viewmodels/        # ViewModel（待实现）
│   └── res/
│       ├── layout/                # XML 布局
│       ├── values/                # 颜色、字符串、主题
│       ├── drawable/              # 背景、形状
│       └── menu/                  # 底部导航
├── build.gradle.kts
└── settings.gradle.kts
```

## 功能对照

| 功能 | 状态 |
|------|------|
| 🔎 搜索 & 下载 | ✅ 框架完成（SearchFragment + DownloadTasksFragment） |
| 📚 书架 | ✅ 框架完成（BookshelfFragment，2列宫格） |
| 📖 阅读器 | ✅ 框架完成（ReaderActivity，沉浸式全屏） |
| 🧩 规则编辑 | ✅ 框架完成（RuleEditorFragment，可展开表单） |
| 🩺 书源诊断 | ✅ 框架完成（DiagnosticFragment） |
| ⚙️ 设置 | ✅ 框架完成（SettingsFragment） |
| ℹ️ 关于 | ✅ 框架完成（AboutFragment + Markwon 渲染） |
| 📋 日志 | ✅ 框架完成（LogFragment） |
| 💾 SQLite 存储 | ✅ 完整实现（SqliteBookRepository） |
| 📁 JSON 设置 | ✅ 完整实现（JsonFileAppSettingsUseCase） |
| 📜 规则加载 | ✅ 完整实现（RuleFileLoader） |

## 构建

```bash
cd pureopus
./gradlew assembleDebug
```

需要 Android SDK（compileSdk 35）和 JDK 17。

## 依赖

| 库 | 用途 | 对应原项目 |
|----|------|-----------|
| Jsoup | HTML 解析 | AngleSharp |
| OkHttp | HTTP 客户端 | System.Net.Http |
| Gson | JSON 序列化 | System.Text.Json |
| Markwon | Markdown 渲染 | Markdown.Avalonia |
| AndroidX SQLite | 数据库 | Microsoft.Data.Sqlite |
| Material Components | UI 组件 | Semi.Avalonia |
