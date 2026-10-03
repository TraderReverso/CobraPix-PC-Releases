package com.example.cobrapix.logic

import android.app.Activity
import android.os.Bundle
import android.util.Log
import com.example.cobrapix.data.CobraPixDatabase
import com.example.cobrapix.data.Installment
import com.example.cobrapix.data.InstallmentStatus
import com.example.cobrapix.data.SettingsRepository
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.firstOrNull
import kotlinx.coroutines.launch

class WhatsAppActionActivity : Activity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        
        val clientId = intent.getLongExtra("CLIENT_ID", -1L)
        Log.d("CobraPixNotification", "WhatsAppActionActivity iniciada clientId=$clientId")

        if (clientId == -1L || clientId == -999L) {
            Log.e("CobraPixNotification", "CLIENT_ID inválido recebido na WhatsAppActionActivity")
            finish()
            return
        }

        CoroutineScope(Dispatchers.IO).launch {
            try {
                val db = CobraPixDatabase.getDatabase(this@WhatsAppActionActivity)
                val settings = SettingsRepository(this@WhatsAppActionActivity)
                val pixKey = settings.pixKey.firstOrNull() ?: ""

                val client = db.clientDao().getClientById(clientId)
                if (client == null) {
                    Log.e("CobraPixNotification", "Cliente com id $clientId não encontrado no banco de dados.")
                    return@launch
                }
                
                Log.d("CobraPixNotification", "Cliente encontrado: ${client.name}")
                val phone = client.phone
                Log.d("CobraPixNotification", "Telefone encontrado: $phone")

                if (phone.isBlank()) {
                    Log.e("CobraPixNotification", "Este cliente não possui um número de WhatsApp válido.")
                    return@launch
                }
                
                val allInstallments = db.installmentDao().getInstallmentsByClient(clientId).firstOrNull() ?: emptyList()
                val pendingInst = allInstallments.filter { it.status != InstallmentStatus.PAID }
                
                val customer = db.monthlyCustomerDao().getByClientId(clientId)
                val pendingMonthly = if (customer != null) {
                    db.monthlyPaymentDao().getPaymentsByCustomer(customer.id).firstOrNull()?.filter { it.status != InstallmentStatus.PAID } ?: emptyList()
                } else emptyList()

                val message = if (pendingMonthly.isNotEmpty()) {
                    val payment = pendingMonthly.first()
                    val template = if (payment.status == InstallmentStatus.OVERDUE) {
                        settings.messageTemplateMonthlyOverdue.firstOrNull() ?: ""
                    } else {
                        settings.messageTemplateMensalista.firstOrNull() ?: ""
                    }
                    WhatsAppHelper.formatMessage(template, client, Installment(id=0, clientId=0, saleId=0, number=payment.month, value=payment.value, dueDate=payment.dueDate), null, pixKey)
                } else if (pendingInst.isNotEmpty()) {
                    val inst = pendingInst.first()
                    val template = if (inst.status == InstallmentStatus.OVERDUE) {
                        settings.messageTemplateOverdue.firstOrNull() ?: ""
                    } else {
                        settings.messageTemplate.firstOrNull() ?: ""
                    }
                    WhatsAppHelper.formatMessage(template, client, inst, allInstallments, pixKey)
                } else {
                    "Olá ${client.name}, tudo bem? Passando para lembrar da sua cobrança pendente."
                }

                Log.d("CobraPixNotification", "Mensagem criada. Tentativa de abrir WhatsApp...")
                
                WhatsAppHelper.openWhatsApp(this@WhatsAppActionActivity, phone, message)
                
            } catch (e: Exception) {
                Log.e("CobraPixNotification", "Erro ao processar fluxo na WhatsAppActionActivity", e)
            } finally {
                // Independente de dar certo ou não, fechamos essa activity invisível
                finish()
            }
        }
    }
}
