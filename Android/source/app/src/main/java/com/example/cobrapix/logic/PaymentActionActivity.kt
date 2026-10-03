package com.example.cobrapix.logic

import android.os.Bundle
import android.util.Log
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.lifecycle.lifecycleScope
import androidx.room.withTransaction
import com.example.cobrapix.data.*
import com.example.cobrapix.ui.PayableItem
import com.example.cobrapix.ui.components.SoloQuestClearDialog
import com.example.cobrapix.ui.theme.CobraPixTheme
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.firstOrNull
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.util.*

class PaymentActionActivity : ComponentActivity() {

    private data class SuccessData(
        val title: String,
        val details: String,
        val phone: String,
        val message: String,
        val receiptAction: (() -> Unit)? = null
    )

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Alterado de INSTALLMENT_ID para CLIENT_ID para suportar a lógica consolidada
        val clientId = intent.getLongExtra("CLIENT_ID", -1)
        if (clientId == -1L) {
            finish()
            return
        }

        var pendingItems by mutableStateOf<List<PayableItem>>(emptyList())
        var selectedItem by mutableStateOf<PayableItem?>(null)
        var successData by mutableStateOf<SuccessData?>(null)
        var isProcessing by mutableStateOf(false)
        var isLoading by mutableStateOf(true)

        lifecycleScope.launch {
            val db = CobraPixDatabase.getDatabase(this@PaymentActionActivity)
            val insts = db.installmentDao().getInstallmentsByClient(clientId).firstOrNull()?.filter { it.status != InstallmentStatus.PAID } ?: emptyList()
            val customer = db.monthlyCustomerDao().getByClientId(clientId)
            val payments = if (customer != null) db.monthlyPaymentDao().getPaymentsByCustomer(customer.id).firstOrNull()?.filter { it.status != InstallmentStatus.PAID } ?: emptyList() else emptyList()
            
            val list = mutableListOf<PayableItem>()
            val clientName = db.clientDao().getClientById(clientId)?.name ?: "Cliente"
            insts.forEach { list.add(PayableItem.InstallmentType(it, clientName)) }
            payments.forEach { list.add(PayableItem.MonthlyType(it, clientName, clientId)) }
            
            pendingItems = list.sortedBy { it.dueDate }
            if (pendingItems.size == 1) selectedItem = pendingItems.first()
            isLoading = false
        }

