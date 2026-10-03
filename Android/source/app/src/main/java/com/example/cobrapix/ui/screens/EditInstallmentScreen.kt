package com.example.cobrapix.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.example.cobrapix.data.InstallmentStatus
import com.example.cobrapix.data.PaymentMethod
import com.example.cobrapix.logic.CurrencyUtils
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.*
import java.text.SimpleDateFormat
import java.util.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun EditInstallmentScreen(
    viewModel: MainViewModel,
    installmentId: Long,
    onBack: () -> Unit
) {
    val payableItem by viewModel.getPayableItem(installmentId).collectAsStateWithLifecycle(initialValue = null)
    val hudColors = LocalCobraPixColors.current
    
    var rawAmount by remember { mutableStateOf("") }
    val formattedAmount = remember(rawAmount) {
        val value = rawAmount.toLongOrNull() ?: 0L
        CurrencyUtils.formatCurrency(value)
    }

    val calendar = Calendar.getInstance().apply {
        set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0)
    }
    var selectedDate by remember { mutableLongStateOf(calendar.timeInMillis) }
    var status by remember { mutableStateOf(InstallmentStatus.PENDING) }
    var method by remember { mutableStateOf(PaymentMethod.NONE) }
    var showDatePicker by remember { mutableStateOf(false) }

    LaunchedEffect(payableItem) {
        payableItem?.let {
            rawAmount = it.value.toString()
            selectedDate = it.dueDate
            status = it.status
            method = when(it) {
                is com.example.cobrapix.ui.PayableItem.InstallmentType -> it.installment.paymentMethod
                is com.example.cobrapix.ui.PayableItem.MonthlyType -> it.payment.paymentMethod
                else -> PaymentMethod.NONE
            }
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { SoloHeader("Ajustar Cobrança", color = hudColors.textPrimary) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Voltar", tint = hudColors.textPrimary)
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.Transparent)
            )
        },
        containerColor = Color.Transparent
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize()) {
            SoloBackground()
            
            Column(modifier = Modifier.padding(padding).padding(16.dp)) {
                SoloCard(borderColor = hudColors.primary) {
                    EditHUDTextField(
                        value = formattedAmount,
                        onValueChange = { input ->
                            val digits = input.filter { it.isDigit() }
                            if (digits.length <= 12) rawAmount = digits
                        },
                        label = "Valor da Cobrança"
                    )
                    Spacer(modifier = Modifier.height(16.dp))
                    Button(
                        onClick = { showDatePicker = true }, 
                        modifier = Modifier.fillMaxWidth(),
                        colors = ButtonDefaults.buttonColors(containerColor = hudColors.surfaceVariant),
                        border = androidx.compose.foundation.BorderStroke(1.dp, hudColors.primary.copy(0.4f))
                    ) {
                        Text("Vencimento: ${SimpleDateFormat("dd/MM/yyyy", Locale.getDefault()).format(Date(selectedDate))}", color = hudColors.textPrimary)
                    }
                    Spacer(modifier = Modifier.height(24.dp))
                    Text("SITUAÇÃO ATUAL", style = MaterialTheme.typography.labelSmall, color = hudColors.secondary)
                    Spacer(modifier = Modifier.height(8.dp))
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        FilterChipHUD("Pendente", status == InstallmentStatus.PENDING) { status = InstallmentStatus.PENDING }
                        FilterChipHUD("Pago", status == InstallmentStatus.PAID) { status = InstallmentStatus.PAID }
                        FilterChipHUD("Atrasado", status == InstallmentStatus.OVERDUE) { status = InstallmentStatus.OVERDUE }
                    }
                    
                    if (status == InstallmentStatus.PAID) {
                        Spacer(modifier = Modifier.height(24.dp))
                        Text("MÉTODO DE PAGAMENTO", style = MaterialTheme.typography.labelSmall, color = hudColors.secondary)
                        Spacer(modifier = Modifier.height(8.dp))
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            FilterChipHUD("PIX", method == PaymentMethod.PIX) { method = PaymentMethod.PIX }
                            FilterChipHUD("DINHEIRO", method == PaymentMethod.CASH) { method = PaymentMethod.CASH }
                            FilterChipHUD("CARTÃO", method == PaymentMethod.CARD) { method = PaymentMethod.CARD }
                        }
                    }
                }

                Spacer(modifier = Modifier.height(32.dp))
                Button(
                    onClick = {
                        payableItem?.let { item ->
                            val valueCents = rawAmount.toLongOrNull() ?: item.value
                            val updatedItem = when(item) {
                                is com.example.cobrapix.ui.PayableItem.InstallmentType -> {
                                    item.copy(installment = item.installment.copy(
                                        value = valueCents,
                                        dueDate = selectedDate,
                                        status = status,
                                        paymentMethod = method,
                                        paymentDate = if (status == InstallmentStatus.PAID) System.currentTimeMillis() else null
                                    ))
                                }
                                is com.example.cobrapix.ui.PayableItem.MonthlyType -> {
                                    item.copy(payment = item.payment.copy(
                                        value = valueCents,
                                        dueDate = selectedDate,
                                        status = status,
                                        paymentMethod = method,
                                        paymentDate = if (status == InstallmentStatus.PAID) System.currentTimeMillis() else null
                                    ))
                                }
                                else -> item
                            }
                            viewModel.updatePayableItem(updatedItem)
                            onBack()
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary)
                ) {
                    Text("CONFIRMAR ALTERAÇÕES", fontWeight = FontWeight.Black, color = Color.White)
                }
            }
        }
    }

    if (showDatePicker) {
        val datePickerState = rememberDatePickerState(initialSelectedDateMillis = selectedDate)
        DatePickerDialog(
            onDismissRequest = { showDatePicker = false },
            confirmButton = {
                TextButton(onClick = {
                    val utcMillis = datePickerState.selectedDateMillis ?: selectedDate
                    val utcCalendar = Calendar.getInstance(TimeZone.getTimeZone("UTC")).apply { timeInMillis = utcMillis }
                    val localCalendar = Calendar.getInstance().apply {
                        set(utcCalendar.get(Calendar.YEAR), utcCalendar.get(Calendar.MONTH), utcCalendar.get(Calendar.DAY_OF_MONTH), 0, 0, 0)
                        set(Calendar.MILLISECOND, 0)
                    }
                    selectedDate = localCalendar.timeInMillis
                    showDatePicker = false
                }) { Text("OK") }
            }
        ) { DatePicker(state = datePickerState) }
    }
}
