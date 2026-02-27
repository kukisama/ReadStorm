# pureopus 迁移到 Jetpack Compose + Material 3 实施手册（可执行版）

> 版本：v1.0  
> 适用范围：`pureopus`（Kotlin + Fragment + XML + ViewBinding）  
> 目标：在不破坏现有功能的前提下，渐进迁移到 **Jetpack Compose + Material 3**。

---

## 1. 目标与边界

### 1.1 迁移目标

1. UI 层逐步迁移为 Compose，业务层（Domain/Application/Infrastructure）保持不变。
2. 引入统一的 Material 3 主题（颜色、字体、圆角、间距、暗色模式）。
3. 每一阶段都必须可构建、可回归、可回滚。
4. 保证 Android Release 包可持续产出（`assembleRelease -x test`）。

### 1.2 非目标（当前阶段不做）

1. 不改数据库表结构。
2. 不改下载/搜索/规则引擎核心算法。
3. 不一次性重写阅读器（阅读器放在最后阶段）。

---

## 2. 当前架构基线（迁移前）

- 导航：`MainActivity + BottomNavigation + Fragment`
- UI 技术：XML + ViewBinding
- 状态管理：`MainViewModel` 聚合子 ViewModel
- 页面：搜索/任务/书架/规则/诊断/设置/关于/日志/阅读

迁移原则：**先“页面壳”后“复杂交互”；先低风险页后高风险页。**

---

## 3. 推荐迁移策略（强制）

采用 **混编渐进迁移**，禁止全量一次切换。

### 3.1 路线总览

- Phase 0：基础设施接入（Compose + M3 + Theme）
- Phase 1：低风险页面迁移（关于 / 日志 / 更多 / 设置）
- Phase 2：中风险页面迁移（搜索 / 下载任务）
- Phase 3：书架迁移（卡片、列表状态、操作菜单）
- Phase 4：规则编辑与诊断迁移
- Phase 5：阅读器迁移（最后）
- Phase 6：清理 XML 与 ViewBinding（按页面完成度下线）

### 3.2 每阶段门禁（Definition of Done）

每个 Phase 完成前必须满足：

1. `./gradlew :app:assembleDebug -x test` 通过
2. `./gradlew :app:assembleRelease -x test` 通过
3. 核心手工回归清单通过（见第 8 节）
4. 变更日志已追加（`docs/变更日志.md`）

---

## 4. 代码组织规范（迁移后）

## 4.1 目录规范

建议新增：

- `app/src/main/java/com/readstorm/app/ui/compose/theme/`
  - `Color.kt`
  - `Type.kt`
  - `Shape.kt`
  - `Theme.kt`
- `app/src/main/java/com/readstorm/app/ui/compose/components/`
  - 通用组件（卡片、状态条、列表项）
- `app/src/main/java/com/readstorm/app/ui/compose/screens/`
  - 页面级 Composable（按功能分包）

### 4.2 状态规范

1. ViewModel 继续作为唯一业务状态来源。
2. Compose 页面通过 `collectAsStateWithLifecycle` 或 `observeAsState` 接收状态。
3. UI 事件通过 `onIntent(...)`/`onAction(...)` 回传 ViewModel，禁止在 Composable 内直接操作 Repo。

### 4.3 组件规范

1. 所有颜色、字体、圆角必须从 `MaterialTheme` 获取。
2. 间距统一 4/8/12/16/24dp。
3. 列表项必须支持：加载中、空态、错误态三种状态。

---

## 5. AI 助手执行协议（可直接照做）

## 5.1 单次任务模板

每次只做一个页面或一个子模块，严格按顺序：

1. 读取目标页面现有 XML + Fragment + ViewModel。
2. 创建对应 Compose Screen（仅 UI，不动业务）。
3. 用 `ComposeView` 在现有 Fragment 中承载新 UI。
4. 对齐交互命令到现有 ViewModel。
5. 编译 Debug + Release。
6. 更新 `docs/变更日志.md`。

### 5.2 禁止事项

1. 禁止跨层调用（UI 直接访问 Infrastructure）。
2. 禁止在同一 PR 同时迁移 3 个以上页面。
3. 禁止跳过 Release 构建验证。

### 5.3 输出要求

每次提交必须包含：

