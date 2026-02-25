package com.readstorm.app.ui.adapters

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.RecyclerView
import com.readstorm.app.databinding.ItemDownloadTaskBinding
import com.readstorm.app.domain.models.DownloadTask

class DownloadTaskAdapter(
    private val onPause: (DownloadTask) -> Unit = {},
    private val onResume: (DownloadTask) -> Unit = {},
    private val onRetry: (DownloadTask) -> Unit = {},
    private val onCancel: (DownloadTask) -> Unit = {},
    private val onDelete: (DownloadTask) -> Unit = {}
) : RecyclerView.Adapter<DownloadTaskAdapter.ViewHolder>() {

    private val items = mutableListOf<DownloadTask>()

    inner class ViewHolder(val binding: ItemDownloadTaskBinding) :
        RecyclerView.ViewHolder(binding.root)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemDownloadTaskBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        val task = items[position]
        holder.binding.apply {
            tvBookTitle.text = task.bookTitle
            tvStatus.text = "状态：${task.status}"
            tvChapterProgress.text = task.chapterProgressDisplay
            progressBar.progress = task.progressPercent
            tvProgressPercent.text = "进度：${task.progressPercent}%"

            // Auto prefetch tag
            val prefetchTag = task.autoPrefetchTagDisplay
            if (prefetchTag.isNotEmpty()) {
                tvAutoPrefetchTag.text = prefetchTag
                tvAutoPrefetchTag.visibility = View.VISIBLE
            } else {
                tvAutoPrefetchTag.visibility = View.GONE
            }

            // Action button visibility based on task state
            btnPause.visibility = if (task.canPause) View.VISIBLE else View.GONE
            btnResume.visibility = if (task.canResume) View.VISIBLE else View.GONE
            btnRetry.visibility = if (task.canRetry) View.VISIBLE else View.GONE
            btnCancel.visibility = if (task.canCancel) View.VISIBLE else View.GONE
            btnDelete.visibility = if (task.canDelete) View.VISIBLE else View.GONE

            btnPause.setOnClickListener { onPause(task) }
            btnResume.setOnClickListener { onResume(task) }
            btnRetry.setOnClickListener { onRetry(task) }
            btnCancel.setOnClickListener { onCancel(task) }
            btnDelete.setOnClickListener { onDelete(task) }
        }
    }

    override fun getItemCount(): Int = items.size

    fun submitList(newItems: List<DownloadTask>) {
        items.clear()
        items.addAll(newItems)
        notifyDataSetChanged()
    }
}
