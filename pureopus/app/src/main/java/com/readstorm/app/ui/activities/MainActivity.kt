package com.readstorm.app.ui.activities

import android.os.Bundle
import android.view.KeyEvent
import android.view.View
import androidx.activity.OnBackPressedCallback
import androidx.appcompat.app.AppCompatActivity
import androidx.fragment.app.Fragment
import androidx.fragment.app.FragmentManager
import androidx.lifecycle.ViewModelProvider
import com.readstorm.app.R
import com.readstorm.app.databinding.ActivityMainBinding
import com.readstorm.app.ui.viewmodels.MainViewModel

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    lateinit var mainViewModel: MainViewModel
        private set

    private var currentTabId = R.id.nav_bookshelf
    private var currentSubPage: String? = null

    private val fragmentTags = mapOf(
        R.id.nav_search to "frag_search",
        R.id.nav_tasks to "frag_tasks",
        R.id.nav_bookshelf to "frag_bookshelf",
        R.id.nav_more to "frag_more"
    )

    private val subPageTitles = mapOf(
        "diagnostic" to R.string.page_diagnostic,
        "rules" to R.string.page_rules,
        "settings" to R.string.page_settings,
        "about" to R.string.page_about,
        "log" to R.string.page_log
    )

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        // Initialize ViewModel
        mainViewModel = ViewModelProvider(this)[MainViewModel::class.java]
        mainViewModel.initialize()

        // Observe state
        mainViewModel.statusMessage.observe(this) { msg ->
            binding.tvStatusMessage.text = msg
        }
        mainViewModel.searchDownload.activeDownloadSummary.observe(this) { summary ->
            if (summary.isNullOrBlank()) {
                binding.tvDownloadSummary.visibility = View.GONE
            } else {
                binding.tvDownloadSummary.visibility = View.VISIBLE
                binding.tvDownloadSummary.text = summary
            }
        }

        setupBottomNavigation()
        setupSubPageHeader()
        setupBackNavigation()

        if (savedInstanceState == null) {
            selectTab(R.id.nav_bookshelf)
        }
    }

    private fun setupBottomNavigation() {
        binding.bottomNavigation.setOnItemSelectedListener { item ->
            if (currentSubPage != null) {
                closeSubPage()
            }
            selectTab(item.itemId)
            true
        }
        binding.bottomNavigation.selectedItemId = R.id.nav_bookshelf
    }

    private fun setupSubPageHeader() {
        binding.btnSubPageBack.setOnClickListener {
            closeSubPage()
        }
    }

    private fun setupBackNavigation() {
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                when {
                    currentSubPage != null -> closeSubPage()
                    currentTabId != R.id.nav_bookshelf -> {
                        binding.bottomNavigation.selectedItemId = R.id.nav_bookshelf
                    }
                    else -> {
                        isEnabled = false
                        onBackPressedDispatcher.onBackPressed()
                    }
                }
            }
        })
    }

    private fun selectTab(tabId: Int) {
        currentTabId = tabId
        val tag = fragmentTags[tabId] ?: return

        val fm = supportFragmentManager
        val existing = fm.findFragmentByTag(tag)

        fm.beginTransaction().apply {
            for (fragTag in fragmentTags.values) {
                fm.findFragmentByTag(fragTag)?.let { hide(it) }
            }
            if (existing != null) {
                show(existing)
            } else {
                val fragment = createTabFragment(tabId) ?: return
                add(R.id.fragmentContainer, fragment, tag)
            }
            commit()
        }

        showMainHeader()
    }

    private fun createTabFragment(tabId: Int): Fragment? {
        val className = when (tabId) {
            R.id.nav_search -> "com.readstorm.app.ui.fragments.SearchFragment"
            R.id.nav_tasks -> "com.readstorm.app.ui.fragments.DownloadTasksFragment"
            R.id.nav_bookshelf -> "com.readstorm.app.ui.fragments.BookshelfFragment"
            R.id.nav_more -> "com.readstorm.app.ui.fragments.MoreFragment"
            else -> return null
        }
        return try {
            Class.forName(className).getDeclaredConstructor().newInstance() as Fragment
        } catch (_: Exception) {
            Fragment()
        }
    }

    fun openSubPage(pageKey: String, fragment: Fragment) {
        currentSubPage = pageKey
        val titleResId = subPageTitles[pageKey] ?: R.string.page_settings
        binding.tvSubPageTitle.setText(titleResId)
        binding.headerBar.visibility = View.GONE
        binding.subPageHeader.visibility = View.VISIBLE
        binding.bottomNavigation.visibility = View.GONE

        supportFragmentManager.beginTransaction()
            .replace(R.id.fragmentContainer, fragment, "subpage_$pageKey")
            .addToBackStack("subpage")
            .commit()
    }

    private fun closeSubPage() {
        currentSubPage = null
        showMainHeader()
        supportFragmentManager.popBackStack("subpage", FragmentManager.POP_BACK_STACK_INCLUSIVE)
        selectTab(currentTabId)
    }

    private fun showMainHeader() {
        binding.headerBar.visibility = View.VISIBLE
        binding.subPageHeader.visibility = View.GONE
        binding.bottomNavigation.visibility = View.VISIBLE
    }

    fun updateStatusMessage(message: String) {
        binding.tvStatusMessage.text = message
    }

    fun updateDownloadSummary(done: Int, total: Int) {
        if (total > 0) {
            binding.tvDownloadSummary.visibility = View.VISIBLE
            binding.tvDownloadSummary.text = getString(R.string.download_summary_format, done, total)
        } else {
            binding.tvDownloadSummary.visibility = View.GONE
        }
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent?): Boolean {
        if (keyCode == KeyEvent.KEYCODE_VOLUME_UP || keyCode == KeyEvent.KEYCODE_VOLUME_DOWN) {
            return super.onKeyDown(keyCode, event)
        }
        return super.onKeyDown(keyCode, event)
    }
}
