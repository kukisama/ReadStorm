package com.readstorm.app.ui.adapters

import android.content.Context
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.BaseAdapter
import android.widget.TextView
import com.readstorm.app.domain.models.BookSourceRule

class SourceSpinnerAdapter(
    private val context: Context
) : BaseAdapter() {

    enum class HealthStatus { Healthy, Unhealthy, Unknown }

    data class SourceItem(
        val source: BookSourceRule,
        val health: HealthStatus = HealthStatus.Unknown
    )

    private val items = mutableListOf<SourceItem>()

    override fun getCount(): Int = items.size

    override fun getItem(position: Int): SourceItem = items[position]

    override fun getItemId(position: Int): Long = items[position].source.id.toLong()

    override fun getView(position: Int, convertView: View?, parent: ViewGroup): View =
        createItemView(position, convertView, parent)

    override fun getDropDownView(position: Int, convertView: View?, parent: ViewGroup): View =
        createItemView(position, convertView, parent)

    private fun createItemView(position: Int, convertView: View?, parent: ViewGroup): View {
        val view = convertView ?: LayoutInflater.from(context).inflate(
            android.R.layout.simple_spinner_dropdown_item, parent, false
        )
        val item = items[position]
        val textView = view.findViewById<TextView>(android.R.id.text1)
        textView.text = item.source.name
        textView.textSize = 14f
        textView.setTextColor(Color.parseColor("#1E293B"))

        val dot = GradientDrawable().apply {
            shape = GradientDrawable.OVAL
            setSize(dpToPx(8), dpToPx(8))
            setColor(
                when (item.health) {
                    HealthStatus.Healthy -> Color.parseColor("#22C55E")
                    HealthStatus.Unhealthy -> Color.parseColor("#EF4444")
                    HealthStatus.Unknown -> Color.parseColor("#9CA3AF")
                }
            )
        }
        textView.setCompoundDrawablesWithIntrinsicBounds(dot, null, null, null)
        textView.compoundDrawablePadding = dpToPx(8)

        return view
    }

    private fun dpToPx(dp: Int): Int =
        (dp * context.resources.displayMetrics.density).toInt()

    fun submitList(sources: List<BookSourceRule>, healthMap: Map<Int, HealthStatus> = emptyMap()) {
        items.clear()
        items.addAll(sources.map { source ->
            SourceItem(source, healthMap[source.id] ?: HealthStatus.Unknown)
        })
        notifyDataSetChanged()
    }

    fun updateHealth(healthMap: Map<Int, HealthStatus>) {
        for (i in items.indices) {
            val source = items[i].source
            items[i] = SourceItem(source, healthMap[source.id] ?: HealthStatus.Unknown)
        }
        notifyDataSetChanged()
    }
}