        setContent {
            CobraPixTheme {
                val data = successData
                if (isLoading) {
                    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
                } else if (data == null) {
                    if (selectedItem == null && pendingItems.isNotEmpty()) {
                        PendingItemsSelector(
                            items = pendingItems,
                            onDismiss = { finish() },
                            onSelect = { selectedItem = it }
                        )
                    } else if (selectedItem != null) {
                        com.example.cobrapix.ui.components.PaymentConfirmDialog(
                            item = selectedItem!!,
                            onDismiss = { if (pendingItems.size > 1) selectedItem = null else finish() },
                            onConfirm = { value, method ->
                                if (!isProcessing) {
                                    isProcessing = true
                                    confirmPayment(selectedItem!!, value, method) { result ->
                                        successData = result
                                        isProcessing = false
                                    }
                                }
                            },
                            isProcessing = isProcessing
                        )
                    } else {
                        LaunchedEffect(Unit) { finish() }
                    }
                } else {
                    SoloQuestClearDialog(
                        title = data.title,
                        details = data.details,
                        onDismiss = { finish() },
                        onSendWhatsApp = {
                            WhatsAppHelper.openWhatsApp(this@PaymentActionActivity, data.phone, data.message)
                            finish()
                        },
                        onShareReceipt = data.receiptAction
                    )
                }
            }
        }
    }

    private fun confirmPayment(item: PayableItem, valuePaid: Long, method: PaymentMethod, onFinished: (SuccessData?) -> Unit) {
        lifecycleScope.launch {
            try {
                val db = CobraPixDatabase.getDatabase(this@PaymentActionActivity)
                val settings = SettingsRepository(this@PaymentActionActivity)
                val pixKey = settings.pixKey.firstOrNull() ?: ""

                var result: SuccessData? = null

                withContext(Dispatchers.IO) {
                    val client = db.clientDao().getClientById(item.clientId) ?: return@withContext
                    
                    when(item) {
                        is PayableItem.InstallmentType -> {
                            var finalInst: Installment? = null
                            var isFullyPaid = false
                            val pDate = System.currentTimeMillis()
                            
                            db.withTransaction {
                                val rows = db.installmentDao().addPaymentIfValid(item.installment.id, valuePaid)
                                if (rows != 1) {
                                    throw Exception("Pagamento recusado: saldo inválido ou erro de concorrência na notificação.")
                                }
                                
                                db.installmentPaymentDao().insertPayment(InstallmentPayment(
                                    installmentId = item.installment.id, value = valuePaid, paymentDate = pDate, paymentMethod = method
                                ))
                                
                                val updated = db.installmentDao().getInstallmentById(item.installment.id) ?: throw Exception("Parcela não encontrada após update.")
                                if (updated.paidAmount == updated.value) {
                                    isFullyPaid = true
                                    finalInst = updated.copy(
                                        status = InstallmentStatus.PAID,
                                        paymentDate = pDate,
                                        paymentMethod = method
                                    )
                                    db.installmentDao().updateInstallment(finalInst!!)
                                } else {
                                    finalInst = updated
                                }
                            }
                            
                            val dbInst = finalInst!!
                            val allInsts = db.installmentDao().getInstallmentsByClient(item.clientId).firstOrNull() ?: emptyList()
                            
                            val isLast = dbInst.number == allInsts.size
                            
                            val template = if (!isFullyPaid) {
                                settings.confirmMessagePartialInstallment.firstOrNull() ?: ""
                            } else if (isLast) {
                                settings.confirmMessageLastInstallment.firstOrNull() ?: ""
                            } else {
                                settings.confirmMessageInstallment.firstOrNull() ?: ""
                            }
                            
                            val punctuality = if (item.status != InstallmentStatus.OVERDUE) "Sua pontualidade foi exemplar! 🤝" else "Pagamento recebido com sucesso!"
                            val msg = WhatsAppHelper.formatMessage(template, client, dbInst, allInsts, pixKey, punctuality, valuePaid)
                            
                            val newPaidAmount = dbInst.paidAmount
                            val receiptAction: () -> Unit = {
                                kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.Dispatchers.IO).launch {
                                    val rName = settings.receiverName.firstOrNull() ?: ""
                                    withContext(Dispatchers.Main) {
                                        ReceiptPdfGenerator.generateAndShareReceipt(
                                            context = this@PaymentActionActivity,
                                            clientName = client.name,
                                            clientPhone = client.phone,
                                            installmentNumber = dbInst.number,
                                            originalValue = dbInst.value,
                                            paidNow = valuePaid,
                                            totalPaid = newPaidAmount,
                                            remaining = dbInst.value - newPaidAmount,
                                            paymentMethod = method,
                                            paymentDate = pDate,
                                            receiverName = rName,
                                            pixKey = pixKey
                                        )
                                    }
                                }
                            }
                            
                            if (isFullyPaid) {
                                val title = if (isLast) "⭐ CONTRATO FINALIZADO 🎓" else "Missão Concluída"
                                result = SuccessData(title, "Pagamento da parcela ${dbInst.number} confirmado.", client.phone, msg, receiptAction)
                            } else {
                                result = SuccessData("Pagamento Registrado", "Pagamento parcial de ${CurrencyUtils.formatCurrency(valuePaid)} confirmado. Resta: ${CurrencyUtils.formatCurrency(dbInst.value - dbInst.paidAmount)}.", client.phone, msg, receiptAction)
                            }
                        }
                        is PayableItem.MonthlyType -> {
                            db.monthlyPaymentDao().update(item.payment.copy(
                                status = InstallmentStatus.PAID,
                                paymentDate = System.currentTimeMillis(),
                                paymentMethod = method
                            ))
                            val dbMonthlyCustomer = db.monthlyCustomerDao().getById(item.payment.monthlyCustomerId)
                            dbMonthlyCustomer?.let { c ->
                                if (c.isActive && !c.isArchived) {
                                    val next = Calendar.getInstance().apply { set(item.payment.year, item.payment.month - 1, c.dueDay); add(Calendar.MONTH, 1) }
                                    val nextMonth = next[Calendar.MONTH] + 1
                                    val nextYear = next[Calendar.YEAR]
                                    val existing = db.monthlyPaymentDao().getPaymentsByCustomer(c.id).firstOrNull()?.find { it.month == nextMonth && it.year == nextYear }
                                    if (existing == null) {
                                        db.monthlyPaymentDao().insert(com.example.cobrapix.data.MonthlyPayment(monthlyCustomerId = c.id, month = nextMonth, year = nextYear, value = c.monthlyValue, dueDate = next.timeInMillis))
                                        val mClient = db.clientDao().getClientById(c.clientId)
                                        mClient?.let { AlarmHelper.scheduleClientAlarm(this@PaymentActionActivity, it.id, it.name, settings.getAlarmHours()) }
                                    }
                                }
                            }
                            
                            val template = settings.confirmMessageMonthly.firstOrNull() ?: ""
                            val msg = WhatsAppHelper.formatMessage(template, client, Installment(id=0, clientId=0, saleId=0, number=item.payment.month, value=item.value, dueDate=item.dueDate), null, pixKey)
                            result = SuccessData("Renovação Concluída", "Sua mensalidade no valor de ${CurrencyUtils.formatCurrency(item.value)} foi confirmada.", client.phone, msg)
                        }
                        is PayableItem.ExpenseType -> { }
                    }

                    val stillPending = db.installmentDao().getInstallmentsByClient(item.clientId).firstOrNull()?.any { it.status != InstallmentStatus.PAID } == true ||
                                     db.monthlyCustomerDao().getByClientId(item.clientId)?.let { c -> db.monthlyPaymentDao().getPaymentsByCustomer(c.id).firstOrNull()?.any { it.status != InstallmentStatus.PAID } } == true
                    
                    if (!stillPending) AlarmHelper.cancelAlarms(applicationContext, item.clientId)
                }
                
                val notificationManager = getSystemService(NOTIFICATION_SERVICE) as android.app.NotificationManager
                notificationManager.cancel(item.clientId.toInt())
                onFinished(result)
            } catch (e: Exception) {
                Log.e("CobraPix", "Erro ao confirmar pagamento", e)
                onFinished(null)
                finish()
            }
        }
    }

    // funcs
}

