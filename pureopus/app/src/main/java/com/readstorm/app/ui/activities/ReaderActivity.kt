package com.readstorm.app.ui.activities

import android.annotation.SuppressLint
import android.content.res.Configuration
import android.graphics.Color
import android.os.Build
import android.os.Bundle
import android.view.GestureDetector
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.widget.SeekBar
import androidx.activity.OnBackPressedCallback
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.recyclerview.widget.LinearLayoutManager
import com.readstorm.app.R
import com.readstorm.app.databinding.ActivityReaderBinding
import com.readstorm.app.domain.models.AppSettings
import kotlin.math.abs

class ReaderActivity : AppCompatActivity() {

    companion object {
        const val EXTRA_BOOK_ID = "book_id"
        private const val SWIPE_THRESHOLD = 100
        private const val SWIPE_VELOCITY_THRESHOLD = 100
    }

    private lateinit var binding: ActivityReaderBinding

    private var bookId: String = ""
    private var toolbarVisible = false
    private var tocVisible = false
    private var settingsSheetVisible = false
    private var useVolumeKeyPaging = false
    private var useSwipePaging = false
    private var hideSystemStatusBar = false
    private var extendIntoCutout = false

    // Reader state
    private var currentChapterIndex = 0
    private var currentPageIndex = 0
    private var totalChapters = 0
    private var isBookmarked = false

    // Settings (loaded from AppSettings)
    private var fontSize = 20
    private var lineHeight = 36
    private var readerBg = "#FFFBF0"
    private var readerFg = "#1E293B"
    private var topReserve = 4
    private var bottomReserve = 0
    private var sideReserve = 12

    private lateinit var gestureDetector: GestureDetector

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        bookId = intent.getStringExtra(EXTRA_BOOK_ID) ?: run {
            finish()
            return
        }

        binding = ActivityReaderBinding.inflate(layoutInflater)
        setContentView(binding.root)

