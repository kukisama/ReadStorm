package com.readstorm.app.ui.fragments

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import com.readstorm.app.R
import com.readstorm.app.databinding.FragmentAboutBinding
import io.noties.markwon.Markwon

class AboutFragment : Fragment() {

    private var _binding: FragmentAboutBinding? = null
    private val binding get() = _binding!!

    override fun onCreateView(
        inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?
    ): View {
        _binding = FragmentAboutBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        val versionName = try {
            requireContext().packageManager
                .getPackageInfo(requireContext().packageName, 0).versionName
        } catch (_: Exception) { "1.0.0" }

        binding.tvVersion.text = "版本 $versionName"

        binding.btnGitHub.setOnClickListener {
            val intent = Intent(Intent.ACTION_VIEW,
                Uri.parse("https://github.com/clayouuz/ReadStorm"))
            startActivity(intent)
        }

        loadReleaseNotes()
    }

    private fun loadReleaseNotes() {
        val markwon = Markwon.create(requireContext())
        val markdown = loadReleaseNotesMarkdown()
        markwon.setMarkdown(binding.tvReleaseNotes, markdown)
    }

    private fun loadReleaseNotesMarkdown(): String {
        return try {
            requireContext().assets.open("RELEASE_NOTES.md").bufferedReader().readText()
        } catch (_: Exception) {
            "暂无更新日志"
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
