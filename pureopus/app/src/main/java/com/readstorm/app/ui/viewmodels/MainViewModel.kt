package com.readstorm.app.ui.viewmodels

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import androidx.lifecycle.viewModelScope
import com.readstorm.app.application.abstractions.*
import com.readstorm.app.domain.models.BookEntity
import com.readstorm.app.domain.models.BookSourceRule
import com.readstorm.app.infrastructure.services.*
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

object TabIndex {
    const val SEARCH = 0
    const val DOWNLOAD_TASK = 1
    const val DIAGNOSTIC = 2
    const val BOOKSHELF = 3
    const val READER = 4
    const val RULE_EDITOR = 5
    const val SETTINGS = 6
    const val ABOUT = 7
    const val LOG = 8
}

class MainViewModel(application: Application) : AndroidViewModel(application) {

    // ── Services (lazy-initialized in init()) ──
    lateinit var appSettingsUseCase: IAppSettingsUseCase
    lateinit var bookRepository: IBookRepository
    lateinit var ruleCatalogUseCase: IRuleCatalogUseCase
    lateinit var ruleFileLoader: RuleFileLoader
    lateinit var searchBooksUseCase: ISearchBooksUseCase
    lateinit var downloadBookUseCase: IDownloadBookUseCase
    lateinit var healthCheckUseCase: ISourceHealthCheckUseCase
    lateinit var diagnosticUseCase: ISourceDiagnosticUseCase
    lateinit var ruleEditorUseCase: IRuleEditorUseCase
    lateinit var autoDownloadPlanner: IReaderAutoDownloadPlanner
    lateinit var downloadQueue: SourceDownloadQueue
    lateinit var coverService: CoverService

    // ── Sub-ViewModels ──
    lateinit var searchDownload: SearchDownloadViewModel
    lateinit var bookshelf: BookshelfViewModel
    lateinit var reader: ReaderViewModel
    lateinit var settings: SettingsViewModel
    lateinit var diagnostic: DiagnosticViewModel
    lateinit var ruleEditor: RuleEditorViewModel

    // ── Shared State ──
    private val _statusMessage = MutableLiveData("就绪：可先用假数据验证 UI 与流程。")
    val statusMessage: LiveData<String> = _statusMessage

    private val _selectedTabIndex = MutableLiveData(TabIndex.SEARCH)
    val selectedTabIndex: LiveData<Int> = _selectedTabIndex

    private val _isReaderTabVisible = MutableLiveData(false)
    val isReaderTabVisible: LiveData<Boolean> = _isReaderTabVisible

    /** 一次性导航事件：当非 null 时，宿主 Activity 应打开阅读器子页 */
    private val _openReaderEvent = MutableLiveData<BookEntity?>(null)
    val openReaderEvent: LiveData<BookEntity?> = _openReaderEvent

    private val _availableSourceCount = MutableLiveData(0)
    val availableSourceCount: LiveData<Int> = _availableSourceCount

    private val _sourcesVersion = MutableLiveData(0)
    val sourcesVersion: LiveData<Int> = _sourcesVersion

    val sources = mutableListOf<SourceItem>()

    // ── Lazy-init infrastructure ──
    private val settingsInitLock = Mutex()
    private val sourcesInitLock = Mutex()
    private val bookshelfInitLock = Mutex()
    private var settingsInitialized = false
    private var sourcesInitialized = false
    private var bookshelfInitialized = false

    fun setStatusMessage(msg: String) {
        _statusMessage.postValue(msg)
        AppLogger.log("MainViewModel", msg)
    }

    fun setSelectedTabIndex(index: Int) {
        _selectedTabIndex.postValue(index)
        viewModelScope.launch { ensureTabInitialized(index) }
    }

    fun setReaderTabVisible(visible: Boolean) {
        _isReaderTabVisible.postValue(visible)
    }

    fun clearOpenReaderEvent() {
        _openReaderEvent.postValue(null)
    }

    fun notifySourcesChanged() {
        _sourcesVersion.postValue((_sourcesVersion.value ?: 0) + 1)
    }

