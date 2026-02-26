# 7.1 测试策略

[← 上一章：CI/CD 流水线](../06-packaging/03-ci-cd-pipeline.md) | [返回首页](../README.md) | [下一章：编写测试用例 →](02-writing-tests.md)

---

## 测试原则

ReadStorm 的测试遵循以下核心原则（详见 `tests/TESTING_PRINCIPLE.md`）：

### 1. ProjectReference 直接引用

测试项目通过 `ProjectReference` 直接引用主项目代码，而不是复制代码：

```xml
<!-- tests/ReadStorm.Tests/ReadStorm.Tests.csproj -->
<ItemGroup>
    <ProjectReference Include="../../src/ReadStorm.Domain/ReadStorm.Domain.csproj" />
    <ProjectReference Include="../../src/ReadStorm.Application/ReadStorm.Application.csproj" />
    <ProjectReference Include="../../src/ReadStorm.Infrastructure/ReadStorm.Infrastructure.csproj" />
</ItemGroup>
```

**好处**：测试的是真实编译后的代码，不是复制品。

### 2. 手写测试 + 自动发现

测试用例由开发者手动编写，xUnit 测试运行器自动发现和执行。

### 3. 范围边界

测试只验证被覆盖的场景，不保证覆盖所有边缘情况。测试的目标是 **回归保护**，而非 100% 覆盖率。

---

## 测试分类

| 类型 | 说明 | 示例 |
|------|------|------|
| **单元测试** | 测试单个类或方法的行为 | 规则解析、数据转换 |
| **集成测试** | 测试多个组件协作 | 搜索流程、数据库操作 |
| **脚本验证** | 验证构建脚本和配置 | 发布脚本正确性 |

---

## 测试框架

| 包 | 版本 | 用途 |
|----|------|------|
| xUnit | 2.9.3 | 测试框架核心 |
| Microsoft.NET.Test.Sdk | 17.14.1 | 测试运行器 |
| xunit.runner.visualstudio | - | IDE 集成 |

---

## 测试覆盖的层次

```
┌─────────────────────────────────────┐
│           可测试区域                 │
├─────────────────────────────────────┤
│ ✅ Domain 层       ← 纯逻辑，最易测 │
│ ✅ Application 层  ← 接口定义       │
│ ✅ Infrastructure  ← 需 Mock 或轻量集成│
│ ⚠️ ViewModel 层   ← 需注入依赖     │
│ ❌ View 层         ← UI 测试困难    │
└─────────────────────────────────────┘
```

### 推荐测试重点

1. **Domain 模型**：数据验证、状态转换
2. **Infrastructure 服务**：规则解析、URL 处理、数据访问
3. **ViewModel 逻辑**：业务流程、状态管理

---

## 运行测试

```bash
# 运行所有测试
dotnet test tests/ReadStorm.Tests/ReadStorm.Tests.csproj

# 详细输出
dotnet test tests/ReadStorm.Tests -v detailed

# 运行特定测试
dotnet test tests/ReadStorm.Tests --filter "FullyQualifiedName~RuleFileLoader"

# 带代码覆盖率
dotnet test tests/ReadStorm.Tests --collect:"XPlat Code Coverage"
```

---

## 测试命名约定

推荐的测试方法命名格式：

```
[被测方法]_[场景]_[期望结果]
```

示例：

```csharp
public class RuleFileLoaderTests
{
    [Fact]
    public void ResolveUrl_AbsoluteUrl_ReturnsUnchanged()
    {
        // ...
    }

    [Fact]
    public void ResolveUrl_RelativePath_CombinesWithBase()
    {
        // ...
    }

    [Fact]
    public void ResolveUrl_FileProtocol_ReturnsOriginal()
    {
        // ...
    }
}
```

---

## 小结

- ReadStorm 使用 xUnit 作为测试框架
- ProjectReference 确保测试的是真实编译代码
- 测试重点是 Domain 和 Infrastructure 层
- 目标是回归保护，不追求 100% 覆盖率

> 💡 具体如何编写测试用例，参见 [7.2 编写测试用例](02-writing-tests.md)

---

[← 上一章：CI/CD 流水线](../06-packaging/03-ci-cd-pipeline.md) | [返回首页](../README.md) | [下一章：编写测试用例 →](02-writing-tests.md)
