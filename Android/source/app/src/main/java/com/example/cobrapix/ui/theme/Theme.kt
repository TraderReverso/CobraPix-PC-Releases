package com.example.cobrapix.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.graphics.Color

@Composable
fun CobraPixTheme(
    darkTheme: Boolean = true,
    content: @Composable () -> Unit
) {
    val hudColors = if (darkTheme) DarkHUDPalette else LightHUDPalette
    
    val colorScheme = if (darkTheme) {
        darkColorScheme(
            primary = hudColors.primary,
            secondary = hudColors.secondary,
            background = hudColors.background,
            surface = hudColors.surface,
            onPrimary = hudColors.textPrimary,
            onBackground = hudColors.textPrimary,
            onSurface = hudColors.textPrimary,
            error = hudColors.error
        )
    } else {
        lightColorScheme(
            primary = hudColors.primary,
            secondary = hudColors.secondary,
            background = hudColors.background,
            surface = hudColors.surface,
            onPrimary = Color.White,
            onBackground = hudColors.textPrimary,
            onSurface = hudColors.textPrimary,
            error = hudColors.error
        )
    }

    CompositionLocalProvider(LocalCobraPixColors provides hudColors) {
        MaterialTheme(
            colorScheme = colorScheme,
            typography = Typography,
            content = content
        )
    }
}
