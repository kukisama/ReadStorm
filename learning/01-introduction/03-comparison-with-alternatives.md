# 1.3 同类技术方案对比

[← 上一章：Avalonia 框架介绍](02-avalonia-framework.md) | [返回首页](../README.md) | [下一章：学习资源与方法 →](04-learning-resources.md)

---

## 为什么需要对比

在选择技术栈时，我们评估了多个跨平台方案。以下是 ReadStorm 在决策时考虑的主要选项和分析。

---

## 方案总览

| 方案 | 语言 | 平台覆盖 | UI 一致性 | 性能 | 生态成熟度 |
|------|------|----------|-----------|------|-----------|
| **Avalonia** | C# | Win/Linux/Mac/Android/iOS | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| .NET MAUI | C# | Win/Mac/Android/iOS | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| Electron | JS/TS | Win/Linux/Mac | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| Flutter | Dart | 全平台 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| Qt | C++ | 全平台 | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| Kotlin Multiplatform | Kotlin | Android/iOS/Desktop | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |

---

## 详细对比分析

### Avalonia vs .NET MAUI

| 维度 | Avalonia | .NET MAUI |
|------|----------|-----------|
| **渲染方式** | Skia 自绘（像素级一致） | 原生控件包装 |
| **Linux 支持** | ✅ 完整支持 | ❌ 不支持 |
| **UI 一致性** | 各平台完全一致 | 各平台外观不同 |
| **学习曲线** | WPF 开发者友好 | Xamarin.Forms 迁移 |
| **社区活跃度** | 快速增长 | 微软官方维护 |

**ReadStorm 选择 Avalonia 的原因**：
1. 需要 Linux 桌面支持（MAUI 不支持）
2. 追求各平台 UI 完全一致
3. 团队有 WPF/XAML 经验

### Avalonia vs Electron

| 维度 | Avalonia | Electron |
|------|----------|----------|
| **内存占用** | 低（~50MB） | 高（~200MB+） |
| **启动速度** | 快 | 慢（加载 Chromium） |
| **安装包大小** | 小（FDD ~15MB） | 大（~100MB+） |
| **技术栈** | C# | JavaScript/HTML/CSS |
| **原生能力** | 直接调用 .NET API | 通过 Node.js 桥接 |

**对比结论**：Electron 生态更大但资源消耗严重，ReadStorm 作为工具类应用追求轻量化。

### Avalonia vs Flutter

| 维度 | Avalonia | Flutter |
|------|----------|---------|
| **语言** | C#（.NET 生态） | Dart（独立生态） |
| **桌面支持** | 成熟 | 逐渐完善 |
| **热重载** | Avalonia Preview | 原生支持 |
| **包管理** | NuGet | pub.dev |
| **学习成本** | C# 开发者友好 | 需学 Dart |

**对比结论**：Flutter 在移动端更成熟，但 ReadStorm 以桌面端为主，C# 生态更适合数据处理密集的场景。

---

## ReadStorm 的双轨策略

ReadStorm 实际上采用了 **双轨并行** 的策略：

1. **主线 - Avalonia 跨平台**：一套 C# 代码覆盖桌面和 Android
2. **副线 - 原生 Kotlin**：`pureopus/` 目录下的纯原生 Android 实现

```
ReadStorm/
├── src/                     ← 主线：Avalonia 跨平台
│   ├── ReadStorm.Desktop/
│   ├── ReadStorm.Android/   ← Avalonia Android
│   └── ...
└── pureopus/                ← 副线：原生 Kotlin Android
    └── app/src/main/java/
```

这种策略的好处：
- 用 Avalonia 快速覆盖全平台
- 用原生 Kotlin 作为性能/体验的对照基准
- 验证不同技术路线的可行性

> 💡 关于双轨策略的详细分析，请参见 [4.4 设计决策与取舍](../04-architecture/04-design-decisions.md)

---

## 选型建议

根据 ReadStorm 的经验，给出以下选型建议：

| 场景 | 推荐方案 |
|------|----------|
| C# 团队 + 全平台桌面应用 | **Avalonia** |
| C# 团队 + 仅移动端 | .NET MAUI |
| Web 团队 + 桌面应用 | Electron |
| 新团队 + 全平台 | Flutter |
| 高性能 + 全平台 | Qt (C++) |
| Android 原生体验 | Kotlin |

---

## 小结

- 没有完美的跨平台方案，只有最适合的
- Avalonia 适合 C# 团队 + 追求 UI 一致性 + 需要 Linux 的场景
- ReadStorm 验证了 Avalonia 在生产级应用中的可行性

---

[← 上一章：Avalonia 框架介绍](02-avalonia-framework.md) | [返回首页](../README.md) | [下一章：学习资源与方法 →](04-learning-resources.md)
