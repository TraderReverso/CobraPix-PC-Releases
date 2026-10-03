package com.example.cobrapix.logic

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.util.Log
import com.example.cobrapix.data.Installment
import com.example.cobrapix.data.Client
import java.text.SimpleDateFormat
import java.util.*

object WhatsAppHelper {

    fun openWhatsApp(
        context: Context,
        phone: String,
        message: String
    ) {
        if (phone.isBlank()) {
            Log.e("CobraPixNotification", "Telefone vazio, cancelando abertura do WhatsApp.")
            return
        }
        
        val cleanPhone = phone.filter { it.isDigit() }
        
        // Verifica se é número brasileiro (10 ou 11 dígitos) e se já tem o código do país
        val finalPhone = if ((cleanPhone.length == 10 || cleanPhone.length == 11) && !cleanPhone.startsWith("55")) {
            "55$cleanPhone"
        } else {
            cleanPhone
        }

        Log.d("CobraPixNotification", "Tentando abrir WhatsApp. Original: $phone | Limpo: $cleanPhone | Final: $finalPhone")

        val i = Intent(Intent.ACTION_VIEW)
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        try {
            val url = "https://api.whatsapp.com/send?phone=$finalPhone&text=" + Uri.encode(message)
            i.setPackage("com.whatsapp")
            i.data = Uri.parse(url)
            context.startActivity(i)
        } catch (e: Exception) {
            Log.e("CobraPixNotification", "com.whatsapp não encontrado, tentando versão Business ou Navegador.")
            try {
                // Tenta WhatsApp Business ou apenas resolve a Intent genérica
                val url = "https://api.whatsapp.com/send?phone=$finalPhone&text=" + Uri.encode(message)
                i.setPackage(null)
                i.data = Uri.parse(url)
                context.startActivity(i)
            } catch (e2: Exception) {
                Log.e("CobraPixNotification", "Nenhum aplicativo capaz de resolver o link do WhatsApp foi encontrado.", e2)
            }
        }
    }

    fun getGreeting(): String {
        val hour = Calendar.getInstance().get(Calendar.HOUR_OF_DAY)
        return when (hour) {
            in 6..11 -> "Bom dia"
            in 12..17 -> "Boa tarde"
            else -> "Boa noite"
        }
    }

    fun formatMessage(
        template: String,
        client: Client,
        installment: Installment,
        allInstallments: List<Installment>?,
        pixKey: String,
        punctualityText: String = "",
        currentPaymentValue: Long? = null
    ): String {
        val finalTemplate = if (!template.contains("{SAUDACAO}")) "{SAUDACAO} $template" else template
        val dateFormat = java.text.SimpleDateFormat("dd/MM/yyyy", java.util.Locale.getDefault())
        val paymentDate = java.text.SimpleDateFormat("dd/MM/yyyy HH:mm", java.util.Locale.getDefault()).format(java.util.Date())

        val totalInstallments = allInstallments?.size ?: 0
        val valorPago = allInstallments?.filter { it.status == com.example.cobrapix.data.InstallmentStatus.PAID }?.sumOf { it.value } ?: 0L
        val valorRestante = allInstallments?.filter { it.status != com.example.cobrapix.data.InstallmentStatus.PAID }?.sumOf { it.value } ?: 0L
        
        // Se houver valor específico pago neste momento (ex: baixa parcial)
        val currentPaymentText = currentPaymentValue?.let { com.example.cobrapix.logic.CurrencyUtils.formatCurrency(it) } ?: ""

        // Cálculo de dias de atraso
        val today = Calendar.getInstance().apply { set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0) }.timeInMillis
        val dueDay = Calendar.getInstance().apply { timeInMillis = installment.dueDate; set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0) }.timeInMillis
        
        val diffMillis = today - dueDay
        val diffDays = (diffMillis / (1000 * 60 * 60 * 24)).toInt()
        val overdueDaysText = when {
            diffDays <= 0 -> "0 dias"
            diffDays == 1 -> "1 dia"
            else -> "$diffDays dias"
        }

        return finalTemplate
            .replace("{SAUDACAO}", getGreeting())
            .replace("{NOME}", client.name)
            .replace("{VALOR}", com.example.cobrapix.logic.CurrencyUtils.formatCurrency(installment.value))
            .replace("{PARCELA}", installment.number.toString())
            .replace("{TOTAL_PARCELAS}", totalInstallments.toString())
            .replace("{VALOR_PAGO}", com.example.cobrapix.logic.CurrencyUtils.formatCurrency(valorPago))
            .replace("{VALOR_RESTANTE}", com.example.cobrapix.logic.CurrencyUtils.formatCurrency(valorRestante))
            .replace("{DIAS_ATRASO}", overdueDaysText)
            .replace("{TEXTO_PONTUALIDADE}", punctualityText)
            .replace("{VENCIMENTO}", dateFormat.format(java.util.Date(installment.dueDate)))
            .replace("{DATA_PAGAMENTO}", paymentDate)
            .replace("{MES}", String.format("%02d", java.util.Calendar.getInstance().get(java.util.Calendar.MONTH) + 1))
            .replace("{ANO}", java.util.Calendar.getInstance().get(java.util.Calendar.YEAR).toString())
            .replace("{PIX}", pixKey)
            .replace("{VALOR_PAGO_AGORA}", currentPaymentText)
    }
}

