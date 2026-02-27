package com.readstorm.app.ui.compose.screens

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.livedata.observeAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.readstorm.app.domain.models.*
import com.readstorm.app.ui.viewmodels.MainViewModel
import com.readstorm.app.ui.viewmodels.RuleListItem
import kotlinx.coroutines.launch

/**
 * "规则编辑器"页面 Compose Screen。
 */
@Composable
fun RuleEditorScreen(mainViewModel: MainViewModel) {
    val ruleItems by mainViewModel.ruleEditor.ruleEditorRules.observeAsState(emptyList())
    val selectedRule by mainViewModel.ruleEditor.ruleEditorSelectedRule.observeAsState()
    val currentRule by mainViewModel.ruleEditor.currentRule.observeAsState()
    val isTesting by mainViewModel.ruleEditor.isRuleTesting.observeAsState(false)
    val isSaving by mainViewModel.ruleEditor.isRuleSaving.observeAsState(false)
    val testStatus by mainViewModel.ruleEditor.ruleTestStatus.observeAsState("")
    val testDiag by mainViewModel.ruleEditor.ruleTestDiagnostics.observeAsState("")
    val testSearch by mainViewModel.ruleEditor.ruleTestSearchPreview.observeAsState("")
    val testToc by mainViewModel.ruleEditor.ruleTestTocPreview.observeAsState("")
    val testContent by mainViewModel.ruleEditor.ruleTestContentPreview.observeAsState("")
    val hasOverride by mainViewModel.ruleEditor.ruleHasUserOverride.observeAsState(false)
    val scope = rememberCoroutineScope()

    // Mutable copy of the rule for editing
    var editingRule by remember(currentRule) { mutableStateOf(currentRule) }
    var testKeyword by remember { mutableStateOf("") }

    LaunchedEffect(Unit) {
        mainViewModel.ruleEditor.loadRuleList()
    }

    fun updateRule(rule: FullBookSourceRule?) {
        editingRule = rule
        mainViewModel.ruleEditor.updateCurrentRule(rule)
    }

    // Section expand state
    var expandBasic by remember { mutableStateOf(false) }
    var expandSearch by remember { mutableStateOf(false) }
    var expandBook by remember { mutableStateOf(false) }
    var expandToc by remember { mutableStateOf(false) }
    var expandChapter by remember { mutableStateOf(false) }
    var expandTest by remember { mutableStateOf(false) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        // ─── Toolbar ───
        item {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(4.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                IconButton(onClick = { scope.launch { mainViewModel.ruleEditor.loadRuleList() } }) {
                    Icon(Icons.Outlined.Refresh, "刷新")
                }
                IconButton(onClick = { mainViewModel.ruleEditor.newRule() }) {
                    Icon(Icons.Outlined.Add, "新建")
                }
                IconButton(onClick = { mainViewModel.ruleEditor.copyRule() }) {
                    Icon(Icons.Outlined.ContentCopy, "复制")
                }
                IconButton(
                    onClick = { scope.launch { mainViewModel.ruleEditor.deleteRule() } },
                    enabled = currentRule != null
                ) {
                    Icon(Icons.Outlined.Delete, "删除", tint = MaterialTheme.colorScheme.error)
                }
                Spacer(Modifier.weight(1f))
                if (hasOverride) {
                    TextButton(onClick = { scope.launch { mainViewModel.ruleEditor.resetRuleToDefault() } }) {
                        Text("恢复默认")
                    }
                }
                Button(
                    onClick = {
                        updateRule(editingRule)
                        scope.launch { mainViewModel.ruleEditor.saveRule() }
                    },
                    enabled = !isSaving && currentRule != null,
                    shape = MaterialTheme.shapes.small
                ) {
                    Text("保存")
                }
            }
        }

        // ─── Rule selector ───
        item {
            RuleSelectorDropdown(
                items = ruleItems,
                selected = selectedRule,
                onSelect = { mainViewModel.ruleEditor.selectRule(it) }
            )
        }

        // ─── Basic Info Section ───
        item {
            ExpandableSection("基本信息", expandBasic, { expandBasic = it }) {
                val r = editingRule ?: return@ExpandableSection
                RuleFieldRow("ID", r.id.toString()) {}
                RuleFieldRow("名称", r.name) { updateRule(r.copy(name = it)) }
                RuleFieldRow("网站URL", r.url) { updateRule(r.copy(url = it)) }
                RuleFieldRow("备注", r.comment) { updateRule(r.copy(comment = it)) }
                RuleFieldRow("类型", r.type) { updateRule(r.copy(type = it)) }
                RuleFieldRow("语言", r.language) { updateRule(r.copy(language = it)) }
            }
        }

        // ─── Search Section ───
        item {
            ExpandableSection("搜索规则", expandSearch, { expandSearch = it }) {
                val s = editingRule?.search ?: RuleSearchSection()
                fun upd(block: RuleSearchSection.() -> RuleSearchSection) {
                    updateRule(editingRule?.copy(search = s.block()))
                }
                RuleFieldRow("搜索URL", s.url) { upd { copy(url = it) } }
                RuleFieldRow("Method", s.method) { upd { copy(method = it) } }
                RuleFieldRow("POST Data", s.data) { upd { copy(data = it) } }
                RuleFieldRow("Cookie", s.cookies) { upd { copy(cookies = it) } }
                RuleFieldRow("结果选择器", s.result) { upd { copy(result = it) } }
                RuleFieldRow("书名选择器", s.bookName) { upd { copy(bookName = it) } }
                RuleFieldRow("作者选择器", s.author) { upd { copy(author = it) } }
                RuleFieldRow("分类选择器", s.category) { upd { copy(category = it) } }
                RuleFieldRow("字数选择器", s.wordCount) { upd { copy(wordCount = it) } }
                RuleFieldRow("状态选择器", s.status) { upd { copy(status = it) } }
                RuleFieldRow("最新章节", s.latestChapter) { upd { copy(latestChapter = it) } }
                RuleFieldRow("更新时间", s.lastUpdateTime) { upd { copy(lastUpdateTime = it) } }
            }
        }

        // ─── Book Detail Section ───
        item {
            ExpandableSection("书籍详情", expandBook, { expandBook = it }) {
                val b = editingRule?.book ?: RuleBookSection()
                fun upd(block: RuleBookSection.() -> RuleBookSection) {
                    updateRule(editingRule?.copy(book = b.block()))
                }
                RuleFieldRow("书名", b.bookName) { upd { copy(bookName = it) } }
                RuleFieldRow("作者", b.author) { upd { copy(author = it) } }
                RuleFieldRow("简介", b.intro) { upd { copy(intro = it) } }
                RuleFieldRow("分类", b.category) { upd { copy(category = it) } }
                RuleFieldRow("封面URL", b.coverUrl) { upd { copy(coverUrl = it) } }
                RuleFieldRow("最新章节", b.latestChapter) { upd { copy(latestChapter = it) } }
                RuleFieldRow("更新时间", b.lastUpdateTime) { upd { copy(lastUpdateTime = it) } }
                RuleFieldRow("状态", b.status) { upd { copy(status = it) } }
            }
        }

        // ─── TOC Section ───
        item {
            ExpandableSection("目录", expandToc, { expandToc = it }) {
                val t = editingRule?.toc ?: RuleTocSection()
                fun upd(block: RuleTocSection.() -> RuleTocSection) {
                    updateRule(editingRule?.copy(toc = t.block()))
                }
                RuleFieldRow("目录URL", t.url) { upd { copy(url = it) } }
                RuleFieldRow("章节项选择器", t.item) { upd { copy(item = it) } }
                RuleFieldRow("偏移量", t.offset.toString()) {
                    upd { copy(offset = it.toIntOrNull() ?: 0) }
                }
                SwitchRow("倒序", t.desc) { upd { copy(desc = it) } }
            }
        }

        // ─── Chapter Section ───
        item {
            ExpandableSection("章节", expandChapter, { expandChapter = it }) {
                val c = editingRule?.chapter ?: RuleChapterSection()
                fun upd(block: RuleChapterSection.() -> RuleChapterSection) {
                    updateRule(editingRule?.copy(chapter = c.block()))
                }
                RuleFieldRow("标题选择器", c.title) { upd { copy(title = it) } }
                RuleFieldRow("正文选择器", c.content) { upd { copy(content = it) } }
                RuleFieldRow("段落标签", c.paragraphTag) { upd { copy(paragraphTag = it) } }
                SwitchRow("段落标签闭合", c.paragraphTagClosed) {
                    upd { copy(paragraphTagClosed = it) }
                }
                RuleFieldRow("过滤文本(正则)", c.filterTxt) { upd { copy(filterTxt = it) } }
                RuleFieldRow("过滤标签选择器", c.filterTag) { upd { copy(filterTag = it) } }
            }
        }

        // ─── Test Section ───
        item {
            ExpandableSection("测试", expandTest, { expandTest = it }) {
                OutlinedTextField(
                    value = testKeyword,
                    onValueChange = {
                        testKeyword = it
                        mainViewModel.ruleEditor.setRuleTestKeyword(it)
                    },
                    label = { Text("测试关键字") },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true
                )
                Spacer(Modifier.height(8.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(
                        onClick = {
                            updateRule(editingRule)
                            mainViewModel.ruleEditor.setRuleTestKeyword(testKeyword)
                            scope.launch { mainViewModel.ruleEditor.testRule() }
                        },
                        enabled = !isTesting && testKeyword.isNotBlank(),
                        shape = MaterialTheme.shapes.small
                    ) {
                        Text("测试")
                    }
                    OutlinedButton(
                        onClick = {
                            updateRule(editingRule)
                            mainViewModel.ruleEditor.setRuleTestKeyword(testKeyword)
                            scope.launch { mainViewModel.ruleEditor.debugRule() }
                        },
                        enabled = !isTesting,
                        shape = MaterialTheme.shapes.small
                    ) {
                        Text("Debug")
                    }
                }
                if (testStatus.isNotEmpty()) {
                    Spacer(Modifier.height(8.dp))
                    Text(testStatus, style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.primary)
                }
                if (testDiag.isNotEmpty()) {
                    PreviewBlock("诊断信息", testDiag)
                }
                if (testSearch.isNotEmpty()) {
                    PreviewBlock("搜索结果", testSearch)
                }
                if (testToc.isNotEmpty()) {
                    PreviewBlock("目录预览", testToc)
                }
                if (testContent.isNotEmpty()) {
                    PreviewBlock("内容预览", testContent)
                }
            }
        }
    }
}

// ─── Composable helpers ───

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun RuleSelectorDropdown(
    items: List<RuleListItem>,
    selected: RuleListItem?,
    onSelect: (RuleListItem) -> Unit
) {
    var expanded by remember { mutableStateOf(false) }

    ExposedDropdownMenuBox(
        expanded = expanded,
        onExpandedChange = { expanded = it }
    ) {
        OutlinedTextField(
            value = selected?.let { "${it.name} (${it.id})" } ?: "选择规则",
            onValueChange = {},
            readOnly = true,
            modifier = Modifier
                .fillMaxWidth()
                .menuAnchor(MenuAnchorType.PrimaryNotEditable),
            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
            singleLine = true
        )
        ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            items.forEach { item ->
                DropdownMenuItem(
                    text = {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            val healthIcon = when (item.isHealthy) {
                                true -> "✅"
                                false -> "❌"
                                null -> "⚪"
                            }
                            Text("$healthIcon ${item.name}")
                        }
                    },
                    onClick = {
                        onSelect(item)
                        expanded = false
                    }
                )
            }
        }
    }
}

