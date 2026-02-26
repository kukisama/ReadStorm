# 2.3 编译与运行

[← 上一章：项目结构解析](02-project-structure.md) | [返回首页](../README.md) | [下一章：C# 核心语言特性 →](../03-language-features/01-csharp-core-features.md)

---

## 桌面端编译

### 基本编译

```bash
# 恢复 NuGet 依赖
dotnet restore

# 编译桌面端项目
dotnet build src/ReadStorm.Desktop/ReadStorm.Desktop.csproj

# 编译并运行
dotnet run --project src/ReadStorm.Desktop/ReadStorm.Desktop.csproj
```

### 调试模式 vs 发布模式

```bash
# Debug 模式（默认，包含调试信息）
dotnet build -c Debug

# Release 模式（优化代码，去掉调试信息）
dotnet build -c Release
```

---

## Android 端编译

### 基本编译

```bash
# 编译 Android 项目
dotnet build src/ReadStorm.Android/ReadStorm.Android.csproj
```

### 快速验证编译

在 Android 开发中，完整编译比较慢。快速验证可以使用：

```bash
# 不嵌入程序集（更快但只用于验证编译）
dotnet build src/ReadStorm.Android/ReadStorm.Android.csproj --no-restore -p:EmbedAssembliesIntoApk=false
```

> ⚠️ **注意**：使用 `EmbedAssembliesIntoApk=false` 构建的 APK 无法在设备上正常运行，仅用于编译检查。真机调试必须用 `EmbedAssembliesIntoApk=true`，否则会报 "No assemblies found" 错误。详见 [8.2 Android 特有问题](../08-troubleshooting/02-android-specific-issues.md)。

### 生成 APK

```bash
# 生成可安装的 APK
dotnet publish src/ReadStorm.Android/ReadStorm.Android.csproj -c Release
```

---

## 运行测试

```bash
# 运行所有测试
dotnet test tests/ReadStorm.Tests/ReadStorm.Tests.csproj

# 运行特定测试
dotnet test tests/ReadStorm.Tests --filter "FullyQualifiedName~SearchTest"

# 详细输出
dotnet test tests/ReadStorm.Tests -v detailed
```

> 💡 关于测试的详细说明，参见 [7.1 测试策略](../07-testing/01-testing-strategy.md)

---

## 常用 .NET CLI 命令

| 命令 | 用途 |
|------|------|
| `dotnet restore` | 恢复 NuGet 包 |
| `dotnet build` | 编译项目 |
| `dotnet run` | 编译并运行 |
| `dotnet test` | 运行测试 |
| `dotnet publish` | 发布（打包） |
| `dotnet clean` | 清理编译输出 |
| `dotnet --info` | 显示 SDK 和运行时信息 |

---

## 编译问题排查

### 常见编译错误

| 错误 | 原因 | 解决方案 |
|------|------|----------|
| `SDK not found` | .NET 10 SDK 未安装 | 安装 SDK，参见 [2.1](01-dev-environment-setup.md) |
| `Package restore failed` | NuGet 源不可达 | 检查网络或 NuGet 镜像配置 |
| `Target framework not supported` | SDK 版本不匹配 | 确认安装了 net10.0 工作负载 |
| `Android workload not installed` | 缺少 Android 工作负载 | `dotnet workload install android` |

> 更多编译问题参见 [8.3 编译与部署问题](../08-troubleshooting/03-build-deploy-issues.md)

---

## 开发工作流

推荐的日常开发流程：

```
1. git pull                          ← 拉取最新代码
2. dotnet restore                    ← 恢复依赖
3. dotnet build                      ← 编译确认
4. 修改代码                           ← 开发新功能
5. dotnet build                      ← 再次编译
6. dotnet test                       ← 运行测试
7. dotnet run --project Desktop      ← 运行验证
8. git commit & push                 ← 提交代码
```

---

## 小结

- 桌面端使用 `dotnet run` 即可快速启动
- Android 端编译较慢，可用 `EmbedAssembliesIntoApk=false` 快速验证
- `dotnet test` 运行测试确保代码质量
- 遇到编译问题先查看 [故障排查](../08-troubleshooting/03-build-deploy-issues.md)

---

[← 上一章：项目结构解析](02-project-structure.md) | [返回首页](../README.md) | [下一章：C# 核心语言特性 →](../03-language-features/01-csharp-core-features.md)
