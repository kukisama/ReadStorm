package com.readstorm.app.ui.fragments

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ArrayAdapter
import androidx.fragment.app.Fragment
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.lifecycleScope
import com.readstorm.app.databinding.FragmentSettingsBinding
import com.readstorm.app.domain.models.AppSettings
import com.readstorm.app.ui.viewmodels.MainViewModel
import kotlinx.coroutines.launch

class SettingsFragment : Fragment() {

    private var _binding: FragmentSettingsBinding? = null
    private val binding get() = _binding!!

    private var settings: AppSettings? = null

    private val mainViewModel: MainViewModel by lazy {
        ViewModelProvider(requireActivity())[MainViewModel::class.java]
    }

    override fun onCreateView(
        inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?
    ): View {
        _binding = FragmentSettingsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        setupExportFormatSpinner()
        setupListeners()
        observeViewModel()
    }

    private fun observeViewModel() {
        mainViewModel.settings.downloadPath.observe(viewLifecycleOwner) { path ->
            if (path.isNotEmpty()) {
                binding.etWorkDirectory.setText(path)
            }
        }
        mainViewModel.settings.saveFeedback.observe(viewLifecycleOwner) { feedback ->
            if (!feedback.isNullOrEmpty()) {
                mainViewModel.setStatusMessage(feedback)
            }
        }
        populateSettingsFields()
    }

    private fun populateSettingsFields() {
        val s = mainViewModel.settings
        binding.apply {
            etConcurrency.setText(s.maxConcurrency.toString())
            etSearchConcurrency.setText(s.aggregateSearchMaxConcurrency.toString())
            switchDiagnosticLog.isChecked = s.enableDiagnosticLog
            switchAutoResume.isChecked = s.autoResumeAndRefreshOnStartup
            switchAutoPrefetch.isChecked = s.readerAutoPrefetchEnabled
            etPrefetchBatchSize.setText(s.readerPrefetchBatchSize.toString())
            etLowWatermark.setText(s.readerPrefetchLowWatermark.toString())
            etProgressLeftPadding.setText(s.bookshelfProgressLeftPaddingPx.toInt().toString())
            etProgressRightPadding.setText(s.bookshelfProgressRightPaddingPx.toInt().toString())
            etProgressTotalWidth.setText(s.bookshelfProgressTotalWidthPx.toInt().toString())
            etProgressMinWidth.setText(s.bookshelfProgressMinWidthPx.toInt().toString())
            val formatIndex = if (s.exportFormat == "epub") 1 else 0
            spinnerExportFormat.setSelection(formatIndex)
        }
    }

    private fun setupExportFormatSpinner() {
        val formats = listOf("txt", "epub")
        val spinnerAdapter = ArrayAdapter(
            requireContext(), android.R.layout.simple_spinner_item, formats
        )
        spinnerAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        binding.spinnerExportFormat.adapter = spinnerAdapter
    }

    private fun setupListeners() {
        binding.btnSaveSettings.setOnClickListener { saveSettings() }
        binding.btnExportDiagLog.setOnClickListener { exportDiagnosticLog() }
        binding.btnExportDatabase.setOnClickListener { exportDatabase() }
    }

    fun loadSettings(appSettings: AppSettings) {
        settings = appSettings
        binding.apply {
            etWorkDirectory.setText(appSettings.downloadPath)
            etConcurrency.setText(appSettings.maxConcurrency.toString())
            etSearchConcurrency.setText(appSettings.aggregateSearchMaxConcurrency.toString())
            switchDiagnosticLog.isChecked = appSettings.enableDiagnosticLog
            switchAutoResume.isChecked = appSettings.autoResumeAndRefreshOnStartup
            switchAutoPrefetch.isChecked = appSettings.readerAutoPrefetchEnabled
            etPrefetchBatchSize.setText(appSettings.readerPrefetchBatchSize.toString())
            etLowWatermark.setText(appSettings.readerPrefetchLowWatermark.toString())
            etProgressLeftPadding.setText(appSettings.bookshelfProgressLeftPaddingPx.toString())
            etProgressRightPadding.setText(appSettings.bookshelfProgressRightPaddingPx.toString())
            etProgressTotalWidth.setText(appSettings.bookshelfProgressTotalWidthPx.toString())
            etProgressMinWidth.setText(appSettings.bookshelfProgressMinWidthPx.toString())

            // Set export format selection
            val formatIndex = if (appSettings.exportFormat == "epub") 1 else 0
            spinnerExportFormat.setSelection(formatIndex)
        }
    }

