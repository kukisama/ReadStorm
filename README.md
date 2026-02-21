# ⚡ ReadStorm（阅读风暴）

> 基于 **.NET 10 + Avalonia** 的跨平台阅读应用，覆盖桌面端与 Android。

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-11.3-blue.svg)](https://avaloniaui.net/)

ReadStorm 当前重点在于：**搜索下载、书架管理、阅读体验、规则处理、诊断与导出**。  
项目采用清晰分层（Domain / Application / Infrastructure / UI），便于持续迭代与测试回归。

---

## ✨ 当前能力（基于现有代码）

| 模块 | 说明 |
| --- | --- |
| 🔎 搜索下载 | 关键词检索、加入下载队列、任务状态过滤、暂停/恢复/重试/取消/删除 |
| 📚 书架管理 | 本地书籍列表、排序与筛选、继续阅读、续传、检查更新、删除 |
| 📖 阅读器 | 分页阅读、目录跳转、上下文章切换、主题与字体/行距调节、进度展示 |
| 🧩 规则处理 | 可视化规则编辑、测试与调试、保存与重置（高级功能） |
| 🩺 诊断能力 | 诊断信息输出、日志查看/清理、可选诊断日志开关 |
| 📤 数据导出 | 导出诊断日志、导出书库数据库、下载内容导出（TXT / EPUB） |
| ⚙️ 设置项 | 并发、导出格式、阅读参数、自动续传/更新、平台相关导出路径策略 |

---

## 🖥️ 平台支持

- **Desktop**：Windows / Linux / macOS（`src/ReadStorm.Desktop`）
- **Android**：`net10.0-android`（`src/ReadStorm.Android`）

> 说明：桌面端与 Android 端共用核心业务层，UI 层按平台做交互适配。

---

## 🚀 快速开始（使用发行包）

前往 [Releases](../../releases) 下载对应平台包。

桌面端常见启动方式：

- Windows：运行 `ReadStorm.Desktop.exe`
- Linux：`chmod +x ReadStorm.Desktop` 后执行 `./ReadStorm.Desktop`
- macOS：执行 `./ReadStorm.Desktop`（首次可能需在系统安全设置中放行）

---

## 🛠️ 从源码构建

### 1) 通用（桌面 + 测试）

```bash
git clone https://github.com/kukisama/ReadStorm.git
cd ReadStorm

dotnet build ReadStorm.slnx
dotnet test ReadStorm.slnx
dotnet run --project src/ReadStorm.Desktop
```

### 2) Android（推荐脚本）

项目已提供一键脚本：`scripts/android-dev-oneclick.ps1`。  
按项目约定，Android 本地联调优先通过该脚本执行（使用 `pwsh`）。

---

## 🧭 主要页面

当前桌面端主导航包含：

- 搜索下载
- 下载任务
- 诊断
- 书架
- 阅读
- 规则处理
- 设置
- 关于

Android 端采用移动端导航方式，功能分组与桌面端保持一致。

---

## 🧱 架构与目录

```text
ReadStorm/
├── src/
│   ├── ReadStorm.Domain/          # 领域模型
│   ├── ReadStorm.Application/     # 用例接口与抽象
│   ├── ReadStorm.Infrastructure/  # 基础设施实现（存储/网络/解析/导出等）
│   ├── ReadStorm.Desktop/         # 桌面 UI
│   └── ReadStorm.Android/         # Android UI
├── tests/
│   └── ReadStorm.Tests/           # xUnit 测试
├── docs/
│   ├── TechnicalGuide.md          # 技术架构说明
│   └── 变更日志.md                 # 变更记录（持续追加）
└── scripts/
    └── android-dev-oneclick.ps1   # Android 一键构建/联调脚本
```

---

## ✅ 质量保障

- 测试工程通过 `ProjectReference` 直接引用主项目代码（非拷贝测试）
- 支持 `dotnet test` 进行回归验证
- 更多测试说明见：`tests/TESTING_PRINCIPLE.md`

---

## 📚 相关文档

- 技术指南：[`docs/TechnicalGuide.md`](docs/TechnicalGuide.md)
- 变更记录：[`docs/变更日志.md`](docs/变更日志.md)
- 发布说明：[`RELEASE_NOTES.md`](RELEASE_NOTES.md)

---

## 📝 许可证

本项目基于 [MIT 许可证](LICENSE) 开源。
