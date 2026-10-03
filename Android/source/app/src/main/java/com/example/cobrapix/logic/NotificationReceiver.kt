package com.example.cobrapix.logic

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.net.toUri
import com.example.cobrapix.R
import com.example.cobrapix.data.CobraPixDatabase
import com.example.cobrapix.data.Installment
import com.example.cobrapix.data.InstallmentStatus
import com.example.cobrapix.data.SettingsRepository
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.firstOrNull
import kotlinx.coroutines.launch
import java.util.*

class NotificationReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        val pendingResult = goAsync()
        val scope = CoroutineScope(Dispatchers.IO)

        scope.launch {
            try {
                val action = intent.action
                if (action == "ACTION_TEST_NOTIFICATION") {
                    showConsolidatedNotification(context, "SISTEMA (TESTE)", 15000, 1, -999L, null)
                    return@launch
                }

                val clientId = intent.getLongExtra("CLIENT_ID", -1)
                if (clientId == -1L) return@launch

                val db = CobraPixDatabase.getDatabase(context)
                val settings = SettingsRepository(context)

                // Atualização global segura antes de processar
                try {
                    db.installmentDao().updatePendingToOverdue(System.currentTimeMillis())
                } catch (e: Exception) {
                    Log.e("CobraPix", "Erro ao atualizar parcelas pendentes", e)
                }

                val client = db.clientDao().getClientById(clientId)
                if (client == null || client.isArchived) {
                    AlarmHelper.cancelAlarms(context, clientId)
                    return@launch
                }

                // Coletar todas as pendências do cliente
                val pendingInst = db.installmentDao().getInstallmentsByClient(clientId).firstOrNull()?.filter { it.status != InstallmentStatus.PAID } ?: emptyList()
                val customer = db.monthlyCustomerDao().getByClientId(clientId)
                val pendingMonthly = if (customer != null) {
                    db.monthlyPaymentDao().getPaymentsByCustomer(customer.id).firstOrNull()?.filter { it.status != InstallmentStatus.PAID } ?: emptyList()
                } else emptyList()

                // Filtrar apenas o que é para HOJE ou ATRASADO
                val endOfToday = Calendar.getInstance().apply { set(Calendar.HOUR_OF_DAY, 23); set(Calendar.MINUTE, 59); set(Calendar.SECOND, 59); set(Calendar.MILLISECOND, 999) }.timeInMillis
                
                val activeInst = pendingInst.filter { it.dueDate <= endOfToday }
                val activeMonthly = pendingMonthly.filter { it.dueDate <= endOfToday }

                if (activeInst.isEmpty() && activeMonthly.isEmpty()) {
                    // Nada para hoje, mas o cliente tem dívidas futuras. 
                    // Vamos agendar o alarme para "acordar" no dia da próxima dívida.
                    val allPending = (pendingInst + pendingMonthly.map { Installment(id=0, clientId=clientId, saleId=0, number=0, value=it.value, dueDate=it.dueDate) })
                    val nextDueDate = allPending.minOfOrNull { it.dueDate }
                    
                    if (nextDueDate != null) {
                        AlarmHelper.scheduleClientAlarm(context, clientId, client.name, settings.getAlarmHours(), nextDueDate)
                    } else {
                        AlarmHelper.cancelAlarms(context, clientId)
                    }
                    return@launch
                }

                // Consolidação apenas do que venceu
                val totalValue = activeInst.sumOf { it.value } + activeMonthly.sumOf { it.value }
                val totalCount = activeInst.size + activeMonthly.size
                
                val soundUri = settings.notificationSoundUri.firstOrNull()
                showConsolidatedNotification(context, client.name, totalValue, totalCount, clientId, soundUri)

                // Reagendar para o próximo horário do ciclo normal (hoje/amanhã)
                AlarmHelper.scheduleClientAlarm(context, clientId, client.name, settings.getAlarmHours())

            } catch (e: Exception) {
                Log.e("CobraPix", "Erro crítico no NotificationReceiver", e)
            } finally {
                pendingResult.finish()
            }
        }
    }

    private fun showConsolidatedNotification(
        context: Context, clientName: String, totalValue: Long, count: Int, clientId: Long, soundUriStr: String? = null
    ) {
        val notificationManager = context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        
        // Geração de ID dinâmico baseado no som para forçar o Android a atualizar o canal
        val soundHash = soundUriStr?.hashCode()?.let { if (it < 0) -it else it } ?: 0
        val channelId = "cobrapix_channel_s$soundHash"
        val channelName = "Alertas de Cobrança"

        val soundUri = soundUriStr?.let { it.toUri() }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            // Tenta buscar o canal. Se não existir ou o som mudou, cria um novo.
            if (notificationManager.getNotificationChannel(channelId) == null) {
                // Opcional: deletar canais antigos do app para não poluir as configurações do sistema
                notificationManager.notificationChannels.forEach { oldChannel ->
                    if (oldChannel.id.startsWith("cobrapix_channel_")) {
                        notificationManager.deleteNotificationChannel(oldChannel.id)
                    }
                }

                val channel = NotificationChannel(channelId, channelName, NotificationManager.IMPORTANCE_HIGH).apply {
                    description = "Notificações de Cobrança do CobraPix"
                    enableLights(true)
                    lightColor = android.graphics.Color.CYAN
                    enableVibration(true)
                    if (soundUri != null) {
                        val audioAttributes = android.media.AudioAttributes.Builder()
                            .setContentType(android.media.AudioAttributes.CONTENT_TYPE_SONIFICATION)
                            .setUsage(android.media.AudioAttributes.USAGE_NOTIFICATION_RINGTONE)
                            .build()
                        setSound(soundUri, audioAttributes)
                    }
                }
                notificationManager.createNotificationChannel(channel)
            }
        }

        val amountStr = CurrencyUtils.formatCurrency(totalValue)
        val title = "🔔 LEMBRETE: $clientName"
        val message = if (count > 1) "$count cobranças pendentes (Total: $amountStr)" else "Cobrança de $amountStr pendente."

        val profileIntent = Intent(Intent.ACTION_VIEW, android.net.Uri.parse("cobrapix://client/$clientId")).apply {
            setPackage(context.packageName)
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val profilePendingIntent = PendingIntent.getActivity(
            context, 
            clientId.toInt() + 3000, 
            profileIntent, 
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val waIntent = Intent(context, WhatsAppActionActivity::class.java).apply {
            putExtra("CLIENT_ID", clientId)
        }
        val waPendingIntent = PendingIntent.getActivity(
            context,
            clientId.toInt() + 2000,
            waIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val builder = NotificationCompat.Builder(context, channelId)
            .setSmallIcon(R.drawable.ic_launcher_foreground)
            .setContentTitle(title)
            .setContentText(message)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setAutoCancel(true)
            .setSound(soundUri)
            .setContentIntent(profilePendingIntent) // Clique principal abre o perfil do cliente
            .addAction(R.drawable.ic_launcher_foreground, "ABRIR CLIENTE", profilePendingIntent)
            
        if (clientId != -999L) {
            builder.addAction(R.drawable.ic_launcher_foreground, "WHATSAPP", waPendingIntent)
        }

        notificationManager.notify(clientId.toInt(), builder.build())
    }
}
