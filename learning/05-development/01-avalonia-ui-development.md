# 5.1 Avalonia UI 开发

[← 上一章：设计决策与取舍](../04-architecture/04-design-decisions.md) | [返回首页](../README.md) | [下一章：ViewModel 与数据绑定 →](02-viewmodel-databinding.md)

---

## AXAML 基础

AXAML（Avalonia XAML）是 Avalonia 的标记语言，用于声明式定义 UI 界面。如果你熟悉 WPF 的 XAML，会发现 AXAML 几乎一模一样。

### 基本结构

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ReadStorm.Desktop.ViewModels"
             x:Class="ReadStorm.Desktop.Views.SearchView"
             x:DataType="vm:SearchDownloadViewModel">

    <!-- 控件内容 -->
    <StackPanel Margin="16">
        <TextBlock Text="搜索" FontSize="24" FontWeight="Bold" />
        <TextBox Text="{Binding SearchKeyword}" Watermark="输入书名..." />
    </StackPanel>

</UserControl>
```

**关键命名空间**：

| 前缀 | URI | 用途 |
|------|-----|------|
| 默认 | `https://github.com/avaloniaui` | Avalonia 控件 |
| `x` | `http://schemas.microsoft.com/winfx/2006/xaml` | XAML 语言特性 |
| `vm` | `using:ReadStorm.Desktop.ViewModels` | 项目 ViewModel 引用 |

---

## 常用布局控件

### StackPanel（堆叠布局）

```xml
<!-- 垂直堆叠（默认） -->
<StackPanel Spacing="8">
    <TextBlock Text="第一行" />
    <TextBlock Text="第二行" />
</StackPanel>

<!-- 水平堆叠 -->
<StackPanel Orientation="Horizontal" Spacing="8">
    <Button Content="按钮1" />
    <Button Content="按钮2" />
</StackPanel>
```

### Grid（网格布局）

```xml
<Grid RowDefinitions="Auto,*,Auto" ColumnDefinitions="200,*">
    <!-- 第0行第0列 -->
    <TextBlock Grid.Row="0" Grid.Column="0" Text="标签" />
    <!-- 第0行第1列 -->
    <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding Value}" />
    <!-- 第1行，跨两列 -->
    <ListBox Grid.Row="1" Grid.ColumnSpan="2"
             ItemsSource="{Binding Items}" />
    <!-- 第2行 -->
    <Button Grid.Row="2" Grid.Column="1" Content="确定" />
</Grid>
```

**行/列定义说明**：

| 值 | 含义 |
|----|------|
| `Auto` | 根据内容自动确定大小 |
| `*` | 占用剩余空间 |
| `2*` | 占用剩余空间的 2 倍 |
| `200` | 固定 200 像素 |

### DockPanel（停靠布局）

```xml
<DockPanel>
    <Menu DockPanel.Dock="Top">...</Menu>
    <StatusBar DockPanel.Dock="Bottom">...</StatusBar>
    <TreeView DockPanel.Dock="Left" Width="200">...</TreeView>
    <!-- 最后一个元素填充剩余空间 -->
    <ContentControl Content="{Binding CurrentView}" />
</DockPanel>
```

---

## 常用交互控件

### 按钮

```xml
<Button Content="搜索"
        Command="{Binding SearchCommand}"
        IsEnabled="{Binding !IsSearching}"
        HorizontalAlignment="Right" />
```

### 文本输入

```xml
<TextBox Text="{Binding SearchKeyword}"
         Watermark="请输入关键词"
         AcceptsReturn="False" />
```

### 列表

```xml
<ListBox ItemsSource="{Binding Books}"
         SelectedItem="{Binding SelectedBook}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBlock Text="{Binding Title}" FontWeight="Bold" />
                <TextBlock Text="{Binding Author}" Opacity="0.6" />
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

### TabControl（选项卡）

ReadStorm 的主界面使用 TabControl 组织多个功能页面：

```xml
<TabControl>
    <TabItem Header="搜索">
        <views:SearchView />
    </TabItem>
    <TabItem Header="书架">
        <views:BookshelfView />
    </TabItem>
    <TabItem Header="阅读">
        <views:ReaderView />
    </TabItem>
    <TabItem Header="设置">
        <views:SettingsView />
    </TabItem>
</TabControl>
```

---

## 样式系统

Avalonia 使用类似 CSS 的选择器语法来定义样式：

### 基本样式

```xml
<UserControl.Styles>
    <!-- 所有按钮的默认样式 -->
    <Style Selector="Button">
        <Setter Property="Margin" Value="4" />
        <Setter Property="Padding" Value="8,4" />
    </Style>

    <!-- 带 class 的样式 -->
    <Style Selector="Button.primary">
        <Setter Property="Background" Value="#1890ff" />
        <Setter Property="Foreground" Value="White" />
    </Style>

    <!-- 鼠标悬停效果 -->
    <Style Selector="Button:pointerover">
        <Setter Property="Opacity" Value="0.8" />
    </Style>
</UserControl.Styles>
```

### ReadStorm 的主题配置

```xml
<!-- App.axaml -->
<Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://Semi.Avalonia/Themes/Index.axaml" />
</Application.Styles>
```

ReadStorm 使用 **Semi.Avalonia** 主题包提供统一的视觉风格。

---

## 资源与资源字典

```xml
<!-- 定义资源 -->
<UserControl.Resources>
    <SolidColorBrush x:Key="AccentBrush" Color="#1890ff" />
    <x:Double x:Key="DefaultFontSize">14</x:Double>
</UserControl.Resources>

<!-- 使用资源 -->
<TextBlock Foreground="{StaticResource AccentBrush}"
           FontSize="{StaticResource DefaultFontSize}" />
```

---

## 图标方案

ReadStorm 在 Android 上使用 MDI（Material Design Icons）字体和 PathIcon：

```xml
<!-- 资源定义 StreamGeometry 图标 -->
<StreamGeometry x:Key="SearchIcon">M9.5,3A6.5,6.5 0 0,1 16,9.5...</StreamGeometry>

<!-- 使用 PathIcon -->
<PathIcon Data="{StaticResource SearchIcon}" Width="24" Height="24" />
```

> ⚠️ **注意**：不要在 AXAML 中直接使用 Unicode emoji 字符（如 🔍），在 Android 真机上不会显示。详见 [8.4 UI 渲染问题](../08-troubleshooting/04-ui-rendering-issues.md)。

---

## 小结

- AXAML 是声明式 UI 定义语言，WPF 开发者上手快
- 常用布局：`StackPanel`、`Grid`、`DockPanel`
- 样式系统使用 CSS 选择器风格
- 图标使用 PathIcon + StreamGeometry，避免 Unicode emoji

> 💡 UI 与数据的连接通过数据绑定实现，参见 [5.2 ViewModel 与数据绑定](02-viewmodel-databinding.md)

---

[← 上一章：设计决策与取舍](../04-architecture/04-design-decisions.md) | [返回首页](../README.md) | [下一章：ViewModel 与数据绑定 →](02-viewmodel-databinding.md)
