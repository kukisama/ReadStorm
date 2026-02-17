# ReadStorm v1.0.0

## 更新
- 基于 Avalonia 的跨平台小说阅读器
- 多书源规则支持（内置 20+ 规则）
- 搜索、下载、书架管理
- 书源诊断与健康检查
- 内置阅读器

---

## 下载说明

| 文件 | 适用平台 |
|------|----------|
| `ReadStorm-win-x64-fdd.zip` | Windows x64（推荐） |
| `ReadStorm-win-arm64-fdd.zip` | Windows ARM64 |
| `ReadStorm-linux-x64-fdd.zip` | Linux x64 |
| `ReadStorm-osx-arm64-fdd.zip` | macOS Apple Silicon (M1/M2/M3) |

### 运行前提

本版本为 **FDD（Framework-dependent）** 发布，需要预先安装：

- [**.NET 10 Runtime**](https://dotnet.microsoft.com/download/dotnet/10.0)
- Linux 用户需要 X11 或 Wayland 图形环境

### 使用方式

**Windows：**
1. 下载 `ReadStorm-win-x64-fdd.zip`
2. 解压到任意目录
3. 运行 `ReadStorm.Desktop.exe`

**Linux：**
1. 下载 `ReadStorm-linux-x64-fdd.zip`
2. 解压并赋予执行权限：`chmod +x ReadStorm.Desktop`
3. 运行 `./ReadStorm.Desktop`

**macOS：**
1. 下载 `ReadStorm-osx-arm64-fdd.zip`
2. 解压后运行 `./ReadStorm.Desktop`
3. 首次运行可能需要在"系统设置 → 隐私与安全性"中允许运行
