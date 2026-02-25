package com.readstorm.app.ui.activities

import android.app.Activity
import android.content.Intent
import android.content.res.Configuration
import android.graphics.Matrix
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.widget.ImageView

class SplashActivity : Activity() {

    companion object {
        private const val MIN_SPLASH_DURATION_MS = 300L
        private const val PORTRAIT_CROP_FOCUS_Y = 0.5f
        private const val LANDSCAPE_CROP_FOCUS_Y = 0.8f
    }

    private var mainHandler: Handler? = null
    private var navigated = false
    private var navigateRunnable: Runnable? = null
    private var splashImage: ImageView? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Prevent duplicate instances when tapped from launcher while already running
        if (!isTaskRoot &&
            intent?.action == Intent.ACTION_MAIN &&
            intent?.hasCategory(Intent.CATEGORY_LAUNCHER) == true
        ) {
            finish()
            return
        }

        mainHandler = Handler(Looper.getMainLooper())

        splashImage = ImageView(this).apply {
            scaleType = ImageView.ScaleType.MATRIX
            val resId = resolveSplashDrawableResource()
            if (resId != 0) {
                setImageResource(resId)
            } else {
                setBackgroundColor(android.graphics.Color.BLACK)
            }
            post { applySplashImageCrop() }
        }
        setContentView(splashImage)

        navigateRunnable = Runnable { navigateToMain() }
        mainHandler?.postDelayed(navigateRunnable!!, MIN_SPLASH_DURATION_MS)
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        splashImage?.apply {
            val resId = resolveSplashDrawableResource()
            if (resId != 0) {
                setImageResource(resId)
            }
            post { applySplashImageCrop() }
        }
    }

    override fun onDestroy() {
        navigateRunnable?.let { mainHandler?.removeCallbacks(it) }
        navigateRunnable = null
        super.onDestroy()
    }

    private fun navigateToMain() {
        if (navigated || isFinishing) return
        navigated = true

        val intent = Intent(this, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)
        }
        startActivity(intent)
        finish()
    }

    private fun resolveSplashDrawableResource(): Int {
        val isLandscape =
            resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE
        if (!isLandscape) {
            val id = resources.getIdentifier("boot", "drawable", packageName)
            if (id != 0) return id
            // No boot drawable; fall back to center-crop black
            return 0
        }

        val landscapeId = resources.getIdentifier("boot_land", "drawable", packageName)
        if (landscapeId != 0) return landscapeId

        val portraitId = resources.getIdentifier("boot", "drawable", packageName)
        if (portraitId != 0) return portraitId
        return 0
    }

    private fun applySplashImageCrop() {
        val imageView = splashImage ?: return
        val drawable = imageView.drawable ?: return

        val viewWidth = imageView.width
        val viewHeight = imageView.height
        val drawableWidth = drawable.intrinsicWidth
        val drawableHeight = drawable.intrinsicHeight

        if (viewWidth <= 0 || viewHeight <= 0 || drawableWidth <= 0 || drawableHeight <= 0) {
            imageView.scaleType = ImageView.ScaleType.CENTER_CROP
            return
        }

        val scale = maxOf(
            viewWidth.toFloat() / drawableWidth,
            viewHeight.toFloat() / drawableHeight
        )
        val scaledWidth = drawableWidth * scale
        val scaledHeight = drawableHeight * scale

        var dx = (viewWidth - scaledWidth) / 2f
        var dy = (viewHeight - scaledHeight) / 2f

        val isLandscape =
            resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE
        val focusY = (if (isLandscape) LANDSCAPE_CROP_FOCUS_Y else PORTRAIT_CROP_FOCUS_Y)
            .coerceIn(0f, 1f)

        if (scaledHeight > viewHeight) {
            dy = (viewHeight - scaledHeight) * focusY
        }

        val matrix = Matrix().apply {
            setScale(scale, scale)
            postTranslate(dx, dy)
        }
        imageView.scaleType = ImageView.ScaleType.MATRIX
        imageView.imageMatrix = matrix
    }
}
