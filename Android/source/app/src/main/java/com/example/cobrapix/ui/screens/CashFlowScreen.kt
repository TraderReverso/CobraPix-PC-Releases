package com.example.cobrapix.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowLeft
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Whatsapp
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.example.cobrapix.data.Expense
import com.example.cobrapix.data.Installment
import com.example.cobrapix.data.PaymentMethod
import com.example.cobrapix.logic.CurrencyUtils
import com.example.cobrapix.logic.WhatsAppHelper
import com.example.cobrapix.ui.MainViewModel
import com.example.cobrapix.ui.PayableItem
import com.example.cobrapix.ui.components.*
import com.example.cobrapix.ui.theme.LocalCobraPixColors
import kotlinx.coroutines.flow.firstOrNull
import kotlinx.coroutines.launch
import java.util.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CashFlowScreen(
    viewModel: MainViewModel,
    onBack: () -> Unit
) {
    val cal = Calendar.getInstance()
    var selectedMonth by remember { mutableIntStateOf(cal.get(Calendar.MONTH) + 1) }
    var selectedYear by remember { mutableIntStateOf(cal.get(Calendar.YEAR)) }
    
    val paidItems by viewModel.getCombinedPaidItems(selectedMonth, selectedYear).collectAsStateWithLifecycle(initialValue = emptyList())
    
    val totalReceived = paidItems.filter { it.value > 0 }.sumOf { it.value }
    val totalExpenses = paidItems.filter { it.value < 0 }.sumOf { -it.value }
    val netBalance = totalReceived - totalExpenses

    val hudColors = LocalCobraPixColors.current
    val context = androidx.compose.ui.platform.LocalContext.current
    val scope = rememberCoroutineScope()
    
    var showAddExpenseDialog by remember { mutableStateOf(false) }

    val pixKey by viewModel.settings.pixKey.collectAsStateWithLifecycle(initialValue = "")
    val confirmTemplateInstallment by viewModel.settings.confirmMessageInstallment.collectAsStateWithLifecycle(initialValue = "")
    val confirmTemplateMonthly by viewModel.settings.confirmMessageMonthly.collectAsStateWithLifecycle(initialValue = "")

    Scaffold(
        topBar = {
            TopAppBar(
                title = { SoloHeader("Fluxo de Caixa", color = hudColors.success) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, "Voltar", tint = hudColors.textPrimary)
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.Transparent)
            )
        },
        floatingActionButton = {
            FloatingActionButton(onClick = { showAddExpenseDialog = true }, containerColor = hudColors.error, contentColor = Color.White) {
                Icon(Icons.Default.Add, "Registrar Saída")
            }
        },
        containerColor = Color.Transparent
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize()) {
            SoloBackground()
            
            Column(modifier = Modifier.padding(padding).fillMaxSize().padding(16.dp)) {
                SoloCard(borderColor = hudColors.secondary) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column {
                            Text("FILTRAR PERÍODO", style = MaterialTheme.typography.labelSmall, color = hudColors.secondary)
                            Text("${getMonthName(selectedMonth)} $selectedYear", fontWeight = FontWeight.Black, color = hudColors.textPrimary)
                        }
                        Row {
                            IconButton(onClick = { 
                                if (selectedMonth == 1) { selectedMonth = 12; selectedYear-- } else selectedMonth--
                            }) { Icon(Icons.AutoMirrored.Filled.KeyboardArrowLeft, null, tint = hudColors.primary) }
                            IconButton(onClick = { 
                                if (selectedMonth == 12) { selectedMonth = 1; selectedYear++ } else selectedMonth++
                            }) { Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = hudColors.primary) }
                        }
                    }
                    
                    Spacer(Modifier.height(16.dp))
                    SummaryRow("Entradas", totalReceived, hudColors.success)
                    SummaryRow("Saídas", totalExpenses, hudColors.error)
                    Divider(Modifier.padding(vertical = 8.dp), color = hudColors.border.copy(alpha = 0.5f))
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        Text("Saldo Líquido", color = hudColors.textPrimary, fontWeight = FontWeight.Black, fontSize = 14.sp)
                        Text(CurrencyUtils.formatCurrency(netBalance), fontWeight = FontWeight.ExtraBold, color = if (netBalance >= 0) hudColors.success else hudColors.error, fontSize = 16.sp)
                    }
                }
                
                Spacer(Modifier.height(24.dp))
                SoloHeader("Livro Caixa (Histórico)", color = hudColors.textPrimary)
                Spacer(Modifier.height(16.dp))
                
                if (paidItems.isEmpty()) {
                    Box(modifier = Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                        Text("Nenhum registro neste período.", color = hudColors.textSecondary, fontSize = 12.sp)
                    }
                } else {
                    LazyColumn(modifier = Modifier.weight(1f)) {
                        items(paidItems) { item ->
                            CashFlowItemHUD(
                                item = item, 
                                onSendReceipt = if (item is PayableItem.ExpenseType) null else {
                                    {
                                        scope.launch {
                                            val client = viewModel.getClient(item.clientId).firstOrNull()
                                            client?.let { c ->
                                                val template = if (item is PayableItem.InstallmentType) confirmTemplateInstallment else confirmTemplateMonthly
                                                val msg = when(item) {
                                                    is PayableItem.InstallmentType -> WhatsAppHelper.formatMessage(template, c, item.installment, null, pixKey)
                                                    is PayableItem.MonthlyType -> WhatsAppHelper.formatMessage(template, c, Installment(id=0, clientId=0, saleId=0, number=item.payment.month, value=item.value, dueDate=item.dueDate), null, pixKey)
                                                    else -> ""
                                                }
                                                WhatsAppHelper.openWhatsApp(context, c.phone, msg)
                                            }
                                        }
                                    }
                                },
                                onDeleteExpense = if (item is PayableItem.ExpenseType) {
                                    { scope.launch { viewModel.deleteExpense(item.expense) } }
                                } else null
                            )
                        }
                    }
                }
            }
        }
    }

    if (showAddExpenseDialog) {
        AddExpenseDialog(
            onDismiss = { showAddExpenseDialog = false },
            onConfirm = { desc, value, method ->
                scope.launch {
                    viewModel.addExpense(desc, value, method)
                    showAddExpenseDialog = false
                }
            }
        )
    }
}

