package com.readstorm.app.ui.compose.theme

import android.os.Build
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext

// ── 浅色方案 ──
private val LightColorScheme = lightColorScheme(
    primary = SlateBlue800,
    onPrimary = Color.White,
    primaryContainer = Blue100,
    onPrimaryContainer = SlateBlue900,

    secondary = Blue500,
    onSecondary = Color.White,
    secondaryContainer = Blue50,
    onSecondaryContainer = Blue600,

    tertiary = SlateBlue600,
    onTertiary = Color.White,
    tertiaryContainer = SlateBlue100,
    onTertiaryContainer = SlateBlue800,

    background = Color.White,
    onBackground = SlateBlue800,
    surface = Color.White,
    onSurface = SlateBlue800,
    surfaceVariant = SlateBlue50,
    onSurfaceVariant = SlateBlue600,

    error = ErrorRed,
    onError = Color.White,
    errorContainer = Color(0xFFFEE2E2),
    onErrorContainer = Color(0xFF991B1B),

    outline = SlateBlue400,
    outlineVariant = SlateBlue200
)

// ── 深色方案 ──
private val DarkColorScheme = darkColorScheme(
    primary = Blue400,
    onPrimary = SlateBlue900,
    primaryContainer = SlateBlue700,
    onPrimaryContainer = Blue200,

    secondary = Blue200,
    onSecondary = SlateBlue900,
    secondaryContainer = SlateBlue700,
    onSecondaryContainer = Blue100,

    tertiary = SlateBlue400,
    onTertiary = SlateBlue900,
    tertiaryContainer = SlateBlue700,
    onTertiaryContainer = SlateBlue200,

    background = SlateBlue900,
    onBackground = SlateBlue100,
    surface = SlateBlue900,
    onSurface = SlateBlue100,
    surfaceVariant = SlateBlue800,
    onSurfaceVariant = SlateBlue400,

    error = ErrorRedDark,
    onError = Color(0xFF7F1D1D),
    errorContainer = Color(0xFF991B1B),
    onErrorContainer = Color(0xFFFEE2E2),

    outline = SlateBlue600,
    outlineVariant = SlateBlue700
)

/**
 * ReadStorm 统一主题入口。
 *
 * @param darkTheme 是否使用深色模式，默认跟随系统
 * @param dynamicColor 是否使用动态颜色（Android 12+），默认关闭以保证品牌一致性
 */
@Composable
fun ReadStormTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    dynamicColor: Boolean = false,
    content: @Composable () -> Unit
) {
    val colorScheme = when {
        dynamicColor && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S -> {
            val context = LocalContext.current
            if (darkTheme) dynamicDarkColorScheme(context)
            else dynamicLightColorScheme(context)
        }
        darkTheme -> DarkColorScheme
        else -> LightColorScheme
    }

    MaterialTheme(
        colorScheme = colorScheme,
        typography = ReadStormTypography,
        shapes = ReadStormShapes
    ) {
        Surface(
            modifier = Modifier.fillMaxSize(),
            color = colorScheme.background,
            content = content
        )
    }
}
