# 8.4 UI 渲染问题

[← 上一章：编译与部署问题](03-build-deploy-issues.md) | [返回首页](../README.md) | [下一章：术语表 →](../09-appendix/01-glossary.md)

---

## Emoji 与 Unicode 图标不显示

### 现象

在 Android 真机上，使用 Unicode emoji 字符作为图标的按钮显示为空白或方块。但在模拟器（如 Android 9 x86）上可以正常显示。

```xml
<!-- 这样写在真机上看不到 -->
<Button Content="🔍" />
<TextBlock Text="📚" />
```

### 原因

Avalonia 使用 **Skia** 作为渲染引擎。Skia 在 Android 上进行文本渲染时，不会加载系统的 emoji 字体（如 NotoColorEmoji）。Supplementary Plane Unicode 字符（U+1F000 以上）无法被渲染。

- 模拟器（x86）可能工作是因为软件渲染路径不同
- 真机（ARM）使用硬件加速的 Skia 渲染，不加载 emoji 字体

### 解决方案

**方案 1：PathIcon + StreamGeometry（ReadStorm 采用）**

```xml
<!-- 定义矢量图标 -->
<StreamGeometry x:Key="SearchIcon">
    M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z
</StreamGeometry>

<!-- 使用图标 -->
<PathIcon Data="{StaticResource SearchIcon}" Width="24" Height="24" />
```

**方案 2：捆绑图标字体**

```xml
<!-- 注册自定义图标字体 -->
<FontFamily x:Key="MdiFont">
    avares://ReadStorm.Android/Assets/Fonts/materialdesignicons-webfont.ttf#Material Design Icons
</FontFamily>

<!-- 使用图标字体（MDI 码点） -->
<TextBlock FontFamily="{StaticResource MdiFont}" Text="&#xF0349;" />
```

ReadStorm Android 项目捆绑了 MDI（Material Design Icons）字体（v7.4.47, SIL OFL 许可），使用码点引用图标。

### 推荐

优先使用 **PathIcon + StreamGeometry**，因为：

- 矢量可缩放，不模糊
- 不依赖字体加载
- 跨平台一致

---

## 字体渲染问题

### 现象

某些中文字符显示为方块或使用了错误的字体。

### 解决方案

1. **确保字体可用**：Skia 默认使用系统字体，但可以捆绑自定义字体

2. **设置字体回退链**：
   ```xml
   <TextBlock FontFamily="Microsoft YaHei, SimHei, WenQuanYi Micro Hei, sans-serif"
              Text="中文文本" />
   ```

3. **Android 上使用系统字体**：
   ```xml
   <!-- Android 系统中文字体通常是 Noto Sans CJK -->
   <TextBlock FontFamily="Noto Sans CJK SC, sans-serif" />
   ```

---

## 主题与样式问题

### 现象

控件外观在不同平台不一致，或切换主题后部分控件样式异常。

### 排查方法

1. **确认主题配置**：
   ```xml
   <Application.Styles>
       <FluentTheme />
       <StyleInclude Source="avares://Semi.Avalonia/Themes/Index.axaml" />
   </Application.Styles>
   ```

2. **检查样式优先级**：局部样式 > 全局样式 > 主题默认

3. **使用 Avalonia DevTools**：
   - 运行时按 F12 打开
   - 检查控件的实际样式值
   - 查看样式覆盖链

### 常见样式问题

| 问题 | 原因 | 解决 |
|------|------|------|
| 按钮颜色不对 | 样式被覆盖 | 检查选择器优先级 |
| 边距异常 | Margin/Padding 冲突 | 使用 DevTools 检查 |
| 暗色模式下不可见 | 未处理暗色主题 | 使用 DynamicResource |

---

## 布局渲染问题

### 现象

控件重叠、溢出或未按预期排列。

### 排查方法

1. **添加调试边框**：
   ```xml
   <Border BorderBrush="Red" BorderThickness="1">
       <YourControl />
   </Border>
   ```

2. **检查布局属性**：
   - `HorizontalAlignment` / `VerticalAlignment`
   - `Margin` / `Padding`
   - `Width` / `Height` / `MinWidth` / `MaxWidth`

3. **Grid 行列定义**：确保 `RowDefinitions` 和 `ColumnDefinitions` 正确

---

## 图片加载问题

### 现象

图片（如书籍封面）无法显示或加载缓慢。

### 排查方法

1. **确认图片 URL 有效**
2. **检查网络权限**（Android）
3. **处理加载失败**：
   ```xml
   <Image Source="{Binding CoverUrl}"
          Width="100" Height="140">
       <!-- 可以设置默认图片 -->
   </Image>
   ```

---

## 小结

UI 渲染问题的核心要点：

- Avalonia + Skia 在 Android 上不渲染系统 emoji
- 使用 PathIcon 或捆绑图标字体替代 Unicode emoji
- 中文字体通过字体回退链确保可用
- 使用 Avalonia DevTools (F12) 调试样式问题
- 布局问题用添加临时边框的方式可视化排查

---

[← 上一章：编译与部署问题](03-build-deploy-issues.md) | [返回首页](../README.md) | [下一章：术语表 →](../09-appendix/01-glossary.md)
