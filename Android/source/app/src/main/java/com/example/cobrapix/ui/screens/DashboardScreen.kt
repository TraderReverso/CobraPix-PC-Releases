package com.example.cobrapix.ui.screens

import androidx.compose.animation.*
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.GenericShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.BarChart
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.example.cobrapix.billing.EntitlementResult
import com.example.cobrapix.logic.CurrencyUtils
import com.example.cobrapix.ui.MainViewModel
import kotlinx.coroutines.launch
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DashboardScreen(
    viewModel: MainViewModel,
    onAddClient: () -> Unit,
    onClientClick: (Long) -> Unit,
    onReportsClick: () -> Unit
) {
    val totalSold by viewModel.totalSold.collectAsStateWithLifecycle(initialValue = 0L)
    val totalReceived by viewModel.totalReceived.collectAsStateWithLifecycle(initialValue = 0L)
    val totalToReceive by viewModel.totalToReceive.collectAsStateWithLifecycle(initialValue = 0L)
    
    val mExpected by viewModel.monthlyRevenueExpected.collectAsStateWithLifecycle(initialValue = 0L)
    val mReceived by viewModel.monthlyRevenueReceived.collectAsStateWithLifecycle(initialValue = 0L)
    
    val todayItems by viewModel.combinedTodayItems.collectAsStateWithLifecycle(initialValue = emptyList())
    val overdueItems by viewModel.combinedOverdueItems.collectAsStateWithLifecycle(initialValue = emptyList())
    
    val activeClientsCount by viewModel.activeClientsCount.collectAsStateWithLifecycle(initialValue = 0)
    val paidCount by viewModel.paidInstallmentsCount.collectAsStateWithLifecycle(initialValue = 0)
    val pendingCount by viewModel.pendingInstallmentsCount.collectAsStateWithLifecycle(initialValue = 0)

    val todayTotal = todayItems.sumOf { it.value }
    val overdueTotal = overdueItems.sumOf { it.value }
    
    val hudColors = LocalCobraPixColors.current
    val scope = rememberCoroutineScope()
    val context = androidx.compose.ui.platform.LocalContext.current

    Scaffold(
        topBar = {
            CenterAlignedTopAppBar(
                title = { 
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(
                            "COBRAPIX", 
                            fontWeight = FontWeight.Black, 
                            letterSpacing = 8.sp,
                            color = hudColors.primary
                        )
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Box(modifier = Modifier.size(6.dp).background(hudColors.secondary, CircleShape))
                            Spacer(Modifier.width(6.dp))
                            Text(
                                "SISTEMA FINANCEIRO ATIVO", 
                                style = MaterialTheme.typography.labelSmall,
                                color = hudColors.secondary,
                                letterSpacing = 1.sp
                            )
                        }
                    }
                },
                actions = { IconButton(onClick = onReportsClick) { Icon(Icons.Default.BarChart, "Relatórios", tint = hudColors.secondary) } },
                colors = TopAppBarDefaults.centerAlignedTopAppBarColors(
                    containerColor = Color.Transparent,
                    titleContentColor = hudColors.primary
                )
            )
        },
        floatingActionButton = {
            FloatingActionButton(
                onClick = {
                    scope.launch {
                        when (viewModel.checkClientLimit()) {
                            is EntitlementResult.Allowed -> onAddClient()
                            is EntitlementResult.LimitReached -> {
                                android.widget.Toast.makeText(context, "Limite do plano gratuito (10 clientes) atingido. O plano PRO permite clientes ilimitados.", android.widget.Toast.LENGTH_LONG).show()
                            }
                            is EntitlementResult.SubscriptionCheckUnavailable -> {
                                android.widget.Toast.makeText(context, "Verificando assinatura com a Play Store. Tente novamente em instantes.", android.widget.Toast.LENGTH_LONG).show()
                            }
                        }
                    }
                },
                containerColor = hudColors.primary,
                contentColor = hudColors.textPrimary,
                shape = GenericShape { size, _ ->
                    val cut = 32f
                    moveTo(cut, 0f); lineTo(size.width, 0f); lineTo(size.width, size.height - cut)
                    lineTo(size.width - cut, size.height); lineTo(0f, size.height); lineTo(0f, cut); close()
                }
            ) {
                Icon(Icons.Default.Add, contentDescription = "Novo Cadastro")
            }
        },
        containerColor = Color.Transparent
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize()) {
            SoloBackground()
            
            var visible by remember { mutableStateOf(false) }
            LaunchedEffect(Unit) { visible = true }

            AnimatedVisibility(
                visible = visible,
                enter = fadeIn(tween(1000)) + slideInVertically(tween(1000)) { it / 10 }
            ) {
                LazyColumn(
                    modifier = Modifier
                        .padding(padding)
                        .fillMaxSize()
                        .padding(horizontal = 16.dp)
                ) {
                    item {
                        Spacer(modifier = Modifier.height(16.dp))
                        MainStatusHUD(
                            sold = totalSold ?: 0L,
                            received = totalReceived ?: 0L,
                            toReceive = totalToReceive ?: 0L,
                            today = todayTotal,
                            overdue = overdueTotal,
                            clientsCount = activeClientsCount,
                            paidCount = paidCount,
                            pendingCount = pendingCount,
                            mExpected = mExpected ?: 0L,
                            mReceived = mReceived ?: 0L
                        )
                        Spacer(modifier = Modifier.height(32.dp))
                        SoloHeader("Cobranças de Hoje", color = hudColors.secondary)
                        Spacer(modifier = Modifier.height(12.dp))
                    }

                    if (todayItems.isEmpty()) {
                        item {
                            Text(
                                "Nenhuma missão pendente para hoje.", 
                                color = hudColors.textPrimary.copy(0.3f),
                                modifier = Modifier.padding(vertical = 24.dp)
                            )
                        }
                    }

                    items(todayItems, key = { it.id }) { item ->
                        PayableListItem(item, onClientClick)
                    }
                    
                    item { Spacer(modifier = Modifier.height(32.dp)) }
                }
            }
        }
    }
}

