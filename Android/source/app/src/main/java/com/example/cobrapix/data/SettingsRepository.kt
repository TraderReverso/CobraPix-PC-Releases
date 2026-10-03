package com.example.cobrapix.data

import android.content.Context
import androidx.datastore.preferences.core.*
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.firstOrNull
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map

private val Context.dataStore by preferencesDataStore(name = "settings")

class SettingsRepository(private val context: Context) {

    companion object {
        private val PIX_KEY = stringPreferencesKey("pix_key")
        private val RECEIVER_NAME = stringPreferencesKey("receiver_name")
        private val CITY = stringPreferencesKey("city")
        private val MESSAGE_TEMPLATE = stringPreferencesKey("message_template")
        private val MESSAGE_TEMPLATE_MENSALISTA = stringPreferencesKey("message_template_mensalista")
        private val MESSAGE_TEMPLATE_MONTHLY_OVERDUE = stringPreferencesKey("message_template_monthly_overdue")
        private val MESSAGE_TEMPLATE_OVERDUE = stringPreferencesKey("message_template_overdue")
        private val CONFIRM_MESSAGE_INSTALLMENT = stringPreferencesKey("confirm_message_installment")
        private val CONFIRM_MESSAGE_PARTIAL_INSTALLMENT = stringPreferencesKey("confirm_message_partial_installment")
        private val CONFIRM_MESSAGE_MONTHLY = stringPreferencesKey("confirm_message_monthly")
        private val CONFIRM_MESSAGE_LAST_INSTALLMENT = stringPreferencesKey("confirm_message_last_installment")
        private val NOTIFICATION_SOUND_URI = stringPreferencesKey("notification_sound_uri")
        private val HOUR_SLOT_1 = intPreferencesKey("hour_slot_1")
        private val HOUR_SLOT_2 = intPreferencesKey("hour_slot_2")
        private val HOUR_SLOT_3 = intPreferencesKey("hour_slot_3")
        private val HOUR_SLOT_4 = intPreferencesKey("hour_slot_4")
        private val HOUR_SLOT_5 = intPreferencesKey("hour_slot_5")
        private val HOUR_SLOT_6 = intPreferencesKey("hour_slot_6")
        private val HOUR_SLOT_7 = intPreferencesKey("hour_slot_7")
        private val THEME_MODE = stringPreferencesKey("theme_mode")
        private val BIOMETRIC_ENABLED = booleanPreferencesKey("biometric_enabled")
        private val SETTINGS_VERSION = intPreferencesKey("settings_version")

        // Padrões V20: 09:00, 11:00, 13:00, 15:00, 17:00, 19:00, 21:00 (em minutos)
        val DEFAULT_HOURS = listOf(540, 660, 780, 900, 1020, 1140, 1260)

        private const val DEFAULT_MESSAGE = "{SAUDACAO}, {NOME}! Tudo bem? 😊\n\nPassando para lembrar do vencimento da sua parcela {PARCELA}/{TOTAL_PARCELAS}, no valor de {VALOR}, com vencimento para hoje ({VENCIMENTO}).\n\n💰 Total já pago: {VALOR_PAGO}\n⏳ Falta pagar: {VALOR_RESTANTE}\n\nPara sua comodidade, segue a chave Pix para pagamento:\n{PIX}\n\nAssim que realizar o pagamento, por favor, envie o comprovante. Obrigado!"
        private const val DEFAULT_MESSAGE_MENSALISTA = "{SAUDACAO}, {NOME}! Tudo bem? 😊\n\nPassando para lembrar do vencimento da sua mensalidade no valor de {VALOR}, com vencimento para hoje.\n\nPara sua comodidade, segue a chave Pix para pagamento:\n{PIX}\n\nAssim que realizar o pagamento, por favor, envie o comprovante. Obrigado!"
        private const val DEFAULT_MESSAGE_OVERDUE = "{SAUDACAO}, {NOME}! Notamos que a sua parcela {PARCELA} está pendente há {DIAS_ATRASO}. ⏳ Para manter seu histórico positivo e encerrar os alertas automáticos, pedimos a gentileza de realizar o pagamento via Pix. Qualquer dúvida, estamos à disposição! 😊"
        private const val DEFAULT_MESSAGE_MONTHLY_OVERDUE = "{SAUDACAO}, {NOME}! Notamos que a sua mensalidade de {VALOR} ({MES}/{ANO}) está pendente há {DIAS_ATRASO}. ⏳ Pedimos a gentileza de realizar o pagamento via Pix para regularizar seu acesso. Obrigado! 😊"

        private const val DEFAULT_CONFIRM_INSTALLMENT = "{SAUDACAO}, {NOME}! 😊\n\nSeu pagamento da parcela {PARCELA}/{TOTAL_PARCELAS}, no valor de {VALOR}, foi confirmado com sucesso em {DATA_PAGAMENTO}.\n\n✅ Pagamento realizado com sucesso!\n\nObrigado pelo pagamento!"
        private const val DEFAULT_CONFIRM_PARTIAL = "{SAUDACAO}, {NOME}! 😊\n\nPagamento parcial recebido com sucesso. Valor recebido: {VALOR_PAGO_AGORA}. Saldo restante da parcela {PARCELA}: {VALOR_RESTANTE}."
        private const val DEFAULT_CONFIRM_MONTHLY = "{SAUDACAO}, {NOME}! 😊\n\nSeu pagamento da mensalidade no valor de {VALOR} foi confirmado com sucesso em {DATA_PAGAMENTO}.\n\n🔄 Renovação realizada com sucesso!\n\nObrigado pela confiança e pelo pagamento!"
        private const val DEFAULT_CONFIRM_LAST_INSTALLMENT = "{SAUDACAO}, {NOME}! 🌟\n\n*PARABÉNS!* Você acaba de quitar a última parcela ({PARCELA}/{TOTAL_PARCELAS}) do seu compromisso.\n\nFicamos muito felizes em concluir essa jornada com você. {TEXTO_PONTUALIDADE}\n\n✅ *CONTRATO FINALIZADO COM SUCESSO!*\n\nObrigado pela confiança e até a próxima! 😊"
    }