@Composable
fun CashFlowItemHUD(item: PayableItem, onSendReceipt: (() -> Unit)?, onDeleteExpense: (() -> Unit)?) {
    val hudColors = LocalCobraPixColors.current
    val isExpense = item is PayableItem.ExpenseType
    val borderColor = if (isExpense) hudColors.error.copy(alpha = 0.5f) else hudColors.success.copy(alpha = 0.3f)
    val valueColor = if (isExpense) hudColors.error else hudColors.success
    val sign = if (isExpense) "-" else "+"
    val displayValue = if (isExpense) -item.value else item.value

    SoloCard(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp), borderColor = borderColor) {
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                Text(item.clientName.uppercase(), fontWeight = FontWeight.Black, color = hudColors.textPrimary, fontSize = 14.sp)
                Text(
                    when (item) {
                        is PayableItem.InstallmentType -> "Parcela ${item.installment.number}"
                        is PayableItem.MonthlyType -> "Mensalidade"
                        is PayableItem.ExpenseType -> "Despesa (${item.paymentMethod})"
                    },
                    style = MaterialTheme.typography.labelSmall, color = hudColors.textSecondary
                )
                val date = java.text.SimpleDateFormat("dd/MM/yyyy", Locale.getDefault()).format(Date(item.dueDate))
                Text("Data: $date", fontSize = 10.sp, color = hudColors.textSecondary)
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("$sign ${CurrencyUtils.formatCurrency(displayValue)}", fontWeight = FontWeight.ExtraBold, color = valueColor, fontSize = 16.sp)
                Spacer(Modifier.width(8.dp))
                if (onSendReceipt != null) {
                    IconButton(onClick = onSendReceipt, modifier = Modifier.size(32.dp)) {
                        Icon(Icons.Default.Whatsapp, null, tint = Color(0xFF25D366), modifier = Modifier.size(20.dp))
                    }
                }
                if (onDeleteExpense != null) {
                    TextButton(onClick = onDeleteExpense) {
                        Text("EXCLUIR", color = hudColors.error, fontSize = 9.sp, fontWeight = FontWeight.Black)
                    }
                }
            }
        }
    }
}

@Composable
fun AddExpenseDialog(onDismiss: () -> Unit, onConfirm: (String, Long, PaymentMethod) -> Unit) {
    val hudColors = LocalCobraPixColors.current
    var description by remember { mutableStateOf("") }
    var rawAmount by remember { mutableStateOf("") }
    val formattedAmount = remember(rawAmount) { CurrencyUtils.formatCurrency(rawAmount.toLongOrNull() ?: 0L) }
    var method by remember { mutableStateOf(PaymentMethod.PIX) }

    androidx.compose.ui.window.Dialog(onDismissRequest = onDismiss) {
        SoloCard(borderColor = hudColors.error) {
            Column {
                SoloHeader("Registrar Saída", color = hudColors.error)
                Spacer(Modifier.height(16.dp))
                EditHUDTextField(value = description, label = "Motivo (ex: Conta de Luz)") { description = it }
                Spacer(Modifier.height(12.dp))
                EditHUDTextField(value = formattedAmount, label = "Valor da Despesa") { input ->
                    val digits = input.filter { it.isDigit() }
                    if (digits.length <= 12) rawAmount = digits
                }
                Spacer(Modifier.height(16.dp))
                Text("MÉTODO DE PAGAMENTO", style = MaterialTheme.typography.labelSmall, color = hudColors.textSecondary)
                Row(modifier = Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    FilterChipHUD("PIX", method == PaymentMethod.PIX) { method = PaymentMethod.PIX }
                    FilterChipHUD("$$$", method == PaymentMethod.CASH) { method = PaymentMethod.CASH }
                    FilterChipHUD("CARTÃO", method == PaymentMethod.CARD) { method = PaymentMethod.CARD }
                }
                Spacer(Modifier.height(24.dp))
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                    TextButton(onClick = onDismiss) { Text("CANCELAR", color = hudColors.textSecondary) }
                    Button(
                        onClick = { onConfirm(description, rawAmount.toLongOrNull() ?: 0L, method) }, 
                        colors = ButtonDefaults.buttonColors(containerColor = hudColors.error),
                        enabled = description.isNotBlank() && rawAmount.isNotBlank()
                    ) { Text("REGISTRAR DESPESA", fontWeight = FontWeight.Black) }
                }
            }
        }
    }
}

fun getMonthName(m: Int) = when(m) {
    1 -> "Janeiro"; 2 -> "Fevereiro"; 3 -> "Março"; 4 -> "Abril"; 5 -> "Maio"; 6 -> "Junho"
    7 -> "Julho"; 8 -> "Agosto"; 9 -> "Setembro"; 10 -> "Outubro"; 11 -> "Novembro"; else -> "Dezembro"
}
