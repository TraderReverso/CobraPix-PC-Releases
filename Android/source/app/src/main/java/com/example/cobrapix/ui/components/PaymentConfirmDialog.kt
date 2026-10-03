package com.example.cobrapix.ui.components

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.example.cobrapix.data.PaymentMethod
import com.example.cobrapix.logic.CurrencyUtils
import com.example.cobrapix.ui.PayableItem
import com.example.cobrapix.ui.theme.LocalCobraPixColors

@Composable
fun PaymentConfirmDialog(
    item: PayableItem,
    onDismiss: () -> Unit,
    onConfirm: (Long, PaymentMethod) -> Unit,
    isProcessing: Boolean = false
) {
    val hudColors = LocalCobraPixColors.current
    
    val maxAllowed = when (item) {
        is PayableItem.InstallmentType -> item.installment.value - item.installment.paidAmount
        else -> item.value
    }

    var rawAmount by remember { mutableStateOf(maxAllowed.toString()) }
    val formattedAmount = remember(rawAmount) { CurrencyUtils.formatCurrency(rawAmount.toLongOrNull() ?: 0L) }
    var method by remember { mutableStateOf(PaymentMethod.PIX) }

    Dialog(onDismissRequest = if (isProcessing) ({}) else onDismiss) {
        SoloCard(borderColor = hudColors.primary) {
            Column {
                if (isProcessing) {
                    Box(modifier = Modifier.fillMaxWidth().padding(32.dp), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator(color = hudColors.primary)
                    }
                } else {
                    SoloHeader("Registrar Pagamento", color = hudColors.primary)
                    Spacer(Modifier.height(16.dp))
                    
                    if (item is PayableItem.InstallmentType) {
                        val paid = item.installment.paidAmount
                        if (paid > 0) {
                            Text("Valor Original: ${CurrencyUtils.formatCurrency(item.installment.value)}", color = hudColors.textSecondary, fontSize = 12.sp)
                            Text("Já Pago: ${CurrencyUtils.formatCurrency(paid)}", color = hudColors.success, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                            Spacer(Modifier.height(4.dp))
                        }
                        Text("Saldo Restante: ${CurrencyUtils.formatCurrency(maxAllowed)}", color = hudColors.warning, fontSize = 14.sp, fontWeight = FontWeight.Black)
                        Spacer(Modifier.height(16.dp))
                        
                        EditHUDTextField(value = formattedAmount, label = "Valor do Pagamento") { input ->
                            val digits = input.filter { it.isDigit() }
                            if (digits.length <= 12) rawAmount = digits
                        }
                    } else {
                        Text("Valor: ${CurrencyUtils.formatCurrency(maxAllowed)}", color = hudColors.warning, fontSize = 14.sp, fontWeight = FontWeight.Black)
                        Spacer(Modifier.height(16.dp))
                    }

                    Spacer(Modifier.height(16.dp))
                    Text("MÉTODO DE PAGAMENTO", style = MaterialTheme.typography.labelSmall, color = hudColors.textSecondary)
                    Row(modifier = Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        FilterChipHUD("PIX", method == PaymentMethod.PIX) { method = PaymentMethod.PIX }
                        FilterChipHUD("$$$", method == PaymentMethod.CASH) { method = PaymentMethod.CASH }
                        FilterChipHUD("CARTÃO", method == PaymentMethod.CARD) { method = PaymentMethod.CARD }
                    }
                    
                    Spacer(Modifier.height(24.dp))
                    
                    val valueToPay = rawAmount.toLongOrNull() ?: 0L
                    val isAmountValid = valueToPay > 0L && valueToPay <= maxAllowed
                    
                    if (!isAmountValid && item is PayableItem.InstallmentType && valueToPay > 0L) {
                        Text("O valor não pode ser maior que o saldo restante.", color = hudColors.error, fontSize = 11.sp)
                        Spacer(Modifier.height(8.dp))
                    }
                    
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                        TextButton(onClick = onDismiss) { Text("CANCELAR", color = hudColors.textSecondary) }
                        Button(
                            onClick = { onConfirm(valueToPay, method) }, 
                            colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary),
                            enabled = isAmountValid
                        ) { Text("CONFIRMAR", fontWeight = FontWeight.Black) }
                    }
                }
            }
        }
    }
}
