# ReadStorm v1.4.0

## 更新

- **2026-02-24（最新）**：阅读下载链路稳定性增强。前台阅读遇到占位章节时，新增“当前章优先直下”与高优先调度（`foreground-direct / force-current / manual-priority`），并配套 1 秒级刷新与去重重排，显著降低“卡在等待下载/下载中不刷新”的体感问题。
- **2026-02-24**：目录与跳章逻辑加固。目录项升级为稳定身份模型（`IndexNo + DisplayTitle`），并在桌面/安卓两端收敛选中回写路径，修复“无效选中后回到第一章”“目录点击后正文未及时切换”等问题。
- **2026-02-24**：书架与阅读体验修复。修复 Android 端书架偶发“短暂重复后恢复”现象；同时增强目录跳章交互稳健性（与桌面端一致的显式命令触发模式）。
- **2026-02-23**：Android 阅读沉浸显示优化。状态栏颜色跟随阅读背景、持续收敛顶部细亮线问题，并减少系统栏与正文交界处色差。
- **2026-02-23**：Android 启动与存储稳定性修复。改进启动图与主题阶段衔接策略，补强工作目录/数据库路径回退与初始化容错，降低重置数据后首启失败风险（含 SQLite Error 14 场景）。
- **2026-02-23**：下载与目录质量改进。新增章节标题归一化去重，减少重复章节进入阅读目录的概率。

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

