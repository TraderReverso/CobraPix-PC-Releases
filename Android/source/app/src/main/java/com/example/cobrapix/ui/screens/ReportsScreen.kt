package com.example.cobrapix.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.automirrored.filled.TrendingUp
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.PieChart
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.LocalCobraPixColors
import java.util.Calendar

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ReportsScreen(
    viewModel: MainViewModel,
    onBack: () -> Unit,
    onNavigateToCashFlow: () -> Unit
) {
    val hudColors = LocalCobraPixColors.current
    val context = LocalContext.current
    
    val sold by viewModel.totalSold.collectAsStateWithLifecycle(initialValue = 0L)
    val received by viewModel.totalReceived.collectAsStateWithLifecycle(initialValue = 0L)
    val toReceive by viewModel.totalToReceive.collectAsStateWithLifecycle(initialValue = 0L)
    val overdue by viewModel.totalOverdueValue.collectAsStateWithLifecycle(initialValue = 0L)
    val receivedToday by viewModel.receivedToday.collectAsStateWithLifecycle(initialValue = 0L)

    val currentYear = Calendar.getInstance().get(Calendar.YEAR)
    val forecastThisYear by viewModel.getAnnualForecast(currentYear).collectAsStateWithLifecycle(initialValue = 0L)
    val forecastNextYear by viewModel.getAnnualForecast(currentYear + 1).collectAsStateWithLifecycle(initialValue = 0L)

    Scaffold(
        topBar = {
            TopAppBar(
                title = { SoloHeader("Monitor Financeiro", color = hudColors.textPrimary) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, "Voltar", tint = hudColors.textPrimary)
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
                // RESUMO GERAL
                SoloCard(borderColor = hudColors.primary) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Default.PieChart, null, tint = hudColors.secondary)
                        Spacer(Modifier.width(8.dp))
                        Text("RESUMO PATRIMONIAL", fontWeight = FontWeight.Black, letterSpacing = 1.sp, color = hudColors.textPrimary)
                    }
                    Spacer(Modifier.height(24.dp))
                    SummaryRow("Vendas Totais", sold ?: 0L, hudColors.textPrimary)
                    SummaryRow("Total Recebido", received ?: 0L, hudColors.success)
                    SummaryRow("Total Pendente", toReceive ?: 0L, hudColors.warning)
                    SummaryRow("Total em Atraso", overdue ?: 0L, hudColors.error)
                }

                Spacer(modifier = Modifier.height(24.dp))

                // ENTRADAS DO DIA E FLUXO
                SoloCard(borderColor = hudColors.success) {
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                        Text("FLUXO DE CAIXA", fontWeight = FontWeight.Bold, color = hudColors.success, fontSize = 12.sp)
                        TextButton(onClick = onNavigateToCashFlow) {
                            Text("VER HISTÓRICO", fontSize = 10.sp, fontWeight = FontWeight.Black, color = hudColors.secondary)
                            Icon(Icons.AutoMirrored.Filled.ArrowForward, null, modifier = Modifier.size(14.dp), tint = hudColors.secondary)
                        }
                    }
                    Spacer(Modifier.height(16.dp))
                    SummaryRow("Entradas de Hoje", receivedToday, hudColors.success)
                }

                Spacer(modifier = Modifier.height(24.dp))

                // PROJEÇÃO ANUAL
                SoloCard(borderColor = hudColors.secondary) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.AutoMirrored.Filled.TrendingUp, null, tint = hudColors.primary)
                        Spacer(Modifier.width(8.dp))
                        Text("PROJEÇÃO DE RECEBIMENTOS", fontWeight = FontWeight.Black, letterSpacing = 1.sp, color = hudColors.textPrimary)
                    }
                    Spacer(Modifier.height(16.dp))
                    SummaryRow("Restante em $currentYear", forecastThisYear, hudColors.textPrimary)
                    SummaryRow("Previsto para ${currentYear + 1}", forecastNextYear, hudColors.textPrimary)
                }
                
                Spacer(modifier = Modifier.height(32.dp))
                
                Button(
                    onClick = { viewModel.exportToCSV(context) },
                    modifier = Modifier.fillMaxWidth().height(56.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = hudColors.surfaceVariant)
                ) {
                    Icon(Icons.Default.Download, null, tint = hudColors.secondary)
                    Spacer(Modifier.width(8.dp))
                    Text("EXPORTAR PLANILHA COMPLETA", fontWeight = FontWeight.Black, color = hudColors.textPrimary)
                }
                
                Spacer(modifier = Modifier.height(32.dp))
            }
        }
    }
}
