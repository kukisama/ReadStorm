package com.readstorm.app.ui.fragments

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ArrayAdapter
import androidx.fragment.app.Fragment
import com.readstorm.app.databinding.FragmentSettingsBinding
import com.readstorm.app.domain.models.AppSettings

class SettingsFragment : Fragment() {

    private var _binding: FragmentSettingsBinding? = null
    private val binding get() = _binding!!

    private var settings: AppSettings? = null

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
        // TODO: invoke settings save use case
    }

    private fun exportDiagnosticLog() {
        // TODO: invoke export diagnostic log
    }

    private fun exportDatabase() {
        // TODO: invoke export database
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