        setupEdgeToEdge()
        setupGestures()
        setupToolbar()
        setupTocOverlay()
        setupSettingsSheet()
        setupBackNavigation()
        applyReaderTheme()
        setTocPanelWidth()
    }

    // ── Edge-to-edge & immersive mode ────────────────────────

    private fun setupEdgeToEdge() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P && extendIntoCutout) {
            window.attributes.layoutInDisplayCutoutMode =
                WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
        }

        ViewCompat.setOnApplyWindowInsetsListener(binding.root) { view, insets ->
            val systemBars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            view.setPadding(systemBars.left, systemBars.top, systemBars.right, systemBars.bottom)
            insets
        }
    }

    private fun enterImmersiveMode() {
        if (!hideSystemStatusBar) return

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.insetsController?.let {
                it.hide(WindowInsets.Type.statusBars() or WindowInsets.Type.navigationBars())
                it.systemBarsBehavior =
                    WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        } else {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility = (
                    View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                            or View.SYSTEM_UI_FLAG_FULLSCREEN
                            or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                            or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                            or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                            or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                    )
        }
    }

    private fun exitImmersiveMode() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.insetsController?.show(
                WindowInsets.Type.statusBars() or WindowInsets.Type.navigationBars()
            )
        } else {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility = View.SYSTEM_UI_FLAG_VISIBLE
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus && hideSystemStatusBar && !toolbarVisible) {
            enterImmersiveMode()
        }
    }

    // ── Gesture handling ─────────────────────────────────────

    @SuppressLint("ClickableViewAccessibility")
    private fun setupGestures() {
        gestureDetector = GestureDetector(this, object : GestureDetector.SimpleOnGestureListener() {
            override fun onFling(
                e1: MotionEvent?,
                e2: MotionEvent,
                velocityX: Float,
                velocityY: Float
            ): Boolean {
                if (!useSwipePaging || e1 == null) return false
                val dx = e2.x - e1.x
                val dy = e2.y - e1.y
                if (abs(dx) > abs(dy) && abs(dx) > SWIPE_THRESHOLD && abs(velocityX) > SWIPE_VELOCITY_THRESHOLD) {
                    if (dx < 0) nextPage() else previousPage()
                    return true
                }
                return false
            }
        })

        // Tap zones
        binding.tapZoneLeft.setOnClickListener { previousPage() }
        binding.tapZoneCenter.setOnClickListener { toggleToolbar() }
        binding.tapZoneRight.setOnClickListener { nextPage() }

        val touchListener = View.OnTouchListener { _, event ->
            gestureDetector.onTouchEvent(event)
            false
        }
        binding.tapZoneLeft.setOnTouchListener(touchListener)
        binding.tapZoneCenter.setOnTouchListener(touchListener)
        binding.tapZoneRight.setOnTouchListener(touchListener)
    }

    // ── Toolbar ──────────────────────────────────────────────

    private fun setupToolbar() {
        binding.btnBack.setOnClickListener { finish() }
        binding.btnToc.setOnClickListener { toggleTocOverlay() }
        binding.btnRefresh.setOnClickListener { refreshCurrentChapter() }
        binding.btnBookmark.setOnClickListener { toggleBookmark() }

        binding.btnPrevChapter.setOnClickListener { navigateChapter(-1) }
        binding.btnNextChapter.setOnClickListener { navigateChapter(1) }
        binding.btnSettings.setOnClickListener { showSettingsSheet() }
    }

    private fun toggleToolbar() {
        toolbarVisible = !toolbarVisible
        val visibility = if (toolbarVisible) View.VISIBLE else View.GONE
        binding.topToolbar.visibility = visibility
        binding.bottomToolbar.visibility = visibility

        if (toolbarVisible) {
            exitImmersiveMode()
        } else {
            enterImmersiveMode()
            hideSettingsSheet()
        }
    }

    private fun showToolbar() {
        toolbarVisible = true
        binding.topToolbar.visibility = View.VISIBLE
        binding.bottomToolbar.visibility = View.VISIBLE
        exitImmersiveMode()
    }

    private fun hideToolbar() {
        toolbarVisible = false
        binding.topToolbar.visibility = View.GONE
        binding.bottomToolbar.visibility = View.GONE
        enterImmersiveMode()
    }

    // ── TOC overlay ──────────────────────────────────────────

    private fun setTocPanelWidth() {
        binding.tocPanel.post {
            val params = binding.tocPanel.layoutParams
            params.width = (binding.root.width * 2 / 3)
            binding.tocPanel.layoutParams = params
        }
    }

    private fun setupTocOverlay() {
        binding.rvTocList.layoutManager = LinearLayoutManager(this)
        binding.tocDimBackground.setOnClickListener { hideTocOverlay() }

        binding.btnTocChapters.setOnClickListener {
            binding.btnTocChapters.setTypeface(null, android.graphics.Typeface.BOLD)
            binding.btnTocBookmarks.setTypeface(null, android.graphics.Typeface.NORMAL)
            // TODO: Switch adapter to chapters list
        }

        binding.btnTocBookmarks.setOnClickListener {
            binding.btnTocBookmarks.setTypeface(null, android.graphics.Typeface.BOLD)
            binding.btnTocChapters.setTypeface(null, android.graphics.Typeface.NORMAL)
            // TODO: Switch adapter to bookmarks list
        }
    }

    private fun toggleTocOverlay() {
        if (tocVisible) hideTocOverlay() else showTocOverlay()
    }

    private fun showTocOverlay() {
        tocVisible = true
        binding.tocOverlay.visibility = View.VISIBLE
        binding.tocPanel.translationX = binding.tocPanel.width.toFloat()
        binding.tocPanel.animate().translationX(0f).setDuration(250).start()
    }

    private fun hideTocOverlay() {
        tocVisible = false
        binding.tocPanel.animate()
            .translationX(binding.tocPanel.width.toFloat())
            .setDuration(250)
            .withEndAction { binding.tocOverlay.visibility = View.GONE }
            .start()
    }

    // ── Settings bottom sheet ────────────────────────────────

    private fun setupSettingsSheet() {
        // Font size controls
        binding.btnFontSizeDecrease.setOnClickListener {
            if (fontSize > 12) {
                fontSize--
                applyReaderTheme()
                updateSettingsDisplay()
            }
        }
        binding.btnFontSizeIncrease.setOnClickListener {
            if (fontSize < 40) {
                fontSize++
                applyReaderTheme()
                updateSettingsDisplay()
            }
        }

        // Line height controls
        binding.btnLineHeightDecrease.setOnClickListener {
            if (lineHeight > 20) {
                lineHeight -= 2
                applyReaderTheme()
                updateSettingsDisplay()
            }
        }
        binding.btnLineHeightIncrease.setOnClickListener {
            if (lineHeight < 60) {
                lineHeight += 2
                applyReaderTheme()
                updateSettingsDisplay()
            }
        }

        // Theme presets
        setupThemePreset(binding.themePaper, "#FFFBF0", "#1E293B")
        setupThemePreset(binding.themeGreen, "#E8F5E9", "#1E293B")
        setupThemePreset(binding.themeBlue, "#E3F2FD", "#1E293B")
        setupThemePreset(binding.themePink, "#FCE4EC", "#1E293B")
        setupThemePreset(binding.themeGray, "#F5F5F5", "#1E293B")

        // Toggle switches
        binding.switchDarkMode.setOnCheckedChangeListener { _, checked ->
            if (checked) {
                readerBg = "#1A1A1A"
                readerFg = "#C8C8C8"
            } else {
                readerBg = "#FFFBF0"
                readerFg = "#1E293B"
            }
            applyReaderTheme()
        }

        binding.switchCutoutExtend.setOnCheckedChangeListener { _, checked ->
            extendIntoCutout = checked
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                window.attributes.layoutInDisplayCutoutMode = if (checked) {
                    WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
                } else {
                    WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_DEFAULT
                }
            }
        }

        binding.switchVolumePaging.setOnCheckedChangeListener { _, checked ->
            useVolumeKeyPaging = checked
        }

        binding.switchSwipePaging.setOnCheckedChangeListener { _, checked ->
            useSwipePaging = checked
        }

        binding.switchHideStatusBar.setOnCheckedChangeListener { _, checked ->
            hideSystemStatusBar = checked
            if (checked && !toolbarVisible) enterImmersiveMode() else exitImmersiveMode()
        }

        // Margin seekbars
        setupSeekBar(binding.seekTopMargin, binding.tvTopMarginValue, topReserve) {
            topReserve = it
            applyReaderTheme()
        }
        setupSeekBar(binding.seekBottomMargin, binding.tvBottomMarginValue, bottomReserve) {
            bottomReserve = it
            applyReaderTheme()
        }
        setupSeekBar(binding.seekSideMargin, binding.tvSideMarginValue, sideReserve) {
            sideReserve = it
            applyReaderTheme()
        }

        // Restore defaults
        binding.btnRestoreDefaults.setOnClickListener { restoreDefaultSettings() }

        updateSettingsDisplay()
    }

    private fun setupThemePreset(view: View, bg: String, fg: String) {
        view.setOnClickListener {
            readerBg = bg
            readerFg = fg
            binding.switchDarkMode.isChecked = false
            applyReaderTheme()
        }
    }

    private fun setupSeekBar(
        seekBar: SeekBar,
        valueText: android.widget.TextView,
        initial: Int,
        onChange: (Int) -> Unit
    ) {
        seekBar.progress = initial
        valueText.text = initial.toString()
        seekBar.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(sb: SeekBar?, progress: Int, fromUser: Boolean) {
                valueText.text = progress.toString()
                if (fromUser) onChange(progress)
            }

            override fun onStartTrackingTouch(sb: SeekBar?) {}
            override fun onStopTrackingTouch(sb: SeekBar?) {}
        })
    }

    private fun showSettingsSheet() {
        if (settingsSheetVisible) {
            hideSettingsSheet()
            return
        }
        settingsSheetVisible = true
        binding.readerSettingsSheet.visibility = View.VISIBLE
        binding.readerSettingsSheet.animate()
            .translationY(0f)
            .setDuration(250)
            .start()
    }

    private fun hideSettingsSheet() {
        if (!settingsSheetVisible) return
        settingsSheetVisible = false
        binding.readerSettingsSheet.animate()
            .translationY(binding.readerSettingsSheet.height.toFloat())
            .setDuration(250)
            .withEndAction {
                binding.readerSettingsSheet.visibility = View.GONE
            }
            .start()
    }

    private fun updateSettingsDisplay() {
        binding.tvFontSizeValue.text = fontSize.toString()
        binding.tvLineHeightValue.text = lineHeight.toString()
        binding.seekTopMargin.progress = topReserve
        binding.tvTopMarginValue.text = topReserve.toString()
        binding.seekBottomMargin.progress = bottomReserve
        binding.tvBottomMarginValue.text = bottomReserve.toString()
        binding.seekSideMargin.progress = sideReserve
        binding.tvSideMarginValue.text = sideReserve.toString()
    }

    private fun restoreDefaultSettings() {
        val defaults = AppSettings()
        fontSize = defaults.readerFontSize
        lineHeight = defaults.readerLineHeight
        readerBg = defaults.readerBackground
        readerFg = defaults.readerForeground
        topReserve = defaults.readerTopReservePx
        bottomReserve = defaults.readerBottomReservePx
        sideReserve = defaults.readerSidePaddingPx
        useVolumeKeyPaging = defaults.readerUseVolumeKeyPaging
        useSwipePaging = defaults.readerUseSwipePaging
        hideSystemStatusBar = defaults.readerHideSystemStatusBar
        extendIntoCutout = defaults.readerExtendIntoCutout

        binding.switchDarkMode.isChecked = defaults.readerDarkMode
        binding.switchCutoutExtend.isChecked = extendIntoCutout
        binding.switchVolumePaging.isChecked = useVolumeKeyPaging
        binding.switchSwipePaging.isChecked = useSwipePaging
        binding.switchHideStatusBar.isChecked = hideSystemStatusBar

        applyReaderTheme()
        updateSettingsDisplay()
    }

    // ── Theme application ────────────────────────────────────

    private fun applyReaderTheme() {
        val bgColor = Color.parseColor(readerBg)
        val fgColor = Color.parseColor(readerFg)

        binding.root.setBackgroundColor(bgColor)
        binding.tvReaderContent.setTextColor(fgColor)
        binding.tvReaderContent.textSize = fontSize.toFloat()

        // Settings values are already in px (per AppSettings field naming convention)
        binding.tvReaderContent.setPadding(sideReserve, topReserve, sideReserve, bottomReserve)

        // Match status bar color to reader background
        window.statusBarColor = bgColor

        // Adjust status bar icon contrast
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            val isLight = isColorLight(bgColor)
            window.insetsController?.setSystemBarsAppearance(
                if (isLight) WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS else 0,
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS
            )
        } else {
            @Suppress("DEPRECATION")
            if (isColorLight(bgColor)) {
                window.decorView.systemUiVisibility =
                    window.decorView.systemUiVisibility or View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR
            } else {
                window.decorView.systemUiVisibility =
                    window.decorView.systemUiVisibility and View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR.inv()
            }
        }
    }

    private fun isColorLight(color: Int): Boolean {
        val r = Color.red(color) / 255.0
        val g = Color.green(color) / 255.0
        val b = Color.blue(color) / 255.0
        val luminance = 0.299 * r + 0.587 * g + 0.114 * b
        return luminance > 0.5
    }

    // ── Navigation ───────────────────────────────────────────

    private fun setupBackNavigation() {
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                when {
                    settingsSheetVisible -> hideSettingsSheet()
                    tocVisible -> hideTocOverlay()
                    toolbarVisible -> hideToolbar()
                    else -> {
                        isEnabled = false
                        onBackPressedDispatcher.onBackPressed()
                    }
                }
            }
        })
    }

    private fun previousPage() {
        if (currentPageIndex > 0) {
            currentPageIndex--
            // TODO: Render page content
            updateProgressDisplay()
        }
    }

    private fun nextPage() {
        currentPageIndex++
        // TODO: Render page content
        updateProgressDisplay()
    }

    private fun navigateChapter(offset: Int) {
        val target = currentChapterIndex + offset
        if (target < 0 || target >= totalChapters) return
        currentChapterIndex = target
        currentPageIndex = 0
        // TODO: Load chapter content
        updateProgressDisplay()
    }

    private fun refreshCurrentChapter() {
        // TODO: Re-fetch current chapter from source
    }

    private fun toggleBookmark() {
        isBookmarked = !isBookmarked
        binding.btnBookmark.setImageResource(
            if (isBookmarked) android.R.drawable.btn_star_big_on
            else android.R.drawable.btn_star_big_off
        )
        // TODO: Persist bookmark via IBookRepository
    }

    private fun updateProgressDisplay() {
        binding.tvProgress.text = if (totalChapters > 0) {
            "${currentChapterIndex + 1}/$totalChapters"
        } else {
            ""
        }
    }

    // ── Source switching overlay ──────────────────────────────

    fun showSourceLoadingOverlay() {
        binding.sourceLoadingOverlay.visibility = View.VISIBLE
    }

    fun hideSourceLoadingOverlay() {
        binding.sourceLoadingOverlay.visibility = View.GONE
    }

    // ── Volume key paging ────────────────────────────────────

    override fun onKeyDown(keyCode: Int, event: KeyEvent?): Boolean {
        if (useVolumeKeyPaging) {
            when (keyCode) {
                KeyEvent.KEYCODE_VOLUME_UP -> {
                    previousPage()
                    return true
                }
                KeyEvent.KEYCODE_VOLUME_DOWN -> {
                    nextPage()
                    return true
                }
            }
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent?): Boolean {
        if (useVolumeKeyPaging &&
            (keyCode == KeyEvent.KEYCODE_VOLUME_UP || keyCode == KeyEvent.KEYCODE_VOLUME_DOWN)
        ) {
            return true
        }
        return super.onKeyUp(keyCode, event)
    }
}
