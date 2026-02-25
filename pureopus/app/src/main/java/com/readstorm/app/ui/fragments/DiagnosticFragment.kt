package com.readstorm.app.ui.fragments

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.readstorm.app.databinding.FragmentDiagnosticBinding
import com.readstorm.app.databinding.ItemDiagnosticLineBinding
import com.readstorm.app.domain.models.SourceDiagnosticResult
import com.readstorm.app.ui.viewmodels.MainViewModel
import kotlinx.coroutines.launch

class DiagnosticFragment : Fragment() {

    private var _binding: FragmentDiagnosticBinding? = null
    private val binding get() = _binding!!

    private val diagnosticLines = mutableListOf<String>()
    private lateinit var adapter: DiagnosticLineAdapter

    private val mainViewModel: MainViewModel by lazy {
        ViewModelProvider(requireActivity())[MainViewModel::class.java]
    }

    override fun onCreateView(
        inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?
    ): View {
        _binding = FragmentDiagnosticBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        setupRecyclerView()
        setupListeners()
        observeViewModel()
    }

    private fun observeViewModel() {
        mainViewModel.diagnostic.diagnosticSummary.observe(viewLifecycleOwner) { summary ->
            binding.tvSummary.text = summary
            binding.tvSummary.visibility =
                if (summary.isNotEmpty()) View.VISIBLE else View.GONE
        }
        mainViewModel.diagnostic.diagnosticLines.observe(viewLifecycleOwner) { lines ->
            diagnosticLines.clear()
            diagnosticLines.addAll(lines)
            adapter.notifyDataSetChanged()
        }
        mainViewModel.diagnostic.isDiagnosing.observe(viewLifecycleOwner) { diagnosing ->
            binding.btnDiagnoseAll.isEnabled = !diagnosing
        }
    }

    private fun setupRecyclerView() {
        adapter = DiagnosticLineAdapter()
        binding.rvDiagnosticLines.layoutManager = LinearLayoutManager(requireContext())
        binding.rvDiagnosticLines.adapter = adapter
    }

    private fun setupListeners() {
        binding.btnDiagnoseAll.setOnClickListener { diagnoseAllSources() }
    }

    private fun diagnoseAllSources() {
        binding.btnDiagnoseAll.isEnabled = false
        lifecycleScope.launch {
            mainViewModel.diagnostic.runBatchDiagnostic()
        }
    }

    fun updateDiagnosticResult(result: SourceDiagnosticResult) {
        binding.tvSummary.text = result.summary
        binding.tvSummary.visibility =
            if (result.summary.isNotEmpty()) View.VISIBLE else View.GONE
        diagnosticLines.clear()
        diagnosticLines.addAll(result.diagnosticLines)
        adapter.notifyDataSetChanged()
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }

    // ── Adapter ──────────────────────────────────────────────────────

    private inner class DiagnosticLineAdapter :
        RecyclerView.Adapter<DiagnosticLineAdapter.ViewHolder>() {

        inner class ViewHolder(val itemBinding: ItemDiagnosticLineBinding) :
            RecyclerView.ViewHolder(itemBinding.root)

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
            val b = ItemDiagnosticLineBinding.inflate(
                LayoutInflater.from(parent.context), parent, false
            )
            return ViewHolder(b)
        }

        override fun onBindViewHolder(holder: ViewHolder, position: Int) {
            holder.itemBinding.tvDiagnosticLine.text = diagnosticLines[position]
        }

        override fun getItemCount() = diagnosticLines.size
    }
}
