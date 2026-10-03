package com.example.cobrapix.data

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.TypeConverters
import androidx.room.TypeConverter

import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase

class Converters {
    @TypeConverter
    fun fromStatus(status: InstallmentStatus): String = status.name

    @TypeConverter
    fun toStatus(value: String): InstallmentStatus = InstallmentStatus.valueOf(value)

    @TypeConverter
    fun fromPaymentMethod(method: PaymentMethod): String = method.name

    @TypeConverter
    fun toPaymentMethod(value: String): PaymentMethod = PaymentMethod.valueOf(value)
}

@Database(entities = [Client::class, Sale::class, Installment::class, InstallmentPayment::class, MonthlyCustomer::class, MonthlyPayment::class, Expense::class], version = 9, exportSchema = false)
@TypeConverters(Converters::class)
abstract class CobraPixDatabase : RoomDatabase() {
    abstract fun clientDao(): ClientDao
    abstract fun saleDao(): SaleDao
    abstract fun installmentDao(): InstallmentDao
    abstract fun installmentPaymentDao(): InstallmentPaymentDao
    abstract fun monthlyCustomerDao(): MonthlyCustomerDao
    abstract fun monthlyPaymentDao(): MonthlyPaymentDao
    abstract fun expenseDao(): ExpenseDao

