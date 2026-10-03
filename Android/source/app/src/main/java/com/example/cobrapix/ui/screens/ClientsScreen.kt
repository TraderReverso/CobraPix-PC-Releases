package com.example.cobrapix.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.*
import androidx.compose.material3.TabRowDefaults.tabIndicatorOffset
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.example.cobrapix.data.Client
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ClientsScreen(
    viewModel: MainViewModel,
    onClientClick: (Long) -> Unit
) {
    var searchQuery by remember { mutableStateOf("") }
    var selectedTab by remember { mutableIntStateOf(0) }
    val tabs = listOf("ATIVOS", "QUITADOS", "ARQUIVADOS")
    
    val hudColors = LocalCobraPixColors.current

    val activeClients by viewModel.activeOperationalClients.collectAsStateWithLifecycle(initialValue = emptyList())
    val settledClients by viewModel.settledClients.collectAsStateWithLifecycle(initialValue = emptyList())
    val archivedClients by viewModel.archivedClients.collectAsStateWithLifecycle(initialValue = emptyList())

    val displayedClients = when(selectedTab) {
        0 -> activeClients
        1 -> settledClients
        else -> archivedClients
    }
    val filteredClients = if (searchQuery.isBlank()) {
        displayedClients
    } else {
        displayedClients.filter { it.name.contains(searchQuery, ignoreCase = true) }
    }

    Scaffold(
        topBar = {
            Column {
                CenterAlignedTopAppBar(
                    title = { SoloHeader("Registro de Membros", color = hudColors.textPrimary) },
                    colors = TopAppBarDefaults.centerAlignedTopAppBarColors(containerColor = Color.Transparent)
                )
                TabRow(
                    selectedTabIndex = selectedTab,
                    containerColor = Color.Transparent,
                    contentColor = hudColors.primary,
                    indicator = { tabPositions ->
                        TabRowDefaults.SecondaryIndicator(
                            Modifier.tabIndicatorOffset(tabPositions[selectedTab]),
                            color = hudColors.primary
                        )
                    }
                ) {
                    tabs.forEachIndexed { index, title ->
                        Tab(
                            selected = selectedTab == index,
                            onClick = { selectedTab = index },
                            text = { Text(title, fontSize = 11.sp, fontWeight = FontWeight.Black) }
                        )
                    }
                }
            }
        },
        containerColor = Color.Transparent
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize()) {
            SoloBackground()
            
            Column(modifier = Modifier.padding(padding).padding(16.dp)) {
                OutlinedTextField(
                    value = searchQuery,
                    onValueChange = { searchQuery = it },
                    label = { Text("Pesquisar cliente...") },
                    modifier = Modifier.fillMaxWidth(),
                    leadingIcon = { Icon(Icons.Default.Search, contentDescription = null, tint = hudColors.secondary) },
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = hudColors.secondary,
                        unfocusedBorderColor = hudColors.border,
                        focusedLabelColor = hudColors.secondary,
                        unfocusedLabelColor = hudColors.textSecondary,
                        focusedTextColor = hudColors.textPrimary,
                        unfocusedTextColor = hudColors.textPrimary
                    )
                )
                Spacer(modifier = Modifier.height(16.dp))
                if (filteredClients.isEmpty()) {
                    Box(modifier = Modifier.fillMaxSize(), contentAlignment = androidx.compose.ui.Alignment.Center) {
                        Text("Nenhum registro encontrado.", color = hudColors.textPrimary.copy(0.3f))
                    }
                } else {
                    LazyColumn(
                        modifier = Modifier.fillMaxSize(),
                        contentPadding = PaddingValues(bottom = 16.dp)
                    ) {
                        items(filteredClients, key = { it.id }) { client ->
                            ClientListItemHUD(client, viewModel, onClientClick)
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun ClientListItemHUD(client: Client, viewModel: MainViewModel, onClick: (Long) -> Unit) {
    val hudColors = LocalCobraPixColors.current
    val score by viewModel.getClientScore(client.id).collectAsStateWithLifecycle(initialValue = null)
    
    SoloCard(
        modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
        borderColor = hudColors.border
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = androidx.compose.ui.Alignment.CenterVertically
        ) {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.weight(1f)) {
                score?.let { RankBadge(it) }
                Spacer(Modifier.width(12.dp))
                Column {
                    Text(client.name.uppercase(), fontWeight = FontWeight.Black, color = hudColors.textPrimary, letterSpacing = 1.sp)
                    Text("WhatsApp: ${client.phone}", style = MaterialTheme.typography.labelSmall, color = hudColors.secondary)
                }
            }
            IconButton(onClick = { onClick(client.id) }) {
                Icon(Icons.Default.Search, contentDescription = "Ver Perfil", tint = hudColors.primary)
            }
        }
    }
}
