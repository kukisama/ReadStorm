# ReadStorm v1.2.0

## 更新
- **2026-02-21（最新）**：集中修复 Android 启动链路稳定性问题。针对“点击图标闪退 / 白屏或蓝屏过渡异常 / 启动图不显示或过小”进行了归并治理：修正路由启动与主题资源、修复 `launch_background` 解析崩溃、统一 `boot.jpg` 资源链路，并将启动页停留时间下调，显著降低体感等待。
- **2026-02-21**：修复 Android 一键联调关键阻塞。处理 `INSTALL_FAILED_UPDATE_INCOMPATIBLE` 自动恢复安装流程，并修正 Debug 安装路径运行时缺失程序集导致的“启动即退”。
- **2026-02-21**：阅读体验与参数调节进一步收敛。完善分页与切行策略、支持音量键翻页、补齐阅读内实时调参与“恢复推荐参数”，并同步默认值以提升开箱即用体验。
- **2026-02-21**：修复书架页稳定性与布局问题。解决书架 DataTemplate 绑定导致的进入即崩，并持续优化进度条与卡片布局在不同尺寸下的显示一致性。
- **2026-02-21**：品牌与交互细节更新。应用名称统一为“阅读风暴”，并补充桌面阅读快捷键提示，降低学习成本。
- **2026-02-20**：完成一轮跨端体验优化：包括阅读沉浸/刘海区域适配、启动白屏治理、关于页独立化与 Markdown 展示、导出流程优化，以及 Android 图标与版本链路统一。

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

