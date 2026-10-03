package com.example.cobrapix.data

import androidx.room.*
import kotlinx.coroutines.flow.Flow

@Dao
interface ClientDao {
    @Query("SELECT * FROM clients WHERE isArchived = 0 ORDER BY name ASC")
    fun getAllActiveClients(): Flow<List<Client>>

    @Query("SELECT * FROM clients WHERE isArchived = 1 ORDER BY name ASC")
    fun getArchivedClients(): Flow<List<Client>>

    @Query("SELECT COUNT(*) FROM clients WHERE isArchived = 0")
    suspend fun getActiveClientCount(): Int

    @Query("SELECT * FROM clients ORDER BY name ASC")
    suspend fun getAllClientsDirect(): List<Client>

    @Query("SELECT * FROM clients WHERE name LIKE '%' || :query || '%' AND isArchived = 0")
    fun searchClients(query: String): Flow<List<Client>>

    @Query("SELECT * FROM clients WHERE id = :id")
    suspend fun getClientById(id: Long): Client?

    @Insert
    suspend fun insertClient(client: Client): Long

    @Update
    suspend fun updateClient(client: Client)

    @Delete
    suspend fun deleteClient(client: Client)
}

@Dao
interface SaleDao {
    @Query("SELECT * FROM sales")
    suspend fun getAllSalesDirect(): List<Sale>

    @Query("SELECT * FROM sales WHERE clientId = :clientId")
    fun getSalesByClient(clientId: Long): Flow<List<Sale>>

    @Insert
    suspend fun insertSale(sale: Sale): Long

    @Update
    suspend fun updateSale(sale: Sale)
}

@Dao
interface InstallmentDao {
    @Query("SELECT * FROM installments ORDER BY number ASC")
    fun getAllInstallments(): Flow<List<Installment>>

    @Query("SELECT * FROM installments ORDER BY number ASC")
    suspend fun getAllInstallmentsDirect(): List<Installment>

    @Query("""
        SELECT i.* FROM installments i
        JOIN clients c ON i.clientId = c.id
        WHERE c.isArchived = 0
        ORDER BY i.dueDate ASC, i.number ASC
    """)
    fun getActiveInstallments(): Flow<List<Installment>>

    @Query("SELECT * FROM installments WHERE clientId = :clientId ORDER BY number ASC")
    fun getInstallmentsByClient(clientId: Long): Flow<List<Installment>>

    @Query("""
        SELECT i.* FROM installments i
        JOIN clients c ON i.clientId = c.id
        WHERE c.isArchived = 0 AND i.dueDate BETWEEN :start AND :end
    """)
    fun getActiveInstallmentsByDateRange(start: Long, end: Long): Flow<List<Installment>>

    @Query("""
        SELECT i.* FROM installments i
        JOIN clients c ON i.clientId = c.id
        WHERE c.isArchived = 0 AND i.status IN ('PENDING', 'OVERDUE') AND i.dueDate < :currentDate
    """)
    fun getActiveOverdueInstallments(currentDate: Long): Flow<List<Installment>>

    @Query("SELECT * FROM installments WHERE id = :id")
    suspend fun getInstallmentById(id: Long): Installment?

    @Insert
    suspend fun insertInstallments(installments: List<Installment>)

    @Update
    suspend fun updateInstallment(installment: Installment)

    @Query("UPDATE installments SET status = 'OVERDUE' WHERE status = 'PENDING' AND dueDate < :currentDate")
    suspend fun updatePendingToOverdue(currentDate: Long)

    @Query("""
        SELECT SUM(totalValue) FROM sales s
        JOIN clients c ON s.clientId = c.id
        WHERE c.isArchived = 0
    """)
    fun getTotalSoldActive(): Flow<Long?>

    @Query("""
        SELECT SUM(paidAmount) FROM installments i
        JOIN clients c ON i.clientId = c.id
        WHERE c.isArchived = 0
    """)
    fun getTotalReceivedActive(): Flow<Long?>

    @Query("""
        SELECT SUM(value - paidAmount) FROM installments i
        JOIN clients c ON i.clientId = c.id
        WHERE c.isArchived = 0 AND i.status != 'PAID'
    """)
    fun getTotalToReceiveActive(): Flow<Long?>

    @Query("SELECT * FROM installments WHERE status IN ('PENDING', 'OVERDUE') AND dueDate < :currentDate")
    fun getOverdueInstallments(currentDate: Long): Flow<List<Installment>>

    @Query("SELECT * FROM installments WHERE dueDate BETWEEN :start AND :end")
    fun getInstallmentsByDateRange(start: Long, end: Long): Flow<List<Installment>>

    @Query("""
        SELECT SUM(value) FROM installments i
        JOIN clients c ON i.clientId = c.id
        WHERE c.isArchived = 0 AND i.status IN ('PENDING', 'OVERDUE') AND i.dueDate BETWEEN :start AND :end
    """)
    fun getForecastValue(start: Long, end: Long): Flow<Long?>

    @Query("""
        SELECT SUM(value - paidAmount) FROM installments i
        JOIN clients c ON i.clientId = c.id
        WHERE c.isArchived = 0 AND i.status IN ('PENDING', 'OVERDUE') AND i.dueDate < :now
    """)
    fun getTotalOverdueValue(now: Long): Flow<Long?>

    @Query("""
        UPDATE installments 
        SET paidAmount = paidAmount + :amount 
        WHERE id = :id 
        AND :amount > 0 
        AND paidAmount + :amount <= value
    """)
    suspend fun addPaymentIfValid(id: Long, amount: Long): Int

