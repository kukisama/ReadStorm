# ReadStorm v1.3.0

## 更新

- **2026-02-22（最新）**：修复 Android 点击“GitHub 项目主页”无法拉起外部浏览器的问题。已改为 Android 平台使用 `Intent(ActionView)` 显式调用系统浏览器。
- **2026-02-22**：关于页体验增强。桌面端与安卓端“关于”页新增 GitHub 项目入口（带图标，可点击跳转浏览器）。
- **2026-02-22**：阅读稳定性与交互优化。为书签添加/跳转/删除补充异常兜底，降低真机场景下未处理异常导致的闪退风险；同时将安卓阅读页书签按钮优化为“未收藏描边 / 已收藏实心”。
- **2026-02-22**：新增阅读记忆 + 书签能力（Desktop + Android）。支持恢复上次阅读位置（章节/页码）以及书签添加、删除、跳转，并支持目录/书签面板切换查看。
- **2026-02-22**：新增安卓阅读手势开关（左滑右滑）。仅在勾选后叠加滑动翻页；未勾选时保留点击热区翻页。
- **2026-02-21**：集中修复 Android 启动链路稳定性问题，覆盖启动闪退、过渡页异常与启动图链路等关键问题。

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

