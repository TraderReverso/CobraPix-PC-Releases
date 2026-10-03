package com.example.cobrapix.data

import androidx.room.Entity
import androidx.room.PrimaryKey
import androidx.room.ForeignKey
import androidx.room.Index

@Entity(tableName = "clients")
data class Client(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val name: String,
    val phone: String,
    val cpf: String = "",
    val address: String = "",
    val observation: String = "",
    val registrationDate: Long = System.currentTimeMillis(),
    val isArchived: Boolean = false
)

@Entity(
    tableName = "sales",
    foreignKeys = [
        ForeignKey(
            entity = Client::class,
            parentColumns = ["id"],
            childColumns = ["clientId"],
            onDelete = ForeignKey.CASCADE
        )
    ],
    indices = [Index("clientId")]
)
data class Sale(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val clientId: Long,
    val totalValue: Long, // Value in cents
    val installmentCount: Int,
    val saleDate: Long = System.currentTimeMillis(),
    val useFifthBusinessDay: Boolean = false,
    val hour1: Int = 540,  // 09:00 (new defaults V20)
    val hour2: Int = 660,  // 11:00
    val hour3: Int = 780,  // 13:00
    val hour4: Int = 900,  // 15:00
    val hour5: Int = 1020, // 17:00
    val hour6: Int = 1140, // 19:00
    val hour7: Int = 1260  // 21:00
)

enum class InstallmentStatus {
    PENDING,
    PAID,
    OVERDUE
}

enum class PaymentMethod {
    PIX,
    CASH,
    CARD,
    OTHER,
    NONE
}

@Entity(
    tableName = "installments",
    foreignKeys = [
        ForeignKey(
            entity = Client::class,
            parentColumns = ["id"],
            childColumns = ["clientId"],
            onDelete = ForeignKey.CASCADE
        ),
        ForeignKey(
            entity = Sale::class,
            parentColumns = ["id"],
            childColumns = ["saleId"],
            onDelete = ForeignKey.CASCADE
        )
    ],
    indices = [Index("clientId"), Index("saleId")]
)
data class Installment(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val clientId: Long,
    val saleId: Long,
    val number: Int, // e.g. 1 of 6
    val value: Long, // Value in cents
    val dueDate: Long,
    val status: InstallmentStatus = InstallmentStatus.PENDING,
    val paymentDate: Long? = null,
    val paymentMethod: PaymentMethod = PaymentMethod.NONE,
    val paidAmount: Long = 0L
)

@Entity(
    tableName = "installment_payments",
    foreignKeys = [
        ForeignKey(
            entity = Installment::class,
            parentColumns = ["id"],
            childColumns = ["installmentId"],
            onDelete = ForeignKey.CASCADE
        )
    ],
    indices = [Index("installmentId")]
)
data class InstallmentPayment(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val installmentId: Long,
    val value: Long,
    val paymentDate: Long,
    val paymentMethod: PaymentMethod
)

@Entity(
    tableName = "monthly_customers",
    foreignKeys = [
        ForeignKey(
            entity = Client::class,
            parentColumns = ["id"],
            childColumns = ["clientId"],
            onDelete = ForeignKey.CASCADE
        )
    ],
    indices = [Index("clientId")]
)
data class MonthlyCustomer(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val clientId: Long,
    val monthlyValue: Long, // cents
    val dueDay: Int,
    val startDate: Long,
    val isActive: Boolean = true,
    val isArchived: Boolean = false,
    val hour1: Int = 540,
    val hour2: Int = 660,
    val hour3: Int = 780,
    val hour4: Int = 900,
    val hour5: Int = 1020,
    val hour6: Int = 1140,
    val hour7: Int = 1260
)

@Entity(
    tableName = "monthly_payments",
    foreignKeys = [
        ForeignKey(
            entity = MonthlyCustomer::class,
            parentColumns = ["id"],
            childColumns = ["monthlyCustomerId"],
            onDelete = ForeignKey.CASCADE
        )
    ],
    indices = [Index("monthlyCustomerId")]
)
data class MonthlyPayment(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val monthlyCustomerId: Long,
    val month: Int, // 1-12
    val year: Int,
    val value: Long, // Adding value to each payment to support historical changes
    val dueDate: Long,
    val status: InstallmentStatus = InstallmentStatus.PENDING,
    val paymentDate: Long? = null,
    val paymentMethod: PaymentMethod = PaymentMethod.NONE
)

@Entity(tableName = "expenses")
data class Expense(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val description: String,
    val value: Long, // Value in cents
    val date: Long,
    val paymentMethod: PaymentMethod = PaymentMethod.NONE
)