@Composable
fun MainStatusHUD(
    sold: Long, received: Long, toReceive: Long, 
    today: Long, overdue: Long, 
    clientsCount: Int, paidCount: Int, pendingCount: Int,
    mExpected: Long, mReceived: Long
) {
    val colors = LocalCobraPixColors.current
    SoloCard(borderColor = if (overdue > 0) colors.error else colors.primary) {
        if (overdue > 0) {
            Text(
                "ALERTA DE SISTEMA: PENDÊNCIAS DETECTADAS",
                color = colors.error,
                fontWeight = FontWeight.Black,
                fontSize = 10.sp,
                letterSpacing = 1.sp
            )
            Text(
                "EM ATRASO: ${CurrencyUtils.formatCurrency(overdue)}",
                color = colors.error,
                fontWeight = FontWeight.ExtraBold,
                fontSize = 22.sp
            )
            Spacer(modifier = Modifier.height(16.dp))
        }

        Text("STATUS DA OPERAÇÃO", style = MaterialTheme.typography.labelSmall, color = colors.secondary, letterSpacing = 2.sp)
        Spacer(modifier = Modifier.height(12.dp))
        
        SoloProgressBar(
            progress = if (sold > 0) received.toFloat() / sold else 0f,
            label = "SALDO EM OPERAÇÃO",
            valueText = "${CurrencyUtils.formatCurrency(received)} / ${CurrencyUtils.formatCurrency(sold)}",
            color = colors.secondary
        )
        
        SoloProgressBar(
            progress = if (mExpected > 0) mReceived.toFloat() / mExpected else 0f,
            label = "RECEITA MENSAL",
            valueText = "${CurrencyUtils.formatCurrency(mReceived)} / ${CurrencyUtils.formatCurrency(mExpected)}",
            color = colors.primary
        )

        Spacer(modifier = Modifier.height(16.dp))
        SummaryRow("A RECEBER (TOTAL):", toReceive, colors.primaryVariant)
        SummaryRow("VALOR PARA HOJE:", today, colors.secondary)

        Spacer(modifier = Modifier.height(16.dp))
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            MetricItem("Clientes", clientsCount.toString())
            MetricItem("Pagas", paidCount.toString())
            MetricItem("Abertas", pendingCount.toString())
        }
    }
}
