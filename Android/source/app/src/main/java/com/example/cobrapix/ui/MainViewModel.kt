package com.example.cobrapix.ui

import android.app.Application
import android.content.Intent
import android.util.Log
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.core.net.toUri
import androidx.room.withTransaction
import com.example.cobrapix.data.*
import com.example.cobrapix.logic.AlarmHelper
import com.example.cobrapix.billing.BillingManager
import com.example.cobrapix.billing.SubscriptionRepository
import com.example.cobrapix.billing.ClientLimitManager
import com.example.cobrapix.billing.EntitlementResult
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import java.util.*
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader

sealed class PayableItem {
    abstract val id: Long
    abstract val clientId: Long
    abstract val value: Long
    abstract val dueDate: Long
    abstract val status: InstallmentStatus
    abstract val clientName: String

    data class InstallmentType(
        val installment: Installment,
        override val clientName: String,
    ) : PayableItem() {
        override val id = installment.id
        override val clientId = installment.clientId
        override val value = installment.value
        override val dueDate = installment.dueDate
        override val status = installment.status
    }

    data class MonthlyType(
        val payment: MonthlyPayment,
        override val clientName: String,
        override val clientId: Long
    ) : PayableItem() {
        override val id = payment.id + 1000000
        override val value = payment.value
        override val dueDate = payment.dueDate
        override val status = payment.status
    }

    data class ExpenseType(
        val expense: Expense
    ) : PayableItem() {
        override val id = expense.id + 2000000
        override val clientId = -1L
        override val value = -expense.value // Negative for expenses
        override val dueDate = expense.date
        override val status = InstallmentStatus.PAID
        override val clientName = expense.description
        val paymentMethod = expense.paymentMethod
    }
}

