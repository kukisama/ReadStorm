package com.readstorm.app.ui.fragments

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.lifecycle.ViewModelProvider
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.readstorm.app.databinding.FragmentDownloadTasksBinding
import com.readstorm.app.databinding.ItemDownloadTaskBinding
import com.readstorm.app.domain.models.DownloadTask
import com.readstorm.app.domain.models.DownloadTaskStatus
import com.readstorm.app.ui.viewmodels.MainViewModel

class DownloadTasksFragment : Fragment() {

    private var _binding: FragmentDownloadTasksBinding? = null
    private val binding get() = _binding!!

    private val tasks = mutableListOf<DownloadTask>()
    private lateinit var adapter: TaskAdapter

    private val mainViewModel: MainViewModel by lazy {
        ViewModelProvider(requireActivity())[MainViewModel::class.java]
    }

    override fun onCreateView(
        inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?
    ): View {
        _binding = FragmentDownloadTasksBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        setupRecyclerView()
        setupListeners()
        observeViewModel()
    }

    private fun observeViewModel() {
        mainViewModel.searchDownload.filteredDownloadTasks.observe(viewLifecycleOwner) { newTasks ->
            updateTasks(newTasks)
        }
        mainViewModel.searchDownload.activeDownloadSummary.observe(viewLifecycleOwner) { summary ->
            updateDownloadSummary(summary ?: "")
        }
    }

    private fun setupRecyclerView() {
        adapter = TaskAdapter()
        binding.rvTasks.layoutManager = LinearLayoutManager(requireContext())
        binding.rvTasks.adapter = adapter
    }

    private fun setupListeners() {
        binding.btnStopAll.setOnClickListener { stopAllDownloads() }
        binding.btnStartAll.setOnClickListener { startAllDownloads() }
    }

    private fun stopAllDownloads() {
        mainViewModel.searchDownload.stopAllDownloads()
    }

    private fun startAllDownloads() {
        mainViewModel.searchDownload.startAllDownloads()
    }

    fun updateTasks(newTasks: List<DownloadTask>) {
        tasks.clear()
        tasks.addAll(newTasks)
        adapter.notifyDataSetChanged()
    }

    fun updateDownloadSummary(text: String) {
        binding.tvDownloadSummary.text = text
        binding.tvDownloadSummary.visibility =
            if (text.isNotEmpty()) View.VISIBLE else View.GONE
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }

    // ── Adapter ──────────────────────────────────────────────────────

    private inner class TaskAdapter :
        RecyclerView.Adapter<TaskAdapter.ViewHolder>() {

        inner class ViewHolder(val itemBinding: ItemDownloadTaskBinding) :
            RecyclerView.ViewHolder(itemBinding.root)

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
            val b = ItemDownloadTaskBinding.inflate(
                LayoutInflater.from(parent.context), parent, false
            )
            return ViewHolder(b)
        }

        override fun onBindViewHolder(holder: ViewHolder, position: Int) {
            val task = tasks[position]
            holder.itemBinding.apply {
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

                btnPause.setOnClickListener { onPauseTask(task) }
                btnResume.setOnClickListener { onResumeTask(task) }
                btnRetry.setOnClickListener { onRetryTask(task) }
                btnCancel.setOnClickListener { onCancelTask(task) }
                btnDelete.setOnClickListener { onDeleteTask(task) }
            }
        }

        override fun getItemCount() = tasks.size
    }

    private fun onPauseTask(task: DownloadTask) {
        mainViewModel.searchDownload.pauseDownload(task)
    }

    private fun onResumeTask(task: DownloadTask) {
        mainViewModel.searchDownload.resumeDownload(task)
    }

    private fun onRetryTask(task: DownloadTask) {
        mainViewModel.searchDownload.retryDownload(task)
    }

    private fun onCancelTask(task: DownloadTask) {
        mainViewModel.searchDownload.cancelDownload(task)
    }

    private fun onDeleteTask(task: DownloadTask) {
        mainViewModel.searchDownload.deleteDownload(task)
    }
}
