# 5.6 跨平台适配

[← 上一章：规则引擎设计](05-rules-engine.md) | [返回首页](../README.md) | [下一章：桌面端打包 →](../06-packaging/01-desktop-packaging.md)

---

## 概述

ReadStorm 同时支持 Windows、Linux、macOS 桌面端和 Android 移动端。虽然 Avalonia + .NET 提供了跨平台基础，但实际开发中仍有大量平台差异需要处理。

---

## 共享与分离

### 代码共享比例

```
Domain 层          100% 共享
Application 层     100% 共享
Infrastructure 层  ~95% 共享（路径、权限有差异）
ViewModel 层       ~90% 共享（部分平台特定逻辑）
View 层            ~30% 共享（布局差异大）
```

### 项目组织

```
src/
├── ReadStorm.Domain/           ← 全平台共享
├── ReadStorm.Application/      ← 全平台共享
├── ReadStorm.Infrastructure/   ← 全平台共享
├── ReadStorm.Desktop/          ← 桌面端专用
│   ├── Views/MainWindow.axaml  ← 桌面窗口布局
│   └── ViewModels/             ← 桌面端 ViewModel
└── ReadStorm.Android/          ← Android 专用
    ├── Views/MainView.axaml    ← 移动端布局
    ├── MainActivity.cs         ← Android Activity
    └── AndroidSystemUiBridge.cs ← Android 系统调用
```

---

## 平台差异处理

### 1. 文件路径

不同平台的文件路径规则不同：

```csharp
// ❌ 硬编码路径分隔符
var path = "data\\books\\book1.db";

// ✅ 使用 Path.Combine
var path = Path.Combine("data", "books", "book1.db");
```

Android 特殊情况：

```csharp
// Android 上某些 SpecialFolder 返回空字符串
var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
// docs == "" 在 Android 上！

// ReadStorm 的回退链方案
var basePath =
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    .NullIfEmpty()
    ?? Environment.GetFolderPath(Environment.SpecialFolder.Personal)
    .NullIfEmpty()
    ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
    .NullIfEmpty()
    ?? AppContext.BaseDirectory;
```

### 2. 外部链接打开

```csharp
// 桌面端 - Process.Start 可以打开 URL
Process.Start(new ProcessStartInfo
{
    FileName = url,
    UseShellExecute = true
});

// Android 端 - 需要使用 Intent
var intent = new Intent(Intent.ActionView, AndroidUri.Parse(url));
intent.AddFlags(ActivityFlags.NewTask);
context.StartActivity(intent);
```

> ⚠️ 在 Android 上使用 `Process.Start(url)` 会报 "No such file or directory" 错误。

### 3. 状态栏和安全区域

```csharp
// Android 沉浸式状态栏（edge-to-edge）
// Avalonia 11.3+ 使用 InsetsManager
var insetsManager = TopLevel.GetTopLevel(this)?.InsetsManager;
if (insetsManager != null)
{
    insetsManager.DisplayEdgeToEdgePreference = true;
}

// 同时需要禁用自动安全区域填充
TopLevel.AutoSafeAreaPadding = false;
```

### 4. 权限管理

```xml
<!-- Android 权限声明 -->
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE"
                 android:maxSdkVersion="28" />
```

Android 29+（Scoped Storage）使用 `GetExternalFilesDir` 不需要额外权限。

---

## UI 适配策略

### 桌面端 vs 移动端布局

```
桌面端 (MainWindow.axaml)：
┌─────────────────────────────────────┐
│  侧边导航栏  │     内容区域          │
│  ┌────────┐ │  ┌──────────────────┐ │
│  │ 搜索   │ │  │                  │ │
│  │ 书架   │ │  │   当前页面内容    │ │
│  │ 阅读   │ │  │                  │ │
│  │ 设置   │ │  │                  │ │
│  └────────┘ │  └──────────────────┘ │
└─────────────────────────────────────┘

移动端 (MainView.axaml)：
┌──────────────────┐
│   状态栏          │
│   ┌──────────────┐│
│   │              ││
│   │  当前页面内容 ││
│   │              ││
│   │              ││
│   └──────────────┘│
│   ┌──────────────┐│
│   │搜索│书架│设置 ││  ← 底部 Tab 栏
│   └──────────────┘│
└──────────────────┘
```

### 响应式布局

```xml
<!-- 使用条件判断适配不同屏幕 -->
<Grid ColumnDefinitions="{Binding ColumnLayout}">
    <!-- 内容自适应 -->
</Grid>
```

---

## Android 系统桥接

ReadStorm 通过 `AndroidSystemUiBridge` 类封装所有 Android 平台特定调用：

```csharp
public class AndroidSystemUiBridge
{
    // 设置沉浸式状态栏
    public void SetEdgeToEdge() { ... }

    // 打开外部链接
    public void OpenExternalUrl(string url) { ... }

    // 获取安全存储路径
    public string GetSafeStoragePath() { ... }

    // 获取设备信息
    public string GetDeviceInfo() { ... }
}
```

---

## 条件编译（少量使用）

```csharp
// 极少数场景需要条件编译
#if ANDROID
    // Android 专用代码
    var path = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath;
#else
    // 桌面端代码
    var path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
#endif
```

> 💡 ReadStorm 尽量通过依赖注入和接口抽象来隔离平台差异，而非条件编译。

---

## 小结

- 业务逻辑层（Domain/Application/Infrastructure）几乎 100% 共享
- UI 层根据平台特点分别设计
- 平台差异通过接口抽象和桥接类隔离
- 关键差异点：文件路径、外链打开、权限、UI 布局
- 优先使用 DI + 接口抽象，避免条件编译

> 💡 Android 平台的具体问题和解决方案参见 [8.2 Android 特有问题](../08-troubleshooting/02-android-specific-issues.md)

---

[← 上一章：规则引擎设计](05-rules-engine.md) | [返回首页](../README.md) | [下一章：桌面端打包 →](../06-packaging/01-desktop-packaging.md)
