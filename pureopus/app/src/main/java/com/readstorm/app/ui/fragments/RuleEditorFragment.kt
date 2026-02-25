package com.readstorm.app.ui.fragments

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.fragment.app.Fragment
import com.readstorm.app.R
import com.readstorm.app.databinding.FragmentRuleEditorBinding
import com.readstorm.app.domain.models.FullBookSourceRule

class RuleEditorFragment : Fragment() {

    private var _binding: FragmentRuleEditorBinding? = null
    private val binding get() = _binding!!

    private var currentRule: FullBookSourceRule? = null

    // Expandable section state tracking
    private val sectionExpanded = mutableMapOf(
        "basic" to false,
        "search" to false,
        "book" to false,
        "toc" to false,
        "chapter" to false,
        "test" to false
    )

    override fun onCreateView(
        inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?
    ): View {
        _binding = FragmentRuleEditorBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        setupExpandableSections()
        setupFieldLabels()
        setupListeners()
    }

    private fun setupExpandableSections() {
        setupSection("basic", binding.tvSectionBasicHeader, binding.sectionBasicContent)
        setupSection("search", binding.tvSectionSearchHeader, binding.sectionSearchContent)
        setupSection("book", binding.tvSectionBookHeader, binding.sectionBookContent)
        setupSection("toc", binding.tvSectionTocHeader, binding.sectionTocContent)
        setupSection("chapter", binding.tvSectionChapterHeader, binding.sectionChapterContent)
        setupSection("test", binding.tvSectionTestHeader, binding.sectionTestContent)
    }

    private fun setupSection(key: String, header: TextView, content: View) {
        header.setOnClickListener {
            val expanded = !(sectionExpanded[key] ?: false)
            sectionExpanded[key] = expanded
            content.visibility = if (expanded) View.VISIBLE else View.GONE
            val prefix = if (expanded) "▼" else "▶"
            val label = header.text.toString().substringAfter(" ")
            header.text = "$prefix $label"
        }
    }

    private fun setupFieldLabels() {
        // Basic info fields
        setFieldLabel(binding.fieldRuleId, "ID")
        setFieldLabel(binding.fieldRuleName, "名称")
        setFieldLabel(binding.fieldRuleUrl, "网站URL")
        setFieldLabel(binding.fieldRuleComment, "备注")
        setFieldLabel(binding.fieldRuleType, "类型")
        setFieldLabel(binding.fieldRuleLanguage, "语言")

        // Search rule fields
        setFieldLabel(binding.fieldSearchUrl, "搜索URL")
        setFieldLabel(binding.fieldSearchMethod, "Method")
        setFieldLabel(binding.fieldSearchPostData, "POST Data")
        setFieldLabel(binding.fieldSearchCookie, "Cookie")
        setFieldLabel(binding.fieldSearchResult, "结果选择器")
        setFieldLabel(binding.fieldSearchBookName, "书名选择器")
        setFieldLabel(binding.fieldSearchAuthor, "作者选择器")
        setFieldLabel(binding.fieldSearchCategory, "分类选择器")
        setFieldLabel(binding.fieldSearchWordCount, "字数选择器")
        setFieldLabel(binding.fieldSearchStatus, "状态选择器")
        setFieldLabel(binding.fieldSearchLatestChapter, "最新章节选择器")
        setFieldLabel(binding.fieldSearchUpdateTime, "更新时间选择器")

        // Book detail fields
        setFieldLabel(binding.fieldBookName, "书名选择器")
        setFieldLabel(binding.fieldBookAuthor, "作者选择器")
        setFieldLabel(binding.fieldBookIntro, "简介选择器")
        setFieldLabel(binding.fieldBookCategory, "分类选择器")
        setFieldLabel(binding.fieldBookCoverUrl, "封面URL选择器")
        setFieldLabel(binding.fieldBookLatestChapter, "最新章节选择器")
        setFieldLabel(binding.fieldBookUpdateTime, "更新时间选择器")
        setFieldLabel(binding.fieldBookStatus, "状态选择器")

        // TOC fields
        setFieldLabel(binding.fieldTocUrl, "目录URL")
        setFieldLabel(binding.fieldTocItem, "章节项选择器")
        setFieldLabel(binding.fieldTocOffset, "偏移量")

        // Chapter fields
        setFieldLabel(binding.fieldChapterTitle, "标题选择器")
        setFieldLabel(binding.fieldChapterContent, "正文选择器")
        setFieldLabel(binding.fieldChapterParagraphTag, "段落标签")
        setFieldLabel(binding.fieldChapterFilterText, "过滤文本（正则）")
        setFieldLabel(binding.fieldChapterFilterTag, "过滤标签选择器")
    }

    private fun setFieldLabel(fieldView: View, label: String) {
        fieldView.findViewById<TextView>(R.id.tvFieldLabel)?.text = label
    }

