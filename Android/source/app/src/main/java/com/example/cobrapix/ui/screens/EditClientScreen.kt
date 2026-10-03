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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.example.cobrapix.billing.EntitlementResult
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.*
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun EditClientScreen(
    viewModel: MainViewModel,
    clientId: Long,
    onBack: () -> Unit,
) {
    val client by viewModel.getClient(clientId).collectAsStateWithLifecycle(initialValue = null)
    val sale by viewModel.getClientSale(clientId).collectAsStateWithLifecycle(initialValue = null)
    val monthlyCustomer by viewModel.getMonthlyCustomer(clientId).collectAsStateWithLifecycle(initialValue = null)
    
    val hudColors = LocalCobraPixColors.current
    val scope = rememberCoroutineScope()
    val context = LocalContext.current
    
    var name by remember { mutableStateOf("") }
    var phone by remember { mutableStateOf("") }
    var cpf by remember { mutableStateOf("") }
    var address by remember { mutableStateOf("") }
    var observation by remember { mutableStateOf("") }

    var h1 by remember { mutableIntStateOf(540) }
    var h2 by remember { mutableIntStateOf(660) }
    var h3 by remember { mutableIntStateOf(780) }
    var h4 by remember { mutableIntStateOf(900) }
    var h5 by remember { mutableIntStateOf(1020) }
    var h6 by remember { mutableIntStateOf(1140) }
    var h7 by remember { mutableIntStateOf(1260) }

    var showTimePickerFor by remember { mutableIntStateOf(0) }

    LaunchedEffect(client) {
        client?.let {
            name = it.name
            phone = it.phone
            cpf = it.cpf
            address = it.address
            observation = it.observation
        }
    }

    LaunchedEffect(sale, monthlyCustomer) {
        sale?.let {
            h1 = it.hour1; h2 = it.hour2; h3 = it.hour3; h4 = it.hour4; h5 = it.hour5; h6 = it.hour6; h7 = it.hour7
        }
        monthlyCustomer?.let {
            h1 = it.hour1; h2 = it.hour2; h3 = it.hour3; h4 = it.hour4; h5 = it.hour5; h6 = it.hour6; h7 = it.hour7
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { SoloHeader("Ajustar Cadastro", color = hudColors.textPrimary) },
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
                    Text("INFORMAÇÕES DO MEMBRO", style = MaterialTheme.typography.labelSmall, color = hudColors.secondary)
                    Spacer(Modifier.height(16.dp))
                    EditHUDTextField(value = name, label = "Nome completo") { name = it }
                    Spacer(Modifier.height(12.dp))
                    EditHUDTextField(value = phone, label = "WhatsApp") { phone = it }
                    Spacer(Modifier.height(12.dp))
                    EditHUDTextField(value = cpf, label = "CPF") { cpf = it }
                    Spacer(Modifier.height(12.dp))
                    EditHUDTextField(value = address, label = "Endereço") { address = it }
                    Spacer(Modifier.height(12.dp))
                    EditHUDTextField(value = observation, label = "Observações", singleLine = false) { observation = it }
                }

                Spacer(modifier = Modifier.height(24.dp))
                SoloCard(borderColor = hudColors.primary) {
                    Text("HORÁRIOS DE COBRANÇA DESTE CLIENTE", style = MaterialTheme.typography.labelSmall, color = hudColors.primary)
                    Spacer(modifier = Modifier.height(16.dp))
                    
                    val slots = listOf(h1, h2, h3, h4, h5, h6, h7)
                    val labels = listOf("Aviso 1", "Aviso 2", "Aviso 3", "Aviso 4", "Aviso 5", "Aviso 6", "Aviso 7")
                    
                    for (i in 0 until 7 step 2) {
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            TimeSlotHUD(label = labels[i], minutes = slots[i], isActive = true, modifier = Modifier.weight(1f)) { showTimePickerFor = i + 1 }
                            if ((i + 1) < 7) {
                                TimeSlotHUD(label = labels[i+1], minutes = slots[i+1], isActive = true, modifier = Modifier.weight(1f)) { showTimePickerFor = i + 2 }
                            } else {
                                Spacer(Modifier.weight(1f))
                            }
                        }
                        Spacer(modifier = Modifier.height(8.dp))
                    }
                }

                Spacer(modifier = Modifier.height(24.dp))
                Button(
                    onClick = {
                        client?.let {
                            viewModel.updateClient(it.copy(name = name, phone = phone, cpf = cpf, address = address, observation = observation))
                            
                            sale?.let { s ->
                                viewModel.updateSale(s.copy(hour1 = h1, hour2 = h2, hour3 = h3, hour4 = h4, hour5 = h5, hour6 = h6, hour7 = h7))
                            }
                            monthlyCustomer?.let { m ->
                                viewModel.updateMonthlyCustomer(m.copy(hour1 = h1, hour2 = h2, hour3 = h3, hour4 = h4, hour5 = h5, hour6 = h6, hour7 = h7))
                            }
                            
                            onBack()
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary)
                ) {
                    Text("SALVAR ALTERAÇÕES", fontWeight = FontWeight.Black, color = Color.White)
                }
                
                Spacer(modifier = Modifier.height(12.dp))
                val isArchived = client?.isArchived ?: false
                OutlinedButton(
                    onClick = {
                        client?.let {
                            if (isArchived) {
                                scope.launch {
                                    when (viewModel.checkClientLimit()) {
                                        is EntitlementResult.Allowed -> {
                                            viewModel.unarchiveClient(it)
                                            onBack()
                                        }
                                        is EntitlementResult.LimitReached -> {
                                            android.widget.Toast.makeText(context, "Limite do plano gratuito atingido. O plano PRO permite clientes ilimitados.", android.widget.Toast.LENGTH_LONG).show()
                                        }
                                        is EntitlementResult.SubscriptionCheckUnavailable -> {
                                            android.widget.Toast.makeText(context, "Verificando assinatura. Tente novamente em instantes.", android.widget.Toast.LENGTH_LONG).show()
                                        }
                                    }
                                }
                            } else {
                                viewModel.archiveClient(it)
                                onBack()
                            }
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.outlinedButtonColors(
                        contentColor = if (isArchived) hudColors.success else hudColors.warning
                    ),
                    border = androidx.compose.foundation.BorderStroke(1.dp, if (isArchived) hudColors.success else hudColors.warning)
                ) {
                    Text(if (isArchived) "DESARQUIVAR REGISTRO" else "ARQUIVAR REGISTRO")
                }
                
                Spacer(modifier = Modifier.height(12.dp))
                TextButton(
                    onClick = {
                        client?.let {
                            viewModel.deleteClient(it)
                            onBack()
                        }
                    },
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text("EXCLUIR PERMANENTEMENTE", color = hudColors.error, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
                Spacer(modifier = Modifier.height(32.dp))
            }
        }
    }

    if (showTimePickerFor > 0) {
        val currentMin = when(showTimePickerFor) { 1 -> h1; 2 -> h2; 3 -> h3; 4 -> h4; 5 -> h5; 6 -> h6; else -> h7 }
        val timeState = rememberTimePickerState(initialHour = currentMin / 60, initialMinute = currentMin % 60, is24Hour = true)
        
        Dialog(onDismissRequest = { showTimePickerFor = 0 }) {
            SoloCard(borderColor = hudColors.primary) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("HORÁRIO DO CLIENTE", fontWeight = FontWeight.Black, letterSpacing = 2.sp, color = hudColors.primary)
                    Spacer(Modifier.height(24.dp))
                    TimeInput(state = timeState)
                    Spacer(Modifier.height(24.dp))
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                        TextButton(onClick = { showTimePickerFor = 0 }) { Text("CANCELAR", color = hudColors.textSecondary) }
                        Spacer(Modifier.width(8.dp))
                        Button(
                            onClick = {
                                val newMin = timeState.hour * 60 + timeState.minute
                                when(showTimePickerFor) {
                                    1 -> h1 = newMin; 2 -> h2 = newMin; 3 -> h3 = newMin; 4 -> h4 = newMin; 5 -> h5 = newMin; 6 -> h6 = newMin; 7 -> h7 = newMin
                                }
                                showTimePickerFor = 0
                            },
                            colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary)
                        ) { Text("OK") }
                    }
                }
            }
        }
    }
}
