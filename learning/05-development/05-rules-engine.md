# 5.5 规则引擎设计

[← 上一章：HTTP 与 HTML 解析](04-http-and-html-parsing.md) | [返回首页](../README.md) | [下一章：跨平台适配 →](06-cross-platform-adaptation.md)

---

## 什么是规则引擎

ReadStorm 最核心的设计之一是 **规则引擎**——通过 JSON 配置文件定义如何从不同的书源网站提取数据，而不是为每个网站硬编码爬虫逻辑。

### 为什么不硬编码

```
硬编码方案（❌）：
├── BiqugeParser.cs      ← 网站 A 的专用解析器
├── ShuqiParser.cs       ← 网站 B 的专用解析器
├── QidianParser.cs      ← 网站 C 的专用解析器
└── ...（每新增一个网站就要写一个类）

规则引擎方案（✅）：
├── RuleEngine.cs        ← 通用解析引擎（一个类搞定所有）
└── rules/
    ├── biquge.json       ← 网站 A 的规则配置
    ├── shuqi.json        ← 网站 B 的规则配置
    ├── qidian.json       ← 网站 C 的规则配置
    └── ...（新增网站只需添加 JSON 文件）
```

---

## 规则文件结构

每个书源规则是一个 JSON 文件，定义了该网站的结构：

```json
{
  "name": "示例书源",
  "baseUrl": "https://www.example.com",
  "searchUrl": "/search?keyword={keyword}",
  "charset": "utf-8",

  "search": {
    "resultSelector": "div.search-result-item",
    "bookNameSelector": "h3.book-name",
    "authorSelector": "span.author",
    "bookUrlSelector": "a.book-link",
    "coverSelector": "img.cover"
  },

  "detail": {
    "chapterListSelector": "ul.chapter-list > li > a",
    "chapterNameAttr": "text",
    "chapterUrlAttr": "href"
  },

  "content": {
    "contentSelector": "div#content",
    "removeSelectors": ["div.ads", "script", "style"],
    "nextPageSelector": "a.next-page"
  }
}
```

### 规则字段说明

| 字段 | 用途 |
|------|------|
| `name` | 书源名称（显示用） |
| `baseUrl` | 书源网站的基础 URL |
| `searchUrl` | 搜索 URL 模板，`{keyword}` 会被替换 |
| `charset` | 网站编码（utf-8, gbk 等） |
| `search.*` | 搜索结果页面的各种 CSS 选择器 |
| `detail.*` | 图书详情页的章节列表选择器 |
| `content.*` | 章节正文页面的内容选择器 |
| `removeSelectors` | 需要移除的广告/脚本元素 |

---

## 规则解析流程

```
1. 用户输入关键词
   ↓
2. 加载书源规则（JSON）
   ↓
3. 拼接搜索 URL: baseUrl + searchUrl.replace("{keyword}", keyword)
   ↓
4. HTTP 请求搜索页面
   ↓
5. 用 search.resultSelector 提取搜索结果列表
   ↓
6. 用 bookNameSelector / authorSelector 提取每条结果的数据
   ↓
7. 返回 List<SearchResult>

用户选择一本书后：
   ↓
8. HTTP 请求图书详情页
   ↓
9. 用 detail.chapterListSelector 提取章节列表
   ↓
10. 逐章下载，用 content.contentSelector 提取正文
```

---

## 规则加载器

### RuleFileLoader

```csharp
// 规则文件可以从多个来源加载
public class RuleFileLoader
{
    // 1. 从内嵌资源加载（随应用打包）
    public List<BookSourceRule> LoadEmbeddedRules()
    {
        // 读取 Infrastructure 项目中 rules/ 目录的嵌入资源
    }

    // 2. 从用户自定义目录加载
    public List<BookSourceRule> LoadUserRules(string directory)
    {
        var files = Directory.GetFiles(directory, "*.json");
        return files.Select(f => LoadRuleFromFile(f)).ToList();
    }

    // URL 解析 - 处理相对路径和绝对路径
    public string ResolveUrl(string baseUrl, string relativeUrl)
    {
        if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absolute))
        {
            // 过滤 file:// 协议（安全考虑）
            if (absolute.IsFile) return relativeUrl;
            return absolute.ToString();
        }
        return new Uri(new Uri(baseUrl), relativeUrl).ToString();
    }
}
```

> ⚠️ **注意**：URL 解析在 Linux/Android 上有差异——`Uri.TryCreate("/path", UriKind.Absolute)` 在这些平台上会成功创建 `file:///path`。ReadStorm 通过 `IsFile` 检查过滤了这种情况。详见 [8.2 Android 特有问题](../08-troubleshooting/02-android-specific-issues.md)。

---

## 规则编辑器

ReadStorm 提供了内置的规则编辑器，方便用户自定义和调试规则：

```
┌──────────────────────────────┐
│  规则编辑器                   │
├──────────────────────────────┤
│  规则列表  │  编辑区域        │
│  ├ 书源A   │  名称: [      ]  │
│  ├ 书源B   │  URL:  [      ]  │
│  └ 书源C   │  搜索选择器:     │
│            │  [              ] │
│  [新建]    │  [测试] [保存]   │
└──────────────────────────────┘
```

---

## 内置规则

ReadStorm 在 `src/ReadStorm.Infrastructure/rules/` 目录中内置了 20+ 个书源规则，作为嵌入资源打包到应用中。

```
src/ReadStorm.Infrastructure/
└── rules/
    ├── source01.json
    ├── source02.json
    ├── ...
    └── source20.json
```

---

## 小结

- 规则引擎是 ReadStorm 最核心的设计，实现了书源的可扩展性
- JSON 规则文件定义网站结构，通用引擎执行解析
- 新增书源只需添加 JSON 文件，无需修改代码
- 注意 URL 解析的跨平台差异

> 💡 规则引擎的设计决策分析参见 [4.4 设计决策与取舍](../04-architecture/04-design-decisions.md)

---

[← 上一章：HTTP 与 HTML 解析](04-http-and-html-parsing.md) | [返回首页](../README.md) | [下一章：跨平台适配 →](06-cross-platform-adaptation.md)