    private fun setupListeners() {
        binding.btnRefreshRules.setOnClickListener { refreshRules() }
        binding.btnNewRule.setOnClickListener { createNewRule() }
        binding.btnCopyRule.setOnClickListener { copyCurrentRule() }
        binding.btnDeleteRule.setOnClickListener { deleteCurrentRule() }
        binding.btnTestRule.setOnClickListener { testCurrentRule() }
        binding.btnSaveRule.setOnClickListener { saveCurrentRule() }
        binding.btnRestoreDefault.setOnClickListener { restoreDefault() }
        binding.btnDebug.setOnClickListener { debugCurrentRule() }
    }

    fun loadRule(rule: FullBookSourceRule) {
        currentRule = rule
        populateFields(rule)
    }

    private fun populateFields(rule: FullBookSourceRule) {
        setFieldValue(binding.fieldRuleId, rule.id.toString())
        setFieldValue(binding.fieldRuleName, rule.name)
        setFieldValue(binding.fieldRuleUrl, rule.url)
        setFieldValue(binding.fieldRuleComment, rule.comment)
        setFieldValue(binding.fieldRuleType, rule.type)
        setFieldValue(binding.fieldRuleLanguage, rule.language)

        rule.search?.let { s ->
            setFieldValue(binding.fieldSearchUrl, s.url)
            setFieldValue(binding.fieldSearchMethod, s.method)
            setFieldValue(binding.fieldSearchPostData, s.data)
            setFieldValue(binding.fieldSearchCookie, s.cookies)
            setFieldValue(binding.fieldSearchResult, s.result)
            setFieldValue(binding.fieldSearchBookName, s.bookName)
            setFieldValue(binding.fieldSearchAuthor, s.author)
            setFieldValue(binding.fieldSearchCategory, s.category)
            setFieldValue(binding.fieldSearchWordCount, s.wordCount)
            setFieldValue(binding.fieldSearchStatus, s.status)
            setFieldValue(binding.fieldSearchLatestChapter, s.latestChapter)
            setFieldValue(binding.fieldSearchUpdateTime, s.lastUpdateTime)
        }

        rule.book?.let { b ->
            setFieldValue(binding.fieldBookName, b.bookName)
            setFieldValue(binding.fieldBookAuthor, b.author)
            setFieldValue(binding.fieldBookIntro, b.intro)
            setFieldValue(binding.fieldBookCategory, b.category)
            setFieldValue(binding.fieldBookCoverUrl, b.coverUrl)
            setFieldValue(binding.fieldBookLatestChapter, b.latestChapter)
            setFieldValue(binding.fieldBookUpdateTime, b.lastUpdateTime)
            setFieldValue(binding.fieldBookStatus, b.status)
        }

        rule.toc?.let { t ->
            setFieldValue(binding.fieldTocUrl, t.url)
            setFieldValue(binding.fieldTocItem, t.item)
            setFieldValue(binding.fieldTocOffset, t.offset.toString())
            binding.switchTocDesc.isChecked = t.desc
        }

        rule.chapter?.let { c ->
            setFieldValue(binding.fieldChapterTitle, c.title)
            setFieldValue(binding.fieldChapterContent, c.content)
            setFieldValue(binding.fieldChapterParagraphTag, c.paragraphTag)
            binding.switchChapterClosed.isChecked = c.paragraphTagClosed
            setFieldValue(binding.fieldChapterFilterText, c.filterTxt)
            setFieldValue(binding.fieldChapterFilterTag, c.filterTag)
        }
    }

    private fun setFieldValue(fieldView: View, value: String) {
        fieldView.findViewById<android.widget.EditText>(R.id.etFieldValue)?.setText(value)
    }

    private fun getFieldValue(fieldView: View): String {
        return fieldView.findViewById<android.widget.EditText>(R.id.etFieldValue)
            ?.text?.toString() ?: ""
    }

    private fun refreshRules() {
        // TODO: invoke rule catalog refresh
    }

    private fun createNewRule() {
        // TODO: create new rule template
    }

    private fun copyCurrentRule() {
        // TODO: copy current rule
    }

    private fun deleteCurrentRule() {
        // TODO: delete current rule
    }

    private fun testCurrentRule() {
        val keyword = binding.etTestKeyword.text?.toString()?.trim() ?: return
        if (keyword.isEmpty()) return
        binding.tvTestStatus.text = "测试中…"
        binding.tvTestStatus.visibility = View.VISIBLE
        // TODO: invoke test use case
    }

    private fun saveCurrentRule() {
        // TODO: collect field values and save
    }

    private fun restoreDefault() {
        // TODO: restore default rule values
    }

    private fun debugCurrentRule() {
        // TODO: open debug view
    }

    fun updateTestResults(
        diagnostics: String,
        searchPreview: String,
        tocPreview: String,
        contentPreview: String
    ) {
        binding.etTestDiagnostics.setText(diagnostics)
        binding.etTestSearchPreview.setText(searchPreview)
        binding.etTestTocPreview.setText(tocPreview)
        binding.etTestContentPreview.setText(contentPreview)

        // Auto-expand test section
        if (sectionExpanded["test"] != true) {
            binding.tvSectionTestHeader.performClick()
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
