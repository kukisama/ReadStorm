# 8.2 Android 特有问题

[← 上一章：常见问题总览](01-common-issues.md) | [返回首页](../README.md) | [下一章：编译与部署问题 →](03-build-deploy-issues.md)

---

## 概述

Android 平台由于运行环境与桌面端差异较大，是 ReadStorm 遇到问题最多的平台。本章记录所有已知的 Android 特有问题及解决方案。

---

## EmbedAssemblies 问题

### 现象

Debug 模式联调时，应用安装到真机后启动立即崩溃，Logcat 中显示：

```
monodroid: No assemblies found in '...' or '...'
```

### 原因

.NET Android 项目默认在 Debug 模式下不将程序集嵌入 APK（`EmbedAssembliesIntoApk=false`），而是通过网络从开发机加载。但在某些网络环境或设备上这不可靠。

### 解决方案

构建时显式设置 `EmbedAssembliesIntoApk=true`：

```bash
dotnet build src/ReadStorm.Android/ReadStorm.Android.csproj \
    -p:EmbedAssembliesIntoApk=true
```

或在 `.csproj` 中设置：

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>
</PropertyGroup>
```

> ⚠️ 这会增加编译时间，但确保应用在真机上可靠启动。快速验证编译可用 `false`，但不可安装到真机。

---

## 文件路径问题

### 现象

`Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)` 在 Android 上返回空字符串，导致文件操作失败。

### 原因

Android 的沙箱机制不映射标准的 .NET 特殊文件夹。

### 解决方案

使用回退链选择可用路径：

```csharp
var basePath =
    GetNonEmptyPath(Environment.SpecialFolder.LocalApplicationData)
    ?? GetNonEmptyPath(Environment.SpecialFolder.Personal)
    ?? GetNonEmptyPath(Environment.SpecialFolder.ApplicationData)
    ?? AppContext.BaseDirectory;

string? GetNonEmptyPath(Environment.SpecialFolder folder)
{
    var p = Environment.GetFolderPath(folder);
    return string.IsNullOrEmpty(p) ? null : p;
}
```

**ReadStorm 中的实现**：`WorkDirectoryManager.cs` 和 `AppLogger.cs` 都使用了这个回退链策略。

---

## 外部链接打开失败

### 现象

用户点击 GitHub 项目链接等外部 URL 时，桌面端正常打开浏览器，Android 真机报错：

```
ErrorStartingProcess: No such file or directory
```

### 原因

`Process.Start(url)` 在桌面端通过 shell 可以识别 URL 并打开浏览器，但 Android 上 Process.Start 试图作为命令执行 URL。

### 解决方案

Android 端使用 `Intent` 打开外部链接：

```csharp
// Android 专用方式
var intent = new Android.Content.Intent(
    Android.Content.Intent.ActionView,
    Android.Net.Uri.Parse(url));
intent.AddFlags(Android.Content.ActivityFlags.NewTask);
Android.App.Application.Context.StartActivity(intent);
```

**ReadStorm 中的实现**：`SettingsViewModel.cs` 中包含了平台判断逻辑。

---

## URI 解析差异

### 现象

在规则引擎中，某些相对路径（如 `/chapter/123`）在 Linux/Android 上被错误地解析为 `file:///chapter/123`（绝对文件路径）。

### 原因

`Uri.TryCreate("/path", UriKind.Absolute)` 在 Windows 上返回 `false`（因为不是有效的 Windows 路径），但在 Linux/Android 上返回 `true`（因为 `/path` 是有效的 Unix 绝对路径）。

### 解决方案

在 URL 解析逻辑中过滤 `file://` 协议：

```csharp
public string ResolveUrl(string baseUrl, string relativeUrl)
{
    if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absolute))
    {
        // 关键：过滤掉被误判为绝对路径的 file:// URL
        if (absolute.IsFile)
            return new Uri(new Uri(baseUrl), relativeUrl).ToString();
        return absolute.ToString();
    }
    return new Uri(new Uri(baseUrl), relativeUrl).ToString();
}
```

**ReadStorm 中的实现**：`RuleFileLoader.cs` 的 `ResolveUrl` 方法。

---

## 存储权限问题

### 现象

Android 10+ 设备上访问外部存储报权限拒绝。

### 原因

Android 10 引入了 Scoped Storage（分区存储），限制了应用对外部存储的直接访问。

### 解决方案

- 使用 `GetExternalFilesDir(null)` 获取应用私有外部存储（无需权限）
- 不使用 `MANAGE_EXTERNAL_STORAGE` 权限（避免 Google Play 审核问题）
- 只为 Android 9 及以下保留 `WRITE_EXTERNAL_STORAGE`

```xml
<uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE"
                 android:maxSdkVersion="28" />
```

---

## 沉浸式状态栏

### 现象

应用内容被 Android 状态栏遮挡，或状态栏颜色不协调。

### 解决方案

Avalonia 11.3+ 使用 `InsetsManager`：

```csharp
var insetsManager = TopLevel.GetTopLevel(this)?.InsetsManager;
if (insetsManager != null)
{
    insetsManager.DisplayEdgeToEdgePreference = true;
}

// 同时禁用自动安全区域填充
TopLevel.AutoSafeAreaPadding = false;
```

> ⚠️ 旧版 Avalonia 的 `DisplayEdgeToEdge` 属性已废弃，使用新的 `DisplayEdgeToEdgePreference`。

---

## 主题兼容性

### 现象

Android 项目使用的主题必须继承 `Theme.AppCompat` 系列，否则某些控件可能无法正确显示。

### 解决方案

```xml
<!-- src/ReadStorm.Android/Resources/values/styles.xml -->
<style name="AppTheme" parent="Theme.AppCompat.Light.NoActionBar">
    <!-- 自定义属性 -->
</style>
```

---

## 问题排查工具

### ADB Logcat

```bash
# 查看应用日志
adb logcat -s monodroid:* Avalonia:* *:E

# 实时查看崩溃
adb logcat *:E | grep -i "readstorm\|avalonia\|mono"
```

### ADB Shell

```bash
# 查看应用数据目录
adb shell ls /data/data/com.readstorm.app/files/

# 拉取日志文件
adb pull /data/data/com.readstorm.app/files/logs/
```

---

## 小结

Android 平台的主要坑点：

| # | 问题 | 根本原因 | 状态 |
|---|------|----------|------|
| 1 | EmbedAssemblies | .NET Android 的部署模式 | ✅ 已解决 |
| 2 | 文件路径为空 | Android 沙箱机制 | ✅ 已解决 |
| 3 | 外链打不开 | Process.Start 不支持 | ✅ 已解决 |
| 4 | URI 解析差异 | Unix 路径规则 | ✅ 已解决 |
| 5 | 存储权限 | Scoped Storage | ✅ 已适配 |
| 6 | Emoji 不显示 | Skia 渲染引擎 | ✅ 已绕过 |

> 💡 Emoji/图标渲染问题详见 [8.4 UI 渲染问题](04-ui-rendering-issues.md)

---

[← 上一章：常见问题总览](01-common-issues.md) | [返回首页](../README.md) | [下一章：编译与部署问题 →](03-build-deploy-issues.md)
