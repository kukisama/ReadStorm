# ReadStorm v1.1.0

## 更新
- 新增 **Android 平台支持**（提供 APK 安装包）
- 发布产物扩展为 **桌面端 + 安卓端** 双端交付
- Android 端已完成基础阅读、下载与规则能力适配，可用于日常使用与联调验证

---


### 运行前提

本版本为 **FDD（Framework-dependent）** 发布，需要预先安装：

- [**.NET 10 Runtime**](https://dotnet.microsoft.com/download/dotnet/10.0)
- Linux 用户需要 X11 或 Wayland 图形环境
- Android 用户需要允许安装 APK（不同系统版本入口可能为"允许此来源安装应用"）

### 使用方式

| 平台 | 包名 | 快速使用 |
|------|------|----------|
| Windows | `ReadStorm-win-x64-fdd.zip` | 解压后运行 `ReadStorm.Desktop.exe` |
| Linux | `ReadStorm-linux-x64-fdd.zip` | 解压 → `chmod +x ReadStorm.Desktop` → `./ReadStorm.Desktop` |
| macOS | `ReadStorm-osx-arm64-fdd.zip` | 解压后运行 `./ReadStorm.Desktop`（首次可能需在系统安全设置中放行） |
| Android | `ReadStorm-android*.apk` | 手机安装 APK；若拦截，开启"允许未知来源安装"后重试 |

