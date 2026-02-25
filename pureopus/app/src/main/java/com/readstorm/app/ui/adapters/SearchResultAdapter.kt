package com.readstorm.app.ui.adapters

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.RecyclerView
import com.readstorm.app.databinding.ItemSearchResultBinding
import com.readstorm.app.domain.models.SearchResult

class SearchResultAdapter(
    private val onItemClick: (SearchResult) -> Unit = {}
) : RecyclerView.Adapter<SearchResultAdapter.ViewHolder>() {

    private val items = mutableListOf<SearchResult>()
    var selectedPosition: Int = RecyclerView.NO_POSITION
        private set

    inner class ViewHolder(val binding: ItemSearchResultBinding) :
        RecyclerView.ViewHolder(binding.root)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemSearchResultBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        val item = items[position]
        holder.binding.apply {
            tvTitle.text = item.title
            tvAuthor.text = "作者：${item.author}"
            tvSource.text = "来源：${item.sourceName}"
            tvLatestChapter.text = "最新章节：${item.latestChapter}"

            root.isSelected = position == selectedPosition
            root.setOnClickListener {
                val prev = selectedPosition
                selectedPosition = holder.adapterPosition
                if (prev != RecyclerView.NO_POSITION) notifyItemChanged(prev)
                notifyItemChanged(selectedPosition)
                onItemClick(item)
            }
        }
    }

    override fun getItemCount(): Int = items.size

    fun submitList(newItems: List<SearchResult>) {
        items.clear()
        items.addAll(newItems)
        selectedPosition = RecyclerView.NO_POSITION
        notifyDataSetChanged()
    }

    fun getSelectedItem(): SearchResult? =
        if (selectedPosition in items.indices) items[selectedPosition] else null
}
