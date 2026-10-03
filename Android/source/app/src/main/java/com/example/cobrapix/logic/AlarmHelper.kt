package com.example.cobrapix.logic

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import android.util.Log
import com.example.cobrapix.data.CobraPixDatabase
import com.example.cobrapix.data.Installment
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import java.util.*

object AlarmHelper {

    /**
     * Agenda um único alarme consolidado para o cliente.
     * @param targetDate Se informado, agenda para o primeiro horário dessa data específica.
     */
    fun scheduleClientAlarm(context: Context, clientId: Long, clientName: String, hours: List<Int>, targetDate: Long? = null) {
        val alarmManager = context.getSystemService(Context.ALARM_SERVICE) as? AlarmManager ?: return
        
        val validHours = hours.filter { it >= 0 }.sorted()
        if (validHours.isEmpty()) {
            cancelAlarms(context, clientId)
            return
        }

        val now = Calendar.getInstance()
        val targetTime = Calendar.getInstance()
        val firstMinute = validHours.first()

        if (targetDate != null && targetDate > (System.currentTimeMillis() + 86400000L)) {
            // Se a data alvo é futura (além de amanhã), agenda para o primeiro horário daquele dia
            targetTime.timeInMillis = targetDate
            targetTime.set(Calendar.HOUR_OF_DAY, firstMinute / 60)
            targetTime.set(Calendar.MINUTE, firstMinute % 60)
            targetTime.set(Calendar.SECOND, 0)
            targetTime.set(Calendar.MILLISECOND, 0)
        } else {
            // Lógica padrão de ciclos diários
            val currentTimeInMinutes = now.get(Calendar.HOUR_OF_DAY) * 60 + now.get(Calendar.MINUTE)
            val nextMinuteToday = validHours.firstOrNull { it > currentTimeInMinutes }

            if (nextMinuteToday != null) {
                targetTime.set(Calendar.HOUR_OF_DAY, nextMinuteToday / 60)
                targetTime.set(Calendar.MINUTE, nextMinuteToday % 60)
                targetTime.set(Calendar.SECOND, 0)
                targetTime.set(Calendar.MILLISECOND, 0)
            } else {
                targetTime.add(Calendar.DAY_OF_YEAR, 1)
                targetTime.set(Calendar.HOUR_OF_DAY, firstMinute / 60)
                targetTime.set(Calendar.MINUTE, firstMinute % 60)
                targetTime.set(Calendar.SECOND, 0)
                targetTime.set(Calendar.MILLISECOND, 0)
            }
        }

        val intent = Intent(context, NotificationReceiver::class.java).apply {
            putExtra("CLIENT_ID", clientId)
            putExtra("CLIENT_NAME", clientName)
            putExtra("TYPE", "CONSOLIDATED")
        }

        val requestCode = clientId.toInt()
        val pendingIntent = PendingIntent.getBroadcast(
            context, requestCode, intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            if (alarmManager.canScheduleExactAlarms()) {
                alarmManager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, targetTime.timeInMillis, pendingIntent)
            } else {
                alarmManager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, targetTime.timeInMillis, pendingIntent)
            }
        } else {
            alarmManager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, targetTime.timeInMillis, pendingIntent)
        }
        Log.d("CobraPix", "Alarme consolidado V22 para $clientName em ${Date(targetTime.timeInMillis)}")
    }

    /**
     * Remove todos os alarmes agendados para este cliente.
     */
    fun cancelAlarms(context: Context, clientId: Long) {
        val alarmManager = context.getSystemService(Context.ALARM_SERVICE) as? AlarmManager ?: return
        val intent = Intent(context, NotificationReceiver::class.java)
        val pendingIntent = PendingIntent.getBroadcast(
            context, clientId.toInt(), intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        alarmManager.cancel(pendingIntent)
        
        // Limpeza de segurança para versões antigas
        for (slot in 0..10) {
            val oldCode = (clientId.toInt() * 10) + slot
            val oldPending = PendingIntent.getBroadcast(context, oldCode, intent, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
            alarmManager.cancel(oldPending)
        }
    }

    // Métodos legados mantidos apenas para evitar erros de compilação durante a transição
    fun scheduleNextValidAlarm(context: Context, installment: Installment, clientName: String, hours: List<Int>) {
        scheduleClientAlarm(context, installment.clientId, clientName, hours)
    }

    fun scheduleMonthlyAlarm(context: Context, payment: com.example.cobrapix.data.MonthlyPayment, clientName: String, hours: List<Int>) {
        val db = CobraPixDatabase.getDatabase(context)
        kotlinx.coroutines.CoroutineScope(Dispatchers.IO).launch {
            val customer = db.monthlyCustomerDao().getById(payment.monthlyCustomerId)
            if (customer != null) scheduleClientAlarm(context, customer.clientId, clientName, hours)
        }
    }

    fun scheduleInstallmentAlarms(context: Context, installment: Installment, clientName: String, dateOverride: Long? = null, hours: List<Int>? = null) {
        scheduleClientAlarm(context, installment.clientId, clientName, hours ?: listOf(540, 660, 780, 900, 1020, 1140, 1260))
    }
}