    @Query("""
        SELECT i.* FROM installments i
        JOIN clients c ON i.clientId = c.id
        WHERE i.status = 'PAID' AND i.paymentDate BETWEEN :start AND :end
    """)
    fun getPaidInstallmentsByDateRange(start: Long, end: Long): Flow<List<Installment>>
}

@Dao
interface InstallmentPaymentDao {
    @Query("SELECT * FROM installment_payments ORDER BY paymentDate ASC")
    suspend fun getAllInstallmentPaymentsDirect(): List<InstallmentPayment>

    @Query("SELECT * FROM installment_payments WHERE installmentId = :installmentId ORDER BY paymentDate ASC")
    fun getPaymentsForInstallment(installmentId: Long): Flow<List<InstallmentPayment>>

    @Query("SELECT * FROM installment_payments WHERE installmentId = :installmentId ORDER BY paymentDate ASC")
    suspend fun getPaymentsForInstallmentDirect(installmentId: Long): List<InstallmentPayment>

    @Query("""
        SELECT p.* FROM installment_payments p
        JOIN installments i ON p.installmentId = i.id
        JOIN clients c ON i.clientId = c.id
        WHERE c.isArchived = 0 AND p.paymentDate BETWEEN :start AND :end
    """)
    fun getPaymentsByDateRange(start: Long, end: Long): Flow<List<InstallmentPayment>>

    @Insert
    suspend fun insertPayment(payment: InstallmentPayment): Long

    @Delete
    suspend fun deletePayment(payment: InstallmentPayment)
}

@Dao
interface MonthlyCustomerDao {
    @Query("SELECT * FROM monthly_customers")
    suspend fun getAllMonthlyCustomersDirect(): List<MonthlyCustomer>

    @Query("SELECT * FROM monthly_customers WHERE isArchived = 0 AND isActive = 1")
    fun getActiveMonthlyCustomers(): Flow<List<MonthlyCustomer>>

    @Query("SELECT * FROM monthly_customers WHERE isArchived = 1")
    fun getArchivedMonthlyCustomers(): Flow<List<MonthlyCustomer>>

    @Query("SELECT * FROM monthly_customers WHERE id = :id")
    suspend fun getById(id: Long): MonthlyCustomer?

    @Query("SELECT * FROM monthly_customers WHERE clientId = :clientId")
    suspend fun getByClientId(clientId: Long): MonthlyCustomer?

    @Insert
    suspend fun insert(customer: MonthlyCustomer): Long

    @Update
    suspend fun update(customer: MonthlyCustomer)
}

@Dao
interface MonthlyPaymentDao {
    @Query("SELECT * FROM monthly_payments")
    fun getAllPaymentsFlow(): Flow<List<MonthlyPayment>>

    @Query("SELECT * FROM monthly_payments")
    suspend fun getAllMonthlyPaymentsDirect(): List<MonthlyPayment>

    @Query("SELECT * FROM monthly_payments WHERE monthlyCustomerId = :customerId ORDER BY dueDate DESC")
    fun getPaymentsByCustomer(customerId: Long): Flow<List<MonthlyPayment>>

    @Query("""
        SELECT p.* FROM monthly_payments p
        JOIN monthly_customers c ON p.monthlyCustomerId = c.id
        WHERE c.isArchived = 0 AND p.dueDate BETWEEN :start AND :end
    """)
    fun getActivePaymentsByDateRange(start: Long, end: Long): Flow<List<MonthlyPayment>>

    @Query("""
        SELECT p.* FROM monthly_payments p
        JOIN monthly_customers c ON p.monthlyCustomerId = c.id
        WHERE c.isArchived = 0 AND p.status IN ('PENDING', 'OVERDUE') AND p.dueDate < :currentDate
    """)
    fun getActiveOverduePayments(currentDate: Long): Flow<List<MonthlyPayment>>

    @Query("SELECT * FROM monthly_payments WHERE id = :id")
    suspend fun getById(id: Long): MonthlyPayment?

    @Insert
    suspend fun insert(payment: MonthlyPayment): Long

    @Update
    suspend fun update(payment: MonthlyPayment)

    @Query("SELECT SUM(monthlyValue) FROM monthly_customers WHERE isArchived = 0 AND isActive = 1")
    fun getMonthlyRevenueExpected(): Flow<Long?>

    @Query("""
        SELECT SUM(p.value) FROM monthly_payments p
        JOIN monthly_customers c ON p.monthlyCustomerId = c.id
        WHERE c.isArchived = 0 AND p.status = 'PAID' AND p.month = :month AND p.year = :year
    """)
    fun getMonthlyReceived(month: Int, year: Int): Flow<Long?>

    @Query("""
        SELECT p.* FROM monthly_payments p
        JOIN monthly_customers c ON p.monthlyCustomerId = c.id
        WHERE p.status = 'PAID' AND p.paymentDate BETWEEN :start AND :end
    """)
    fun getPaidPaymentsByDateRange(start: Long, end: Long): Flow<List<MonthlyPayment>>
}

@Dao
interface ExpenseDao {
    @Query("SELECT * FROM expenses ORDER BY date DESC")
    fun getAllExpenses(): Flow<List<Expense>>

    @Query("SELECT * FROM expenses ORDER BY date DESC")
    suspend fun getAllExpensesDirect(): List<Expense>

    @Query("SELECT * FROM expenses WHERE date BETWEEN :start AND :end ORDER BY date DESC")
    fun getExpensesByDateRange(start: Long, end: Long): Flow<List<Expense>>

    @Insert
    suspend fun insertExpense(expense: Expense): Long

    @Delete
    suspend fun deleteExpense(expense: Expense)
}
