package com.example.cobrapix.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.example.cobrapix.data.MonthlyCustomer
import com.example.cobrapix.logic.CurrencyUtils
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MensalistasScreen(
    viewModel: MainViewModel,
    onClientClick: (Long) -> Unit
) {
    val activeMensalistas by viewModel.activeMonthlyCustomers.collectAsStateWithLifecycle(initialValue = emptyList())
    val clients by viewModel.activeClients.collectAsStateWithLifecycle(initialValue = emptyList())
    val expectedRevenue by viewModel.monthlyRevenueExpected.collectAsStateWithLifecycle(initialValue = 0L)
    val receivedRevenue by viewModel.monthlyRevenueReceived.collectAsStateWithLifecycle(initialValue = 0L)
    
    val hudColors = LocalCobraPixColors.current

    Scaffold(
        topBar = {
            TopAppBar(
                title = { SoloHeader("Contratos Mensais", color = hudColors.primaryVariant) },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.Transparent)
            )
        },
        containerColor = Color.Transparent
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize()) {
            SoloBackground()
            
            LazyColumn(modifier = Modifier.padding(padding).padding(16.dp)) {
                item {
                    SoloCard(borderColor = hudColors.primaryVariant) {
                        Text("RESUMO MENSAL", style = MaterialTheme.typography.labelSmall, color = hudColors.secondary, letterSpacing = 1.sp)
                        Spacer(Modifier.height(12.dp))
                        SummaryRow("RECEITA PREVISTA:", expectedRevenue ?: 0L, hudColors.textPrimary)
                        SummaryRow("VALOR RECEBIDO:", receivedRevenue ?: 0L, hudColors.success)
                        
                        Spacer(Modifier.height(16.dp))
                        val expected = expectedRevenue ?: 0L
                        val received = receivedRevenue ?: 0L
                        
                        SoloProgressBar(
                            progress = if (expected > 0L) received.toFloat() / expected.toFloat() else 0f,
                            label = "Progresso Mensal",
                            valueText = "",
                            color = hudColors.primaryVariant
                        )
                    }
                    Spacer(Modifier.height(32.dp))
                    SoloHeader("Contratos Ativos", color = hudColors.textPrimary.copy(0.5f))
                    Spacer(Modifier.height(12.dp))
                }

                if (activeMensalistas.isEmpty()) {
                    item {
                        Text("Nenhum contrato mensal ativo.", color = hudColors.textPrimary.copy(0.3f), modifier = Modifier.padding(vertical = 16.dp))
                    }
                }

                items(activeMensalistas) { customer ->
                    val client = clients.find { it.id == customer.clientId }
                    MonthlyCustomerItemHUD(customer, client?.name ?: "Cliente", onClientClick)
                }
            }
        }
    }
}

@Composable
fun MonthlyCustomerItemHUD(customer: MonthlyCustomer, clientName: String, onClick: (Long) -> Unit) {
    val hudColors = LocalCobraPixColors.current
    SoloCard(
        modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
        borderColor = hudColors.border
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clickable { onClick(customer.clientId) },
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = androidx.compose.ui.Alignment.CenterVertically
        ) {
            Column {
                Text(clientName.uppercase(), fontWeight = FontWeight.Black, color = hudColors.textPrimary, letterSpacing = 1.sp)
                Text("Vencimento todo dia ${customer.dueDay}", style = MaterialTheme.typography.labelSmall, color = hudColors.secondary)
                Text(CurrencyUtils.formatCurrency(customer.monthlyValue), fontWeight = FontWeight.Bold, color = hudColors.primaryVariant)
            }
            StatusBadge(status = com.example.cobrapix.data.InstallmentStatus.PENDING)
        }
    }
}