    companion object {
        // ... (previous migrations omitted for brevity, keeping only the execution parts if needed but let's just append V8)
        private val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE clients ADD COLUMN isArchived INTEGER NOT NULL DEFAULT 0")
                db.execSQL("ALTER TABLE installments ADD COLUMN paymentMethod TEXT NOT NULL DEFAULT 'NONE'")
            }
        }

        private val MIGRATION_2_3 = object : Migration(2, 3) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // Clients: Add cpf and address
                db.execSQL("ALTER TABLE clients ADD COLUMN cpf TEXT NOT NULL DEFAULT ''")
                db.execSQL("ALTER TABLE clients ADD COLUMN address TEXT NOT NULL DEFAULT ''")

                // Sales: Double to Long (INTEGER)
                db.execSQL("CREATE TABLE sales_new (id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, clientId INTEGER NOT NULL, totalValue INTEGER NOT NULL, installmentCount INTEGER NOT NULL, saleDate INTEGER NOT NULL, FOREIGN KEY(clientId) REFERENCES clients(id) ON UPDATE NO ACTION ON DELETE CASCADE)")
                db.execSQL("INSERT INTO sales_new (id, clientId, totalValue, installmentCount, saleDate) SELECT id, clientId, CAST(totalValue * 100 AS INTEGER), installmentCount, saleDate FROM sales")
                db.execSQL("DROP TABLE sales")
                db.execSQL("ALTER TABLE sales_new RENAME TO sales")
                db.execSQL("CREATE INDEX index_sales_clientId ON sales(clientId)")

                // Installments: Double to Long (INTEGER)
                db.execSQL("CREATE TABLE installments_new (id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, clientId INTEGER NOT NULL, saleId INTEGER NOT NULL, number INTEGER NOT NULL, value INTEGER NOT NULL, dueDate INTEGER NOT NULL, status TEXT NOT NULL, paymentDate INTEGER, paymentMethod TEXT NOT NULL, FOREIGN KEY(clientId) REFERENCES clients(id) ON UPDATE NO ACTION ON DELETE CASCADE, FOREIGN KEY(saleId) REFERENCES sales(id) ON UPDATE NO ACTION ON DELETE CASCADE)")
                db.execSQL("INSERT INTO installments_new (id, clientId, saleId, number, value, dueDate, status, paymentDate, paymentMethod) SELECT id, clientId, saleId, number, CAST(value * 100 AS INTEGER), dueDate, status, paymentDate, paymentMethod FROM installments")
                db.execSQL("DROP TABLE installments")
                db.execSQL("ALTER TABLE installments_new RENAME TO installments")
                db.execSQL("CREATE INDEX index_installments_clientId ON installments(clientId)")
                db.execSQL("CREATE INDEX index_installments_saleId ON installments(saleId)")
            }
        }

        private val MIGRATION_3_4 = object : Migration(3, 4) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE sales ADD COLUMN useFifthBusinessDay INTEGER NOT NULL DEFAULT 0")
            }
        }

        private val MIGRATION_4_5 = object : Migration(4, 5) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE sales ADD COLUMN hour1 INTEGER NOT NULL DEFAULT 600")
                db.execSQL("ALTER TABLE sales ADD COLUMN hour2 INTEGER NOT NULL DEFAULT 720")
                db.execSQL("ALTER TABLE sales ADD COLUMN hour3 INTEGER NOT NULL DEFAULT 960")
                db.execSQL("ALTER TABLE sales ADD COLUMN hour4 INTEGER NOT NULL DEFAULT 1260")
            }
        }

        private val MIGRATION_5_6 = object : Migration(5, 6) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("CREATE TABLE IF NOT EXISTS `monthly_customers` (`id` INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, `clientId` INTEGER NOT NULL, `monthlyValue` INTEGER NOT NULL, `dueDay` INTEGER NOT NULL, `startDate` INTEGER NOT NULL, `isActive` INTEGER NOT NULL, `isArchived` INTEGER NOT NULL, `hour1` INTEGER NOT NULL, `hour2` INTEGER NOT NULL, `hour3` INTEGER NOT NULL, `hour4` INTEGER NOT NULL, FOREIGN KEY(`clientId`) REFERENCES `clients`(`id`) ON UPDATE NO ACTION ON DELETE CASCADE )")
                db.execSQL("CREATE INDEX IF NOT EXISTS `index_monthly_customers_clientId` ON `monthly_customers` (`clientId`)")
                db.execSQL("CREATE TABLE IF NOT EXISTS `monthly_payments` (`id` INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, `monthlyCustomerId` INTEGER NOT NULL, `month` INTEGER NOT NULL, `year` INTEGER NOT NULL, `value` INTEGER NOT NULL, `dueDate` INTEGER NOT NULL, `status` TEXT NOT NULL, `paymentDate` INTEGER, `paymentMethod` TEXT NOT NULL, FOREIGN KEY(`monthlyCustomerId`) REFERENCES `monthly_customers`(`id`) ON UPDATE NO ACTION ON DELETE CASCADE )")
                db.execSQL("CREATE INDEX IF NOT EXISTS `index_monthly_payments_monthlyCustomerId` ON `monthly_payments` (`monthlyCustomerId`)")
            }
        }

        private val MIGRATION_6_7 = object : Migration(6, 7) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // Add hour5, hour6, hour7 to sales
                db.execSQL("ALTER TABLE sales ADD COLUMN hour5 INTEGER NOT NULL DEFAULT 1020")
                db.execSQL("ALTER TABLE sales ADD COLUMN hour6 INTEGER NOT NULL DEFAULT 1140")
                db.execSQL("ALTER TABLE sales ADD COLUMN hour7 INTEGER NOT NULL DEFAULT 1260")
                
                // Add hour5, hour6, hour7 to monthly_customers
                db.execSQL("ALTER TABLE monthly_customers ADD COLUMN hour5 INTEGER NOT NULL DEFAULT 1020")
                db.execSQL("ALTER TABLE monthly_customers ADD COLUMN hour6 INTEGER NOT NULL DEFAULT 1140")
                db.execSQL("ALTER TABLE monthly_customers ADD COLUMN hour7 INTEGER NOT NULL DEFAULT 1260")
            }
        }

        private val MIGRATION_7_8 = object : Migration(7, 8) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("CREATE TABLE IF NOT EXISTS `expenses` (`id` INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, `description` TEXT NOT NULL, `value` INTEGER NOT NULL, `date` INTEGER NOT NULL, `paymentMethod` TEXT NOT NULL)")
            }
        }

        private val MIGRATION_8_9 = object : Migration(8, 9) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE installments ADD COLUMN paidAmount INTEGER NOT NULL DEFAULT 0")
                db.execSQL("UPDATE installments SET paidAmount = value WHERE status = 'PAID'")
                
                db.execSQL("CREATE TABLE IF NOT EXISTS `installment_payments` (`id` INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, `installmentId` INTEGER NOT NULL, `value` INTEGER NOT NULL, `paymentDate` INTEGER NOT NULL, `paymentMethod` TEXT NOT NULL, FOREIGN KEY(`installmentId`) REFERENCES `installments`(`id`) ON UPDATE NO ACTION ON DELETE CASCADE )")
                db.execSQL("CREATE INDEX IF NOT EXISTS `index_installment_payments_installmentId` ON `installment_payments` (`installmentId`)")
                
                db.execSQL("INSERT INTO installment_payments (installmentId, value, paymentDate, paymentMethod) SELECT id, value, paymentDate, paymentMethod FROM installments WHERE status = 'PAID' AND paymentDate IS NOT NULL")
            }
        }

        @Volatile
        private var INSTANCE: CobraPixDatabase? = null

        fun getDatabase(context: Context): CobraPixDatabase {
            return INSTANCE ?: synchronized(this) {
                val instance = Room.databaseBuilder(
                    context.applicationContext,
                    CobraPixDatabase::class.java,
                    "cobrapix_database"
                )
                    .addMigrations(MIGRATION_1_2, MIGRATION_2_3, MIGRATION_3_4, MIGRATION_4_5, MIGRATION_5_6, MIGRATION_6_7, MIGRATION_7_8, MIGRATION_8_9)
                    .build()
                INSTANCE = instance
                instance
            }
        }
    }
}