    val pixKey: Flow<String> = context.dataStore.data.map { it[PIX_KEY] ?: "" }
    val receiverName: Flow<String> = context.dataStore.data.map { it[RECEIVER_NAME] ?: "" }
    val city: Flow<String> = context.dataStore.data.map { it[CITY] ?: "" }
    val messageTemplate: Flow<String> = context.dataStore.data.map { it[MESSAGE_TEMPLATE] ?: DEFAULT_MESSAGE }
    val messageTemplateMensalista: Flow<String> = context.dataStore.data.map { it[MESSAGE_TEMPLATE_MENSALISTA] ?: DEFAULT_MESSAGE_MENSALISTA }
    val messageTemplateMonthlyOverdue: Flow<String> = context.dataStore.data.map { it[MESSAGE_TEMPLATE_MONTHLY_OVERDUE] ?: DEFAULT_MESSAGE_MONTHLY_OVERDUE }
    val messageTemplateOverdue: Flow<String> = context.dataStore.data.map { it[MESSAGE_TEMPLATE_OVERDUE] ?: DEFAULT_MESSAGE_OVERDUE }
    val confirmMessageInstallment: Flow<String> = context.dataStore.data.map { it[CONFIRM_MESSAGE_INSTALLMENT] ?: DEFAULT_CONFIRM_INSTALLMENT }
    val confirmMessagePartialInstallment: Flow<String> = context.dataStore.data.map { it[CONFIRM_MESSAGE_PARTIAL_INSTALLMENT] ?: DEFAULT_CONFIRM_PARTIAL }
    val confirmMessageMonthly: Flow<String> = context.dataStore.data.map { it[CONFIRM_MESSAGE_MONTHLY] ?: DEFAULT_CONFIRM_MONTHLY }
    val confirmMessageLastInstallment: Flow<String> = context.dataStore.data.map { it[CONFIRM_MESSAGE_LAST_INSTALLMENT] ?: DEFAULT_CONFIRM_LAST_INSTALLMENT }
    val notificationSoundUri: Flow<String?> = context.dataStore.data.map { it[NOTIFICATION_SOUND_URI] }

    val hourSlot1: Flow<Int> = context.dataStore.data.map { it[HOUR_SLOT_1] ?: DEFAULT_HOURS[0] }
    val hourSlot2: Flow<Int> = context.dataStore.data.map { it[HOUR_SLOT_2] ?: DEFAULT_HOURS[1] }
    val hourSlot3: Flow<Int> = context.dataStore.data.map { it[HOUR_SLOT_3] ?: DEFAULT_HOURS[2] }
    val hourSlot4: Flow<Int> = context.dataStore.data.map { it[HOUR_SLOT_4] ?: DEFAULT_HOURS[3] }
    val hourSlot5: Flow<Int> = context.dataStore.data.map { it[HOUR_SLOT_5] ?: DEFAULT_HOURS[4] }
    val hourSlot6: Flow<Int> = context.dataStore.data.map { it[HOUR_SLOT_6] ?: DEFAULT_HOURS[5] }
    val hourSlot7: Flow<Int> = context.dataStore.data.map { it[HOUR_SLOT_7] ?: DEFAULT_HOURS[6] }

    val themeMode: Flow<String> = context.dataStore.data.map { it[THEME_MODE] ?: "DARK" }
    val isBiometricEnabled: Flow<Boolean> = context.dataStore.data.map { it[BIOMETRIC_ENABLED] ?: false }