    fun initialize() {
        val context = getApplication<Application>()
        val workDir = WorkDirectoryManager.getDefaultWorkDirectory(context)

        appSettingsUseCase = JsonFileAppSettingsUseCase(context)
        bookRepository = SqliteBookRepository(context)
        ruleCatalogUseCase = EmbeddedRuleCatalogUseCase(context)
        ruleFileLoader = RuleFileLoader
        searchBooksUseCase = HybridSearchBooksUseCase(context, ruleCatalogUseCase)
        downloadBookUseCase = RuleBasedDownloadBookUseCase(context, bookRepository)
        healthCheckUseCase = FastSourceHealthCheckUseCase(context)
        diagnosticUseCase = RuleBasedSourceDiagnosticUseCase(context)
        ruleEditorUseCase = FileBasedRuleEditorUseCase(context)
        autoDownloadPlanner = ReaderAutoDownloadPlanner(bookRepository)
        downloadQueue = SourceDownloadQueue()
        coverService = CoverService(context, bookRepository)

        settings = SettingsViewModel(this, appSettingsUseCase)
        diagnostic = DiagnosticViewModel(this)
        ruleEditor = RuleEditorViewModel(this)
        bookshelf = BookshelfViewModel(this, bookRepository, appSettingsUseCase)
        reader = ReaderViewModel(this, bookRepository)
        searchDownload = SearchDownloadViewModel(this, bookRepository)

        viewModelScope.launch {
            try {
                ensureSettingsInitialized()
                ensureSourcesInitialized()
            } catch (e: Exception) {
                setStatusMessage("初始化异常：${e.message}")
            }
        }
    }

    // ── Lazy Initialization ──

    private suspend fun ensureSettingsInitialized() {
        if (settingsInitialized) return
        settingsInitLock.withLock {
            if (settingsInitialized) return
            settings.loadSettings()
            settingsInitialized = true
        }
    }

    private suspend fun ensureSourcesInitialized() {
        if (sourcesInitialized) return
        sourcesInitLock.withLock {
            if (sourcesInitialized) return
            loadRuleStats()
            sourcesInitialized = true
        }
    }

    private suspend fun ensureBookshelfInitialized() {
        if (bookshelfInitialized) return
        bookshelfInitLock.withLock {
            if (bookshelfInitialized) return
            bookshelf.init()
            bookshelfInitialized = true
        }
    }

    private suspend fun ensureTabInitialized(tabIndex: Int) {
        try {
            when (tabIndex) {
                TabIndex.SEARCH, TabIndex.DOWNLOAD_TASK -> ensureSourcesInitialized()
                TabIndex.DIAGNOSTIC -> ensureSourcesInitialized()
                TabIndex.BOOKSHELF -> ensureBookshelfInitialized()
                TabIndex.READER -> ensureSettingsInitialized()
                TabIndex.SETTINGS, TabIndex.ABOUT -> ensureSettingsInitialized()
                TabIndex.RULE_EDITOR -> {
                    ensureSourcesInitialized()
                    ruleEditor.loadRuleList()
                }
            }
        } catch (e: Exception) {
            setStatusMessage("模块初始化失败：${e.message}")
        }
    }

    private suspend fun loadRuleStats() {
        try {
            val rules = ruleCatalogUseCase.getAll()
            sources.clear()
            sources.add(SourceItem(id = 0, name = "全部书源", url = "", searchSupported = true))
            rules.forEach { sources.add(SourceItem.fromRule(it)) }

            if (rules.isNotEmpty()) {
                searchDownload.selectedSourceId = rules.first().id
            }
            _availableSourceCount.postValue(rules.size)
            notifySourcesChanged()
            setStatusMessage("就绪：已加载 ${rules.size} 条书源规则，可切换测试。")
        } catch (e: Exception) {
            setStatusMessage("加载书源失败：${e.message}")
        }
    }

    fun openDbBookAndSwitchToReader(book: BookEntity) {
        viewModelScope.launch {
            try {
                ensureSettingsInitialized()
                reader.openBook(book)
                setReaderTabVisible(true)
                _openReaderEvent.postValue(book)
            } catch (e: Exception) {
                setStatusMessage("打开书籍失败：${e.message}")
                AppLogger.log("MainViewModel", "openDbBookAndSwitchToReader error: ${e.stackTraceToString()}")
            }
        }
    }

    override fun onCleared() {
        super.onCleared()
        reader.onCleared()
    }
}