@Composable
fun PendingItemsSelector(items: List<PayableItem>, onDismiss: () -> Unit, onSelect: (PayableItem) -> Unit) {
    Dialog(onDismissRequest = onDismiss) {
        Card(modifier = Modifier.padding(16.dp)) {
            Column(modifier = Modifier.padding(16.dp)) {
                Text("Qual dívida foi paga?", style = MaterialTheme.typography.titleLarge)
                Spacer(modifier = Modifier.height(16.dp))
                items.forEach { item ->
                    val label = when(item) {
                        is PayableItem.InstallmentType -> "Parcela ${item.installment.number}"
                        is PayableItem.MonthlyType -> "Mensalidade ${item.payment.month}/${item.payment.year}"
                        is PayableItem.ExpenseType -> "Despesa"
                    }
                    Button(
                        onClick = { onSelect(item) },
                        modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp)
                    ) {
                        Column {
                            Text(label)
                            Text(CurrencyUtils.formatCurrency(item.value), style = MaterialTheme.typography.labelSmall)
                        }
                    }
                }
                Spacer(modifier = Modifier.height(8.dp))
                TextButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) { Text("CANCELAR") }
            }
        }
    }
}

@Composable
fun PaymentMethodDialog(onDismiss: () -> Unit, onConfirm: (PaymentMethod) -> Unit, isProcessing: Boolean) {
    Dialog(onDismissRequest = if (isProcessing) ({}) else onDismiss) {
        Card(modifier = Modifier.padding(16.dp)) {
            Column(modifier = Modifier.padding(16.dp)) {
                Text(if (isProcessing) "Processando..." else "Como você recebeu?", style = MaterialTheme.typography.titleLarge)
                Spacer(modifier = Modifier.height(16.dp))
                if (!isProcessing) {
                    PaymentOption("PIX", PaymentMethod.PIX, onConfirm)
                    PaymentOption("DINHEIRO", PaymentMethod.CASH, onConfirm)
                    PaymentOption("CARTÃO", PaymentMethod.CARD, onConfirm)
                    PaymentOption("OUTRO", PaymentMethod.OTHER, onConfirm)
                    Spacer(modifier = Modifier.height(8.dp))
                    TextButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) {
                        Text("CANCELAR")
                    }
                } else {
                    CircularProgressIndicator(modifier = Modifier.padding(16.dp).align(Alignment.CenterHorizontally))
                }
            }
        }
    }
}

@Composable
fun PaymentOption(label: String, method: PaymentMethod, onConfirm: (PaymentMethod) -> Unit) {
    Button(
        onClick = { onConfirm(method) },
        modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp)
    ) {
        Text(label)
    }
}