    suspend fun checkAndMigrate() {
        val prefs = context.dataStore.data.map { it }.firstOrNull() ?: return
        val version = prefs[SETTINGS_VERSION] ?: 1

        if (version < 3) {
            context.dataStore.edit { edit ->
                // Versão 3: Expansão para 7 slots e correção de minutos
                val h1 = edit[HOUR_SLOT_1]
                val h2 = edit[HOUR_SLOT_2]
                val h3 = edit[HOUR_SLOT_3]
                val h4 = edit[HOUR_SLOT_4]

                // Se o valor estiver < 24, provavelmente era Hora pura salva em versões anteriores
                if (h1 != null && h1 < 24) edit[HOUR_SLOT_1] = h1 * 60
                if (h2 != null && h2 < 24) edit[HOUR_SLOT_2] = h2 * 60
                if (h3 != null && h3 < 24) edit[HOUR_SLOT_3] = h3 * 60
                if (h4 != null && h4 < 24) edit[HOUR_SLOT_4] = h4 * 60

                // Slots novos sempre inicializam com os novos padrões V20
                if (edit[HOUR_SLOT_5] == null) edit[HOUR_SLOT_5] = DEFAULT_HOURS[4]
                if (edit[HOUR_SLOT_6] == null) edit[HOUR_SLOT_6] = DEFAULT_HOURS[5]
                if (edit[HOUR_SLOT_7] == null) edit[HOUR_SLOT_7] = DEFAULT_HOURS[6]

                edit[SETTINGS_VERSION] = 3
            }
        }
    }

    suspend fun getAlarmHours(): List<Int> {
        checkAndMigrate()
        val h1 = hourSlot1.firstOrNull() ?: DEFAULT_HOURS[0]
        val h2 = hourSlot2.firstOrNull() ?: DEFAULT_HOURS[1]
        val h3 = hourSlot3.firstOrNull() ?: DEFAULT_HOURS[2]
        val h4 = hourSlot4.firstOrNull() ?: DEFAULT_HOURS[3]
        val h5 = hourSlot5.firstOrNull() ?: DEFAULT_HOURS[4]
        val h6 = hourSlot6.firstOrNull() ?: DEFAULT_HOURS[5]
        val h7 = hourSlot7.firstOrNull() ?: DEFAULT_HOURS[6]

        return listOf(h1, h2, h3, h4, h5, h6, h7)
    }

    suspend fun savePixKey(value: String) {
        context.dataStore.edit { it[PIX_KEY] = value }
    }

    suspend fun saveReceiverName(value: String) {
        context.dataStore.edit { it[RECEIVER_NAME] = value }
    }

    suspend fun saveCity(value: String) {
        context.dataStore.edit { it[CITY] = value }
    }

    suspend fun saveMessageTemplate(value: String) {
        context.dataStore.edit { it[MESSAGE_TEMPLATE] = value }
    }

    suspend fun saveMessageTemplateMensalista(value: String) {
        context.dataStore.edit { it[MESSAGE_TEMPLATE_MENSALISTA] = value }
    }

    suspend fun saveMessageTemplateMonthlyOverdue(value: String) {
        context.dataStore.edit { it[MESSAGE_TEMPLATE_MONTHLY_OVERDUE] = value }
    }

    suspend fun saveMessageTemplateOverdue(value: String) {
        context.dataStore.edit { it[MESSAGE_TEMPLATE_OVERDUE] = value }
    }

    suspend fun saveConfirmMessageInstallment(value: String) {
        context.dataStore.edit { it[CONFIRM_MESSAGE_INSTALLMENT] = value }
    }

    suspend fun saveConfirmMessagePartialInstallment(value: String) {
        context.dataStore.edit { it[CONFIRM_MESSAGE_PARTIAL_INSTALLMENT] = value }
    }

    suspend fun saveConfirmMessageMonthly(value: String) {
        context.dataStore.edit { it[CONFIRM_MESSAGE_MONTHLY] = value }
    }

    suspend fun saveConfirmMessageLastInstallment(value: String) {
        context.dataStore.edit { it[CONFIRM_MESSAGE_LAST_INSTALLMENT] = value }
    }

    suspend fun saveNotificationSoundUri(value: String) {
        context.dataStore.edit { it[NOTIFICATION_SOUND_URI] = value }
    }

    suspend fun saveThemeMode(value: String) {
        context.dataStore.edit { it[THEME_MODE] = value }
    }

    suspend fun setBiometricEnabled(value: Boolean) {
        context.dataStore.edit { it[BIOMETRIC_ENABLED] = value }
    }

    suspend fun saveHourSlots(slots: List<Int>) {
        context.dataStore.edit { edit ->
            if (slots.size >= 1) edit[HOUR_SLOT_1] = slots[0]
            if (slots.size >= 2) edit[HOUR_SLOT_2] = slots[1]
            if (slots.size >= 3) edit[HOUR_SLOT_3] = slots[2]
            if (slots.size >= 4) edit[HOUR_SLOT_4] = slots[3]
            if (slots.size >= 5) edit[HOUR_SLOT_5] = slots[4]
            if (slots.size >= 6) edit[HOUR_SLOT_6] = slots[5]
            if (slots.size >= 7) edit[HOUR_SLOT_7] = slots[6]
            edit[SETTINGS_VERSION] = 3
        }
    }

    suspend fun resetMessagesToDefault() {
        context.dataStore.edit {
            it.remove(MESSAGE_TEMPLATE)
            it.remove(MESSAGE_TEMPLATE_MENSALISTA)
            it.remove(MESSAGE_TEMPLATE_MONTHLY_OVERDUE)
            it.remove(MESSAGE_TEMPLATE_OVERDUE)
            it.remove(CONFIRM_MESSAGE_PARTIAL_INSTALLMENT)
        }
    }
}