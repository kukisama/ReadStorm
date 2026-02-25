package com.readstorm.app.ui.adapters

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.RecyclerView
import com.readstorm.app.databinding.ItemDiagnosticLineBinding

class DiagnosticLineAdapter :
    RecyclerView.Adapter<DiagnosticLineAdapter.ViewHolder>() {

    private val lines = mutableListOf<String>()

    inner class ViewHolder(val binding: ItemDiagnosticLineBinding) :
        RecyclerView.ViewHolder(binding.root)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemDiagnosticLineBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.binding.tvDiagnosticLine.text = lines[position]
    }

    override fun getItemCount(): Int = lines.size

    fun submitList(newLines: List<String>) {
        lines.clear()
        lines.addAll(newLines)
        notifyDataSetChanged()
    }
}