data class ClientScore(
    val rank: String,
    val percentage: Int,
    val totalPaid: Int,
    val onTime: Int
)

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val db = CobraPixDatabase.getDatabase(application)
    private val clientDao = db.clientDao()
    private val installmentDao = db.installmentDao()
    private val saleDao = db.saleDao()
    val settings = SettingsRepository(application)

    val billingManager = BillingManager(application)
    val subscriptionRepository = SubscriptionRepository(billingManager)
    val clientLimitManager = ClientLimitManager(clientDao, subscriptionRepository)

    // --- Optimized Flows ---
    val activeClients = clientDao.getAllActiveClients()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val archivedClients = clientDao.getArchivedClients()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val allInstallments = installmentDao.getAllInstallments()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val activeMonthlyCustomers = db.monthlyCustomerDao().getActiveMonthlyCustomers()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    private val allMonthlyPayments = db.monthlyPaymentDao().getAllPaymentsFlow()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val activeOperationalClients = combine(activeClients, allInstallments, allMonthlyPayments, activeMonthlyCustomers) { clients, installments, payments, customers ->
        clients.filter { client ->
            val pendingInst = installments.any { it.clientId == client.id && it.status != InstallmentStatus.PAID }
            val customer = customers.find { it.clientId == client.id }
            val pendingMonthly = customer?.let { c -> payments.any { it.monthlyCustomerId == c.id && it.status != InstallmentStatus.PAID } } ?: false
            pendingInst || pendingMonthly
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val settledClients = combine(activeClients, allInstallments, allMonthlyPayments, activeMonthlyCustomers) { clients, installments, payments, customers ->
        clients.filter { client ->
            val hasInst = installments.any { it.clientId == client.id }
            val customer = customers.find { it.clientId == client.id }
            if (!hasInst && customer == null) return@filter false
            val pendingInst = installments.any { it.clientId == client.id && it.status != InstallmentStatus.PAID }
            val pendingMonthly = customer?.let { c -> payments.any { it.monthlyCustomerId == c.id && it.status != InstallmentStatus.PAID } } ?: false
            !pendingInst && !pendingMonthly
        }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val totalSold = installmentDao.getTotalSoldActive()
    val totalReceived = installmentDao.getTotalReceivedActive()
    val totalToReceive = installmentDao.getTotalToReceiveActive()

    val totalOverdueValue = combine(allInstallments, activeClients, allMonthlyPayments, activeMonthlyCustomers) { insts, clients, pays, customers ->
        val now = System.currentTimeMillis()
        val sumInst = insts.filter { it.status != InstallmentStatus.PAID && it.dueDate < now && clients.any { c -> c.id == it.clientId } }.sumOf { it.value }
        val sumPays = pays.filter { it.status != InstallmentStatus.PAID && it.dueDate < now && customers.any { c -> c.id == it.monthlyCustomerId } }.sumOf { it.value }
        sumInst + sumPays
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), 0L)

    val receivedToday = combine(allInstallments, allMonthlyPayments) { insts, pays ->
        val start = getStartOfDay()
        val end = getEndOfDay()
        val sumInst = insts.filter { it.status == InstallmentStatus.PAID && (it.paymentDate ?: 0L) in start..end }.sumOf { it.value }
        val sumPays = pays.filter { it.status == InstallmentStatus.PAID && (it.paymentDate ?: 0L) in start..end }.sumOf { it.value }
        sumInst + sumPays
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), 0L)

    fun getAnnualForecast(year: Int): Flow<Long> = combine(allInstallments, activeClients, allMonthlyPayments, activeMonthlyCustomers) { insts, clients, pays, customers ->
        val cal = Calendar.getInstance()
        cal.set(year, 0, 1, 0, 0, 0); cal.set(Calendar.MILLISECOND, 0); val start = cal.timeInMillis
        cal.set(year, 11, 31, 23, 59, 59); cal.set(Calendar.MILLISECOND, 999); val end = cal.timeInMillis
        
        val sumInst = insts.filter { it.status != InstallmentStatus.PAID && it.dueDate in start..end && clients.any { c -> c.id == it.clientId } }.sumOf { it.value }
        val sumPays = pays.filter { it.status != InstallmentStatus.PAID && it.dueDate in start..end && customers.any { c -> c.id == it.monthlyCustomerId } }.sumOf { it.value }
        sumInst + sumPays
    }

    val activeClientsCount = activeClients.map { it.size }
    val paidInstallmentsCount = combine(allInstallments, activeClients) { insts, clients ->
        insts.count { inst -> inst.status == InstallmentStatus.PAID && clients.any { it.id == inst.clientId } }
    }
    val pendingInstallmentsCount = combine(allInstallments, activeClients) { insts, clients ->
        insts.count { inst -> inst.status != InstallmentStatus.PAID && clients.any { it.id == inst.clientId } }
    }

    private val _paymentSuccessEvent = MutableSharedFlow<PayableItem>()
    val paymentSuccessEvent = _paymentSuccessEvent.asSharedFlow()
    private var isProcessingPayment = false

    val combinedOverdueItems: Flow<List<PayableItem>> = combine(installmentDao.getActiveOverdueInstallments(System.currentTimeMillis()), db.monthlyPaymentDao().getActiveOverduePayments(System.currentTimeMillis()), activeClients, activeMonthlyCustomers) { insts, payments, clients, customers ->
        val list = mutableListOf<PayableItem>()
        insts.forEach { inst -> list.add(PayableItem.InstallmentType(inst, clients.find { it.id == inst.clientId }?.name ?: "Cliente")) }
        payments.forEach { p -> 
            val client = customers.find { it.id == p.monthlyCustomerId }?.let { c -> clients.find { it.id == c.clientId } }
            list.add(PayableItem.MonthlyType(p, client?.name ?: "Mensalista", client?.id ?: 0L))
        }
        list.sortedBy { it.dueDate }
    }

    val combinedTodayItems: Flow<List<PayableItem>> = combine(installmentDao.getActiveInstallmentsByDateRange(getStartOfDay(), getEndOfDay()), db.monthlyPaymentDao().getActivePaymentsByDateRange(getStartOfDay(), getEndOfDay()), activeClients, activeMonthlyCustomers) { insts, payments, clients, customers ->
        val list = mutableListOf<PayableItem>()
        insts.forEach { inst -> list.add(PayableItem.InstallmentType(inst, clients.find { it.id == inst.clientId }?.name ?: "Cliente")) }
        payments.forEach { p -> 
            val client = customers.find { it.id == p.monthlyCustomerId }?.let { c -> clients.find { it.id == c.clientId } }
            list.add(PayableItem.MonthlyType(p, client?.name ?: "Mensalista", client?.id ?: 0L))
        }
        list
    }

    val monthlyRevenueExpected = db.monthlyPaymentDao().getMonthlyRevenueExpected()
    val monthlyRevenueReceived = db.monthlyPaymentDao().getMonthlyReceived(Calendar.getInstance()[Calendar.MONTH] + 1, Calendar.getInstance()[Calendar.YEAR])

    // --- CRUD ---
    suspend fun checkClientLimit(): EntitlementResult {
        return clientLimitManager.canCreateOrReactivateClient()
    }

    fun getClient(id: Long) = flow { emit(clientDao.getClientById(id)) }
    fun searchClients(query: String) = clientDao.searchClients(query)
    fun archiveClient(client: Client) { viewModelScope.launch { clientDao.updateClient(client.copy(isArchived = true)); AlarmHelper.cancelAlarms(getApplication(), client.id) } }
    
    fun unarchiveClient(client: Client) { 
        viewModelScope.launch { 
            if (clientLimitManager.canCreateOrReactivateClient() == EntitlementResult.Allowed) {
                clientDao.updateClient(client.copy(isArchived = false))
                rescheduleAllActiveAlarms() 
            }
        } 
    }
    
    fun updateClient(client: Client) { viewModelScope.launch { clientDao.updateClient(client) } }
    fun deleteClient(client: Client) { viewModelScope.launch { AlarmHelper.cancelAlarms(getApplication(), client.id); clientDao.deleteClient(client) } }

    fun addExpense(description: String, value: Long, method: PaymentMethod) {
        viewModelScope.launch(Dispatchers.IO) {
            db.expenseDao().insertExpense(Expense(description = description, value = value, date = System.currentTimeMillis(), paymentMethod = method))
        }
    }

    fun deleteExpense(expense: Expense) {
        viewModelScope.launch(Dispatchers.IO) {
            db.expenseDao().deleteExpense(expense)
        }
    }

    fun updateInstallment(installment: Installment) {
        viewModelScope.launch {
            installmentDao.updateInstallment(installment)
            val client = clientDao.getClientById(installment.clientId)
            if (client != null && !client.isArchived) {
                if (installment.status != InstallmentStatus.PAID) AlarmHelper.scheduleClientAlarm(getApplication(), client.id, client.name, settings.getAlarmHours())
                else {
                    val pending = getClientPayableItems(client.id).firstOrNull()?.any { it.status != InstallmentStatus.PAID } ?: false
                    if (!pending) AlarmHelper.cancelAlarms(getApplication(), client.id)
                }
            }
        }
    }

    fun getPayableItem(id: Long) = flow {
        if (id >= 1000000) {
            db.monthlyPaymentDao().getById(id - 1000000)?.let { p ->
                val client = db.monthlyCustomerDao().getById(p.monthlyCustomerId)?.let { clientDao.getClientById(it.clientId) }
                emit(PayableItem.MonthlyType(p, client?.name ?: "Mensalista", client?.id ?: 0L))
            } ?: emit(null)
        } else {
            installmentDao.getInstallmentById(id)?.let { inst -> emit(PayableItem.InstallmentType(inst, clientDao.getClientById(inst.clientId)?.name ?: "Cliente")) } ?: emit(null)
        }
    }

    fun updatePayableItem(item: PayableItem) {
        viewModelScope.launch {
            when (item) {
                is PayableItem.InstallmentType -> updateInstallment(item.installment)
                is PayableItem.MonthlyType -> {
                    db.monthlyPaymentDao().update(item.payment)
                    clientDao.getClientById(item.clientId)?.let { client ->
                        if (!client.isArchived) {
                            if (item.status != InstallmentStatus.PAID) {
                                AlarmHelper.scheduleClientAlarm(getApplication(), client.id, client.name, settings.getAlarmHours())
                            } else {
                                val pending = getClientPayableItems(client.id).firstOrNull()?.any { it.status != InstallmentStatus.PAID } ?: false
                                if (!pending) AlarmHelper.cancelAlarms(getApplication(), client.id)
                            }
                        }
                    }
                }
                is PayableItem.ExpenseType -> {
                    // Atualizações de despesas não são suportadas diretamente nesta view
                }
            }
        }
    }

    fun getMonthlyCustomer(clientId: Long) = flow { emit(db.monthlyCustomerDao().getByClientId(clientId)) }
    fun updateMonthlyCustomer(customer: MonthlyCustomer) { viewModelScope.launch { db.monthlyCustomerDao().update(customer); clientDao.getClientById(customer.clientId)?.let { if (!it.isArchived) AlarmHelper.scheduleClientAlarm(getApplication(), it.id, it.name, settings.getAlarmHours()) } } }

    fun updateGlobalHoursAndApplyToAll(slots: List<Int>) {
        viewModelScope.launch(Dispatchers.IO) {
            settings.saveHourSlots(slots)
            
            // Força a atualização para todos os clientes, para que as novas regras globais afetem as vendas existentes.
            val sales = saleDao.getAllSalesDirect()
            sales.forEach { s ->
                saleDao.updateSale(s.copy(
                    hour1 = slots[0], hour2 = slots[1], hour3 = slots[2], hour4 = slots[3],
                    hour5 = slots[4], hour6 = slots[5], hour7 = slots[6]
                ))
            }
            
            val customers = db.monthlyCustomerDao().getAllMonthlyCustomersDirect()
            customers.forEach { c ->
                db.monthlyCustomerDao().update(c.copy(
                    hour1 = slots[0], hour2 = slots[1], hour3 = slots[2], hour4 = slots[3],
                    hour5 = slots[4], hour6 = slots[5], hour7 = slots[6]
                ))
            }
            
            rescheduleAllActiveAlarms()
        }
    }

    fun rescheduleAllActiveAlarms() {
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val clients = clientDao.getAllClientsDirect()
                val globalHours = settings.getAlarmHours()
                val endOfToday = Calendar.getInstance().apply { set(Calendar.HOUR_OF_DAY, 23); set(Calendar.MINUTE, 59); set(Calendar.SECOND, 59); set(Calendar.MILLISECOND, 999) }.timeInMillis

                clients.forEach { client ->
                    val insts = installmentDao.getInstallmentsByClient(client.id).firstOrNull() ?: emptyList()
                    val mCust = db.monthlyCustomerDao().getByClientId(client.id)
                    val mPays = if (mCust != null) db.monthlyPaymentDao().getPaymentsByCustomer(mCust.id).firstOrNull() ?: emptyList() else emptyList()
                    
                    val pending = (insts + mPays.map { Installment(id=0, clientId=client.id, saleId=0, number=0, value=it.value, dueDate=it.dueDate, status=it.status) })
                        .filter { it.status != InstallmentStatus.PAID }

                    if (pending.isNotEmpty() && !client.isArchived) {
                        val earliest = pending.minByOrNull { it.dueDate }!!
                        if (earliest.dueDate > endOfToday) AlarmHelper.scheduleClientAlarm(getApplication(), client.id, client.name, globalHours, earliest.dueDate)
                        else AlarmHelper.scheduleClientAlarm(getApplication(), client.id, client.name, globalHours)
                    } else AlarmHelper.cancelAlarms(getApplication(), client.id)
                }
            } catch (e: Exception) { Log.e("CobraPix", "Reschedule Error", e) }
        }
    }

    fun getClientScore(clientId: Long): Flow<ClientScore> = combine(installmentDao.getInstallmentsByClient(clientId), flow { val c = db.monthlyCustomerDao().getByClientId(clientId); if (c != null) emitAll(db.monthlyPaymentDao().getPaymentsByCustomer(c.id)) else emit(emptyList()) }) { insts, monthly ->
        val paid = (insts + monthly).filter { (it is Installment && it.status == InstallmentStatus.PAID) || (it is MonthlyPayment && it.status == InstallmentStatus.PAID) }
        if (paid.isEmpty()) return@combine ClientScore("NOVO", 0, 0, 0)
        var onTime = 0
        paid.forEach { p ->
            val pDate = if (p is Installment) (p.paymentDate ?: 0L) else (p as MonthlyPayment).paymentDate ?: 0L
            val dDate = if (p is Installment) p.dueDate else (p as MonthlyPayment).dueDate
            if (pDate <= (dDate + 86399000L)) onTime++
        }
        val pct = (onTime * 100) / paid.size
        val rank = when { pct >= 95 -> "S"; pct >= 80 -> "A"; pct >= 50 -> "B"; else -> "C" }
        ClientScore(rank, pct, paid.size, onTime)
    }

    fun playTestSound() { viewModelScope.launch { settings.notificationSoundUri.firstOrNull()?.let { val mp = android.media.MediaPlayer(); mp.setDataSource(getApplication(), it.toUri()); mp.prepare(); mp.start() } } }

    fun addClientWithSale(name: String, phone: String, cpf: String, address: String, installmentValue: Long, count: Int, firstDate: Long, obs: String, fifth: Boolean = false, custom: List<Int>? = null) {
        viewModelScope.launch(Dispatchers.IO) {
            if (clientLimitManager.canCreateOrReactivateClient() != EntitlementResult.Allowed) return@launch
            
            val safeHours = if (custom != null && custom.size >= 2) settings.getAlarmHours().toMutableList().apply { this[0] = custom[0]; this[1] = custom[1] } else settings.getAlarmHours()
            val clientId = clientDao.insertClient(Client(name = name, phone = normalizePhoneNumber(phone), cpf = cpf, address = address, observation = obs))
            val saleId = saleDao.insertSale(Sale(clientId = clientId, totalValue = installmentValue * count, installmentCount = count, useFifthBusinessDay = fifth, hour1 = safeHours[0], hour2 = safeHours[1], hour3 = safeHours[2], hour4 = safeHours[3], hour5 = safeHours[4], hour6 = safeHours[5], hour7 = safeHours[6]))
            val calendar = Calendar.getInstance().apply { timeInMillis = firstDate }
            val list = mutableListOf<Installment>()
            for (i in 1..count) {
                val due = if (i > 1) { calendar.add(Calendar.MONTH, 1); if (fifth) calculateFifthBusinessDay(calendar[Calendar.MONTH], calendar[Calendar.YEAR]) else calendar.timeInMillis } else { if (fifth) calculateFifthBusinessDay(calendar[Calendar.MONTH], calendar[Calendar.YEAR]) else firstDate }
                list.add(Installment(clientId = clientId, saleId = saleId, number = i, value = installmentValue, dueDate = due))
            }
            installmentDao.insertInstallments(list); AlarmHelper.scheduleClientAlarm(getApplication(), clientId, name, settings.getAlarmHours())
        }
    }

    fun addNewSaleToExistingClient(clientId: Long, value: Long, count: Int, firstDate: Long, fifth: Boolean = false) {
        viewModelScope.launch(Dispatchers.IO) {
            val client = clientDao.getClientById(clientId) ?: return@launch
            val safeHours = saleDao.getSalesByClient(clientId).firstOrNull()?.firstOrNull()?.let { listOf(it.hour1, it.hour2, it.hour3, it.hour4, it.hour5, it.hour6, it.hour7) } ?: settings.getAlarmHours()
            val saleId = saleDao.insertSale(Sale(clientId = clientId, totalValue = value * count, installmentCount = count, useFifthBusinessDay = fifth, hour1 = safeHours[0], hour2 = safeHours[1], hour3 = safeHours[2], hour4 = safeHours[3], hour5 = safeHours[4], hour6 = safeHours[5], hour7 = safeHours[6]))
            val calendar = Calendar.getInstance().apply { timeInMillis = firstDate }
            val list = mutableListOf<Installment>()
            for (i in 1..count) {
                val due = if (i > 1) { calendar.add(Calendar.MONTH, 1); if (fifth) calculateFifthBusinessDay(calendar[Calendar.MONTH], calendar[Calendar.YEAR]) else calendar.timeInMillis } else { if (fifth) calculateFifthBusinessDay(calendar[Calendar.MONTH], calendar[Calendar.YEAR]) else firstDate }
                list.add(Installment(clientId = clientId, saleId = saleId, number = i, value = value, dueDate = due))
            }
            installmentDao.insertInstallments(list); AlarmHelper.scheduleClientAlarm(getApplication(), clientId, client.name, settings.getAlarmHours())
        }
    }

    fun addMonthlyCustomer(name: String, phone: String, cpf: String, address: String, value: Long, day: Int, start: Long, obs: String, custom: List<Int>? = null) {
        viewModelScope.launch(Dispatchers.IO) {
            if (clientLimitManager.canCreateOrReactivateClient() != EntitlementResult.Allowed) return@launch

            val safeHours = if (custom != null && custom.size >= 2) settings.getAlarmHours().toMutableList().apply { this[0] = custom[0]; this[1] = custom[1] } else settings.getAlarmHours()
            val clientId = clientDao.insertClient(Client(name = name, phone = normalizePhoneNumber(phone), cpf = cpf, address = address, observation = obs))
            val customerId = db.monthlyCustomerDao().insert(MonthlyCustomer(clientId = clientId, monthlyValue = value, dueDay = day, startDate = start, hour1 = safeHours[0], hour2 = safeHours[1], hour3 = safeHours[2], hour4 = safeHours[3], hour5 = safeHours[4], hour6 = safeHours[5], hour7 = safeHours[6]))
            db.monthlyPaymentDao().insert(MonthlyPayment(monthlyCustomerId = customerId, month = Calendar.getInstance().apply { timeInMillis = start }[Calendar.MONTH] + 1, year = Calendar.getInstance().apply { timeInMillis = start }[Calendar.YEAR], value = value, dueDate = start))
            AlarmHelper.scheduleClientAlarm(getApplication(), clientId, name, settings.getAlarmHours())
        }
    }

    fun getClientSale(clientId: Long) = flow { emit(saleDao.getSalesByClient(clientId).firstOrNull()?.firstOrNull()) }
    fun updateSale(sale: Sale) { viewModelScope.launch(Dispatchers.IO) { saleDao.updateSale(sale); clientDao.getClientById(sale.clientId)?.let { AlarmHelper.scheduleClientAlarm(getApplication(), it.id, it.name, settings.getAlarmHours()) } } }

    private fun calculateFifthBusinessDay(m: Int, y: Int): Long {
        val cal = Calendar.getInstance().apply { set(y, m, 1, 0, 0, 0); set(Calendar.MILLISECOND, 0) }
        var count = 0
        while (count < 5) { if (cal[Calendar.DAY_OF_WEEK] != Calendar.SATURDAY && cal[Calendar.DAY_OF_WEEK] != Calendar.SUNDAY) count++; if (count < 5) cal.add(Calendar.DAY_OF_MONTH, 1) }
        return cal.timeInMillis
    }

    private fun normalizePhoneNumber(p: String): String { val d = p.filter { it.isDigit() }; return if (d.length == 11) "55$d" else d }
    
    private suspend fun generateNextMonthlyPayment(customerId: Long, last: MonthlyPayment) {
        db.monthlyCustomerDao().getById(customerId)?.let { c ->
            if (!c.isActive || c.isArchived) return
            val next = Calendar.getInstance().apply { set(last.year, last.month - 1, c.dueDay); add(Calendar.MONTH, 1) }
            val nextMonth = next[Calendar.MONTH] + 1
            val nextYear = next[Calendar.YEAR]
            
            // Verificação dupla para evitar duplicação (CORREÇÃO 4)
            val existing = db.monthlyPaymentDao().getPaymentsByCustomer(customerId).firstOrNull()?.find { it.month == nextMonth && it.year == nextYear }
            if (existing == null) {
                db.monthlyPaymentDao().insert(MonthlyPayment(monthlyCustomerId = customerId, month = nextMonth, year = nextYear, value = c.monthlyValue, dueDate = next.timeInMillis))
                clientDao.getClientById(c.clientId)?.let { AlarmHelper.scheduleClientAlarm(getApplication(), it.id, it.name, settings.getAlarmHours()) }
            }
        }
    }

    sealed class PaymentResult {
        object Success : PaymentResult()
        object InvalidAmount : PaymentResult()
        object ConcurrencyOrBalanceChanged : PaymentResult()
        data class Error(val message: String) : PaymentResult()
    }

    fun markAsPaid(item: PayableItem, method: PaymentMethod = PaymentMethod.PIX) {
        if (isProcessingPayment) return
        isProcessingPayment = true
        viewModelScope.launch {
            try {
                when(item) {
                    is PayableItem.InstallmentType -> { 
                        val remaining = item.installment.value - item.installment.paidAmount
                        if (remaining > 0) {
                            processPartialPayment(item, remaining, method)
                        }
                    }
                    is PayableItem.MonthlyType -> { 
                        if (item.status != InstallmentStatus.PAID) { 
                            db.monthlyPaymentDao().update(item.payment.copy(status = InstallmentStatus.PAID, paymentDate = System.currentTimeMillis(), paymentMethod = method))
                            generateNextMonthlyPayment(item.payment.monthlyCustomerId, item.payment)
                            val pending = getClientPayableItems(item.clientId).firstOrNull()?.any { it.status != InstallmentStatus.PAID && it.id != item.id } ?: false
                            if (!pending) AlarmHelper.cancelAlarms(getApplication(), item.clientId)
                            _paymentSuccessEvent.emit(item) 
                        } 
                    }
                    is PayableItem.ExpenseType -> { }
                }
            } finally { isProcessingPayment = false }
        }
    }

    suspend fun registerPartialPayment(item: PayableItem.InstallmentType, valuePaid: Long, method: PaymentMethod): PaymentResult {
        if (isProcessingPayment) return PaymentResult.Error("Operação em andamento.")
        isProcessingPayment = true
        try {
            return processPartialPayment(item, valuePaid, method)
        } finally {
            isProcessingPayment = false
        }
    }

    private suspend fun processPartialPayment(item: PayableItem.InstallmentType, valuePaid: Long, method: PaymentMethod): PaymentResult {
        if (valuePaid <= 0L) return PaymentResult.InvalidAmount
        
        var finalInst: Installment? = null
        var isFullyPaid = false
        val pDate = System.currentTimeMillis()
        var concurrencyError = false
        
        kotlinx.coroutines.withContext(Dispatchers.IO) {
            db.withTransaction {
                val rows = db.installmentDao().addPaymentIfValid(item.installment.id, valuePaid)
                if (rows != 1) {
                    Log.e("CobraPix", "Pagamento recusado: saldo inválido ou erro de concorrência para parcela ${item.installment.id}")
                    concurrencyError = true
                    return@withTransaction
                }
                
                db.installmentPaymentDao().insertPayment(
                    InstallmentPayment(installmentId = item.installment.id, value = valuePaid, paymentDate = pDate, paymentMethod = method)
                )
                
                val updated = db.installmentDao().getInstallmentById(item.installment.id) ?: return@withTransaction
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
        }
        
        if (concurrencyError) {
            return PaymentResult.ConcurrencyOrBalanceChanged
        }
        
        if (finalInst != null) {
            if (isFullyPaid) {
                val pending = getClientPayableItems(item.clientId).firstOrNull()?.any { it.status != InstallmentStatus.PAID && it.id != item.id } ?: false
                if (!pending) AlarmHelper.cancelAlarms(getApplication(), item.clientId)
                _paymentSuccessEvent.emit(item.copy(installment = finalInst!!))
            }
            return PaymentResult.Success
        } else {
            return PaymentResult.Error("Erro desconhecido ao processar parcela.")
        }
    }

    fun getClientPayableItems(clientId: Long): Flow<List<PayableItem>> = flow {
        clientDao.getClientById(clientId)?.let { client ->
            emitAll(combine(installmentDao.getInstallmentsByClient(clientId), db.monthlyCustomerDao().getByClientId(clientId)?.let { db.monthlyPaymentDao().getPaymentsByCustomer(it.id) } ?: flowOf(emptyList())) { insts, payments ->
                val list = mutableListOf<PayableItem>()
                insts.forEach { list.add(PayableItem.InstallmentType(it, client.name)) }
                payments.forEach { list.add(PayableItem.MonthlyType(it, client.name, clientId)) }
                list.sortedBy { it.dueDate }
            })
        }
    }

    fun getCombinedPaidItems(month: Int, year: Int): Flow<List<PayableItem>> = flow {
        val cal = Calendar.getInstance()
        cal.set(year, month - 1, 1, 0, 0, 0); cal.set(Calendar.MILLISECOND, 0); val start = cal.timeInMillis
        cal.add(Calendar.MONTH, 1); cal.add(Calendar.MILLISECOND, -1); val end = cal.timeInMillis

        emitAll(combine(installmentDao.getPaidInstallmentsByDateRange(start, end), db.monthlyPaymentDao().getPaidPaymentsByDateRange(start, end), db.expenseDao().getExpensesByDateRange(start, end), activeClients, activeMonthlyCustomers) { insts, payments, expenses, clients, customers ->
            val list = mutableListOf<PayableItem>()
            insts.forEach { inst -> list.add(PayableItem.InstallmentType(inst, clients.find { it.id == inst.clientId }?.name ?: "Cliente")) }
            payments.forEach { p -> 
                val client = customers.find { it.id == p.monthlyCustomerId }?.let { c -> clients.find { it.id == c.clientId } }
                list.add(PayableItem.MonthlyType(p, client?.name ?: "Mensalista", client?.id ?: 0L))
            }
            expenses.forEach { e ->
                list.add(PayableItem.ExpenseType(e))
            }
            list.sortedByDescending { it.dueDate }
        })
    }

    fun getInstallmentPayments(installmentId: Long): Flow<List<InstallmentPayment>> {
        return db.installmentPaymentDao().getPaymentsForInstallment(installmentId)
    }

    private fun getStartOfDay() = Calendar.getInstance().apply { set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0) }.timeInMillis
    private fun getEndOfDay() = Calendar.getInstance().apply { set(Calendar.HOUR_OF_DAY, 23); set(Calendar.MINUTE, 59); set(Calendar.SECOND, 59); set(Calendar.MILLISECOND, 999) }.timeInMillis

    fun exportToCSV(context: android.content.Context) {
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val csv = StringBuilder()
                // Adiciona o BOM (Byte Order Mark) para forçar o Excel e o Planilhas a lerem como UTF-8 (corrige acentos)
                csv.append("\uFEFF")
                csv.append("Data da Baixa;Tipo;Descricao;Detalhes;Metodo;Entrada;Saida\n")
                
                // Funções de formatação segura para tabelas BR
                fun cleanText(text: String) = text.replace(";", ",").replace("\n", " ")
                fun formatMoney(cents: Long) = String.format(Locale("pt", "BR"), "%.2f", cents / 100.0)

                // Entradas (Vendas)
                val clients = clientDao.getAllClientsDirect()
                clients.forEach { c ->
                    val insts = installmentDao.getInstallmentsByClient(c.id).firstOrNull() ?: emptyList()
                    insts.filter { it.status == InstallmentStatus.PAID }.forEach { i ->
                        val pDate = i.paymentDate?.let { java.text.SimpleDateFormat("dd/MM/yyyy HH:mm").format(Date(it)) } ?: ""
                        csv.append("$pDate;Venda Parcelada;${cleanText(c.name)};Parcela ${i.number};${i.paymentMethod};${formatMoney(i.value)};0,00\n")
                    }
                    val mCust = db.monthlyCustomerDao().getByClientId(c.id)
                    if (mCust != null) {
                        val mPays = db.monthlyPaymentDao().getPaymentsByCustomer(mCust.id).firstOrNull() ?: emptyList()
                        mPays.filter { it.status == InstallmentStatus.PAID }.forEach { p ->
                            val pDate = p.paymentDate?.let { java.text.SimpleDateFormat("dd/MM/yyyy HH:mm").format(Date(it)) } ?: ""
                            csv.append("$pDate;Mensalidade;${cleanText(c.name)};Mes ${p.month}/${p.year};${p.paymentMethod};${formatMoney(p.value)};0,00\n")
                        }
                    }
                }

                // Saídas (Despesas)
                val expenses = db.expenseDao().getAllExpensesDirect()
                expenses.forEach { e ->
                    val eDate = java.text.SimpleDateFormat("dd/MM/yyyy HH:mm").format(Date(e.date))
                    csv.append("$eDate;Despesa;${cleanText(e.description)};-;${e.paymentMethod};0,00;${formatMoney(e.value)}\n")
                }

                val fileName = "LivroCaixa_CobraPix_${System.currentTimeMillis()}.csv"
                val file = java.io.File(context.cacheDir, fileName)
                file.writeText(csv.toString())

                val uri = androidx.core.content.FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
                val intent = Intent(Intent.ACTION_SEND).apply {
                    type = "text/csv"
                    putExtra(Intent.EXTRA_SUBJECT, "Relatório Livro Caixa - CobraPix")
                    putExtra(Intent.EXTRA_STREAM, uri)
                    addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                }
                context.startActivity(Intent.createChooser(intent, "Compartilhar Livro Caixa").apply { addFlags(Intent.FLAG_ACTIVITY_NEW_TASK) })
            } catch (e: Exception) { Log.e("CobraPix", "CSV Error", e) }
        }
    }

    // --- BLINDAGEM DE BACKUP ---
    fun exportBackup(uri: android.net.Uri) {
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val backup = JSONObject().apply {
                    put("app", "CobraPix"); put("backupVersion", 3)
                    put("clients", JSONArray().apply { clientDao.getAllClientsDirect().forEach { put(JSONObject().apply { put("id", it.id); put("name", it.name); put("phone", it.phone); put("cpf", it.cpf); put("address", it.address); put("observation", it.observation); put("registrationDate", it.registrationDate); put("isArchived", it.isArchived) }) } })
                    put("sales", JSONArray().apply { saleDao.getAllSalesDirect().forEach { put(JSONObject().apply { put("id", it.id); put("clientId", it.clientId); put("totalValue", it.totalValue); put("installmentCount", it.installmentCount); put("saleDate", it.saleDate); put("useFifthBusinessDay", it.useFifthBusinessDay); put("hour1", it.hour1); put("hour2", it.hour2); put("hour3", it.hour3); put("hour4", it.hour4); put("hour5", it.hour5); put("hour6", it.hour6); put("hour7", it.hour7) }) } })
                    put("installments", JSONArray().apply { installmentDao.getAllInstallmentsDirect().forEach { put(JSONObject().apply { put("id", it.id); put("clientId", it.clientId); put("saleId", it.saleId); put("number", it.number); put("value", it.value); put("dueDate", it.dueDate); put("status", it.status.name); put("paymentDate", it.paymentDate ?: JSONObject.NULL); put("paymentMethod", it.paymentMethod.name); put("paidAmount", it.paidAmount) }) } })
                    put("monthly_customers", JSONArray().apply { db.monthlyCustomerDao().getAllMonthlyCustomersDirect().forEach { put(JSONObject().apply { put("id", it.id); put("clientId", it.clientId); put("monthlyValue", it.monthlyValue); put("dueDay", it.dueDay); put("startDate", it.startDate); put("isActive", it.isActive); put("isArchived", it.isArchived); put("hour1", it.hour1); put("hour2", it.hour2); put("hour3", it.hour3); put("hour4", it.hour4); put("hour5", it.hour5); put("hour6", it.hour6); put("hour7", it.hour7) }) } })
                    put("monthly_payments", JSONArray().apply { db.monthlyPaymentDao().getAllMonthlyPaymentsDirect().forEach { put(JSONObject().apply { put("id", it.id); put("monthlyCustomerId", it.monthlyCustomerId); put("month", it.month); put("year", it.year); put("value", it.value); put("dueDate", it.dueDate); put("status", it.status.name); put("paymentDate", it.paymentDate ?: JSONObject.NULL); put("paymentMethod", it.paymentMethod.name) }) } })
                    put("installment_payments", JSONArray().apply { db.installmentPaymentDao().getAllInstallmentPaymentsDirect().forEach { put(JSONObject().apply { put("id", it.id); put("installmentId", it.installmentId); put("value", it.value); put("paymentDate", it.paymentDate); put("paymentMethod", it.paymentMethod.name) }) } })
                    put("settings", JSONObject().apply {
                        put("pixKey", settings.pixKey.firstOrNull() ?: ""); put("receiverName", settings.receiverName.firstOrNull() ?: "")
                        put("city", settings.city.firstOrNull() ?: ""); put("messageTemplate", settings.messageTemplate.firstOrNull() ?: "")
                        put("messageTemplateMensalista", settings.messageTemplateMensalista.firstOrNull() ?: "")
                        put("confirmMessageInstallment", settings.confirmMessageInstallment.firstOrNull() ?: "")
                        put("confirmMessagePartialInstallment", settings.confirmMessagePartialInstallment.firstOrNull() ?: "")
                        put("confirmMessageMonthly", settings.confirmMessageMonthly.firstOrNull() ?: "")
                        put("confirmMessageLastInstallment", settings.confirmMessageLastInstallment.firstOrNull() ?: "")
                        put("notificationSoundUri", settings.notificationSoundUri.firstOrNull() ?: JSONObject.NULL)
                        put("h1", settings.hourSlot1.firstOrNull() ?: 540); put("h2", settings.hourSlot2.firstOrNull() ?: 660)
                        put("h3", settings.hourSlot3.firstOrNull() ?: 780); put("h4", settings.hourSlot4.firstOrNull() ?: 900)
                        put("h5", settings.hourSlot5.firstOrNull() ?: 1020); put("h6", settings.hourSlot6.firstOrNull() ?: 1140); put("h7", settings.hourSlot7.firstOrNull() ?: 1260)
                    })
                }
                getApplication<Application>().contentResolver.openOutputStream(uri)?.use { it.write(backup.toString().toByteArray()) }
            } catch (e: Exception) { Log.e("CobraPix", "Backup Error", e) }
        }
    }

    fun importBackup(uri: android.net.Uri) {
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val content = StringBuilder()
                getApplication<Application>().contentResolver.openInputStream(uri)?.use { input -> BufferedReader(InputStreamReader(input)).use { reader -> var line: String?; while (reader.readLine().also { line = it } != null) content.append(line) } }
                val backup = JSONObject(content.toString())
                if (backup.optString("app") != "CobraPix") return@launch
                db.clearAllTables()
                backup.optJSONArray("clients")?.let { for (i in 0 until it.length()) { val obj = it.getJSONObject(i); clientDao.insertClient(Client(id = obj.getLong("id"), name = obj.getString("name"), phone = obj.getString("phone"), cpf = obj.optString("cpf", ""), address = obj.optString("address", ""), observation = obj.optString("observation", ""), registrationDate = obj.optLong("registrationDate", System.currentTimeMillis()), isArchived = obj.optBoolean("isArchived", false))) } }
                backup.optJSONArray("sales")?.let { for (i in 0 until it.length()) { val obj = it.getJSONObject(i); saleDao.insertSale(Sale(id = obj.getLong("id"), clientId = obj.getLong("clientId"), totalValue = obj.getLong("totalValue"), installmentCount = obj.getInt("installmentCount"), saleDate = obj.getLong("saleDate"), useFifthBusinessDay = obj.optBoolean("useFifthBusinessDay", false), hour1 = obj.optInt("hour1", 540), hour2 = obj.optInt("hour2", 660), hour3 = obj.optInt("hour3", 780), hour4 = obj.optInt("hour4", 900), hour5 = obj.optInt("hour5", 1020), hour6 = obj.optInt("hour6", 1140), hour7 = obj.optInt("hour7", 1260))) } }
                backup.optJSONArray("installments")?.let { val list = mutableListOf<Installment>(); for (i in 0 until it.length()) { val obj = it.getJSONObject(i); val status = InstallmentStatus.valueOf(obj.getString("status")); val value = obj.getLong("value"); val paidAmount = if (obj.has("paidAmount")) obj.getLong("paidAmount") else if (status == InstallmentStatus.PAID) value else 0L; list.add(Installment(id = obj.getLong("id"), clientId = obj.getLong("clientId"), saleId = obj.getLong("saleId"), number = obj.getInt("number"), value = value, dueDate = obj.getLong("dueDate"), status = status, paymentDate = if (obj.isNull("paymentDate")) null else obj.getLong("paymentDate"), paymentMethod = PaymentMethod.valueOf(obj.optString("paymentMethod", "NONE")), paidAmount = paidAmount)) }; installmentDao.insertInstallments(list) }
                backup.optJSONArray("installment_payments")?.let { for (i in 0 until it.length()) { val obj = it.getJSONObject(i); db.installmentPaymentDao().insertPayment(InstallmentPayment(id = obj.getLong("id"), installmentId = obj.getLong("installmentId"), value = obj.getLong("value"), paymentDate = obj.getLong("paymentDate"), paymentMethod = PaymentMethod.valueOf(obj.optString("paymentMethod", "NONE")))) } }
                backup.optJSONArray("monthly_customers")?.let { for (i in 0 until it.length()) { val obj = it.getJSONObject(i); db.monthlyCustomerDao().insert(MonthlyCustomer(id = obj.getLong("id"), clientId = obj.getLong("clientId"), monthlyValue = obj.getLong("monthlyValue"), dueDay = obj.getInt("dueDay"), startDate = obj.getLong("startDate"), isActive = obj.optBoolean("isActive", true), isArchived = obj.optBoolean("isArchived", false), hour1 = obj.optInt("hour1", 540), hour2 = obj.optInt("hour2", 660), hour3 = obj.optInt("hour3", 780), hour4 = obj.optInt("hour4", 900), hour5 = obj.optInt("hour5", 1020), hour6 = obj.optInt("hour6", 1140), hour7 = obj.optInt("hour7", 1260))) } }
                backup.optJSONArray("monthly_payments")?.let { for (i in 0 until it.length()) { val obj = it.getJSONObject(i); db.monthlyPaymentDao().insert(MonthlyPayment(id = obj.getLong("id"), monthlyCustomerId = obj.getLong("monthlyCustomerId"), month = obj.getInt("month"), year = obj.getInt("year"), value = obj.getLong("value"), dueDate = obj.getLong("dueDate"), status = InstallmentStatus.valueOf(obj.getString("status")), paymentDate = if (obj.isNull("paymentDate")) null else obj.getLong("paymentDate"), paymentMethod = PaymentMethod.valueOf(obj.optString("paymentMethod", "NONE")))) } }
                backup.optJSONObject("settings")?.let { s ->
                    settings.savePixKey(s.optString("pixKey", "")); settings.saveReceiverName(s.optString("receiverName", "")); settings.saveCity(s.optString("city", "")); settings.saveMessageTemplate(s.optString("messageTemplate", ""))
                    settings.saveMessageTemplateMensalista(s.optString("messageTemplateMensalista", ""))
                    if (s.has("confirmMessageInstallment")) settings.saveConfirmMessageInstallment(s.getString("confirmMessageInstallment"))
                    if (s.has("confirmMessagePartialInstallment")) settings.saveConfirmMessagePartialInstallment(s.getString("confirmMessagePartialInstallment"))
                    if (s.has("confirmMessageMonthly")) settings.saveConfirmMessageMonthly(s.getString("confirmMessageMonthly"))
                    if (s.has("confirmMessageLastInstallment")) settings.saveConfirmMessageLastInstallment(s.getString("confirmMessageLastInstallment"))
                    if (!s.isNull("notificationSoundUri")) settings.saveNotificationSoundUri(s.getString("notificationSoundUri"))
                    val hList = listOf(s.optInt("h1", 540), s.optInt("h2", 660), s.optInt("h3", 780), s.optInt("h4", 900), s.optInt("h5", 1020), s.optInt("h6", 1140), s.optInt("h7", 1260))
                    settings.saveHourSlots(hList)
                }
                rescheduleAllActiveAlarms()
            } catch (e: Exception) { Log.e("CobraPix", "Import Error", e) }
        }
    }
}
