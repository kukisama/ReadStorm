package com.readstorm.app.ui.adapters

import android.graphics.BitmapFactory
import android.util.Base64
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.RecyclerView
import com.readstorm.app.databinding.ItemBookshelfGridBinding
import com.readstorm.app.domain.models.BookEntity

class BookshelfAdapter(
    private val onClick: (BookEntity) -> Unit = {},
    private val onLongClick: (View, BookEntity) -> Unit = { _, _ -> }
) : RecyclerView.Adapter<BookshelfAdapter.ViewHolder>() {

    private val items = mutableListOf<BookEntity>()

    inner class ViewHolder(val binding: ItemBookshelfGridBinding) :
        RecyclerView.ViewHolder(binding.root)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemBookshelfGridBinding.inflate(
            LayoutInflater.from(parent.context), parent, false
        )
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        val book = items[position]
        holder.binding.apply {
            tvTitle.text = book.title
            tvAuthor.text = book.author
            tvInitial.text = book.titleInitial
            progressBar.progress = book.progressPercent
            tvPercent.text = "${book.progressPercent}%"

            // Cover image from base64 string
            if (book.hasCover && !book.coverImage.isNullOrBlank()) {
                try {
                    val bytes = Base64.decode(book.coverImage, Base64.DEFAULT)
                    val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                    if (bmp != null) {
                        ivCover.setImageBitmap(bmp)
                        ivCover.visibility = View.VISIBLE
                        layoutPlaceholder.visibility = View.GONE
                    } else {
                        showPlaceholder()
                    }
                } catch (_: Exception) {
                    showPlaceholder()
                }
            } else {
                showPlaceholder()
            }

            root.setOnClickListener { onClick(book) }
            root.setOnLongClickListener { v ->
                onLongClick(v, book)
                true
            }
        }
    }

    private fun ItemBookshelfGridBinding.showPlaceholder() {
        ivCover.visibility = View.GONE
        layoutPlaceholder.visibility = View.VISIBLE
    }

    override fun getItemCount(): Int = items.size

    fun submitList(newItems: List<BookEntity>) {
        items.clear()
        items.addAll(newItems)
        notifyDataSetChanged()
    }
}
