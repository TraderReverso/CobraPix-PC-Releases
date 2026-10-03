package com.example.cobrapix.ui.components

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AccessTime
import androidx.compose.material.icons.filled.QrCode
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.cobrapix.data.InstallmentStatus
import com.example.cobrapix.logic.CurrencyUtils
import com.example.cobrapix.ui.ClientScore
import com.example.cobrapix.ui.PayableItem
import com.example.cobrapix.ui.theme.*
import java.util.*

@Composable
fun StatusBadge(status: InstallmentStatus) {
    val hudColors = LocalCobraPixColors.current
    val (text, color) = when (status) {
        InstallmentStatus.PENDING -> "PENDENTE" to hudColors.secondary
        InstallmentStatus.PAID -> "CONCLUÍDO" to hudColors.success
        InstallmentStatus.OVERDUE -> "ATRASADO" to hudColors.error
    }
    Surface(
        color = color.copy(alpha = 0.1f),
        shape = MaterialTheme.shapes.small,
        border = androidx.compose.foundation.BorderStroke(0.5.dp, color.copy(alpha = 0.4f)),
    ) {
        Text(
            text = text,
            modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp),
            color = color,
            fontSize = 9.sp,
            fontWeight = FontWeight.Black,
            letterSpacing = 1.sp
        )
    }
}

@Composable
fun PayableListItem(item: PayableItem, onClick: (Long) -> Unit) {
    val hudColors = LocalCobraPixColors.current
    SoloCard(
        modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
        borderColor = when (item.status) {
            InstallmentStatus.OVERDUE -> hudColors.error
            InstallmentStatus.PAID -> hudColors.success
            else -> hudColors.primary.copy(alpha = 0.4f)
        }
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(modifier = Modifier.weight(1f)) {
                val label = when(item) {
                    is PayableItem.InstallmentType -> "PARCELA ${item.installment.number}"
                    is PayableItem.MonthlyType -> "MENSALIDADE"
                    is PayableItem.ExpenseType -> "DESPESA"
                }
                Text(
                    item.clientName.uppercase(), 
                    fontWeight = FontWeight.Black, 
                    color = hudColors.textPrimary,
                    fontSize = 14.sp,
                    letterSpacing = 1.sp
                )
                Text(label, style = MaterialTheme.typography.labelSmall, color = hudColors.secondary)
                Text(
                    CurrencyUtils.formatCurrency(item.value), 
                    fontWeight = FontWeight.ExtraBold, 
                    color = hudColors.primaryVariant,
                    fontSize = 18.sp
                )
                Text(
                    "VENCIMENTO: " + java.text.SimpleDateFormat("dd/MM/yyyy", Locale.getDefault()).format(Date(item.dueDate)),
                    style = MaterialTheme.typography.labelSmall,
                    color = hudColors.textPrimary.copy(alpha = 0.4f)
                )
            }
            Column(horizontalAlignment = Alignment.End) {
                StatusBadge(item.status)
                if (item.status != InstallmentStatus.PAID) {
                    TextButton(onClick = { onClick(item.clientId) }) {
                        Text("VER PERFIL", fontSize = 11.sp, color = hudColors.primary, fontWeight = FontWeight.Bold)
                    }
                }
            }
        }
    }
}

@Composable
fun MetricItem(label: String, value: String) {
    val hudColors = LocalCobraPixColors.current
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(value, fontWeight = FontWeight.Black, fontSize = 18.sp, color = hudColors.textPrimary)
        Text(label.uppercase(), style = MaterialTheme.typography.labelSmall, color = hudColors.secondary, fontSize = 9.sp, letterSpacing = 1.sp)
    }
}

@Composable
fun SummaryRow(label: String, value: Long, valueColor: Color) {
    val hudColors = LocalCobraPixColors.current
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(label, color = hudColors.textPrimary.copy(0.7f), fontSize = 12.sp, fontWeight = FontWeight.Medium)
        Text(CurrencyUtils.formatCurrency(value), fontWeight = FontWeight.Bold, color = valueColor, fontSize = 13.sp)
    }
}

