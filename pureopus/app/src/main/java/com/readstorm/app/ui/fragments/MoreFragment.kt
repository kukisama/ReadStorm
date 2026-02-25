package com.readstorm.app.ui.fragments

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import com.readstorm.app.databinding.FragmentMoreBinding
import com.readstorm.app.ui.activities.MainActivity

class MoreFragment : Fragment() {

    private var _binding: FragmentMoreBinding? = null
    private val binding get() = _binding!!

    override fun onCreateView(
        inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?
    ): View {
        _binding = FragmentMoreBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        setupNavigation()
    }

    private fun setupNavigation() {
        binding.btnDiagnostic.setOnClickListener {
            navigateToSubPage("diagnostic", DiagnosticFragment())
        }
        binding.btnRules.setOnClickListener {
            navigateToSubPage("rules", RuleEditorFragment())
        }
        binding.btnSettings.setOnClickListener {
            navigateToSubPage("settings", SettingsFragment())
        }
        binding.btnAbout.setOnClickListener {
            navigateToSubPage("about", AboutFragment())
        }
        binding.btnLog.setOnClickListener {
            navigateToSubPage("log", LogFragment())
        }
    }

    private fun navigateToSubPage(pageKey: String, fragment: Fragment) {
        (activity as? MainActivity)?.openSubPage(pageKey, fragment)
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