    private fun collectSettings(): AppSettings {
        val current = settings ?: AppSettings()
        return current.copy(
            maxConcurrency = binding.etConcurrency.text.toString().toIntOrNull() ?: current.maxConcurrency,
            aggregateSearchMaxConcurrency = binding.etSearchConcurrency.text.toString().toIntOrNull() ?: current.aggregateSearchMaxConcurrency,
            exportFormat = binding.spinnerExportFormat.selectedItem?.toString() ?: current.exportFormat,
            enableDiagnosticLog = binding.switchDiagnosticLog.isChecked,
            autoResumeAndRefreshOnStartup = binding.switchAutoResume.isChecked,
            readerAutoPrefetchEnabled = binding.switchAutoPrefetch.isChecked,
            readerPrefetchBatchSize = binding.etPrefetchBatchSize.text.toString().toIntOrNull() ?: current.readerPrefetchBatchSize,
            readerPrefetchLowWatermark = binding.etLowWatermark.text.toString().toIntOrNull() ?: current.readerPrefetchLowWatermark,
            bookshelfProgressLeftPaddingPx = binding.etProgressLeftPadding.text.toString().toIntOrNull() ?: current.bookshelfProgressLeftPaddingPx,
            bookshelfProgressRightPaddingPx = binding.etProgressRightPadding.text.toString().toIntOrNull() ?: current.bookshelfProgressRightPaddingPx,
            bookshelfProgressTotalWidthPx = binding.etProgressTotalWidth.text.toString().toIntOrNull() ?: current.bookshelfProgressTotalWidthPx,
            bookshelfProgressMinWidthPx = binding.etProgressMinWidth.text.toString().toIntOrNull() ?: current.bookshelfProgressMinWidthPx
        )
    }

    private fun saveSettings() {
        val updated = collectSettings()
        val s = mainViewModel.settings
        s.maxConcurrency = updated.maxConcurrency
        s.aggregateSearchMaxConcurrency = updated.aggregateSearchMaxConcurrency
        s.exportFormat = updated.exportFormat
        s.enableDiagnosticLog = updated.enableDiagnosticLog
        s.autoResumeAndRefreshOnStartup = updated.autoResumeAndRefreshOnStartup
        s.readerAutoPrefetchEnabled = updated.readerAutoPrefetchEnabled
        s.readerPrefetchBatchSize = updated.readerPrefetchBatchSize
        s.readerPrefetchLowWatermark = updated.readerPrefetchLowWatermark
        s.bookshelfProgressLeftPaddingPx = updated.bookshelfProgressLeftPaddingPx.toDouble()
        s.bookshelfProgressRightPaddingPx = updated.bookshelfProgressRightPaddingPx.toDouble()
        s.bookshelfProgressTotalWidthPx = updated.bookshelfProgressTotalWidthPx.toDouble()
        s.bookshelfProgressMinWidthPx = updated.bookshelfProgressMinWidthPx.toDouble()
        lifecycleScope.launch {
            mainViewModel.settings.saveSettings()
        }
    }

    private fun exportDiagnosticLog() {
        lifecycleScope.launch {
            mainViewModel.settings.exportDiagnosticLog()
        }
    }

    private fun exportDatabase() {
        lifecycleScope.launch {
            mainViewModel.settings.exportDatabase()
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
