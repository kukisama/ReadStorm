# 6.1 桌面端打包

[← 上一章：跨平台适配](../05-development/06-cross-platform-adaptation.md) | [返回首页](../README.md) | [下一章：Android 端打包 →](02-android-packaging.md)

---

## 发布模式

ReadStorm 桌面端采用 **FDD（Framework-Dependent Deployment）** 模式发布——应用本身不包含 .NET Runtime，用户需要提前安装。

### FDD vs SCD 对比

| 维度 | FDD（ReadStorm 选择） | SCD |
|------|:---:|:---:|
| 安装包大小 | ~15MB | ~80MB+ |
| 需要预装 Runtime | ✅ 需要 | ❌ 不需要 |
| Runtime 安全更新 | 自动获取 | 需重新发布 |
| 启动速度 | 快 | 快 |

> 💡 选择 FDD 的设计理由参见 [4.4 设计决策与取舍](../04-architecture/04-design-decisions.md)

---

## dotnet publish 命令

### Windows x64

```bash
dotnet publish src/ReadStorm.Desktop/ReadStorm.Desktop.csproj \
    -c Release \
    -r win-x64 \
    --no-self-contained \
    -o publish/win-x64
```

### Windows ARM64

```bash
dotnet publish src/ReadStorm.Desktop/ReadStorm.Desktop.csproj \
    -c Release \
    -r win-arm64 \
    --no-self-contained \
    -o publish/win-arm64
```

### Linux x64

```bash
dotnet publish src/ReadStorm.Desktop/ReadStorm.Desktop.csproj \
    -c Release \
    -r linux-x64 \
    --no-self-contained \
    -o publish/linux-x64
```

### macOS ARM64

```bash
dotnet publish src/ReadStorm.Desktop/ReadStorm.Desktop.csproj \
    -c Release \
    -r osx-arm64 \
    --no-self-contained \
    -o publish/osx-arm64
```

---

## 发布参数说明

| 参数 | 说明 |
|------|------|
| `-c Release` | Release 配置（优化代码） |
| `-r win-x64` | 目标运行时标识符（RID） |
| `--no-self-contained` | FDD 模式 |
| `-o publish/win-x64` | 输出目录 |
| `-p:PublishSingleFile=true` | 单文件发布（可选） |
| `-p:PublishTrimmed=true` | 裁剪未使用的代码（慎用） |

### 可用的 RID

| RID | 平台 |
|-----|------|
| `win-x64` | Windows 64位 |
| `win-arm64` | Windows ARM |
| `linux-x64` | Linux 64位 |
| `osx-arm64` | macOS Apple Silicon |

---

## 打包为 ZIP

发布后通常打包为 ZIP 分发：

```bash
# 进入发布目录
cd publish/win-x64

# 打包
zip -r ../../ReadStorm-win-x64.zip .
```

---

## 用户运行前提

FDD 模式要求用户安装 .NET 10 Runtime：

- **下载地址**：https://dotnet.microsoft.com/download/dotnet/10.0
- **选择**：".NET Desktop Runtime"（桌面端）或 ".NET Runtime"（最小安装）
- **验证**：`dotnet --list-runtimes`

ReadStorm 在 `RELEASE_NOTES.md` 中明确提示了这个前提条件。

---

## 小结

- 桌面端使用 FDD 模式发布，安装包小
- `dotnet publish` 命令支持多平台交叉编译
- 发布后打包为 ZIP 分发
- 用户需预装 .NET 10 Runtime

> 💡 自动化发布流程参见 [6.3 CI/CD 流水线](03-ci-cd-pipeline.md)

---

[← 上一章：跨平台适配](../05-development/06-cross-platform-adaptation.md) | [返回首页](../README.md) | [下一章：Android 端打包 →](02-android-packaging.md)
