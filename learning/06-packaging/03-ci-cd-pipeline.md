# 6.3 CI/CD 流水线

[← 上一章：Android 端打包](02-android-packaging.md) | [返回首页](../README.md) | [下一章：测试策略 →](../07-testing/01-testing-strategy.md)

---

## 概述

ReadStorm 使用 GitHub Actions 实现自动化构建和发布。当推送版本标签时，自动构建所有平台的安装包并创建 GitHub Release。

---

## 工作流配置

工作流定义在 `.github/workflows/release.yml`：

### 触发条件

```yaml
on:
  push:
    tags:
      - 'v*'      # 匹配 v1.0.0 格式
      - '[0-9]*'  # 匹配纯数字格式
```

当推送匹配的标签时自动触发构建。

### 构建矩阵

```yaml
strategy:
  matrix:
    include:
      - rid: win-x64
        os: windows-latest
      - rid: win-arm64
        os: windows-latest
      - rid: linux-x64
        os: ubuntu-latest
      - rid: osx-arm64
        os: macos-latest
```

四个桌面平台 + Android 共五个并行构建任务。

---

## 构建流程

```
推送 v1.4.0 标签
    ↓
GitHub Actions 触发
    ↓
┌─────────────┬──────────────┬──────────────┬───────────────┬──────────┐
│ win-x64     │ win-arm64    │ linux-x64    │ osx-arm64     │ android  │
│ 编译+打包   │ 编译+打包    │ 编译+打包    │ 编译+打包      │ 编译 APK │
└──────┬──────┴──────┬───────┴──────┬───────┴───────┬───────┴────┬─────┘
       │             │              │               │            │
       └─────────────┴──────────────┴───────────────┴────────────┘
                              ↓
                    创建 GitHub Release
                    上传所有构建产物
                    发布说明（从 RELEASE_NOTES.md 读取）
```

### 关键步骤

```yaml
steps:
  # 1. 检出代码
  - uses: actions/checkout@v4

  # 2. 安装 .NET SDK
  - uses: actions/setup-dotnet@v4
    with:
      dotnet-version: '10.0.x'

  # 3. 恢复依赖
  - run: dotnet restore

  # 4. 发布
  - run: dotnet publish src/ReadStorm.Desktop -c Release -r ${{ matrix.rid }}

  # 5. 打包 ZIP
  - run: zip -r ReadStorm-${{ matrix.rid }}.zip publish/

  # 6. 上传到 Release
  - uses: softprops/action-gh-release@v2
    with:
      files: ReadStorm-*.zip
      body_path: RELEASE_NOTES.md
```

---

## 版本管理

ReadStorm 使用 `RELEASE_NOTES.md` 作为版本真源：

1. 在 `RELEASE_NOTES.md` 中更新版本号和发布说明
2. 推送代码
3. 创建并推送标签：`git tag v1.4.0 && git push origin v1.4.0`
4. GitHub Actions 自动构建并发布

```bash
# 发布流程
git add .
git commit -m "Release v1.4.0"
git tag v1.4.0
git push origin main --tags
```

---

## 构建产物

| 产物 | 说明 |
|------|------|
| `ReadStorm-win-x64.zip` | Windows 64位版本 |
| `ReadStorm-win-arm64.zip` | Windows ARM 版本 |
| `ReadStorm-linux-x64.zip` | Linux 64位版本 |
| `ReadStorm-osx-arm64.zip` | macOS Apple Silicon 版本 |
| `ReadStorm.apk` | Android APK |

---

## 发布说明

Release 的正文内容自动从 `RELEASE_NOTES.md` 读取，包含：

- 版本号和日期
- 平台支持列表
- 运行前提（.NET 10 Runtime）
- 新功能和修复列表
- 已知问题

---

## 小结

- GitHub Actions 实现全自动构建和发布
- 矩阵构建并行编译所有平台
- 版本信息从 RELEASE_NOTES.md 自动提取
- 推送标签即可触发完整发布流程

---

[← 上一章：Android 端打包](02-android-packaging.md) | [返回首页](../README.md) | [下一章：测试策略 →](../07-testing/01-testing-strategy.md)
