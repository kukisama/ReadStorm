package com.readstorm.app.ui.fragments

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.view.inputmethod.EditorInfo
import androidx.fragment.app.Fragment
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.readstorm.app.databinding.FragmentSearchBinding
import com.readstorm.app.databinding.ItemSearchResultBinding
import com.readstorm.app.domain.models.SearchResult
import com.readstorm.app.ui.activities.MainActivity
import com.readstorm.app.ui.viewmodels.MainViewModel
import kotlinx.coroutines.launch

class SearchFragment : Fragment() {

    private var _binding: FragmentSearchBinding? = null
    private val binding get() = _binding!!

    private val searchResults = mutableListOf<SearchResult>()
    private var selectedPosition = RecyclerView.NO_POSITION
    private lateinit var adapter: SearchResultAdapter

    private val mainViewModel: MainViewModel by lazy {
        ViewModelProvider(requireActivity())[MainViewModel::class.java]
    }

    override fun onCreateView(
        inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?
    ): View {
        _binding = FragmentSearchBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        setupRecyclerView()
        setupListeners()
        observeViewModel()
    }

    private fun setupRecyclerView() {
        adapter = SearchResultAdapter()
        binding.rvSearchResults.layoutManager = LinearLayoutManager(requireContext())
        binding.rvSearchResults.adapter = adapter
    }

    private fun setupListeners() {
        binding.etSearchKeyword.setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_SEARCH) {
                performSearch()
                true
            } else false
        }
        binding.btnSearch.setOnClickListener { performSearch() }
        binding.btnQueueDownload.setOnClickListener {
            mainViewModel.searchDownload.queueDownload()
        }
        binding.btnRefreshSource.setOnClickListener { refreshSourceHealth() }
    }

    private fun observeViewModel() {
        mainViewModel.searchDownload.searchResults.observe(viewLifecycleOwner) { results ->
            updateSearchResults(results)
        }
        mainViewModel.searchDownload.isSearching.observe(viewLifecycleOwner) { loading ->
            showLoading(loading)
        }
        mainViewModel.searchDownload.hasNoSearchResults.observe(viewLifecycleOwner) { noResults ->
            binding.tvEmptyState.visibility = if (noResults) View.VISIBLE else View.GONE
        }
    }

    private fun performSearch() {
        val keyword = binding.etSearchKeyword.text?.toString()?.trim() ?: return
        if (keyword.isEmpty()) return
        mainViewModel.searchDownload.setSearchKeyword(keyword)
        lifecycleScope.launch {
            mainViewModel.searchDownload.search(keyword)
        }
    }

    private fun refreshSourceHealth() {
        binding.btnRefreshSource.isEnabled = false
        mainViewModel.setStatusMessage("书源健康检测功能将在实现后可用。")
        binding.btnRefreshSource.isEnabled = true
    }

    fun updateSearchResults(results: List<SearchResult>) {
        searchResults.clear()
        searchResults.addAll(results)
        selectedPosition = RecyclerView.NO_POSITION
        adapter.notifyDataSetChanged()
        binding.rvSearchResults.visibility =
            if (results.isNotEmpty()) View.VISIBLE else View.GONE
    }

    private fun showLoading(loading: Boolean) {
        binding.layoutLoading.visibility = if (loading) View.VISIBLE else View.GONE
        binding.rvSearchResults.visibility = if (loading) View.GONE else View.VISIBLE
        binding.tvEmptyState.visibility = View.GONE
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }

    private inner class SearchResultAdapter :
        RecyclerView.Adapter<SearchResultAdapter.ViewHolder>() {

        inner class ViewHolder(val itemBinding: ItemSearchResultBinding) :
            RecyclerView.ViewHolder(itemBinding.root)

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
            val b = ItemSearchResultBinding.inflate(
                LayoutInflater.from(parent.context), parent, false
            )
            return ViewHolder(b)
        }

        override fun onBindViewHolder(holder: ViewHolder, position: Int) {
            val item = searchResults[position]
            holder.itemBinding.apply {
                tvTitle.text = item.title
                tvAuthor.text = "作者：${item.author}"
                tvSource.text = "来源：${item.sourceName}"
                tvLatestChapter.text = "最新章节：${item.latestChapter}"
                root.isSelected = position == selectedPosition
                root.setOnClickListener {
                    val prev = selectedPosition
                    selectedPosition = holder.adapterPosition
                    mainViewModel.searchDownload.setSelectedSearchResult(item)
                    if (prev != RecyclerView.NO_POSITION) notifyItemChanged(prev)
                    notifyItemChanged(selectedPosition)
                }
            }
        }

        override fun getItemCount() = searchResults.size
    }
}
