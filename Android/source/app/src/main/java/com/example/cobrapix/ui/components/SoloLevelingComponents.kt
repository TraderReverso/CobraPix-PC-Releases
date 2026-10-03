package com.example.cobrapix.ui.components

import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.GenericShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Whatsapp
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.example.cobrapix.ui.theme.*
import kotlin.math.cos
import kotlin.math.sin

/**
 * Diálogo holográfico de confirmação (MISSÃO CONCLUÍDA)
 */
@Composable
fun SoloQuestClearDialog(
    title: String,
    details: String,
    onDismiss: () -> Unit,
    onSendWhatsApp: () -> Unit,
    onShareReceipt: (() -> Unit)? = null
) {
    val colors = LocalCobraPixColors.current
    Dialog(onDismissRequest = onDismiss) {
        SoloCard(borderColor = colors.success) {
            Column(
                modifier = Modifier.fillMaxWidth(),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Icon(
                    Icons.Default.CheckCircle,
                    contentDescription = null,
                    modifier = Modifier.size(64.dp),
                    tint = colors.success
                )
                Spacer(modifier = Modifier.height(16.dp))
                Text(
                    title.uppercase(),
                    style = MaterialTheme.typography.headlineSmall,
                    fontWeight = FontWeight.Black,
                    letterSpacing = 4.sp,
                    color = colors.success,
                    textAlign = TextAlign.Center
                )
                Spacer(modifier = Modifier.height(8.dp))
                Text(
                    details,
                    style = MaterialTheme.typography.bodyMedium,
                    color = colors.textPrimary,
                    textAlign = TextAlign.Center
                )
                Spacer(modifier = Modifier.height(24.dp))
                Button(
                    onClick = onSendWhatsApp,
                    modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF25D366))
                ) {
                    Icon(Icons.Default.Whatsapp, null)
                    Spacer(Modifier.width(8.dp))
                    Text("ENVIAR WHATSAPP", fontSize = 12.sp, fontWeight = FontWeight.Black)
                }
                
                if (onShareReceipt != null) {
                    Spacer(modifier = Modifier.height(8.dp))
                    OutlinedButton(
                        onClick = onShareReceipt,
                        modifier = Modifier.fillMaxWidth(),
                        colors = ButtonDefaults.outlinedButtonColors(contentColor = colors.primary)
                    ) {
                        Text("COMPARTILHAR RECIBO (PDF)", fontSize = 12.sp, fontWeight = FontWeight.Black)
                    }
                }
                
                Spacer(modifier = Modifier.height(8.dp))
                TextButton(
                    onClick = onDismiss,
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text("FECHAR", color = colors.textSecondary)
                }
            }
        }
    }
}

/**
 * Card HUD animado com borda neon pulsante.
 */
@Composable
fun SoloCard(
    modifier: Modifier = Modifier,
    borderColor: Color? = null,
    content: @Composable ColumnScope.() -> Unit
) {
    val colors = LocalCobraPixColors.current
    val hudClock = LocalHUDAnimationClock.current
    val actualBorderColor = borderColor ?: colors.primary
    
    val hudShape = remember {
        GenericShape { size, _ ->
            val cut = 24f
            moveTo(cut, 0f); lineTo(size.width - cut, 0f); lineTo(size.width, cut)
            lineTo(size.width, size.height - cut); lineTo(size.width - cut, size.height)
            lineTo(cut, size.height); lineTo(0f, size.height - cut); lineTo(0f, cut); close()
        }
    }

    Box(
        modifier = modifier
            .then(if (!colors.isDark) Modifier.shadow(4.dp, hudShape) else Modifier)
            .background(colors.surface.copy(alpha = if (colors.isDark) 0.85f else 1f), hudShape)
            .drawBehind {
                val alpha = if (colors.isDark) hudClock.pulseAlpha.value else 0.4f
                val brush = Brush.linearGradient(
                    listOf(actualBorderColor.copy(alpha = alpha), actualBorderColor.copy(0.05f))
                )
                val path = Path().apply {
                    val cut = 24f
                    moveTo(cut, 0f); lineTo(size.width - cut, 0f); lineTo(size.width, cut)
                    lineTo(size.width, size.height - cut); lineTo(size.width - cut, size.height)
                    lineTo(cut, size.height); lineTo(0f, size.height - cut); lineTo(0f, cut); close()
                }
                drawPath(path, brush, style = Stroke(width = 1.dp.toPx()))
            }
            .padding(1.dp)
    ) {
        Column(
            modifier = Modifier.padding(20.dp),
            content = content
        )
    }
}

/**
 * Fundo com grade hexagonal.
 */
