package com.example.cobrapix.ui.theme

import androidx.compose.animation.core.*
import androidx.compose.runtime.*

/**
 * Clock centralizado de animação.
 * Expõe as propriedades como State para permitir leitura em DrawScope sem recompor a árvore inteira.
 */
@Stable
class HUDAnimationClock(
    val pulseAlpha: State<Float>,
    val gridAlpha: State<Float>,
    val shimmerShift: State<Float>,
    val particleTime: State<Float>
)

val LocalHUDAnimationClock = staticCompositionLocalOf<HUDAnimationClock> {
    error("HUDAnimationClock not provided")
}

@Composable
fun HUDAnimationProvider(content: @Composable () -> Unit) {
    val infiniteTransition = rememberInfiniteTransition(label = "hudClock")
    
    val pulse = infiniteTransition.animateFloat(
        initialValue = 0.3f, targetValue = 0.8f,
        animationSpec = infiniteRepeatable(tween(5000, easing = LinearEasing), RepeatMode.Reverse),
        label = "pulse"
    )
    
    val grid = infiniteTransition.animateFloat(
        initialValue = 0.02f, targetValue = 0.05f,
        animationSpec = infiniteRepeatable(tween(8000, easing = LinearEasing), RepeatMode.Reverse),
        label = "grid"
    )
    
    val shimmer = infiniteTransition.animateFloat(
        initialValue = -1f, targetValue = 2f,
        animationSpec = infiniteRepeatable(tween(2500, easing = LinearEasing)),
        label = "shimmer"
    )
    
    val pTime = infiniteTransition.animateFloat(
        initialValue = 0f, targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(12000, easing = LinearEasing)),
        label = "particle"
    )

    val clock = remember { HUDAnimationClock(pulse, grid, shimmer, pTime) }

    CompositionLocalProvider(LocalHUDAnimationClock provides clock) {
        content()
    }
}
