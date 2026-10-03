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
import com.example.cobrapix.logic.CurrencyUtils
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun EditMonthlyContractScreen(
    viewModel: MainViewModel,
    clientId: Long,
    onBack: () -> Unit
) {
    val customer by viewModel.getMonthlyCustomer(clientId).collectAsStateWithLifecycle(initialValue = null)
    val hudColors = LocalCobraPixColors.current
    
    var rawAmount by remember { mutableStateOf("") }
    val formattedAmount = remember(rawAmount) {
        val value = rawAmount.toLongOrNull() ?: 0L
        CurrencyUtils.formatCurrency(value)
    }
    var dueDay by remember { mutableStateOf("") }

    LaunchedEffect(customer) {
        customer?.let {
            rawAmount = it.monthlyValue.toString()
            dueDay = it.dueDay.toString()
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { SoloHeader("Ajustar Contrato", color = hudColors.textPrimary) },
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
                SoloCard(borderColor = hudColors.primaryVariant) {
                    Text("DADOS DO CONTRATO MENSAL", style = MaterialTheme.typography.labelSmall, color = hudColors.primaryVariant)
                    Spacer(Modifier.height(16.dp))
                    EditHUDTextField(
                        value = formattedAmount,
                        onValueChange = { input ->
                            val digits = input.filter { it.isDigit() }
                            if (digits.length <= 12) rawAmount = digits
                        },
                        label = "Valor Mensal"
                    )
                    Spacer(modifier = Modifier.height(16.dp))
                    EditHUDTextField(
                        value = dueDay,
                        onValueChange = { input ->
                            val digits = input.filter { it.isDigit() }
                            if (digits.length <= 2) dueDay = digits
                        },
                        label = "Dia do Vencimento (Fixo)"
                    )
                }

                Spacer(modifier = Modifier.height(32.dp))
                Button(
                    onClick = {
                        customer?.let {
                            val valueCents = rawAmount.toLongOrNull() ?: it.monthlyValue
                            val day = dueDay.toIntOrNull() ?: it.dueDay
                            viewModel.updateMonthlyCustomer(it.copy(
                                monthlyValue = valueCents,
                                dueDay = day
                            ))
                            onBack()
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.buttonColors(containerColor = hudColors.primaryVariant)
                ) {
                    Text("SALVAR ALTERAÇÕES", fontWeight = FontWeight.Black, color = Color.White)
                }
            }
        }
    }
}