1. 改动文件清单
2. 验证命令与结果
3. 回滚说明（如需）

---

## 6. 分阶段实施细则（可排期）

## Phase 0：基础接入（1~2 天）

### 任务

1. 引入 Compose BOM、Material3、Activity Compose 依赖。
2. 配置 `buildFeatures.compose = true` 与 Kotlin compiler extension。
3. 落地 `ReadStormTheme`（Light/Dark + Dynamic Color 可选）。
4. 建立 3~5 个基础组件：
   - `RsTopBar`
   - `RsPrimaryButton`
   - `RsCard`
   - `RsEmptyState`
   - `RsLoading`

### 验收

- 任意测试页面可渲染 Compose 组件。

## Phase 1：低风险页（2~3 天）

迁移页面：`More`、`About`、`Log`、`Settings`

### 重点

1. 先做静态信息页，再做有输入页。
2. 设置页输入使用受控状态，失焦或点击保存时提交 ViewModel。
3. 日志页支持滚动到底部、清空。

### 验收

- 页面功能与旧版一致，无崩溃。

## Phase 2：中风险页（3~4 天）

迁移页面：`Search`、`DownloadTasks`

### 重点

1. 搜索结果列表虚拟化性能（`LazyColumn` + key）。
2. 任务状态颜色统一：排队/下载中/失败/完成。
3. 搜索空态、失败态可视化。

### 验收

- 搜索、加入队列、暂停/恢复/重试全链路通过。

## Phase 3：书架页（2~3 天）

### 重点

1. 书架卡片统一比例，封面占位策略统一。
2. 进度条与百分比布局对齐，避免溢出。
3. 长按菜单与点击打开行为一致。

## Phase 4：规则编辑 + 诊断（3~5 天）

### 重点

1. 规则编辑分组折叠 UI（基本信息/搜索/正文/测试）
2. 诊断结果分级展示（Info/Warning/Error）

## Phase 5：阅读器（5~8 天，高风险）

### 重点

1. 手势与分页保持现有语义。
2. 工具栏显示/隐藏、目录层、书签层完整迁移。
3. 大文本渲染性能（分页缓存、remember、derivedStateOf）。

---

## 7. 编码规范（Compose 专项）

1. Composable 命名：`XxxScreen`（页面）、`XxxCard`（组件）
2. `@Preview`：每个页面至少 2 个（亮色/暗色）
3. 避免在 Composable 内直接创建协程；统一由 ViewModel 发起
4. 对列表项提供稳定 key：`items(items, key = { it.id })`
5. 大列表禁止在 Composable 中做复杂排序/过滤（放 ViewModel）

---

## 8. 测试与验证计划

## 8.1 自动化验证（每阶段必跑）

1. 编译验证：
   - `./gradlew :app:assembleDebug -x test`
   - `./gradlew :app:assembleRelease -x test`
2. （可选）UI 测试：
   - Compose UI Test：关键点击、输入、列表渲染

### 8.2 手工回归清单（每阶段必做）

1. 首页启动 -> 进入各 Tab 不崩溃
2. 搜索 -> 结果展示 -> 加入下载队列
3. 下载任务暂停/恢复/重试
4. 书架打开书籍 -> 阅读 -> 返回
5. 规则页加载/保存/测试
6. 诊断页可执行并可见结果
7. 日志页可看到日志并可清空

### 8.3 性能基线对比（迁移前后都测）

至少记录：

1. 冷启动首帧时间（ms）
2. 搜索结果页滚动帧率（jank 比例）
3. 书架页首屏渲染时长
4. 阅读页翻页平均耗时
5. 内存占用峰值（MB）

建议工具：Android Studio Profiler + Macrobenchmark。

---

## 9. 回滚策略

1. 迁移期间保留原 Fragment 入口，使用 feature flag 切换旧/新 UI。
2. 若新 UI 出现严重问题：
   - 切回旧 XML 页面
   - 保留新 Compose 代码但不挂载
3. 每个页面单独迁移，确保回滚粒度小。

---

## 10. 与现有架构相比：性能是好还是差？

结论：**不是绝对“更好”或“更差”，但在规范实现下，整体体验通常更好；不规范实现会更差。**

### 10.1 常见“会更好”的场景

