package com.example.cobrapix.logic

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.example.cobrapix.data.CobraPixDatabase
import com.example.cobrapix.data.InstallmentStatus
import com.example.cobrapix.data.SettingsRepository
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.firstOrNull
import kotlinx.coroutines.launch

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED) {
            val pendingResult = goAsync()
            val scope = CoroutineScope(Dispatchers.IO)
            scope.launch {
                try {
                    val db = CobraPixDatabase.getDatabase(context)
                    val settings = SettingsRepository(context)
                    val globalHours = settings.getAlarmHours()

                    val clients = db.clientDao().getAllActiveClients().firstOrNull() ?: emptyList()
                    clients.forEach { client ->
                        val pendingInst = db.installmentDao().getInstallmentsByClient(client.id).firstOrNull()?.filter { it.status != InstallmentStatus.PAID } ?: emptyList()
                        val customer = db.monthlyCustomerDao().getByClientId(client.id)
                        val pendingMonthly = if (customer != null) {
                            db.monthlyPaymentDao().getPaymentsByCustomer(customer.id).firstOrNull()?.filter { it.status != InstallmentStatus.PAID } ?: emptyList()
                        } else emptyList()

                        if (pendingInst.isNotEmpty() || pendingMonthly.isNotEmpty()) {
                            AlarmHelper.scheduleClientAlarm(context, client.id, client.name, globalHours)
                        }
                    }
                } catch (e: Exception) {
                    Log.e("CobraPix", "Erro ao restaurar alarmes no boot", e)
                } finally {
                    pendingResult.finish()
                }
            }
        }
    }
}
