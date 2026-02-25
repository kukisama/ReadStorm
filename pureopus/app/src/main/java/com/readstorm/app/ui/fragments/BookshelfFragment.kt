package com.readstorm.app.ui.fragments

import android.content.Intent
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
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.readstorm.app.R
import com.readstorm.app.databinding.FragmentBookshelfBinding
import com.readstorm.app.databinding.ItemBookshelfGridBinding
import com.readstorm.app.domain.models.BookEntity
import com.readstorm.app.ui.activities.ReaderActivity

class BookshelfFragment : Fragment() {

    private var _binding: FragmentBookshelfBinding? = null
    private val binding get() = _binding!!

    private val allBooks = mutableListOf<BookEntity>()
    private val filteredBooks = mutableListOf<BookEntity>()
    private lateinit var adapter: BookGridAdapter

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
    }

    private fun setupRecyclerView() {
        adapter = BookGridAdapter()
        binding.rvBooks.layoutManager = GridLayoutManager(requireContext(), 2)
        binding.rvBooks.adapter = adapter
    }

    private fun setupListeners() {
        binding.btnCheckUpdates.setOnClickListener { checkAllUpdates() }

        binding.etBookFilter.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: Editable?) { applyFilter(s?.toString() ?: "") }
        })
    }

    private fun applyFilter(query: String) {
        filteredBooks.clear()
        if (query.isBlank()) {
            filteredBooks.addAll(allBooks)
        } else {
            filteredBooks.addAll(allBooks.filter {
                it.title.contains(query, ignoreCase = true) ||
                    it.author.contains(query, ignoreCase = true)
            })
        }
        adapter.notifyDataSetChanged()
    }

    private fun checkAllUpdates() {
        // TODO: invoke check all updates use case
    }

    fun updateBooks(books: List<BookEntity>) {
        allBooks.clear()
        allBooks.addAll(books)
        applyFilter(binding.etBookFilter.text?.toString() ?: "")
    }

    private fun openReader(book: BookEntity) {
        val intent = Intent(requireContext(), ReaderActivity::class.java).apply {
            putExtra("bookId", book.id)
        }
        startActivity(intent)
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
                when (item.itemId) {
                    1 -> openReader(book)
                    2 -> { /* TODO: resume download */ }
                    3 -> { /* TODO: check updates */ }
                    4 -> { /* TODO: export txt */ }
                    5 -> { /* TODO: refresh cover */ }
                    6 -> { /* TODO: delete book */ }
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

    // ── Adapter ──────────────────────────────────────────────────────

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

                // Cover image
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