@Composable
fun TimeSlotHUD(
    label: String, 
    minutes: Int, 
    isActive: Boolean = true,
    onToggle: ((Boolean) -> Unit)? = null,
    modifier: Modifier = Modifier, 
    onClick: () -> Unit
) {
    val hudColors = LocalCobraPixColors.current
    val timeStr = if (!isActive || minutes < 0) "--:--" else String.format(Locale.getDefault(), "%02d:%02d", minutes / 60, minutes % 60)
    
    OutlinedCard(
        onClick = onClick,
        modifier = modifier,
        colors = CardDefaults.outlinedCardColors(containerColor = hudColors.surfaceVariant.copy(alpha = 0.3f)),
        border = androidx.compose.foundation.BorderStroke(1.dp, if(isActive) hudColors.primary.copy(0.2f) else hudColors.border.copy(0.2f))
    ) {
        Column(modifier = Modifier.padding(12.dp)) {
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                Text(label, style = MaterialTheme.typography.labelSmall, color = if(isActive) hudColors.secondary else hudColors.textSecondary)
                if (onToggle != null) {
                    Switch(
                        checked = isActive, 
                        onCheckedChange = onToggle,
                        modifier = Modifier.height(16.dp),
                        colors = SwitchDefaults.colors(
                            checkedThumbColor = hudColors.primary,
                            checkedTrackColor = hudColors.primary.copy(alpha = 0.3f)
                        )
                    )
                }
            }
            Spacer(Modifier.height(4.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Default.AccessTime, null, modifier = Modifier.size(16.dp), tint = if(isActive) hudColors.secondary else hudColors.textSecondary)
                Spacer(Modifier.width(8.dp))
                Text(timeStr, fontWeight = FontWeight.Bold, color = if(isActive) hudColors.textPrimary else hudColors.textSecondary)
            }
        }
    }
}

@Composable
fun RankBadge(score: ClientScore) {
    val (color, label) = when (score.rank) {
        "S" -> Color(0xFF00E5FF) to "S"
        "A" -> Color(0xFFFFD600) to "A"
        "B" -> Color(0xFFE0E0E0) to "B"
        "C" -> Color(0xFFFF5252) to "C"
        else -> Color.Gray to "?"
    }
    Surface(
        modifier = Modifier.size(28.dp),
        color = color.copy(0.15f),
        shape = androidx.compose.foundation.shape.CircleShape,
        border = androidx.compose.foundation.BorderStroke(1.5.dp, color)
    ) {
        Box(contentAlignment = Alignment.Center) {
            Text(
                text = label,
                color = color,
                fontSize = 14.sp,
                fontWeight = FontWeight.Black,
                textAlign = TextAlign.Center
            )
        }
    }
}

@Composable
fun EditHUDTextField(value: String, label: String, singleLine: Boolean = true, onValueChange: (String) -> Unit) {
    val hudColors = LocalCobraPixColors.current
    var textFieldValueState by remember { mutableStateOf(TextFieldValue(text = value, selection = androidx.compose.ui.text.TextRange(value.length))) }
    
    LaunchedEffect(value) {
        if (textFieldValueState.text != value) {
            textFieldValueState = textFieldValueState.copy(text = value, selection = androidx.compose.ui.text.TextRange(value.length))
        }
    }

    OutlinedTextField(
        value = textFieldValueState,
        onValueChange = {
            textFieldValueState = it
            if (value != it.text) {
                onValueChange(it.text)
            }
        },
        label = { Text(label) },
        modifier = Modifier.fillMaxWidth(),
        singleLine = singleLine,
        minLines = if (singleLine) 1 else 3,
        colors = OutlinedTextFieldDefaults.colors(
            focusedBorderColor = hudColors.secondary,
            unfocusedBorderColor = hudColors.border,
            focusedTextColor = hudColors.textPrimary,
            unfocusedTextColor = hudColors.textPrimary,
            focusedLabelColor = hudColors.secondary,
            unfocusedLabelColor = hudColors.textSecondary
        )
    )
}
