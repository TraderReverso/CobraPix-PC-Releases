package com.example.cobrapix.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.QrCode
import androidx.compose.material.icons.filled.Receipt
import androidx.compose.material.icons.filled.Whatsapp
import androidx.compose.material3.*
import androidx.compose.runtime.*
import kotlinx.coroutines.flow.firstOrNull
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import android.content.Context
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.example.cobrapix.data.Installment
import com.example.cobrapix.data.InstallmentStatus
import com.example.cobrapix.logic.CurrencyUtils
import com.example.cobrapix.logic.WhatsAppHelper
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.PayableItem
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.*
import kotlinx.coroutines.launch
import java.util.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ClientDetailScreen(
    viewModel: MainViewModel,
    clientId: Long,
    onBack: () -> Unit,
    onEditClient: () -> Unit,
    onEditInstallment: (Long) -> Unit,
    onEditMonthlyContract: (Long) -> Unit
) {
    val client by viewModel.getClient(clientId).collectAsStateWithLifecycle(initialValue = null)
    val monthlyCustomer by viewModel.getMonthlyCustomer(clientId).collectAsStateWithLifecycle(initialValue = null)
    val payableItems by viewModel.getClientPayableItems(clientId).collectAsStateWithLifecycle(initialValue = emptyList())
    val score by viewModel.getClientScore(clientId).collectAsStateWithLifecycle(initialValue = null)

    val pixKey by viewModel.settings.pixKey.collectAsStateWithLifecycle(initialValue = "")
    val receiverName by viewModel.settings.receiverName.collectAsStateWithLifecycle(initialValue = "")
    val city by viewModel.settings.city.collectAsStateWithLifecycle(initialValue = "")
    val templateInstallment by viewModel.settings.messageTemplate.collectAsStateWithLifecycle(initialValue = "")
    val templateOverdue by viewModel.settings.messageTemplateOverdue.collectAsStateWithLifecycle(initialValue = "")
    val templateMonthly by viewModel.settings.messageTemplateMensalista.collectAsStateWithLifecycle(initialValue = "")
    val templateMonthlyOverdue by viewModel.settings.messageTemplateMonthlyOverdue.collectAsStateWithLifecycle(initialValue = "")
    val confirmTemplateInstallment by viewModel.settings.confirmMessageInstallment.collectAsStateWithLifecycle(initialValue = "")
    val confirmTemplateMonthly by viewModel.settings.confirmMessageMonthly.collectAsStateWithLifecycle(initialValue = "")
    val confirmTemplateLastInstallment by viewModel.settings.confirmMessageLastInstallment.collectAsStateWithLifecycle(initialValue = "")

    var showSuccessDialog by remember { mutableStateOf<PayableItem?>(null) }
    var showReceiptsFor by remember { mutableStateOf<Installment?>(null) }
    var showAddPaymentDialog by remember { mutableStateOf(false) }
    var itemToPay by remember { mutableStateOf<PayableItem?>(null) }
    var showPixDialogFor by remember { mutableStateOf<PayableItem?>(null) }

    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val hudColors = LocalCobraPixColors.current

    LaunchedEffect(Unit) {
        viewModel.paymentSuccessEvent.collect { item ->
            if (item.clientId == clientId) showSuccessDialog = item
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { SoloHeader(client?.name ?: "Perfil", color = hudColors.textPrimary) },
                navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Voltar", tint = hudColors.textPrimary) } },
                actions = { client?.let { IconButton(onClick = onEditClient) { Icon(Icons.Default.Edit, "Editar", tint = hudColors.secondary) } } },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.Transparent)
            )
        },
        containerColor = Color.Transparent
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize()) {
            SoloBackground()
            client?.let { c ->
                LazyColumn(modifier = Modifier.padding(padding).fillMaxSize().padding(16.dp)) {
                    item {
                        SoloCard(borderColor = hudColors.secondary) {
                            Text("DADOS DO CADASTRO", style = MaterialTheme.typography.labelSmall, color = hudColors.secondary, letterSpacing = 2.sp)
                            Spacer(Modifier.height(12.dp))
                            Text("WhatsApp: ${c.phone}", fontSize = 15.sp, color = hudColors.textPrimary)
                            if (c.cpf.isNotBlank()) Text("CPF: ${c.cpf}", fontSize = 14.sp, color = hudColors.textPrimary.copy(0.8f))
                            
                            score?.let { s ->
                                Spacer(Modifier.height(16.dp))
                                Row(verticalAlignment = Alignment.CenterVertically) {
                                    RankBadge(s)
                                    Spacer(Modifier.width(12.dp))
                                    Column {
                                        Text("PONTUALIDADE: ${s.percentage}%", fontWeight = FontWeight.Bold, fontSize = 12.sp, color = hudColors.textPrimary)
                                        Text("${s.onTime} de ${s.totalPaid} pagamentos no prazo", fontSize = 10.sp, color = hudColors.textSecondary)
                                    }
                                }
                                Spacer(Modifier.height(8.dp))
                                SoloProgressBar(progress = s.percentage / 100f, label = "Nível de Confiança", valueText = s.rank, color = when(s.rank){"S"->Color(0xFF00E5FF) "A"->Color(0xFFFFD600) "B"->Color(0xFFE0E0E0) else->Color(0xFFFF5252)})
                            }

                            monthlyCustomer?.let {
                                Spacer(Modifier.height(16.dp))
                                OutlinedButton(onClick = { onEditMonthlyContract(clientId) }, modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.outlinedButtonColors(contentColor = hudColors.primaryVariant), border = androidx.compose.foundation.BorderStroke(1.dp, hudColors.primaryVariant.copy(0.5f))) {
                                    Text("AJUSTAR CONTRATO MENSAL", fontSize = 10.sp, fontWeight = FontWeight.Black)
                                }
                            }
                        }
                        Spacer(modifier = Modifier.height(32.dp))
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                            SoloHeader("Histórico Financeiro", color = hudColors.primary)
                            Button(onClick = { showAddPaymentDialog = true }, colors = ButtonDefaults.buttonColors(containerColor = hudColors.primaryVariant), modifier = Modifier.height(32.dp), contentPadding = PaddingValues(horizontal = 8.dp, vertical = 0.dp)) {
                                Icon(Icons.Default.Add, null, modifier = Modifier.size(16.dp))
                                Spacer(Modifier.width(4.dp))
                                Text("NOVA", fontSize = 10.sp, fontWeight = FontWeight.Black)
                            }
                        }
                        Spacer(modifier = Modifier.height(16.dp))
                    }
                    items(payableItems) { item ->
                        PayableDetailItemHUD(
                            item = item,
                            onMarkPaid = { itemToPay = item },
                            onWhatsApp = {
                                val isPaid = item.status == InstallmentStatus.PAID
                                val template = when {
                                    isPaid -> {
                                        if (item is PayableItem.InstallmentType) {
                                            val allInsts = payableItems.filterIsInstance<PayableItem.InstallmentType>().map { it.installment }
                                            val isLast = item.installment.number == allInsts.size
                                            if (isLast) confirmTemplateLastInstallment else confirmTemplateInstallment
                                        } else confirmTemplateMonthly
                                    }
                                    item is PayableItem.MonthlyType -> {
                                        if (item.status == InstallmentStatus.OVERDUE) templateMonthlyOverdue else templateMonthly
                                    }
                                    item.status == InstallmentStatus.OVERDUE -> templateOverdue
                                    else -> templateInstallment
                                }
                                var msg = when(item) {
                                    is PayableItem.InstallmentType -> {
                                        val list = payableItems.filterIsInstance<PayableItem.InstallmentType>().map { it.installment }
                                        val punct = if (isPaid) "Pagamento recebido com sucesso!" else ""
                                        WhatsAppHelper.formatMessage(template, c, item.installment, list, pixKey, punct)
                                    }
                                    is PayableItem.MonthlyType -> {
                                        WhatsAppHelper.formatMessage(template, c, Installment(id=0, clientId=0, saleId=0, number=item.payment.month, value=item.value, dueDate=item.dueDate), null, pixKey)
                                    }
                                    is PayableItem.ExpenseType -> ""
                                }
                                
                                if (isPaid && msg.isNotBlank()) {
                                    msg += "\n\n*(2ª Via de Comprovante)*"
                                }
                                
                                WhatsAppHelper.openWhatsApp(context, c.phone, msg)
                            },
                            onEdit = { onEditInstallment(item.id) },
                            onReceipts = {
                                if (item is PayableItem.InstallmentType) {
                                    showReceiptsFor = item.installment
                                }
                            },
                            onGeneratePix = { showPixDialogFor = item }
                        )
                    }
                }
            } ?: run {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator(color = hudColors.primary) }
            }

            showPixDialogFor?.let { item ->
                PixGeneratorDialog(
                    item = item,
                    viewModel = viewModel,
                    onDismiss = { showPixDialogFor = null }
                )
            }

            itemToPay?.let { item ->
                PaymentConfirmDialog(
                    item = item,
                    onDismiss = { itemToPay = null },
                    onConfirm = { value, method ->
                        if (item is PayableItem.InstallmentType) {
                            scope.launch {
                                val result = viewModel.registerPartialPayment(item, value, method)
                                when(result) {
                                    is MainViewModel.PaymentResult.Success -> {
                                        itemToPay = null
                                    }
                                    is MainViewModel.PaymentResult.InvalidAmount -> {
                                        android.widget.Toast.makeText(context, "Valor inválido para pagamento.", android.widget.Toast.LENGTH_SHORT).show()
                                    }
                                    is MainViewModel.PaymentResult.ConcurrencyOrBalanceChanged -> {
                                        android.widget.Toast.makeText(context, "Pagamento não registrado. O saldo desta parcela foi alterado ou o valor informado não é mais válido.", android.widget.Toast.LENGTH_LONG).show()
                                        itemToPay = null
                                    }
                                    is MainViewModel.PaymentResult.Error -> {
                                        android.widget.Toast.makeText(context, "Erro ao processar: ${result.message}", android.widget.Toast.LENGTH_SHORT).show()
                                    }
                                }
                            }
                        } else {
                            viewModel.markAsPaid(item, method)
                            itemToPay = null
                        }
                    }
                )
            }
            
            showReceiptsFor?.let { inst ->
                ReceiptsDialog(
                    installment = inst,
                    viewModel = viewModel,
                    onDismiss = { showReceiptsFor = null }
                )
            }
        }
    }

    if (showAddPaymentDialog) {
        AddPaymentDialog(onDismiss = { showAddPaymentDialog = false }, onConfirm = { value, count, date, useFifth ->
            viewModel.addNewSaleToExistingClient(clientId, value, count, date, useFifth)
            showAddPaymentDialog = false
        })
    }


    showSuccessDialog?.let { item ->
        val isLast = if (item is PayableItem.InstallmentType) {
            val total = payableItems.count { it is PayableItem.InstallmentType }
            item.installment.number == total
        } else false

        val title = if (isLast) "⭐ CONTRATO FINALIZADO 🎓" else if (item is PayableItem.InstallmentType) "Missão Concluída" else "Renovação Concluída"
        val details = if (isLast) "Parabéns! Você concluiu todos os pagamentos deste cliente."
                     else when (item) {
                         is PayableItem.InstallmentType -> "Pagamento da parcela ${item.installment.number} confirmado."
                         is PayableItem.MonthlyType -> "Mensalidade confirmada com sucesso."
                         is PayableItem.ExpenseType -> ""
                     }

        SoloQuestClearDialog(title = title, details = details, onDismiss = { showSuccessDialog = null }, onSendWhatsApp = {
            client?.let { c ->
                val template = if (isLast) confirmTemplateLastInstallment else if (item is PayableItem.InstallmentType) confirmTemplateInstallment else confirmTemplateMonthly
                val msg = when (item) {
                    is PayableItem.InstallmentType -> {
                        val list = payableItems.filterIsInstance<PayableItem.InstallmentType>().map { it.installment }
                        val punct = if (item.status != InstallmentStatus.OVERDUE) "Sua pontualidade foi exemplar! 🤝" else "Pagamento recebido!"
                        WhatsAppHelper.formatMessage(template, c, item.installment, list, pixKey, punct)
                    }
                    is PayableItem.MonthlyType -> {
                        WhatsAppHelper.formatMessage(template, c, Installment(id=0, clientId=0, saleId=0, number=item.payment.month, value=item.value, dueDate=item.dueDate), null, pixKey)
                    }
                    is PayableItem.ExpenseType -> ""
                }
                WhatsAppHelper.openWhatsApp(context, c.phone, msg)
            }
            showSuccessDialog = null
        })
    }
}