@Composable
fun SoloBackground(modifier: Modifier = Modifier) {
    val colors = LocalCobraPixColors.current
    val hudClock = LocalHUDAnimationClock.current
    
    Box(modifier = modifier.fillMaxSize().background(colors.background)) {
        var cachedGrid by remember { mutableStateOf<ImageBitmap?>(null) }
        
        Canvas(modifier = Modifier.fillMaxSize()) {
            val alpha = hudClock.gridAlpha.value
            
            if (cachedGrid == null || cachedGrid?.width != size.width.toInt() || cachedGrid?.height != size.height.toInt()) {
                val bitmap = ImageBitmap(size.width.toInt(), size.height.toInt())
                val canvas = Canvas(bitmap)
                val paint = Paint().apply {
                    color = colors.primary
                    style = PaintingStyle.Stroke
                    strokeWidth = 1f
                }
                
                val hexSize = 80f
                val h = hexSize * 2
                val w = Math.sqrt(3.0).toFloat() * hexSize
                val path = Path()

                for (y in -1..(size.height / (h * 0.75f)).toInt() + 1) {
                    for (x in -1..(size.width / w).toInt() + 1) {
                        val xOffset = if (y % 2 != 0) w / 2 else 0f
                        val centerX = x * w + xOffset
                        val centerY = y * h * 0.75f
                        path.reset()
                        for (i in 0..5) {
                            val angle = Math.toRadians(60.0 * i - 30.0)
                            val px = centerX + hexSize * cos(angle).toFloat()
                            val py = centerY + hexSize * sin(angle).toFloat()
                            if (i == 0) path.moveTo(px, py) else path.lineTo(px, py)
                        }
                        path.close()
                        canvas.drawPath(path, paint)
                    }
                }
                cachedGrid = bitmap
            }
            
            cachedGrid?.let { 
                drawImage(it, colorFilter = ColorFilter.tint(Color.White.copy(alpha = alpha)))
            }
        }
        
        ParticleEffect(colors.secondary)
    }
}

@Composable
fun ParticleEffect(color: Color) {
    val hudClock = LocalHUDAnimationClock.current
    val particlesBase = remember {
        List(8) { Offset((0..100).random() / 100f, (0..100).random() / 100f) }
    }
    val brush = remember(color) { Brush.radialGradient(listOf(color, Color.Transparent)) }

    Canvas(modifier = Modifier.fillMaxSize().alpha(0.1f)) {
        val time = hudClock.particleTime.value
        particlesBase.forEachIndexed { i, base ->
            val speed = 0.2f + (i % 4) * 0.15f
            val x = size.width * base.x
            val y = (size.height * (base.y - time * speed) + size.height) % size.height
            drawCircle(brush = brush, radius = 6f, center = Offset(x, y))
        }
    }
}

/**
 * FilterChip customizado para o sistema HUD
 */
@Composable
fun FilterChipHUD(label: String, selected: Boolean, onClick: () -> Unit) {
    val hudColors = LocalCobraPixColors.current
    FilterChip(
        selected = selected,
        onClick = onClick,
        label = { Text(label, fontSize = 10.sp, fontWeight = FontWeight.Bold) },
        colors = FilterChipDefaults.filterChipColors(
            selectedContainerColor = hudColors.primary.copy(alpha = 0.3f),
            selectedLabelColor = hudColors.textPrimary,
            labelColor = hudColors.textSecondary,
            containerColor = Color.Transparent
        ),
        border = FilterChipDefaults.filterChipBorder(
            enabled = true,
            selected = selected,
            borderColor = hudColors.border,
            selectedBorderColor = hudColors.primary
        )
    )
}

/**
 * Barra de progresso com animação de fluxo (shimmer).
 */
@Composable
fun SoloProgressBar(
    progress: Float,
    label: String,
    valueText: String,
    color: Color? = null
) {
    val colors = LocalCobraPixColors.current
    val hudClock = LocalHUDAnimationClock.current
    val actualColor = color ?: colors.primary
    
    val animatedProgress by animateFloatAsState(
        targetValue = progress.coerceIn(0f, 1f),
        animationSpec = tween(2000, easing = FastOutSlowInEasing),
        label = "progress"
    )

    Column(modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.Bottom
        ) {
            Text(label.uppercase(), fontSize = 11.sp, color = colors.textPrimary, fontWeight = FontWeight.Black, letterSpacing = 1.sp)
            Text(valueText, fontSize = 10.sp, color = colors.textSecondary)
        }
        Spacer(modifier = Modifier.height(8.dp))
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(14.dp)
                .background(colors.background.copy(0.3f))
                .border(0.5.dp, colors.primary.copy(0.1f))
        ) {
            Box(
                modifier = Modifier
                    .fillMaxWidth(animatedProgress)
                    .fillMaxHeight()
                    .background(
                        Brush.horizontalGradient(listOf(actualColor.copy(0.4f), actualColor))
                    )
                    .drawBehind {
                        val shimmerShift = hudClock.shimmerShift.value
                        val brush = Brush.horizontalGradient(
                            listOf(Color.Transparent, Color.White.copy(0.15f), Color.Transparent),
                            startX = size.width * shimmerShift,
                            endX = size.width * (shimmerShift + 1f)
                        )
                        drawRect(brush)
                        
                        if (animatedProgress > 0f) {
                            drawCircle(
                                color = actualColor, radius = 8.dp.toPx(),
                                center = Offset(size.width, size.height / 2), alpha = 0.5f
                            )
                        }
                    }
            )
        }
    }
}

/**
 * Título técnico com linha de energia
 */
@Composable
fun SoloHeader(
    text: String,
    modifier: Modifier = Modifier,
    color: Color? = null
) {
    val colors = LocalCobraPixColors.current
    val actualColor = color ?: colors.primary
    
    Column(modifier = modifier) {
        Text(
            text = text.uppercase(),
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Black,
            letterSpacing = 3.sp,
            color = actualColor
        )
        Canvas(modifier = Modifier.fillMaxWidth().height(2.dp).padding(top = 4.dp)) {
            drawLine(
                brush = Brush.horizontalGradient(listOf(actualColor, Color.Transparent)),
                start = Offset(0f, 0f),
                end = Offset(size.width, 0f),
                strokeWidth = 4f
            )
        }
    }
}
