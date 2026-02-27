package com.readstorm.app.ui.fragments

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.compose.ui.platform.ComposeView
import androidx.compose.ui.platform.ViewCompositionStrategy
import androidx.fragment.app.Fragment
import com.readstorm.app.ui.activities.MainActivity
import com.readstorm.app.ui.compose.screens.MoreScreen
import com.readstorm.app.ui.compose.theme.ReadStormTheme

class MoreFragment : Fragment() {

    override fun onCreateView(
        inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?
    ): View {
        return ComposeView(requireContext()).apply {
            setViewCompositionStrategy(ViewCompositionStrategy.DisposeOnViewTreeLifecycleDestroyed)
            setContent {
                ReadStormTheme {
                    MoreScreen(
                        onNavigate = { pageKey ->
                            val fragment: Fragment = when (pageKey) {
                                "diagnostic" -> DiagnosticFragment()
                                "rules" -> RuleEditorFragment()
                                "settings" -> SettingsFragment()
                                "about" -> AboutFragment()
                                "log" -> LogFragment()
                                else -> return@MoreScreen
                            }
                            (activity as? MainActivity)?.openSubPage(pageKey, fragment)
                        }
                    )
                }
            }
        }
    }
}
