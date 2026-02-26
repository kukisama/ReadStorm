# 6.2 Android 端打包

[← 上一章：桌面端打包](01-desktop-packaging.md) | [返回首页](../README.md) | [下一章：CI/CD 流水线 →](03-ci-cd-pipeline.md)

---

## 概述

ReadStorm Android 版基于 Avalonia.Android，最终输出为标准的 APK 文件。

---

## 前提条件

### 必需安装

1. **.NET 10 SDK** + Android 工作负载：
   ```bash
   dotnet workload install android
   ```

2. **JDK 17**：Android 构建需要
   ```bash
   java -version
   # openjdk version "17.x.x"
   ```

3. **Android SDK**：通过 Android Studio 或命令行工具安装

### 验证环境

```bash
# 确认 Android 工作负载
dotnet workload list
# 应包含 android

# 确认 JDK
java -version
```

---

## 编译 APK

### Debug 编译

```bash
# 标准编译
dotnet build src/ReadStorm.Android/ReadStorm.Android.csproj -c Debug

# 快速验证编译（不嵌入程序集，不可在真机运行）
dotnet build src/ReadStorm.Android/ReadStorm.Android.csproj \
    --no-restore -p:EmbedAssembliesIntoApk=false
```

> ⚠️ **关键坑点**：Debug 模式联调时**必须**设置 `EmbedAssembliesIntoApk=true`，否则真机启动会报 "No assemblies found" 并立即崩溃。`EmbedAssembliesIntoApk=false` 只能用于快速检查编译是否通过。

### Release 编译

```bash
dotnet publish src/ReadStorm.Android/ReadStorm.Android.csproj \
    -c Release \
    -o publish/android
```

---

## APK 签名

正式发布需要签名：

```bash
# 生成签名密钥（首次）
keytool -genkey -v -keystore readstorm.keystore \
    -keyalg RSA -keysize 2048 -validity 10000 \
    -alias readstorm

# 在 .csproj 中配置签名
# <AndroidKeyStore>True</AndroidKeyStore>
# <AndroidSigningKeyStore>readstorm.keystore</AndroidSigningKeyStore>
# <AndroidSigningKeyAlias>readstorm</AndroidSigningKeyAlias>
```

---

## 一键开发脚本

ReadStorm 提供了 PowerShell 一键脚本简化 Android 开发流程：

```powershell
# scripts/android-dev-oneclick.ps1
# 自动完成：编译 → 签名 → 安装 → 启动

.\scripts\android-dev-oneclick.ps1
```

脚本功能：
- 自动检测项目路径
- 编译 APK（确保 EmbedAssembliesIntoApk=true）
- 推导 APK 输出路径
- 安装到连接的设备
- 启动应用

---

## 安装到设备

### 通过 ADB

```bash
# 连接设备
adb devices

# 安装 APK
adb install -r path/to/readstorm.apk

# 启动应用
adb shell am start -n com.readstorm.app/.MainActivity
```

### 直接传输

将 APK 文件传输到手机，在文件管理器中点击安装。

---

## Android 特有配置

### AndroidManifest.xml

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
          package="com.readstorm.app">

    <!-- 网络权限（必需：书源访问） -->
    <uses-permission android:name="android.permission.INTERNET" />

    <!-- 存储权限（仅 Android 9 及以下需要） -->
    <uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE"
                     android:maxSdkVersion="28" />

    <application
        android:label="ReadStorm"
        android:icon="@mipmap/ic_launcher"
        android:theme="@style/AppTheme">
        <!-- ... -->
    </application>
</manifest>
```

### 存储策略

- Android 29+（Scoped Storage）：使用 `GetExternalFilesDir`，无需权限
- 数据存储在应用私有目录，USB 可访问
- 不使用 `MANAGE_EXTERNAL_STORAGE`，避免 Google Play 审核问题

---

## 常见问题

| 问题 | 原因 | 解决 |
|------|------|------|
| "No assemblies found" 崩溃 | EmbedAssembliesIntoApk=false | 设为 true |
| 编译超慢 | 正常现象 | 快速验证用 EmbedAssembliesIntoApk=false |
| 权限被拒绝 | Scoped Storage 限制 | 使用 GetExternalFilesDir |

> 💡 更多 Android 问题参见 [8.2 Android 特有问题](../08-troubleshooting/02-android-specific-issues.md)

---

## 小结

- Android APK 通过 `dotnet publish` 构建
- Debug 联调必须 `EmbedAssembliesIntoApk=true`
- 正式发布需要签名
- 使用一键脚本简化开发流程

---

[← 上一章：桌面端打包](01-desktop-packaging.md) | [返回首页](../README.md) | [下一章：CI/CD 流水线 →](03-ci-cd-pipeline.md)
