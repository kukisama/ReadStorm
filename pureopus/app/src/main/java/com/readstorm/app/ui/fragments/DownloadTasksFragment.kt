package com.readstorm.app.ui.fragments

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.compose.ui.platform.ComposeView
import androidx.compose.ui.platform.ViewCompositionStrategy
import androidx.fragment.app.Fragment
import androidx.lifecycle.ViewModelProvider
import com.readstorm.app.ui.compose.screens.DownloadTasksScreen
import com.readstorm.app.ui.compose.theme.ReadStormTheme
import com.readstorm.app.ui.viewmodels.MainViewModel

class DownloadTasksFragment : Fragment() {

    private val mainViewModel: MainViewModel by lazy {
        ViewModelProvider(requireActivity())[MainViewModel::class.java]
    }

    override fun onCreateView(
        inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?
    ): View {
        return ComposeView(requireContext()).apply {
            setViewCompositionStrategy(ViewCompositionStrategy.DisposeOnViewTreeLifecycleDestroyed)
            setContent {
                ReadStormTheme {
                    DownloadTasksScreen(mainViewModel = mainViewModel)
                }
            }
        }
    }
}
