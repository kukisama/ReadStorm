# ⚡ ReadStorm

> **跨平台小说阅读器** — 搜索、下载、阅读，一站搞定。

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-11.3-blue.svg)](https://avaloniaui.net/)

ReadStorm 是一款基于 **.NET 10 + Avalonia** 构建的跨平台桌面小说阅读器。内置 **20+ 书源规则**，支持一键搜索、批量下载、离线阅读，并可将内容导出为 TXT 或 EPUB。无论你是网文爱好者还是轻量阅读需求者，ReadStorm 都能提供流畅、简洁的阅读体验。

---

## ✨ 功能亮点

| 功能 | 说明 |
|------|------|
| 🔍 **多书源搜索** | 同时查询多个书源，快速找到目标小说 |
| 📥 **智能下载** | 支持全本下载、范围下载、最新章节追更 |
| 📖 **内置阅读器** | 可自定义字体、配色、行距，多种纸张主题预设 |
| 📚 **书架管理** | 自动记录阅读进度，随时继续上次阅读 |
| 📤 **格式导出** | 一键导出为 TXT 或 EPUB 格式 |
| 🔧 **书源编辑器** | 可视化创建、编辑、测试自定义书源规则 |
| 🩺 **书源诊断** | 内置健康检查与诊断工具，快速排查书源问题 |
| 🖥️ **跨平台** | 原生支持 Windows、Linux、macOS |

---

## 🖼️ 界面预览

<!-- 如果有截图，可以放在此处 -->
<!-- ![主界面](docs/images/main.png) -->

---

## 🚀 快速开始

### 环境要求

- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows x64 / ARM64、Linux x64（X11 或 Wayland）、macOS ARM64（M1/M2/M3）

### 下载安装

前往 [Releases](../../releases) 页面，根据你的操作系统下载对应版本：

| 文件 | 适用平台 |
|------|----------|
| `ReadStorm-win-x64-fdd.zip` | Windows x64（推荐） |
| `ReadStorm-win-arm64-fdd.zip` | Windows ARM64 |
| `ReadStorm-linux-x64-fdd.zip` | Linux x64 |
| `ReadStorm-osx-arm64-fdd.zip` | macOS Apple Silicon |

### 运行方式

**Windows：**
```bash
# 解压后双击运行
ReadStorm.Desktop.exe
```

**Linux：**
```bash
chmod +x ReadStorm.Desktop
./ReadStorm.Desktop
```

**macOS：**
```bash
./ReadStorm.Desktop
# 首次运行需在"系统设置 → 隐私与安全性"中允许
```

---

## 📖 使用指南

### 1. 搜索小说

打开应用后进入 **搜索** 页签，输入关键词，选择书源（或使用全部书源），点击搜索。搜索结果会显示书名、作者和最新章节信息。

### 2. 下载小说

在搜索结果中双击目标小说即可加入下载队列。支持三种下载模式：

- **全本下载**：下载所有章节
- **范围下载**：指定起止章节
- **追更模式**：仅下载最新 N 章

下载过程支持暂停、恢复和重试。

### 3. 阅读小说

下载完成后进入 **书架** 页签，双击书籍即可打开内置阅读器。阅读器支持：

- 上一章 / 下一章快捷切换
- 目录跳转
- 自定义字体大小、颜色、背景色
- 多种纸张主题预设（如护眼模式、夜间模式）

### 4. 导出内容

在设置中选择导出格式（TXT 或 EPUB），即可将已下载的书籍导出到本地。

### 5. 自定义书源

进入 **书源编辑器** 页签，可以：

- 查看和修改内置书源规则
- 创建新书源（通过 CSS 选择器定义内容提取规则）
- 在线测试搜索、目录、章节提取效果
- 重置书源到默认配置

---

## 🛠️ 从源码构建

```bash
# 克隆仓库
git clone https://github.com/kukisama/ReadStorm.git
cd ReadStorm

# 构建
dotnet build ReadStorm.slnx

# 运行
dotnet run --project src/ReadStorm.Desktop

# 运行测试
dotnet test ReadStorm.slnx
```

---

## 📁 项目结构

```
ReadStorm/
├── src/
│   ├── ReadStorm.Domain/          # 领域模型（纯 C#，无外部依赖）
│   ├── ReadStorm.Application/     # 用例接口与抽象定义
│   ├── ReadStorm.Infrastructure/  # 基础设施实现（数据库、HTTP、HTML 解析）
│   └── ReadStorm.Desktop/         # Avalonia 桌面 UI
├── tests/
│   └── ReadStorm.Tests/           # xUnit 单元测试与集成测试
└── docs/
    └── TechnicalGuide.md          # 技术架构指南
```

更多技术细节请参阅 [技术指南](docs/TechnicalGuide.md)。

---

## 📝 许可证

本项目基于 [MIT 许可证](LICENSE) 开源。
