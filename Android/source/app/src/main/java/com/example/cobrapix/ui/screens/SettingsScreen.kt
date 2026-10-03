package com.example.cobrapix.ui.screens

import android.Manifest
import android.app.AlarmManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Audiotrack
import androidx.compose.material.icons.filled.Backup
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.biometric.BiometricManager
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.*
import kotlinx.coroutines.launch
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    viewModel: MainViewModel,
    onBack: () -> Unit,
    onNavigateToPro: () -> Unit
) {
    val pixKey by viewModel.settings.pixKey.collectAsStateWithLifecycle(initialValue = "")
    val receiverName by viewModel.settings.receiverName.collectAsStateWithLifecycle(initialValue = "")
    val city by viewModel.settings.city.collectAsStateWithLifecycle(initialValue = "")
    val soundUri by viewModel.settings.notificationSoundUri.collectAsStateWithLifecycle(initialValue = null)
    
    val templateNormal by viewModel.settings.messageTemplate.collectAsStateWithLifecycle(initialValue = "")
    val templateOverdue by viewModel.settings.messageTemplateOverdue.collectAsStateWithLifecycle(initialValue = "")
    
    val confirmTemplatePartial by viewModel.settings.confirmMessagePartialInstallment.collectAsStateWithLifecycle(initialValue = "")

    val templateMonthly by viewModel.settings.messageTemplateMensalista.collectAsStateWithLifecycle(initialValue = "")
    val templateMonthlyOverdue by viewModel.settings.messageTemplateMonthlyOverdue.collectAsStateWithLifecycle(initialValue = "")

    val h1 by viewModel.settings.hourSlot1.collectAsStateWithLifecycle(initialValue = 540)
    val h2 by viewModel.settings.hourSlot2.collectAsStateWithLifecycle(initialValue = 660)
    val h3 by viewModel.settings.hourSlot3.collectAsStateWithLifecycle(initialValue = 780)
    val h4 by viewModel.settings.hourSlot4.collectAsStateWithLifecycle(initialValue = 900)
    val h5 by viewModel.settings.hourSlot5.collectAsStateWithLifecycle(initialValue = 1020)
    val h6 by viewModel.settings.hourSlot6.collectAsStateWithLifecycle(initialValue = 1140)
    val h7 by viewModel.settings.hourSlot7.collectAsStateWithLifecycle(initialValue = 1260)
    
    val themeMode by viewModel.settings.themeMode.collectAsStateWithLifecycle(initialValue = "DARK")
    val isBiometricEnabled by viewModel.settings.isBiometricEnabled.collectAsStateWithLifecycle(initialValue = false)

    // Precisamos de uma referência global pro Dialog atualizar o editHX correto
    // Vou usar mutableIntState global para a tela de settings ao invés de local no card
    var editH1 by remember(h1) { mutableIntStateOf(h1) }
    var editH2 by remember(h2) { mutableIntStateOf(h2) }
    var editH3 by remember(h3) { mutableIntStateOf(h3) }
    var editH4 by remember(h4) { mutableIntStateOf(h4) }
    var editH5 by remember(h5) { mutableIntStateOf(h5) }
    var editH6 by remember(h6) { mutableIntStateOf(h6) }
    var editH7 by remember(h7) { mutableIntStateOf(h7) }
    
    val scope = rememberCoroutineScope()
    val hudColors = LocalCobraPixColors.current
    val context = androidx.compose.ui.platform.LocalContext.current

    var showTimePickerFor by remember { mutableIntStateOf(0) }

    val pickAudioLauncher = rememberLauncherForActivityResult(contract = ActivityResultContracts.OpenDocument()) { uri ->
        uri?.let {
            try { context.contentResolver.takePersistableUriPermission(it, Intent.FLAG_GRANT_READ_URI_PERMISSION) } catch (e: Exception) { e.printStackTrace() }
            scope.launch { viewModel.settings.saveNotificationSoundUri(it.toString()) }
        }
    }
    val exportBackupLauncher = rememberLauncherForActivityResult(contract = ActivityResultContracts.CreateDocument("application/octet-stream")) { uri -> uri?.let { viewModel.exportBackup(it) } }
    val importBackupLauncher = rememberLauncherForActivityResult(contract = ActivityResultContracts.OpenDocument()) { uri -> uri?.let { viewModel.importBackup(it) } }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { SoloHeader("Painel de Controle", color = hudColors.textPrimary) },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.Transparent)
            )
        },
        containerColor = Color.Transparent
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize()) {
            SoloBackground()
            
            Column(
                modifier = Modifier.padding(padding).fillMaxSize().padding(16.dp).verticalScroll(rememberScrollState())
            ) {
                // ACESSO PRO
                SoloCard(borderColor = hudColors.warning) {
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text("COBRAPIX PRO", fontWeight = FontWeight.Black, color = hudColors.warning, fontSize = 14.sp)
                            Text("Clientes ilimitados", color = hudColors.textSecondary, fontSize = 11.sp)
                        }
                        Button(
                            onClick = onNavigateToPro,
                            colors = ButtonDefaults.buttonColors(containerColor = hudColors.warning)
                        ) {
                            Text("VER PLANOS", color = Color.Black, fontWeight = FontWeight.Bold, fontSize = 10.sp)
                        }
                    }
                }
                
                Spacer(modifier = Modifier.height(24.dp))
                
                SoloCard(borderColor = hudColors.secondary) {
                    Text("APARÊNCIA DO SISTEMA", fontWeight = FontWeight.Bold, color = hudColors.secondary, fontSize = 12.sp)
                    Spacer(modifier = Modifier.height(12.dp))
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                        Text("Tema do Aplicativo", color = hudColors.textPrimary)
                        Row {
                            FilterChipHUD("CLARO", themeMode == "LIGHT") { scope.launch { viewModel.settings.saveThemeMode("LIGHT") } }
                            Spacer(Modifier.width(8.dp))
                            FilterChipHUD("ESCURO", themeMode == "DARK") { scope.launch { viewModel.settings.saveThemeMode("DARK") } }
                        }
                    }
                }
                
                Spacer(modifier = Modifier.height(24.dp))
                SoloCard(borderColor = hudColors.primary) {
                    Text("HORÁRIOS DE COBRANÇA (7 AVISOS)", fontWeight = FontWeight.Bold, color = hudColors.primary, fontSize = 12.sp)
                    Spacer(modifier = Modifier.height(16.dp))
                    
                    val slots = listOf(editH1, editH2, editH3, editH4, editH5, editH6, editH7)
                    val labels = listOf("Aviso 1", "Aviso 2", "Aviso 3", "Aviso 4", "Aviso 5", "Aviso 6", "Aviso 7")
                    
                    for (i in 0 until 7 step 2) {
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            val isActive1 = slots[i] >= 0
                            TimeSlotHUD(
                                label = labels[i], 
                                minutes = slots[i], 
                                isActive = isActive1,
                                onToggle = {
                                    if (it) {
                                        when(i) {
                                            0 -> editH1 = 540; 1 -> editH2 = 660; 2 -> editH3 = 780
                                            3 -> editH4 = 900; 4 -> editH5 = 1020; 5 -> editH6 = 1140; 6 -> editH7 = 1260
                                        }
                                    } else {
                                        when(i) {
                                            0 -> editH1 = -1; 1 -> editH2 = -1; 2 -> editH3 = -1
                                            3 -> editH4 = -1; 4 -> editH5 = -1; 5 -> editH6 = -1; 6 -> editH7 = -1
                                        }
                                    }
                                },
                                modifier = Modifier.weight(1f)
                            ) { showTimePickerFor = i + 1 }
                            
                            if ((i + 1) < 7) {
                                val isActive2 = slots[i+1] >= 0
                                TimeSlotHUD(
                                    label = labels[i+1], 
                                    minutes = slots[i+1], 
                                    isActive = isActive2,
                                    onToggle = {
                                        if (it) {
                                            when(i+1) {
                                                1 -> editH2 = 660; 2 -> editH3 = 780; 3 -> editH4 = 900
                                                4 -> editH5 = 1020; 5 -> editH6 = 1140; 6 -> editH7 = 1260
                                            }
                                        } else {
                                            when(i+1) {
                                                1 -> editH2 = -1; 2 -> editH3 = -1; 3 -> editH4 = -1
                                                4 -> editH5 = -1; 5 -> editH6 = -1; 6 -> editH7 = -1
                                            }
                                        }
                                    },
                                    modifier = Modifier.weight(1f)
                                ) { showTimePickerFor = i + 2 }
                            } else {
                                Spacer(Modifier.weight(1f))
                            }
                        }
                        Spacer(modifier = Modifier.height(8.dp))
                    }
                    
                    Spacer(modifier = Modifier.height(8.dp))
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedButton(
                            onClick = {
                                editH1 = 540; editH2 = 660; editH3 = 780; editH4 = 900; editH5 = 1020; editH6 = 1140; editH7 = 1260
                            },
                            modifier = Modifier.weight(1f),
                            colors = ButtonDefaults.outlinedButtonColors(contentColor = hudColors.textSecondary)
                        ) {
                            Text("RESTAURAR", fontSize = 10.sp)
                        }
                        Button(
                            onClick = {
                                scope.launch {
                                    val newSlots = listOf(editH1, editH2, editH3, editH4, editH5, editH6, editH7)
                                    viewModel.updateGlobalHoursAndApplyToAll(newSlots)
                                    android.widget.Toast.makeText(context, "Ritmo de cobrança salvo!", android.widget.Toast.LENGTH_SHORT).show()
                                }
                            },
                            modifier = Modifier.weight(1f),
                            colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary)
                        ) {
                            Text("SALVAR", fontSize = 10.sp)
                        }
                    }
                }

                Spacer(modifier = Modifier.height(24.dp))
                SoloCard(borderColor = hudColors.secondary) {
                    Text("DADOS PARA O PIX", fontWeight = FontWeight.Bold, color = hudColors.secondary, fontSize = 12.sp)
                    Spacer(modifier = Modifier.height(12.dp))
                    EditHUDTextField(value = pixKey, label = "Chave Pix") { scope.launch { viewModel.settings.savePixKey(it) } }
                    Spacer(modifier = Modifier.height(8.dp))
                    EditHUDTextField(value = receiverName, label = "Nome do recebedor") { scope.launch { viewModel.settings.saveReceiverName(it) } }
                    Spacer(modifier = Modifier.height(8.dp))
                    EditHUDTextField(value = city, label = "Cidade") { scope.launch { viewModel.settings.saveCity(it) } }
                }

                Spacer(modifier = Modifier.height(24.dp))
                SoloCard(borderColor = hudColors.textPrimary.copy(alpha = 0.4f)) {
                    Text("SAÚDE DO SISTEMA E SEGURANÇA", fontWeight = FontWeight.Bold, color = hudColors.textPrimary, fontSize = 12.sp)
                    Spacer(modifier = Modifier.height(12.dp))
                    PermissionCheckItemHUD("Notificações", if (Build.VERSION.SDK_INT >= 33) context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED else true) {
                        if (Build.VERSION.SDK_INT >= 33) context.startActivity(Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).apply { putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName) })
                    }
                    PermissionCheckItemHUD("Alarmes Precisos", if (Build.VERSION.SDK_INT >= 31) (context.getSystemService(Context.ALARM_SERVICE) as AlarmManager).canScheduleExactAlarms() else true) {
                        if (Build.VERSION.SDK_INT >= 31) context.startActivity(Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM))
                    }

                    Spacer(modifier = Modifier.height(8.dp))
                    val biometricManager = BiometricManager.from(context)
                    val canAuthenticate = biometricManager.canAuthenticate(BiometricManager.Authenticators.BIOMETRIC_STRONG or BiometricManager.Authenticators.DEVICE_CREDENTIAL) == BiometricManager.BIOMETRIC_SUCCESS
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column {
                            Text("Bloqueio por Biometria", color = hudColors.textPrimary, fontSize = 14.sp)
                            Text(
                                if (canAuthenticate) "DISPONÍVEL" else "INDISPONÍVEL",
                                color = if (canAuthenticate) hudColors.success else hudColors.error,
                                style = MaterialTheme.typography.labelSmall,
                                fontWeight = FontWeight.Bold
                            )
                        }
                        Switch(
                            checked = isBiometricEnabled,
                            onCheckedChange = { isChecked ->
                                if (canAuthenticate || !isChecked) {
                                    scope.launch { viewModel.settings.setBiometricEnabled(isChecked) }
                                } else {
                                    android.widget.Toast.makeText(context, "Aparelho não possui biometria configurada.", android.widget.Toast.LENGTH_SHORT).show()
                                }
                            },
                            colors = SwitchDefaults.colors(checkedThumbColor = hudColors.primary, checkedTrackColor = hudColors.primary.copy(alpha = 0.3f))
                        )
                    }
                }

                Spacer(modifier = Modifier.height(24.dp))
                SoloCard(borderColor = hudColors.primaryVariant) {
                    Text("SOM DA COBRANÇA", fontWeight = FontWeight.Bold, color = hudColors.primaryVariant, fontSize = 12.sp)
                    Spacer(modifier = Modifier.height(8.dp))
                    OutlinedCard(colors = CardDefaults.outlinedCardColors(containerColor = hudColors.background.copy(alpha = 0.4f))) {
                        Row(modifier = Modifier.padding(12.dp).fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                            Text(soundUri?.substringAfterLast("/") ?: "Padrão do Sistema", modifier = Modifier.weight(1.0f), color = hudColors.textPrimary, fontSize = 12.sp)
                            IconButton(onClick = { pickAudioLauncher.launch(arrayOf("audio/*")) }) { Icon(Icons.Default.Audiotrack, "Selecionar", tint = hudColors.primaryVariant) }
                        }
                    }
                    TextButton(onClick = { viewModel.playTestSound() }) { Text("TESTAR SINAL SONORO", color = hudColors.primaryVariant, fontSize = 11.sp, fontWeight = FontWeight.Bold) }
                }

                Spacer(modifier = Modifier.height(24.dp))
                SoloCard(borderColor = hudColors.primary) {
                    Text("PERSONALIZAÇÃO DE MENSAGENS", fontWeight = FontWeight.Bold, color = hudColors.primary, fontSize = 12.sp)
                    Spacer(modifier = Modifier.height(16.dp))
                    Text("VENDAS PARCELADAS", style = MaterialTheme.typography.labelSmall, color = hudColors.secondary)
                    
                    var editTNormal by remember(templateNormal) { mutableStateOf(templateNormal) }
                    MessageEditItem("Lembrete (No dia)", editTNormal) { editTNormal = it }
                    
                    var editTOverdue by remember(templateOverdue) { mutableStateOf(templateOverdue) }
                    MessageEditItem("Atraso (1 dia+)", editTOverdue) { editTOverdue = it }
                    
                    var editTConfirmPartial by remember(confirmTemplatePartial) { mutableStateOf(confirmTemplatePartial) }
                    MessageEditItem("Confirmação Parcial (Recibo)", editTConfirmPartial) { editTConfirmPartial = it }
                    
                    Spacer(modifier = Modifier.height(16.dp))
                    Text("CONTRATOS MENSALISTAS", style = MaterialTheme.typography.labelSmall, color = hudColors.secondary)
                    
                    var editTMensal by remember(templateMonthly) { mutableStateOf(templateMonthly) }
                    MessageEditItem("Lembrete Mensal (No dia)", editTMensal) { editTMensal = it }
                    
                    var editTMensalOverdue by remember(templateMonthlyOverdue) { mutableStateOf(templateMonthlyOverdue) }
                    MessageEditItem("Atraso Mensal (1 dia+)", editTMensalOverdue) { editTMensalOverdue = it }
                    
                    Spacer(modifier = Modifier.height(16.dp))
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedButton(
                            onClick = { 
                                scope.launch { 
                                    viewModel.settings.resetMessagesToDefault() 
                                    android.widget.Toast.makeText(context, "Textos restaurados!", android.widget.Toast.LENGTH_SHORT).show()
                                } 
                            },
                            modifier = Modifier.weight(1f),
                            colors = ButtonDefaults.outlinedButtonColors(contentColor = hudColors.textSecondary)
                        ) {
                            Text("RESTAURAR", fontSize = 10.sp)
                        }
                        Button(
                            onClick = {
                                scope.launch {
                                    viewModel.settings.saveMessageTemplate(editTNormal)
                                    viewModel.settings.saveMessageTemplateOverdue(editTOverdue)
                                    viewModel.settings.saveConfirmMessagePartialInstallment(editTConfirmPartial)
                                    viewModel.settings.saveMessageTemplateMensalista(editTMensal)
                                    viewModel.settings.saveMessageTemplateMonthlyOverdue(editTMensalOverdue)
                                    android.widget.Toast.makeText(context, "Textos salvos!", android.widget.Toast.LENGTH_SHORT).show()
                                }
                            },
                            modifier = Modifier.weight(1f),
                            colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary)
                        ) {
                            Text("SALVAR", fontSize = 10.sp)
                        }
                    }
                    
                    Spacer(modifier = Modifier.height(8.dp))
                    Text("Tags: {NOME}, {VALOR}, {PARCELA}, {VENCIMENTO}, {DIAS_ATRASO}, {MES}, {ANO}, {PIX}, {VALOR_PAGO_AGORA}, {VALOR_RESTANTE}", style = MaterialTheme.typography.labelSmall, color = hudColors.textSecondary)
                }

                Spacer(modifier = Modifier.height(24.dp))
                SoloCard(borderColor = hudColors.secondary) {
                    Text("PROTOCOLO DE BACKUP", fontWeight = FontWeight.Bold, color = hudColors.secondary, fontSize = 12.sp)
                    Spacer(modifier = Modifier.height(12.dp))
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
                        Button(onClick = { exportBackupLauncher.launch("CobraPix_Backup.cpb") }, modifier = Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = hudColors.secondary)) {
                            Icon(Icons.Default.Backup, null); Spacer(Modifier.width(8.dp)); Text("EXPORTAR", fontWeight = FontWeight.Bold)
                        }
                        Spacer(Modifier.width(8.dp))
                        OutlinedButton(onClick = { importBackupLauncher.launch(arrayOf("application/octet-stream", "*/*")) }, modifier = Modifier.weight(1f), colors = ButtonDefaults.outlinedButtonColors(contentColor = hudColors.secondary)) {
                            Text("IMPORTAR", fontWeight = FontWeight.Bold)
                        }
                    }
                }
                
                Spacer(modifier = Modifier.height(48.dp))
                Button(onClick = onBack, modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary)) {
                    Text("FECHAR SISTEMA", fontWeight = FontWeight.Black, letterSpacing = 2.sp, color = Color.White)
                }
                Spacer(modifier = Modifier.height(32.dp))
            }
        }
    }

    if (showTimePickerFor > 0) {
        val currentMin = when(showTimePickerFor) { 1 -> editH1; 2 -> editH2; 3 -> editH3; 4 -> editH4; 5 -> editH5; 6 -> editH6; else -> editH7 }
        val timeState = rememberTimePickerState(initialHour = currentMin / 60, initialMinute = currentMin % 60, is24Hour = true)
        
        Dialog(onDismissRequest = { showTimePickerFor = 0 }) {
            SoloCard(borderColor = hudColors.primary) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("AJUSTAR HORÁRIO", fontWeight = FontWeight.Black, color = hudColors.primary)
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
                                    1 -> editH1 = newMin; 2 -> editH2 = newMin; 3 -> editH3 = newMin
                                    4 -> editH4 = newMin; 5 -> editH5 = newMin; 6 -> editH6 = newMin
                                    else -> editH7 = newMin
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

@Composable
fun MessageEditItem(label: String, value: String, onValueChange: (String) -> Unit) {
    val hudColors = LocalCobraPixColors.current
    Column(modifier = Modifier.padding(vertical = 8.dp)) {
        Text(label, style = MaterialTheme.typography.labelSmall, color = hudColors.secondary)
        Spacer(Modifier.height(4.dp))
        EditHUDTextField(value = value, label = "", singleLine = false, onValueChange = onValueChange)
    }
}

@Composable
fun PermissionCheckItemHUD(label: String, granted: Boolean, onFix: () -> Unit) {
    val hudColors = LocalCobraPixColors.current
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column {
            Text(label, color = hudColors.textPrimary, fontSize = 14.sp)
            Text(
                if (granted) "OPERACIONAL" else "BLOQUEADO",
                color = if (granted) hudColors.success else hudColors.error,
                style = MaterialTheme.typography.labelSmall,
                fontWeight = FontWeight.Bold
            )
        }
        if (!granted) {
            TextButton(onClick = onFix) { Text("ATIVAR", color = hudColors.secondary, fontWeight = FontWeight.Bold) }
        } else {
            Icon(Icons.Default.Check, null, tint = hudColors.success, modifier = Modifier.size(16.dp))
        }
    }
}
