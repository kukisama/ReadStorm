# 1.2 Avalonia UI 框架介绍

[← 上一章：C# 与 .NET 概述](01-csharp-dotnet-overview.md) | [返回首页](../README.md) | [下一章：同类技术对比 →](03-comparison-with-alternatives.md)

---

## 什么是 Avalonia

Avalonia 是一个开源的跨平台 UI 框架，基于 .NET 构建，允许你用 **一套代码** 开发同时运行在 Windows、Linux、macOS、Android、iOS 和 WebAssembly 上的桌面和移动应用。

### 核心理念

- **真正的跨平台**：不是 "在每个平台模拟"，而是通过 Skia 渲染引擎直接绘制像素
- **XAML 驱动**：使用 AXAML（Avalonia XAML）声明式定义 UI
- **MVVM 友好**：原生支持数据绑定、命令、模板
- **像素级一致**：所有平台的渲染结果完全一致

### Avalonia vs WPF

如果你熟悉 WPF（Windows Presentation Foundation），Avalonia 会让你感到亲切：

| 特性 | WPF | Avalonia |
|------|-----|----------|
| 平台 | 仅 Windows | 全平台 |
| 标记语言 | XAML | AXAML（99% 兼容） |
| 渲染引擎 | DirectX | Skia |
| 数据绑定 | `{Binding}` | `{Binding}` |
| 样式系统 | Style + Trigger | Style + Selector（CSS 风格） |
| 开源 | 部分 | 完全开源 |

---

## Avalonia 的架构

```
┌──────────────────────────────┐
│       你的应用代码            │
│   (AXAML + ViewModel + C#)   │
├──────────────────────────────┤
│     Avalonia 框架核心         │
│  (控件库、样式、数据绑定)      │
├──────────────────────────────┤
│      Skia 渲染引擎            │
│  (跨平台 2D 图形库)           │
├──────────────────────────────┤
│       平台适配层              │
│  (Win32 / X11 / Android 等)  │
└──────────────────────────────┘
```

---

## 在 ReadStorm 中的使用

### AXAML 界面定义

ReadStorm 的所有界面都用 AXAML 编写：

```xml
<!-- 搜索页面示例 -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="16">
        <TextBox Watermark="输入书名搜索..."
                 Text="{Binding SearchKeyword}" />
        <Button Content="搜索"
                Command="{Binding SearchCommand}" />
        <ListBox ItemsSource="{Binding SearchResults}">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding BookName}" />
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </StackPanel>
</UserControl>
```

### 主题系统

ReadStorm 使用 Semi.Avalonia 主题包：

```xml
<!-- App.axaml -->
<Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://Semi.Avalonia/Themes/Index.axaml" />
</Application.Styles>
```

### 平台项目结构

```
src/
├── ReadStorm.Desktop/      ← 桌面端入口 (Avalonia.Desktop)
│   ├── Program.cs           ← Desktop 启动
│   ├── MainWindow.axaml     ← 主窗口
│   └── Views/               ← 桌面端视图
├── ReadStorm.Android/       ← Android 端入口 (Avalonia.Android)
│   ├── MainActivity.cs      ← Android Activity
│   ├── MainView.axaml       ← 移动端主视图
│   └── Views/               ← 移动端视图
```

---

## Avalonia 核心概念速览

### 1. 控件（Controls）

Avalonia 提供丰富的内置控件：

- **布局**：`StackPanel`、`Grid`、`DockPanel`、`WrapPanel`
- **输入**：`TextBox`、`ComboBox`、`Slider`、`CheckBox`
- **展示**：`TextBlock`、`Image`、`ListBox`、`DataGrid`
- **容器**：`ScrollViewer`、`TabControl`、`Expander`

### 2. 样式（Styles）

Avalonia 使用类似 CSS 选择器的样式系统：

```xml
<Style Selector="Button.primary">
    <Setter Property="Background" Value="#1890ff" />
    <Setter Property="Foreground" Value="White" />
</Style>
```

### 3. 数据绑定（Data Binding）

MVVM 模式的核心，将 UI 和数据解耦：

```xml
<TextBlock Text="{Binding BookTitle}" />
<Button Command="{Binding SaveCommand}" IsEnabled="{Binding CanSave}" />
```

> 💡 更多 UI 开发细节参见 [5.1 Avalonia UI 开发](../05-development/01-avalonia-ui-development.md)

---

## Avalonia 生态

| 包名 | 用途 | ReadStorm 是否使用 |
|------|------|:---:|
| Avalonia | 核心框架 | ✅ |
| Avalonia.Desktop | 桌面平台支持 | ✅ |
| Avalonia.Android | Android 平台支持 | ✅ |
| Avalonia.Themes.Fluent | Fluent 主题 | ✅ |
| Semi.Avalonia | Semi Design 控件库 | ✅ |
| Markdown.Avalonia | Markdown 渲染 | ✅ |
| Avalonia.Diagnostics | 调试诊断工具 | ✅ |

---

## 小结

- Avalonia 是当前 .NET 生态中最成熟的跨平台 UI 框架
- 它使用 AXAML + C# 的组合，对 WPF 开发者友好
- ReadStorm 用它同时覆盖了桌面和移动端
- Skia 渲染引擎保证了像素级一致的跨平台体验

> ⚠️ **注意**：Avalonia 使用 Skia 进行文本渲染，在 Android 真机上不会加载系统 emoji 字体，因此补充平面 Unicode 表情符号（U+1Fxxx）无法正常渲染。ReadStorm 使用 PathIcon + StreamGeometry 方案替代。详见 [8.4 UI 渲染问题](../08-troubleshooting/04-ui-rendering-issues.md)。

---

[← 上一章：C# 与 .NET 概述](01-csharp-dotnet-overview.md) | [返回首页](../README.md) | [下一章：同类技术对比 →](03-comparison-with-alternatives.md)
