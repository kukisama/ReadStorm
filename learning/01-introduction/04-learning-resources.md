# 1.4 学习资源与方法

[← 上一章：同类技术对比](03-comparison-with-alternatives.md) | [返回首页](../README.md) | [下一章：开发环境配置 →](../02-environment/01-dev-environment-setup.md)

---

## 学习路线图

```
第一阶段：基础入门（1-2 周）
├── C# 基础语法
├── .NET CLI 基本操作
└── 简单控制台应用

第二阶段：框架学习（2-3 周）
├── Avalonia AXAML 基础
├── MVVM 模式理解
└── 简单桌面应用

第三阶段：项目实战（3-4 周）
├── 阅读 ReadStorm 源码
├── 理解清洁架构
└── 尝试添加新功能

第四阶段：进阶提升（持续）
├── 性能优化
├── 跨平台适配
└── CI/CD 自动化
```

---

## 官方文档

### C# 和 .NET

| 资源 | 链接 | 说明 |
|------|------|------|
| C# 官方文档 | https://learn.microsoft.com/dotnet/csharp/ | 语言规范和教程 |
| .NET 文档 | https://learn.microsoft.com/dotnet/ | 平台概述和 API 参考 |
| C# 语言参考 | https://learn.microsoft.com/dotnet/csharp/language-reference/ | 语法细节 |
| .NET API 浏览器 | https://learn.microsoft.com/dotnet/api/ | 所有 API 文档 |

### Avalonia

| 资源 | 链接 | 说明 |
|------|------|------|
| Avalonia 官方文档 | https://docs.avaloniaui.net/ | 框架使用指南 |
| Avalonia GitHub | https://github.com/AvaloniaUI/Avalonia | 源码和 Issue |
| Avalonia 示例库 | https://github.com/AvaloniaUI/Avalonia.Samples | 官方示例代码 |

### 相关框架

| 资源 | 链接 | 说明 |
|------|------|------|
| CommunityToolkit.Mvvm | https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/ | MVVM 工具包 |
| AngleSharp | https://anglesharp.github.io/ | HTML 解析库 |
| Semi.Avalonia | https://github.com/irihitech/Semi.Avalonia | UI 主题包 |

---

## 学习方法

### 1. 读源码的正确姿势

阅读 ReadStorm 源码的推荐顺序：

```
1. 先看 Domain 层    → 了解数据模型（最简单、无依赖）
2. 再看 Application  → 了解接口定义（知道系统能做什么）
3. 然后看 ViewModel  → 了解业务流程（UI 和逻辑的桥梁）
4. 接着看 View       → 了解界面实现（AXAML 怎么写）
5. 最后看 Infra      → 了解具体实现（怎么存储、怎么网络请求）
```

> 💡 **提示**：不要试图一次读懂所有代码。先选一个功能（比如搜索），从 UI 到后端追踪完整流程。

### 2. 动手实验

```bash
# 克隆项目
git clone https://github.com/kukisama/ReadStorm.git

# 尝试构建
dotnet build src/ReadStorm.Desktop

# 运行桌面版
dotnet run --project src/ReadStorm.Desktop

# 运行测试
dotnet test tests/ReadStorm.Tests
```

### 3. 逐步修改

在理解代码后，尝试小修改：

1. 修改一个按钮的文字
2. 添加一个简单的绑定
3. 在 ViewModel 中添加一个属性
4. 创建一个新的视图

### 4. 搜索问题的技巧

- **GitHub Issues**：`https://github.com/AvaloniaUI/Avalonia/issues` + 搜索关键词
- **Stack Overflow**：标签 `[avaloniaui]` 或 `[c#]`
- **Avalonia Telegram/Discord**：官方社区，响应快
- **NuGet**：搜索可用的包 `https://www.nuget.org/`

---

## 推荐书籍和视频

### 书籍

| 书名 | 适合阶段 | 说明 |
|------|----------|------|
| 《C# in Depth》 | 进阶 | 深入理解 C# 语言特性 |
| 《CLR via C#》 | 高级 | .NET 运行时原理 |
| 《Clean Architecture》 | 架构 | 清洁架构设计思想 |
| 《Head First Design Patterns》 | 入门 | 设计模式图解 |

### 在线课程

- **Microsoft Learn**：免费的 C# 和 .NET 交互式学习路径
- **Pluralsight**：系统化的 .NET 视频课程
- **YouTube**：搜索 "Avalonia tutorial" 有大量免费教程

---

## 常见学习误区

| 误区 | 正确做法 |
|------|----------|
| 试图一次学完所有内容 | 按功能模块逐步深入 |
| 只看文档不写代码 | 每学一个概念就动手实验 |
| 从零开始造轮子 | 先读现有项目（如 ReadStorm）的代码 |
| 遇到问题就放弃 | 善用搜索引擎和社区，参考 [故障排查](../08-troubleshooting/01-common-issues.md) |
| 忽视架构只关注语法 | 了解 [清洁架构](../04-architecture/01-clean-architecture.md) 等设计原则 |

---

## 小结

- 学习有路线，按阶段推进效率最高
- 官方文档是最权威的参考
- 动手实验比被动阅读更有效
- ReadStorm 本身就是最好的学习案例

---

[← 上一章：同类技术对比](03-comparison-with-alternatives.md) | [返回首页](../README.md) | [下一章：开发环境配置 →](../02-environment/01-dev-environment-setup.md)
