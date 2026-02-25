package com.readstorm.app.ui.fragments

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import com.readstorm.app.databinding.FragmentLogBinding

class LogFragment : Fragment() {

    private var _binding: FragmentLogBinding? = null
    private val binding get() = _binding!!

    override fun onCreateView(
        inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?
    ): View {
        _binding = FragmentLogBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        binding.btnClearLog.setOnClickListener { clearLog() }
    }

    fun appendLog(message: String) {
        val current = binding.tvLogContent.text?.toString() ?: ""
        binding.tvLogContent.text = if (current.isEmpty()) message else "$current\n$message"
        binding.scrollLog.post {
            binding.scrollLog.fullScroll(View.FOCUS_DOWN)
        }
    }

    fun setLogContent(content: String) {
        binding.tvLogContent.text = content
    }

    private fun clearLog() {
        binding.tvLogContent.text = ""
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