@Composable
fun PixGeneratorDialog(item: PayableItem, viewModel: MainViewModel, onDismiss: () -> Unit) {
    val hudColors = LocalCobraPixColors.current
    val context = LocalContext.current
    val pixKey by viewModel.settings.pixKey.collectAsStateWithLifecycle(initialValue = "")
    val receiverName by viewModel.settings.receiverName.collectAsStateWithLifecycle(initialValue = "")
    val city by viewModel.settings.city.collectAsStateWithLifecycle(initialValue = "")

    val maxAllowed = when (item) {
        is PayableItem.InstallmentType -> item.installment.value - item.installment.paidAmount
        else -> item.value
    }

    var rawAmount by remember { mutableStateOf(maxAllowed.toString()) }
    val formattedAmount = remember(rawAmount) { CurrencyUtils.formatCurrency(rawAmount.toLongOrNull() ?: 0L) }
    
    var generatedPayload by remember { mutableStateOf<String?>(null) }
    var generatedBitmap by remember { mutableStateOf<android.graphics.Bitmap?>(null) }

    androidx.compose.ui.window.Dialog(onDismissRequest = onDismiss) {
        SoloCard(borderColor = hudColors.primaryVariant) {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                SoloHeader("Cobrança PIX", color = hudColors.primaryVariant)
                Spacer(Modifier.height(16.dp))

                if (pixKey.isBlank()) {
                    Text("Chave PIX não configurada nos Ajustes.", color = hudColors.error, fontSize = 14.sp, fontWeight = FontWeight.Bold)
                    Spacer(Modifier.height(16.dp))
                    Button(onClick = onDismiss, colors = ButtonDefaults.buttonColors(containerColor = hudColors.primaryVariant)) {
                        Text("FECHAR")
                    }
                    return@SoloCard
                }

                if (generatedPayload == null) {
                    Text("Saldo Restante: ${CurrencyUtils.formatCurrency(maxAllowed)}", color = hudColors.warning, fontSize = 14.sp, fontWeight = FontWeight.Black)
                    Spacer(Modifier.height(16.dp))
                    
                    EditHUDTextField(value = formattedAmount, label = "Valor a cobrar") { input ->
                        val digits = input.filter { it.isDigit() }
                        if (digits.length <= 12) rawAmount = digits
                    }
                    Spacer(Modifier.height(8.dp))

                    val valueToCharge = rawAmount.toLongOrNull() ?: 0L
                    val isAmountValid = valueToCharge > 0L && valueToCharge <= maxAllowed

                    if (!isAmountValid && valueToCharge > 0L) {
                        Text("O valor não pode ser maior que o saldo restante.", color = hudColors.error, fontSize = 11.sp)
                        Spacer(Modifier.height(8.dp))
                    }

                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                        TextButton(onClick = onDismiss) { Text("CANCELAR", color = hudColors.textSecondary) }
                        Button(
                            onClick = {
                                try {
                                    val payload = com.example.cobrapix.logic.PixGenerator.generatePayload(
                                        pixKey = pixKey,
                                        amount = valueToCharge,
                                        receiverName = receiverName,
                                        city = city
                                    )
                                    generatedPayload = payload
                                    generatedBitmap = com.example.cobrapix.logic.QrCodeHelper.generateQrCodeBitmap(payload)
                                } catch (e: IllegalArgumentException) {
                                    android.widget.Toast.makeText(context, e.message, android.widget.Toast.LENGTH_LONG).show()
                                }
                            },
                            colors = ButtonDefaults.buttonColors(containerColor = hudColors.primaryVariant),
                            enabled = isAmountValid
                        ) { Text("GERAR", fontWeight = FontWeight.Black) }
                    }
                } else {
                    generatedBitmap?.let { bmp ->
                        androidx.compose.foundation.Image(
                            bitmap = bmp.asImageBitmap(),
                            contentDescription = "QR Code PIX",
                            modifier = Modifier.size(200.dp)
                        )
                        Spacer(Modifier.height(16.dp))
                    }
                    
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        OutlinedButton(
                            onClick = {
                                val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
                                val clip = android.content.ClipData.newPlainText("Copia e Cola", generatedPayload)
                                clipboard.setPrimaryClip(clip)
                                android.widget.Toast.makeText(context, "Código copiado!", android.widget.Toast.LENGTH_SHORT).show()
                            },
                            modifier = Modifier.weight(1f),
                            colors = ButtonDefaults.outlinedButtonColors(contentColor = hudColors.textPrimary)
                        ) {
                            Text("COPIAR PIX", fontSize = 10.sp, fontWeight = FontWeight.Bold)
                        }
                        Spacer(Modifier.width(8.dp))
                        Button(
                            onClick = {
                                generatedBitmap?.let { com.example.cobrapix.logic.QrCodeHelper.shareQrCode(context, it) }
                            },
                            modifier = Modifier.weight(1f),
                            colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF25D366))
                        ) {
                            Text("COMPARTILHAR", fontSize = 10.sp, fontWeight = FontWeight.Bold)
                        }
                    }
                    Spacer(Modifier.height(16.dp))
                    TextButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) { Text("FECHAR", color = hudColors.textSecondary) }
                }
            }
        }
    }
}

