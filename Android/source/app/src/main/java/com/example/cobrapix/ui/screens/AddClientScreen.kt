package com.example.cobrapix.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.example.cobrapix.logic.CurrencyUtils
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.*
import java.util.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AddClientScreen(
    viewModel: MainViewModel,
    onBack: () -> Unit
) {
    var name by remember { mutableStateOf("") }
    var phone by remember { mutableStateOf("") }
    var address by remember { mutableStateOf("") }
    
    var rawAmount by remember { mutableStateOf("") }
    val formattedAmount = remember(rawAmount) {
        val value = rawAmount.toLongOrNull() ?: 0L
        CurrencyUtils.formatCurrency(value)
    }

    var installmentCount by remember { mutableStateOf("1") }
    var billingType by remember { mutableIntStateOf(0) } // 0: Parcelado, 1: Mensalista
    var useFifthBusinessDay by remember { mutableStateOf(value = false) }
    
    var h1 by remember { mutableIntStateOf(540) } // 09:00
    var h2 by remember { mutableIntStateOf(720) } // 12:00
    var showTimePickerFor by remember { mutableIntStateOf(0) }

    val calendar = Calendar.getInstance().apply {
        set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0)
    }
    var selectedDate by remember { mutableLongStateOf(calendar.timeInMillis) }
    var showDatePicker by remember { mutableStateOf(false) }
    
    val hudColors = LocalCobraPixColors.current

    Scaffold(
        topBar = {
            TopAppBar(
                title = { SoloHeader("Novo Protocolo", color = hudColors.textPrimary) },
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
            
            Column(
                modifier = Modifier
                    .padding(padding)
                    .fillMaxSize()
                    .padding(16.dp)
                    .verticalScroll(rememberScrollState())
            ) {
                SoloCard(borderColor = hudColors.secondary) {
                    Text("INFORMAÇÕES DO CLIENTE", style = MaterialTheme.typography.labelSmall, color = hudColors.secondary)
                    Spacer(Modifier.height(16.dp))
                    EditHUDTextField(value = name, label = "Nome completo") { name = it }
                    Spacer(Modifier.height(12.dp))
                    EditHUDTextField(value = phone, label = "WhatsApp") { phone = it }
                    Spacer(Modifier.height(12.dp))
                    EditHUDTextField(value = address, label = "Endereço (Opcional)") { address = it }
                }
                
                Spacer(modifier = Modifier.height(24.dp))
                SoloCard(borderColor = hudColors.primary) {
                    Text("DADOS FINANCEIROS", style = MaterialTheme.typography.labelSmall, color = hudColors.primary)
                    Spacer(Modifier.height(16.dp))
                    EditHUDTextField(value = formattedAmount, label = "Valor exato da Parcela") { input ->
                        val digits = input.filter { it.isDigit() }
                        if (digits.length <= 12) rawAmount = digits
                    }
                    Spacer(Modifier.height(16.dp))
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        FilterChipHUD("Parcelado", billingType == 0) { billingType = 0 }
                        FilterChipHUD("Mensalista", billingType == 1) { billingType = 1 }
                    }
                    Spacer(modifier = Modifier.height(16.dp))
                    if (billingType == 0) {
                        EditHUDTextField(value = installmentCount, label = "Qtd Parcelas") { installmentCount = it.filter { c -> c.isDigit() } }
                        Spacer(modifier = Modifier.height(12.dp))
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Checkbox(checked = useFifthBusinessDay, onCheckedChange = { useFifthBusinessDay = it })
                            Text("Usar 5º Dia Útil", color = hudColors.textPrimary, fontSize = 14.sp)
                        }
                    }
                    
                    if (!useFifthBusinessDay) {
                        Spacer(modifier = Modifier.height(16.dp))
                        Button(
                            onClick = { showDatePicker = true }, 
                            modifier = Modifier.fillMaxWidth(),
                            colors = ButtonDefaults.buttonColors(containerColor = hudColors.surfaceVariant),
                            border = androidx.compose.foundation.BorderStroke(1.dp, hudColors.primary.copy(0.4f))
                        ) {
                            val prefix = if (billingType == 1) "Dia do Vencimento: " else "Primeiro vencimento: "
                            Text(
                                "$prefix${java.text.SimpleDateFormat("dd/MM/yyyy", Locale.getDefault()).format(Date(selectedDate))}",
                                color = hudColors.textPrimary
                            )
                        }
                    } else {
                        Spacer(modifier = Modifier.height(8.dp))
                        Text("Vencimento automático no 5º dia útil.", color = hudColors.secondary, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                    }
                }

                Spacer(modifier = Modifier.height(24.dp))
                SoloCard(borderColor = hudColors.primaryVariant) {
                    Text("RITMO DE COBRANÇA (OPCIONAL)", style = MaterialTheme.typography.labelSmall, color = hudColors.primaryVariant)
                    Spacer(Modifier.height(16.dp))
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        TimeSlotHUD(label = "Aviso 1", minutes = h1, isActive = true, modifier = Modifier.weight(1f)) { showTimePickerFor = 1 }
                        TimeSlotHUD(label = "Aviso 2", minutes = h2, isActive = true, modifier = Modifier.weight(1f)) { showTimePickerFor = 2 }
                    }
                    Spacer(Modifier.height(8.dp))
                    Text("Outros horários seguirão o padrão global.", style = MaterialTheme.typography.labelSmall, color = hudColors.textSecondary)
                }

                Spacer(modifier = Modifier.height(32.dp))
                Button(
                    onClick = {
                        val valueCents = rawAmount.toLongOrNull() ?: 0L
                        val customHours = listOf(h1, h2)
                        if (billingType == 0) {
                            val count = installmentCount.toIntOrNull() ?: 1
                            viewModel.addClientWithSale(name, phone, "", address, valueCents, count, selectedDate, "", useFifthBusinessDay, customHours)
                        } else {
                            val cal = Calendar.getInstance().apply { timeInMillis = selectedDate }
                            val day = cal[Calendar.DAY_OF_MONTH]
                            viewModel.addMonthlyCustomer(name, phone, "", address, valueCents, day, selectedDate, "", customHours)
                        }
                        onBack()
                    },
                    modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary),
                    enabled = name.isNotBlank() && phone.isNotBlank() && rawAmount.isNotBlank()
                ) {
                    Text("INICIAR PROTOCOLO", fontWeight = FontWeight.Black, color = Color.White)
                }
                Spacer(modifier = Modifier.height(32.dp))
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
                        set(utcCalendar[Calendar.YEAR], utcCalendar[Calendar.MONTH], utcCalendar[Calendar.DAY_OF_MONTH], 0, 0, 0)
                        set(Calendar.MILLISECOND, 0)
                    }
                    selectedDate = localCalendar.timeInMillis
                    showDatePicker = false
                }) { Text("OK") }
            }
        ) { DatePicker(state = datePickerState) }
    }

    if (showTimePickerFor > 0) {
        val currentMin = if (showTimePickerFor == 1) h1 else h2
        val timeState = rememberTimePickerState(initialHour = currentMin / 60, initialMinute = currentMin % 60, is24Hour = true)
        
        Dialog(onDismissRequest = { showTimePickerFor = 0 }) {
            SoloCard(borderColor = hudColors.primaryVariant) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("DEFINIR HORÁRIO", fontWeight = FontWeight.Black, color = hudColors.primaryVariant)
                    Spacer(Modifier.height(16.dp))
                    TimeInput(state = timeState)
                    Spacer(Modifier.height(24.dp))
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                        TextButton(onClick = { showTimePickerFor = 0 }) { Text("CANCELAR", color = hudColors.textSecondary) }
                        Button(
                            onClick = {
                                val min = (timeState.hour * 60) + timeState.minute
                                if (showTimePickerFor == 1) h1 = min else h2 = min
                                showTimePickerFor = 0
                            },
                            colors = ButtonDefaults.buttonColors(containerColor = hudColors.primaryVariant)
                        ) { Text("OK") }
                    }
                }
            }
        }
    }
}
