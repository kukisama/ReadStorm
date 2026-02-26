# 2.1 开发环境配置

[← 上一章：学习资源与方法](../01-introduction/04-learning-resources.md) | [返回首页](../README.md) | [下一章：项目结构解析 →](02-project-structure.md)

---

## 前提条件

开始 ReadStorm 开发前，你需要准备以下工具。

---

## 必需工具

### 1. .NET 10 SDK

ReadStorm 基于 .NET 10 构建，必须安装对应版本的 SDK。

**下载地址**：https://dotnet.microsoft.com/download/dotnet/10.0

**验证安装**：

```bash
dotnet --version
# 应输出 10.0.x

dotnet --list-sdks
# 应包含 10.0.x 版本
```

> ⚠️ **注意**：确保安装的是 SDK 而非仅 Runtime。SDK 包含编译工具，Runtime 只能运行已编译的程序。

### 2. IDE / 编辑器

推荐以下 IDE（按推荐度排序）：

| IDE | 平台 | 特点 | 推荐指数 |
|-----|------|------|---------|
| **JetBrains Rider** | 全平台 | AXAML 预览、强大重构 | ⭐⭐⭐⭐⭐ |
| **Visual Studio 2022** | Windows | 官方支持、调试强大 | ⭐⭐⭐⭐ |
| **VS Code** | 全平台 | 轻量、需装插件 | ⭐⭐⭐ |

**VS Code 推荐插件**：

- C# (ms-dotnettools.csharp)
- C# Dev Kit (ms-dotnettools.csdevkit)
- Avalonia for VSCode (avaloniateam.vscode-avalonia)

### 3. Git

版本控制必备：

```bash
git --version
# 应输出 2.x.x

# 基本配置
git config --global user.name "你的名字"
git config --global user.email "你的邮箱"
```

---

## Android 开发（可选）

如果需要构建 Android 版本，还需要额外配置：

### 1. Android SDK

```bash
# 安装 .NET Android 工作负载
dotnet workload install android
```

### 2. Java JDK

Android 构建需要 JDK 17：

```bash
java -version
# 应输出 openjdk 17.x.x 或更高
```

### 3. Android SDK 组件

需要以下组件：

- Android SDK Build-Tools
- Android SDK Platform（API Level 对应目标版本）
- Android SDK Platform-Tools

> 💡 **提示**：如果你只需要桌面端开发，可以跳过 Android 配置。详细的 Android 环境配置参见 [6.2 Android 端打包](../06-packaging/02-android-packaging.md)。

---

## 克隆项目

```bash
# 克隆 ReadStorm 仓库
git clone https://github.com/kukisama/ReadStorm.git
cd ReadStorm

# 查看项目结构
ls -la
```

---

## 恢复依赖

```bash
# 恢复所有 NuGet 包
dotnet restore

# 如果只恢复桌面端项目
dotnet restore src/ReadStorm.Desktop/ReadStorm.Desktop.csproj
```

---

## 验证环境

```bash
# 编译项目（验证环境是否正确）
dotnet build src/ReadStorm.Desktop/ReadStorm.Desktop.csproj

# 如果成功，说明环境配置正确
# 如果失败，查看错误信息并参考故障排查章节
```

> ⚠️ 编译失败？参见 [8.3 编译与部署问题](../08-troubleshooting/03-build-deploy-issues.md)

---

## 环境变量（可选）

某些场景可能需要设置环境变量：

```bash
# Linux/macOS
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$DOTNET_ROOT

# Windows PowerShell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet"
```

---

## 小结

最小化开发环境清单：

- [x] .NET 10 SDK
- [x] IDE（推荐 Rider 或 VS）
- [x] Git
- [ ] Android SDK（可选，移动端开发需要）
- [ ] JDK 17（可选，Android 构建需要）

---

[← 上一章：学习资源与方法](../01-introduction/04-learning-resources.md) | [返回首页](../README.md) | [下一章：项目结构解析 →](02-project-structure.md)