@Composable
fun ReceiptsDialog(installment: Installment, viewModel: MainViewModel, onDismiss: () -> Unit) {
    val hudColors = LocalCobraPixColors.current
    val context = LocalContext.current
    val payments by viewModel.getInstallmentPayments(installment.id).collectAsStateWithLifecycle(initialValue = emptyList())
    val client by viewModel.getClient(installment.clientId).collectAsStateWithLifecycle(initialValue = null)

    androidx.compose.ui.window.Dialog(onDismissRequest = onDismiss) {
        SoloCard(borderColor = hudColors.primaryVariant) {
            Column {
                SoloHeader("Histórico de Pagamentos", color = hudColors.primaryVariant)
                Spacer(Modifier.height(16.dp))
                
                if (payments.isEmpty()) {
                    Text("Nenhum pagamento registrado.", color = hudColors.textSecondary)
                } else {
                    var accumulated = 0L
                    payments.forEach { p ->
                        accumulated += p.value
                        val currentRemaining = installment.value - accumulated
                        
                        Row(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                            Column {
                                Text(java.text.SimpleDateFormat("dd/MM/yy HH:mm", Locale.getDefault()).format(Date(p.paymentDate)), fontSize = 10.sp, color = hudColors.textSecondary)
                                Text(CurrencyUtils.formatCurrency(p.value), fontWeight = FontWeight.Bold, color = hudColors.success)
                            }
                            IconButton(onClick = {
                                client?.let { c ->
                                    kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.Dispatchers.IO).launch {
                                        val rName = viewModel.settings.receiverName.firstOrNull() ?: ""
                                        val pKey = viewModel.settings.pixKey.firstOrNull() ?: ""
                                        kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.Main) {
                                            com.example.cobrapix.logic.ReceiptPdfGenerator.generateAndShareReceipt(
                                                context = context,
                                                clientName = c.name,
                                                clientPhone = c.phone,
                                                installmentNumber = installment.number,
                                                originalValue = installment.value,
                                                paidNow = p.value,
                                                totalPaid = accumulated,
                                                remaining = currentRemaining,
                                                paymentMethod = p.paymentMethod,
                                                paymentDate = p.paymentDate,
                                                receiverName = rName,
                                                pixKey = pKey
                                            )
                                        }
                                    }
                                }
                            }) {
                                Icon(Icons.Default.Receipt, "Gerar Recibo", tint = hudColors.primaryVariant)
                            }
                        }
                        androidx.compose.material3.HorizontalDivider(color = hudColors.border.copy(alpha = 0.3f))
                    }
                }
                
                Spacer(Modifier.height(16.dp))
                TextButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) { Text("FECHAR", color = hudColors.textSecondary) }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)

