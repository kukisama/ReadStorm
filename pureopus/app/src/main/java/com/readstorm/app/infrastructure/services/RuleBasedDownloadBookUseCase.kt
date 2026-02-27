package com.readstorm.app.infrastructure.services

import android.content.Context
import com.readstorm.app.application.abstractions.IBookRepository
import com.readstorm.app.application.abstractions.IDownloadBookUseCase
import com.readstorm.app.application.abstractions.ILiveDiagnosticSink
import com.readstorm.app.domain.models.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.jsoup.Jsoup
import java.text.SimpleDateFormat
import java.util.*

class RuleBasedDownloadBookUseCase(
    private val context: Context,
    private val bookRepo: IBookRepository,
    private val liveSink: ILiveDiagnosticSink? = null
) : IDownloadBookUseCase {

    private val httpClient: OkHttpClient = RuleHttpHelper.createHttpClient()
    private val dateFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.getDefault())

    private fun trace(msg: String) {
        val line = "[${dateFormat.format(Date())}] $msg"
        liveSink?.append(line)
    }

    override suspend fun queue(task: DownloadTask, selectedBook: SearchResult, mode: DownloadMode) {
        withContext(Dispatchers.IO) {
            try {
                trace("[download-start] taskId=${task.id}, sourceId=${selectedBook.sourceId}, book='${selectedBook.title}', mode=$mode")
                task.transitionTo(DownloadTaskStatus.Downloading)

                if (selectedBook.url.isBlank()) {
                    throw IllegalStateException("当前搜索结果未包含书籍详情 URL，无法下载。")
                }

                val rule = RuleFileLoader.loadRule(context, selectedBook.sourceId)
                    ?: throw IllegalStateException("未找到书源 ${selectedBook.sourceId} 对应规则文件。")

                // 1. Fetch TOC
                val tocChapters = fetchToc(rule, selectedBook)
                trace("[toc] parsedChapters=${tocChapters.size}")

                if (tocChapters.isEmpty()) {
                    throw IllegalStateException("目录解析为空，请检查书源规则。")
                }

                // 2. Find or create book entity
                var book = bookRepo.findBook(selectedBook.title, selectedBook.author)
                if (book == null) {
                    book = BookEntity(
                        id = UUID.randomUUID().toString(),
                        title = selectedBook.title,
                        author = selectedBook.author,
                        sourceId = selectedBook.sourceId,
                        tocUrl = selectedBook.url,
                        totalChapters = tocChapters.size
                    )
                    bookRepo.upsertBook(book)
                    trace("[book] created new bookId=${book.id}")
                } else {
                    book.totalChapters = tocChapters.size
                    book.sourceId = selectedBook.sourceId
                    book.tocUrl = selectedBook.url
                    bookRepo.upsertBook(book)
                    trace("[book] updated existing bookId=${book.id}")
                }

                // 3. Insert chapters
                val existingChapters = bookRepo.getChapters(book.id)
                val existingMap = existingChapters.associateBy { it.indexNo }

                val newChapters = tocChapters.mapIndexedNotNull { index, (title, url) ->
                    if (existingMap.containsKey(index) && existingMap[index]?.status == ChapterStatus.Done) {
                        null // skip already downloaded chapters
                    } else {
                        ChapterEntity(
                            bookId = book.id,
                            indexNo = index,
                            title = title,
                            sourceId = selectedBook.sourceId,
                            sourceUrl = url,
                            status = ChapterStatus.Pending
                        )
                    }
                }

                if (newChapters.isNotEmpty()) {
                    bookRepo.insertChapters(book.id, newChapters)
                }
                trace("[chapters] new=${newChapters.size}, existing=${existingChapters.size}")

                // 4. Download chapters
                var doneCount = existingChapters.count { it.status == ChapterStatus.Done }
                val totalToDownload = if (mode == DownloadMode.FullBook) {
                    newChapters
                } else {
                    newChapters.take(10) // partial mode
                }

                for (ch in totalToDownload) {
                    try {
                        val content = fetchChapterContent(rule, ch.sourceUrl)
                        if (content.isNotBlank()) {
                            bookRepo.updateChapter(book.id, ch.indexNo, ChapterStatus.Done, content, null)
                            doneCount++
                        } else {
                            bookRepo.updateChapter(book.id, ch.indexNo, ChapterStatus.Failed, null, "内容为空")
                        }
                    } catch (e: Exception) {
                        bookRepo.updateChapter(book.id, ch.indexNo, ChapterStatus.Failed, null, e.message)
                    }

                    val percent = if (tocChapters.isNotEmpty()) (doneCount * 100 / tocChapters.size) else 0
                    task.updateProgress(percent)
                    task.updateChapterProgress(doneCount, tocChapters.size, ch.title)
                }

                // 5. Update book progress
                book.doneChapters = doneCount
                bookRepo.upsertBook(book)

                task.transitionTo(DownloadTaskStatus.Succeeded)
                trace("[download-end] success, doneChapters=$doneCount/${tocChapters.size}")
            } catch (e: Exception) {
                task.error = e.message ?: "未知错误"
                task.transitionTo(DownloadTaskStatus.Failed)
                trace("[download-error] ${e.message}")
            }
        }
    }

    override suspend fun checkNewChapters(book: BookEntity): Int = withContext(Dispatchers.IO) {
        try {
            val rule = RuleFileLoader.loadRule(context, book.sourceId) ?: return@withContext 0
            val searchResult = SearchResult(
                title = book.title,
                author = book.author,
                sourceId = book.sourceId,
                sourceName = rule.name,
                url = book.tocUrl,
                latestChapter = "",
                updatedAt = System.currentTimeMillis()
            )
            val tocChapters = fetchToc(rule, searchResult)
            val existingCount = bookRepo.getChapters(book.id).size
            maxOf(0, tocChapters.size - existingCount)
        } catch (_: Exception) {
            0
        }
    }

    override suspend fun fetchChapterFromSource(
        book: BookEntity,
        chapterTitle: String,
        sourceId: Int
    ): Triple<Boolean, String, String> = withContext(Dispatchers.IO) {
        try {
            val rule = RuleFileLoader.loadRule(context, sourceId)
                ?: return@withContext Triple(false, "", "未找到书源规则")

            val chapters = bookRepo.getChapters(book.id)
            val chapter = chapters.find { it.title == chapterTitle }
                ?: return@withContext Triple(false, "", "未找到章节")

            val content = fetchChapterContent(rule, chapter.sourceUrl)
            if (content.isNotBlank()) {
                Triple(true, content, "获取成功")
            } else {
                Triple(false, "", "内容为空")
            }
        } catch (e: Exception) {
            Triple(false, "", e.message ?: "未知错误")
        }
    }

    private fun fetchToc(
        rule: FullBookSourceRule,
        selectedBook: SearchResult
    ): List<Pair<String, String>> {
        val toc = rule.toc ?: return emptyList()
        val itemSelector = RuleHttpHelper.normalizeSelector(toc.item)
        if (itemSelector.isBlank()) return emptyList()

        val tocUrl = toc.url.ifBlank { selectedBook.url }
        if (tocUrl.isBlank()) return emptyList()

        val request = Request.Builder().url(tocUrl).build()
        val response = httpClient.newCall(request).execute()
        val html = response.use { resp ->
            if (!resp.isSuccessful) return emptyList()
            resp.body?.string() ?: ""
        }
        if (html.isBlank()) return emptyList()

        val doc = Jsoup.parse(html, tocUrl)
        val items = doc.select(itemSelector)

        val chapters = items.mapNotNull { item ->
            val title = item.text().trim()
            if (title.isBlank()) return@mapNotNull null
            val href = item.attr("href")
            val url = RuleHttpHelper.resolveUrl(tocUrl, href)
            title to url
        }.let { list ->
            if (toc.offset > 0 && toc.offset < list.size) list.drop(toc.offset) else list
        }

        return if (toc.desc) chapters.reversed() else chapters
    }

    private fun fetchChapterContent(rule: FullBookSourceRule, chapterUrl: String): String {
        val chapter = rule.chapter ?: return ""
        val contentSelector = RuleHttpHelper.normalizeSelector(chapter.content)
        if (contentSelector.isBlank() || chapterUrl.isBlank()) return ""

        val request = Request.Builder().url(chapterUrl).build()
        val response = httpClient.newCall(request).execute()
        val html = response.use { resp ->
            if (!resp.isSuccessful) return ""
            resp.body?.string() ?: ""
        }
        if (html.isBlank()) return ""

        val doc = Jsoup.parse(html, chapterUrl)
        val contentElement = doc.selectFirst(contentSelector) ?: return ""

        // Apply filter rules
        if (chapter.filterTag.isNotBlank()) {
            contentElement.select(chapter.filterTag).remove()
        }

        var text = if (chapter.paragraphTag.isNotBlank()) {
            contentElement.select(chapter.paragraphTag).joinToString("\n") { it.text() }
        } else {
            contentElement.text()
        }

        // Apply text filters
        if (chapter.filterTxt.isNotBlank()) {
            val filters = chapter.filterTxt.split("||").map { it.trim() }.filter { it.isNotBlank() }
            for (filter in filters) {
                text = text.replace(filter, "")
            }
        }

        return text.trim()
    }
}
