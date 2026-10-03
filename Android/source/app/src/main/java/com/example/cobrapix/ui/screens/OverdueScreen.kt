package com.example.cobrapix.ui.screens

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
import com.example.cobrapix.logic.CurrencyUtils
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OverdueScreen(
    viewModel: MainViewModel,
    onClientClick: (Long) -> Unit
) {
    val overdueItems by viewModel.combinedOverdueItems.collectAsStateWithLifecycle(initialValue = emptyList())
    val hudColors = LocalCobraPixColors.current

    Scaffold(
        topBar = {
            CenterAlignedTopAppBar(
                title = { 
                    SoloHeader("Zona de Perigo", color = hudColors.error)
                },
                colors = TopAppBarDefaults.centerAlignedTopAppBarColors(
                    containerColor = Color.Transparent,
                    titleContentColor = hudColors.error
                )
            )
        },
        containerColor = Color.Transparent
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize()) {
            SoloBackground()
            
            Column(modifier = Modifier.padding(padding).padding(16.dp)) {
                val totalValue = overdueItems.sumOf { it.value }
                
                SoloCard(borderColor = hudColors.error) {
                    Text("MISSÕES EM FALHA DETECTADAS", color = hudColors.error, fontWeight = FontWeight.Black, fontSize = 11.sp, letterSpacing = 1.sp)
                    Text(
                        "PENALIDADE TOTAL: ${CurrencyUtils.formatCurrency(totalValue)}",
                        fontWeight = FontWeight.Black,
                        color = hudColors.textPrimary,
                        fontSize = 20.sp
                    )
                    Text("${overdueItems.size} COBRANÇAS EM ATRASO", style = MaterialTheme.typography.labelSmall, color = hudColors.error.copy(0.7f))
                }
                
                Spacer(modifier = Modifier.height(24.dp))
                SoloHeader("Log de Pendências", color = hudColors.textPrimary.copy(0.4f))
                Spacer(modifier = Modifier.height(16.dp))
                
                if (overdueItems.isEmpty()) {
                    Box(modifier = Modifier.fillMaxSize(), contentAlignment = androidx.compose.ui.Alignment.Center) {
                        Text("Nenhuma pendência crítica no momento.", color = hudColors.textPrimary.copy(0.3f))
                    }
                } else {
                    LazyColumn(modifier = Modifier.fillMaxSize()) {
                        items(overdueItems) { item ->
                            PayableListItem(item, onClientClick)
                        }
                    }
                }
            }
        }
    }
}
