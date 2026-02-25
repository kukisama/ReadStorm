package com.readstorm.app.ui.fragments

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.readstorm.app.databinding.FragmentDiagnosticBinding
import com.readstorm.app.databinding.ItemDiagnosticLineBinding
import com.readstorm.app.domain.models.SourceDiagnosticResult

class DiagnosticFragment : Fragment() {

    private var _binding: FragmentDiagnosticBinding? = null
    private val binding get() = _binding!!

    private val diagnosticLines = mutableListOf<String>()
    private lateinit var adapter: DiagnosticLineAdapter

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
        // TODO: invoke diagnostic use case
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