@Composable
private fun ExpandableSection(
    title: String,
    expanded: Boolean,
    onToggle: (Boolean) -> Unit,
    content: @Composable ColumnScope.() -> Unit
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = MaterialTheme.shapes.medium,
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)
    ) {
        Column {
            TextButton(
                onClick = { onToggle(!expanded) },
                modifier = Modifier.fillMaxWidth(),
                shape = MaterialTheme.shapes.medium
            ) {
                val prefix = if (expanded) "▼" else "▶"
                Text(
                    "$prefix $title",
                    style = MaterialTheme.typography.titleSmall.copy(fontWeight = FontWeight.Bold),
                    modifier = Modifier.fillMaxWidth()
                )
            }
            AnimatedVisibility(visible = expanded) {
                Column(
                    modifier = Modifier.padding(horizontal = 12.dp, vertical = 4.dp),
                    verticalArrangement = Arrangement.spacedBy(6.dp),
                    content = content
                )
            }
        }
    }
}

@Composable
private fun RuleFieldRow(label: String, value: String, onValueChange: (String) -> Unit) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        modifier = Modifier.fillMaxWidth(),
        singleLine = true,
        textStyle = MaterialTheme.typography.bodySmall
    )
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, onCheckedChange: (Boolean) -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 4.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(label, style = MaterialTheme.typography.bodyMedium)
        Switch(checked = checked, onCheckedChange = onCheckedChange)
    }
}

@Composable
private fun PreviewBlock(title: String, text: String) {
    Spacer(Modifier.height(8.dp))
    Text(title, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary)
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = MaterialTheme.shapes.small,
        color = MaterialTheme.colorScheme.surfaceVariant,
        tonalElevation = 1.dp
    ) {
        Text(
            text = text,
            style = MaterialTheme.typography.bodySmall,
            modifier = Modifier.padding(8.dp)
        )
    }
}
