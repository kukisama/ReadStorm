# 8.3 编译与部署问题

[← 上一章：Android 特有问题](02-android-specific-issues.md) | [返回首页](../README.md) | [下一章：UI 渲染问题 →](04-ui-rendering-issues.md)

---

## .NET SDK 未找到

### 现象

```
error: SDK 'Microsoft.NET.Sdk' not found
```

### 解决方案

1. 确认安装了 .NET 10 SDK：
   ```bash
   dotnet --list-sdks
   ```
2. 如果未安装，从 https://dotnet.microsoft.com/download/dotnet/10.0 下载
3. 确认 `PATH` 环境变量包含 dotnet 路径

---

## NuGet 包恢复失败

### 现象

```
error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json
```

### 解决方案

1. **检查网络连接**
2. **使用镜像源**（国内用户）：

   创建或修改 `nuget.config`：
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
       <packageSources>
           <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
           <add key="huaweicloud"
                value="https://repo.huaweicloud.com/repository/nuget/v3/index.json" />
       </packageSources>
   </configuration>
   ```

3. **清理缓存**：
   ```bash
   dotnet nuget locals all --clear
   dotnet restore
   ```

---

## Android 编译速度优化

### 现象

Android 项目编译需要几分钟，严重影响开发效率。

### 优化方法

1. **快速验证编译**（不嵌入程序集）：
   ```bash
   dotnet build src/ReadStorm.Android/ReadStorm.Android.csproj \
       --no-restore -p:EmbedAssembliesIntoApk=false
   ```

2. **增量编译**：
   ```bash
   # 不清理直接编译，利用增量缓存
   dotnet build src/ReadStorm.Android/ReadStorm.Android.csproj
   ```

3. **减少构建范围**：
   ```bash
   # 只编译改动的项目
   dotnet build src/ReadStorm.Infrastructure --no-restore
   ```

> ⚠️ `EmbedAssembliesIntoApk=false` 构建的 APK 不能在真机上运行，仅用于编译验证。

---

## 目标框架版本问题

### 现象

```
error NETSDK1045: The current .NET SDK does not support targeting .NET 10.0.
```

### 解决方案

确保安装了对应版本的 SDK：

```bash
# 查看已安装的 SDK 版本
dotnet --list-sdks

# 如果版本不对，安装正确版本
# https://dotnet.microsoft.com/download/dotnet/10.0
```

---

## Android 工作负载缺失

### 现象

```
error NETSDK1147: To build this project, the .NET Android workload must be installed.
```

### 解决方案

```bash
# 安装 Android 工作负载
dotnet workload install android

# 验证安装
dotnet workload list
```

---

## 发布配置问题

### 现象

发布后的产物过大、缺少文件或无法运行。

### 排查清单

1. **确认发布模式**：
   ```bash
   # FDD（推荐）
   dotnet publish -c Release --no-self-contained

   # 不要误用 SCD
   # dotnet publish -c Release --self-contained  ← 包会很大
   ```

2. **确认 RID**：
   ```bash
   dotnet publish -r win-x64  # 指定目标平台
   ```

3. **检查产物**：
   ```bash
   ls -la publish/
   # 应包含 ReadStorm.Desktop.dll 和其他依赖
   ```

---

## 依赖冲突

### 现象

```
warning NU1608: Detected package version outside of dependency constraint
```

### 解决方案

1. 更新冲突的包到兼容版本
2. 在 `.csproj` 中指定明确的版本号
3. 使用 `dotnet list package --outdated` 检查过期包

---

## 多平台编译问题

### 现象

在 Linux 上编译 Windows 项目，或反过来。

### 解决方案

.NET 支持交叉编译（`-r` 参数），但某些平台特定的资源可能需要在对应平台上构建。

```bash
# 在 Linux 上构建 Windows 版本
dotnet publish -r win-x64 -c Release --no-self-contained

# 在 Windows 上构建 Linux 版本
dotnet publish -r linux-x64 -c Release --no-self-contained
```

> 💡 ReadStorm 的 CI/CD 在对应平台上构建，确保兼容性。参见 [6.3 CI/CD 流水线](../06-packaging/03-ci-cd-pipeline.md)。

---

## 小结

编译与部署问题的排查步骤：

1. 确认 SDK 版本和工作负载
2. 确认 NuGet 包可正常恢复
3. 区分 Debug 和 Release 配置
4. 区分 FDD 和 SCD 发布模式
5. 注意 Android 编译的特殊要求

---

[← 上一章：Android 特有问题](02-android-specific-issues.md) | [返回首页](../README.md) | [下一章：UI 渲染问题 →](04-ui-rendering-issues.md)
