package com.readstorm.app.ui.viewmodels

data class PaperPreset(
    val name: String,
    val background: String,
    val foreground: String
) {
    val display: String get() = "$name ($background)"

    companion object {
        val defaults = listOf(
            PaperPreset("纸色", "#FFFBF0", "#1E293B"),
            PaperPreset("白色", "#FFFFFF", "#1E293B"),
            PaperPreset("绿色", "#E8F5E9", "#1B5E20"),
            PaperPreset("灰色", "#F5F5F5", "#212121"),
            PaperPreset("暗色", "#1E293B", "#CBD5E1")
        )
    }
}
