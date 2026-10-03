package com.example.cobrapix.ui.theme

import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color

// CobraPix HUD Color System
data class CobraPixHUDColors(
    val background: Color,
    val surface: Color,
    val surfaceVariant: Color,
    val primary: Color,
    val primaryVariant: Color,
    val secondary: Color,
    val textPrimary: Color,
    val textSecondary: Color,
    val success: Color,
    val warning: Color,
    val error: Color,
    val border: Color,
    val isDark: Boolean
)

val DarkHUDPalette = CobraPixHUDColors(
    background = Color(0xFF03040A),
    surface = Color(0xFF0B0D18),
    surfaceVariant = Color(0xFF070914),
    primary = Color(0xFF8B5CF6),
    primaryVariant = Color(0xFFA855F7),
    secondary = Color(0xFF22D3EE),
    textPrimary = Color(0xFFF8FAFC),
    textSecondary = Color(0xFF94A3B8),
    success = Color(0xFF22C55E),
    warning = Color(0xFFF59E0B),
    error = Color(0xFFEF4444),
    border = Color(0xFF8B5CF6).copy(alpha = 0.3f),
    isDark = true
)

val LightHUDPalette = CobraPixHUDColors(
    background = Color(0xFFF5F7FB),
    surface = Color(0xFFFFFFFF),
    surfaceVariant = Color(0xFFEDF2F7),
    primary = Color(0xFF7C3AED),
    primaryVariant = Color(0xFF8B5CF6),
    secondary = Color(0xFF0891B2),
    textPrimary = Color(0xFF0F172A),
    textSecondary = Color(0xFF475569),
    success = Color(0xFF16A34A),
    warning = Color(0xFFD97706),
    error = Color(0xFFDC2626),
    border = Color(0xFFE2E8F0),
    isDark = false
)

val LocalCobraPixColors = staticCompositionLocalOf { DarkHUDPalette }

// Legacy references for compatibility during migration
val SoloDeepBlack = Color(0xFF020205)
val SoloSpace = Color(0xFF0A0A15)
val SoloPurple = Color(0xFFA020F0)
val SoloBlue = Color(0xFF00E5FF)
val SoloText = Color(0xFFE0E0FF)
val SoloBorder = Color(0xFF2C2C3C)
val CorPago = Color(0xFF22C55E)
val CorAtrasado = Color(0xFFEF4444)
val CianoHUD = Color(0xFF22D3EE)
val RoxoPrincipal = Color(0xFF8B5CF6)
val RoxoClaro = Color(0xFFC084FC)
val BrancoHUD = Color(0xFFF8FAFC)
val FundoPrincipal = Color(0xFF03040A)
val FundoPainel = Color(0xFF0B0D18)
val FundoSecundario = Color(0xFF070914)
val CinzaTexto = Color(0xFF94A3B8)
val CorAtencao = Color(0xFFF59E0B)
