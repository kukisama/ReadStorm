package com.readstorm.app.domain.models

import com.google.gson.annotations.SerializedName

data class FullBookSourceRule(
    @SerializedName("id") var id: Int = 0,
    @SerializedName("url") var url: String = "",
    @SerializedName("name") var name: String = "",
    @SerializedName("comment") var comment: String = "",
    @SerializedName("type") var type: String = "html",
    @SerializedName("language") var language: String = "",
    @SerializedName("search") var search: RuleSearchSection? = null,
    @SerializedName("book") var book: RuleBookSection? = null,
    @SerializedName("toc") var toc: RuleTocSection? = null,
    @SerializedName("chapter") var chapter: RuleChapterSection? = null
) {
    val fileName: String get() = "rule-$id.json"
}

data class RuleSearchSection(
    @SerializedName("url") var url: String = "",
    @SerializedName("method") var method: String = "get",
    @SerializedName("data") var data: String = "{}",
    @SerializedName("cookies") var cookies: String = "",
    @SerializedName("result") var result: String = "",
    @SerializedName("bookName") var bookName: String = "",
    @SerializedName("author") var author: String = "",
    @SerializedName("category") var category: String = "",
    @SerializedName("wordCount") var wordCount: String = "",
    @SerializedName("status") var status: String = "",
    @SerializedName("latestChapter") var latestChapter: String = "",
    @SerializedName("lastUpdateTime") var lastUpdateTime: String = "",
    @SerializedName("pagination") var pagination: Boolean = false,
    @SerializedName("nextPage") var nextPage: String = "",
    @SerializedName("limitPage") var limitPage: Int = 3
)

data class RuleBookSection(
    @SerializedName("bookName") var bookName: String = "",
    @SerializedName("author") var author: String = "",
    @SerializedName("intro") var intro: String = "",
    @SerializedName("category") var category: String = "",
    @SerializedName("coverUrl") var coverUrl: String = "",
    @SerializedName("latestChapter") var latestChapter: String = "",
    @SerializedName("lastUpdateTime") var lastUpdateTime: String = "",
    @SerializedName("status") var status: String = ""
)

data class RuleTocSection(
    @SerializedName("url") var url: String = "",
    @SerializedName("item") var item: String = "",
    @SerializedName("offset") var offset: Int = 0,
    @SerializedName("desc") var desc: Boolean = false,
    @SerializedName("pagination") var pagination: Boolean = false,
    @SerializedName("nextPage") var nextPage: String = ""
)

data class RuleChapterSection(
    @SerializedName("title") var title: String = "",
    @SerializedName("content") var content: String = "",
    @SerializedName("paragraphTagClosed") var paragraphTagClosed: Boolean = false,
    @SerializedName("paragraphTag") var paragraphTag: String = "",
    @SerializedName("filterTxt") var filterTxt: String = "",
    @SerializedName("filterTag") var filterTag: String = "",
    @SerializedName("pagination") var pagination: Boolean = false,
    @SerializedName("nextPage") var nextPage: String = ""
)