@Composable
fun AddPaymentDialog(onDismiss: () -> Unit, onConfirm: (Long, Int, Long, Boolean) -> Unit) {
    val hudColors = LocalCobraPixColors.current
    var rawAmount by remember { mutableStateOf("") }
    val formattedAmount = remember(rawAmount) { CurrencyUtils.formatCurrency(rawAmount.toLongOrNull() ?: 0L) }
    var count by remember { mutableStateOf("1") }
    var fifth by remember { mutableStateOf(false) }
    var date by remember { mutableLongStateOf(System.currentTimeMillis()) }
    var showDP by remember { mutableStateOf(false) }

    androidx.compose.ui.window.Dialog(onDismissRequest = onDismiss) {
        SoloCard(borderColor = hudColors.primary) {
            Column {
                SoloHeader("Nova Cobrança", color = hudColors.primary)
                Spacer(Modifier.height(16.dp))
                EditHUDTextField(value = formattedAmount, label = "Valor exato da Parcela") { input ->
                    val digits = input.filter { it.isDigit() }
                    if (digits.length <= 12) rawAmount = digits
                }
                Spacer(Modifier.height(12.dp))
                EditHUDTextField(value = count, label = "Qtd Parcelas") { count = it.filter { it.isDigit() } }
                Spacer(Modifier.height(12.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(fifth, { fifth = it })
                    Text("Usar 5º Dia Útil", color = hudColors.textPrimary, fontSize = 14.sp)
                }
                if (!fifth) {
                    Spacer(Modifier.height(12.dp))
                    Button(onClick = { showDP = true }, modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.buttonColors(containerColor = hudColors.surfaceVariant), border = androidx.compose.foundation.BorderStroke(1.dp, hudColors.primary.copy(0.4f))) {
                        Text("Vencimento: ${java.text.SimpleDateFormat("dd/MM/yyyy", Locale.getDefault()).format(Date(date))}", color = hudColors.textPrimary)
                    }
                }
                Spacer(Modifier.height(24.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                    TextButton(onClick = onDismiss) { Text("CANCELAR", color = hudColors.textSecondary) }
                    Button(onClick = { onConfirm(rawAmount.toLongOrNull() ?: 0L, count.toIntOrNull() ?: 1, date, fifth) }, colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary), enabled = rawAmount.isNotBlank()) { Text("ADICIONAR") }
                }
            }
        }
    }
    if (showDP) {
        val dpState = rememberDatePickerState(initialSelectedDateMillis = date)
        DatePickerDialog(onDismissRequest = { showDP = false }, confirmButton = {
            TextButton(onClick = {
                val utc = dpState.selectedDateMillis ?: date
                val cal = Calendar.getInstance().apply { timeInMillis = utc }
                date = Calendar.getInstance().apply { set(cal[Calendar.YEAR], cal[Calendar.MONTH], cal[Calendar.DAY_OF_MONTH], 0,0,0); set(Calendar.MILLISECOND, 0) }.timeInMillis
                showDP = false
            }) { Text("OK") }
        }) { DatePicker(dpState) }
    }
}

@Composable
fun PayableDetailItemHUD(
    item: PayableItem, 
    onMarkPaid: () -> Unit, 
    onWhatsApp: () -> Unit, 
    onEdit: () -> Unit, 
    onReceipts: () -> Unit,
    onGeneratePix: (() -> Unit)? = null
) {
    val hudColors = LocalCobraPixColors.current
    SoloCard(modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp), borderColor = if (item.status == InstallmentStatus.PAID) hudColors.success else if (item.status == InstallmentStatus.OVERDUE) hudColors.error else hudColors.primary.copy(0.3f)) {
        Column {
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                Column {
                    val label = when(item) { is PayableItem.InstallmentType -> "Parcela ${item.installment.number}"; is PayableItem.MonthlyType -> "Mensalidade"; else -> "Despesa" }
                    Text(label.uppercase(), fontWeight = FontWeight.Black, color = hudColors.secondary, fontSize = 11.sp, letterSpacing = 1.sp)
                    Text(CurrencyUtils.formatCurrency(item.value), fontWeight = FontWeight.ExtraBold, fontSize = 18.sp, color = hudColors.textPrimary)
                    
                    if (item is PayableItem.InstallmentType && item.installment.paidAmount > 0) {
                        Text("Pago: ${CurrencyUtils.formatCurrency(item.installment.paidAmount)}", color = hudColors.success, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                        if (item.status != InstallmentStatus.PAID) {
                            Text("Restante: ${CurrencyUtils.formatCurrency(item.value - item.installment.paidAmount)}", color = hudColors.warning, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                        }
                    }
                    
                    Text("VENCIMENTO: ${java.text.SimpleDateFormat("dd/MM/yyyy", Locale.getDefault()).format(Date(item.dueDate))}", style = MaterialTheme.typography.labelSmall, color = hudColors.textPrimary.copy(0.5f))
                }
                StatusBadge(item.status)
            }
            if (item.status != InstallmentStatus.PAID && item !is PayableItem.ExpenseType) {
                Spacer(modifier = Modifier.height(16.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Button(onClick = onWhatsApp, modifier = Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF25D366)), contentPadding = PaddingValues(0.dp)) {
                        Icon(Icons.Default.Whatsapp, null, modifier = Modifier.size(16.dp)); Spacer(Modifier.width(4.dp)); Text("WHATSAPP", fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                    Button(onClick = onMarkPaid, modifier = Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = hudColors.primary), contentPadding = PaddingValues(0.dp)) {
                        Text("PAGO", fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                    
                    if (onGeneratePix != null) {
                        IconButton(onClick = onGeneratePix, modifier = Modifier.size(40.dp)) { Icon(Icons.Default.QrCode, "Gerar Pix", tint = hudColors.primaryVariant) }
                    }
                    
                    IconButton(onClick = onEdit, modifier = Modifier.size(40.dp)) { Icon(Icons.Default.Edit, null, tint = hudColors.textPrimary.copy(0.6f)) }
                    if (item is PayableItem.InstallmentType && item.installment.paidAmount > 0) {
                        IconButton(onClick = onReceipts, modifier = Modifier.size(40.dp)) { Icon(Icons.Default.Receipt, "Recibos", tint = hudColors.textPrimary) }
                    }
                }
            } else if (item !is PayableItem.ExpenseType) {
                val pDate = when(item) { is PayableItem.InstallmentType -> item.installment.paymentDate; is PayableItem.MonthlyType -> item.payment.paymentDate; else -> null }
                pDate?.let { Text("CONFIRMADO EM: ${java.text.SimpleDateFormat("dd/MM/yyyy HH:mm").format(Date(it))}", style = MaterialTheme.typography.labelSmall, color = hudColors.success, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 12.dp)) }
                
                Spacer(modifier = Modifier.height(12.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Button(onClick = onWhatsApp, modifier = Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF25D366)), contentPadding = PaddingValues(0.dp)) {
                        Icon(Icons.Default.Whatsapp, null, modifier = Modifier.size(16.dp)); Spacer(Modifier.width(4.dp)); Text("ENVIAR COMPROVANTE", fontSize = 10.sp, fontWeight = FontWeight.Bold)
                    }
                    if (item is PayableItem.InstallmentType && item.installment.paidAmount > 0) {
                        IconButton(onClick = onReceipts, modifier = Modifier.size(40.dp)) { Icon(Icons.Default.Receipt, "Recibos", tint = hudColors.textPrimary) }
                    }
                }
            }
        }
    }
}
