package com.readstorm.app.ui.fragments

import android.graphics.BitmapFactory
import android.os.Bundle
import android.text.Editable
import android.text.TextWatcher
import android.util.Base64
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.PopupMenu
import androidx.fragment.app.Fragment
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.readstorm.app.databinding.FragmentBookshelfBinding
import com.readstorm.app.databinding.ItemBookshelfGridBinding
import com.readstorm.app.domain.models.BookEntity
import com.readstorm.app.ui.viewmodels.MainViewModel
import kotlinx.coroutines.launch

class BookshelfFragment : Fragment() {

    private var _binding: FragmentBookshelfBinding? = null
    private val binding get() = _binding!!

    private val filteredBooks = mutableListOf<BookEntity>()
    private lateinit var adapter: BookGridAdapter

    private val mainViewModel: MainViewModel by lazy {
        ViewModelProvider(requireActivity())[MainViewModel::class.java]
    }

    override fun onCreateView(
        inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?
    ): View {
        _binding = FragmentBookshelfBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        setupRecyclerView()
        setupListeners()
        observeViewModel()
    }

    private fun setupRecyclerView() {
        adapter = BookGridAdapter()
        binding.rvBooks.layoutManager = GridLayoutManager(requireContext(), 2)
        binding.rvBooks.adapter = adapter
    }

    private fun setupListeners() {
        binding.btnCheckUpdates.setOnClickListener {
            lifecycleScope.launch {
                mainViewModel.bookshelf.checkAllNewChapters()
            }
        }

        binding.etBookFilter.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: Editable?) {
                mainViewModel.bookshelf.bookshelfFilterText = s?.toString() ?: ""
            }
        })
    }

    private fun observeViewModel() {
        mainViewModel.bookshelf.filteredDbBooks.observe(viewLifecycleOwner) { books ->
            filteredBooks.clear()
            filteredBooks.addAll(books)
            adapter.notifyDataSetChanged()
        }
    }

    private fun openReader(book: BookEntity) {
        mainViewModel.openDbBookAndSwitchToReader(book)
    }

    private fun showContextMenu(view: View, book: BookEntity) {
        PopupMenu(requireContext(), view).apply {
            menu.add(0, 1, 0, "继续阅读")
            menu.add(0, 2, 1, "续传下载")
            menu.add(0, 3, 2, "检查更新")
            menu.add(0, 4, 3, "导出txt")
            menu.add(0, 5, 4, "刷新封面")
            menu.add(0, 6, 5, "删除书籍")
            setOnMenuItemClickListener { item ->
                lifecycleScope.launch {
                    when (item.itemId) {
                        1 -> openReader(book)
                        2 -> mainViewModel.bookshelf.resumeBookDownload(book)
                        3 -> mainViewModel.bookshelf.checkNewChapters(book)
                        4 -> mainViewModel.bookshelf.exportDbBook(book)
                        5 -> mainViewModel.bookshelf.refreshCover(book)
                        6 -> mainViewModel.bookshelf.removeDbBook(book)
                    }
                }
                true
            }
            show()
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }

    private inner class BookGridAdapter :
        RecyclerView.Adapter<BookGridAdapter.ViewHolder>() {

        inner class ViewHolder(val itemBinding: ItemBookshelfGridBinding) :
            RecyclerView.ViewHolder(itemBinding.root)

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
            val b = ItemBookshelfGridBinding.inflate(
                LayoutInflater.from(parent.context), parent, false
            )
            return ViewHolder(b)
        }

        override fun onBindViewHolder(holder: ViewHolder, position: Int) {
            val book = filteredBooks[position]
            holder.itemBinding.apply {
                tvTitle.text = book.title
                tvAuthor.text = book.author
                tvInitial.text = book.titleInitial
                progressBar.progress = book.progressPercent
                tvPercent.text = "${book.progressPercent}%"

                if (book.hasCover && !book.coverImage.isNullOrBlank()) {
                    try {
                        val bytes = Base64.decode(book.coverImage, Base64.DEFAULT)
                        val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                        if (bmp != null) {
                            ivCover.setImageBitmap(bmp)
                            ivCover.visibility = View.VISIBLE
                            layoutPlaceholder.visibility = View.GONE
                        }
                    } catch (_: Exception) {
                        ivCover.visibility = View.GONE
                        layoutPlaceholder.visibility = View.VISIBLE
                    }
                } else {
                    ivCover.visibility = View.GONE
                    layoutPlaceholder.visibility = View.VISIBLE
                }

                root.setOnClickListener { openReader(book) }
                root.setOnLongClickListener { v ->
                    showContextMenu(v, book)
                    true
                }
            }
        }

        override fun getItemCount() = filteredBooks.size
    }
}