1. 动效与状态驱动 UI（Compose 更自然）
2. 复杂主题/暗黑模式切换（M3 成本更低）
3. 页面迭代速度（少样板代码）

### 10.2 常见“会变差”的场景

1. 不稳定状态频繁重组（recomposition 过多）
2. 列表缺少稳定 key
3. 在 Composable 做重计算（排序/解析）
4. 阅读器大文本一次性重绘

### 10.3 对 pureopus 的预判

- **短期（前 1~2 个阶段）**：
  - APK 体积可能小幅上升；
  - 首次进入页面可能略慢（初始化成本）。
- **中期（规范完成后）**：
  - 滚动、交互流畅度通常会优于当前 XML 版本；
  - UI 维护成本显著下降。

### 10.4 评分（10 分制）

- 视觉上限：9.2
- 开发效率（中后期）：8.8
- 性能稳定性（规范实现）：8.4
- 迁移风险（当前项目）：6.0

综合建议：**值得迁移，但必须走渐进方案，不要一次性重写。**

---

## 11. 里程碑模板（给 AI 助手用）

每完成一个页面，必须输出：

1. 已迁移页面：`XxxFragment -> XxxComposeScreen`
2. 改动文件：列出路径
3. 编译结果：Debug/Release
4. 回归结果：通过项/失败项
5. 风险与下步计划

---

## 12. 最终交付标准

当以下条件全部满足，判定迁移完成：

1. 所有主页面均已 Compose 化（阅读器除外可分支延期）
2. Release 构建连续 3 次通过
3. 关键链路手工回归全部通过
4. 迁移文档与变更日志完整
5. 旧 XML 页面仅保留必要回滚副本

---

## 13. 已知踩坑与硬约束（必须执行）

本节基于 pureopus 已发生问题整理。AI 助手每次迁移任务必须逐项对照，任何一项不满足都不得宣告完成。

### 13.1 已知真实踩坑（历史复盘）

1. **首次 Release 打包失败（AAPT 资源链接失败）**
  - 现象：`AndroidManifest.xml` 引用了图标资源，但资源文件不存在。
  - 典型报错：`resource ... not found`。
  - 根因：资源引用与资源文件未建立真实存在关系（仅“写了引用”，未“落盘文件”）。

2. **书源不可见 / 加载失败**
  - 现象：书源下拉为空或搜索无源可用。
  - 根因：`rule-*.json` 未同步到 `app/src/main/assets/`，运行时无法从 assets 枚举规则。


### 13.2 资源硬约束（迁移期间强制）

1. **Manifest 中的每个资源引用必须有本地文件**
  - 例如 `@drawable/icon` 必须存在 `app/src/main/res/drawable/icon.*`。
  - 禁止“先引用后补文件”的提交方式。

2. **构建前必须执行编译外预拷贝脚本**
  - 脚本：`pureopus/scripts/sync-brand-assets.ps1`
  - 至少保证以下文件存在：
    - `app/src/main/res/drawable/icon.png`
    - `app/src/main/res/drawable/boot.jpg`
    - `app/src/main/assets/rule-*.json`（至少 1 个）

3. **Compose 迁移不得破坏现有资源链路**
  - UI 层改造（Compose/XML）与资源链路（脚本同步）分离。
  - 禁止在 Compose 迁移中移除、绕过或弱化资源存在性校验。

### 13.3 每次任务的“存在性检查”模板（必跑）

在执行 `assembleDebug/assembleRelease` 前，先进行：

1. 执行 `sync-brand-assets.ps1`。
2. 检查关键文件是否存在：
  - `drawable/icon.png`
  - `drawable/boot.jpg`
  - `assets/rule-*.json`
3. 再执行：
  - `./gradlew :app:assembleDebug -x test`
  - `./gradlew :app:assembleRelease -x test`

### 13.4 AI 助手输出补充要求（强制）

除第 5 节原有输出项外，新增两项必须输出：

1. **资源存在性结果**：列出关键文件检查结果（存在/不存在）。
2. **脚本执行结果**：`sync-brand-assets.ps1` 是否执行成功、同步了多少规则文件。

---

如果需要，可以基于本手册继续生成《Phase 1 详细任务卡（按文件到函数级）》供 AI 助手直接逐条执行。