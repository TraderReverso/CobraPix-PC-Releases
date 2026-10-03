package com.example.cobrapix.logic

import java.text.Normalizer
import java.util.Locale

object PixGenerator {

    fun generatePayload(
        pixKey: String,
        amount: Long,
        receiverName: String,
        city: String,
        txid: String = "***"
    ): String {
        if (amount <= 0L) {
            throw IllegalArgumentException("O valor da cobrança Pix deve ser maior que zero.")
        }
        
        if (pixKey.isBlank()) {
            throw IllegalArgumentException("A chave Pix está vazia. Configure-a na tela de Ajustes.")
        }

        val cleanName = removeAccents(receiverName).trim()
        if (cleanName.isBlank()) {
            throw IllegalArgumentException("O nome do recebedor está vazio ou possui caracteres inválidos. Configure-o na tela de Ajustes.")
        }
        if (cleanName.length > 25) {
            throw IllegalArgumentException("O nome do recebedor possui mais de 25 caracteres permitidos. Abrevie o nome na tela de Ajustes.")
        }

        val cleanCity = removeAccents(city).trim()
        if (cleanCity.isBlank()) {
            throw IllegalArgumentException("A cidade do recebedor está vazia ou possui caracteres inválidos. Configure-a na tela de Ajustes.")
        }
        if (cleanCity.length > 15) {
            throw IllegalArgumentException("A cidade possui mais de 15 caracteres permitidos. Abrevie a cidade na tela de Ajustes.")
        }

        val cleanTxid = removeAccents(txid).take(25).trim()

        val reais = amount / 100
        val centavos = amount % 100
        val amountStr = "${reais}.${centavos.toString().padStart(2, '0')}"

        val gui = formatField("00", "br.gov.bcb.pix")
        val key = formatField("01", pixKey)
        val merchantAccountInfo = formatField("26", gui + key)

        val txidField = formatField("05", cleanTxid)
        val additionalDataField = formatField("62", txidField)

        val payload = formatField("00", "01") +
                merchantAccountInfo +
                formatField("52", "0000") +
                formatField("53", "986") +
                formatField("54", amountStr) +
                formatField("58", "BR") +
                formatField("59", cleanName) +
                formatField("60", cleanCity) +
                additionalDataField +
                "6304"

        return payload + crc16(payload)
    }

    private fun formatField(id: String, value: String): String {
        val len = value.length.toString().padStart(2, '0')
        return "$id$len$value"
    }

    private fun removeAccents(str: String): String {
        if (str.isBlank()) return ""
        val normalized = Normalizer.normalize(str, Normalizer.Form.NFD)
        return normalized.replace("[\\p{InCombiningDiacriticalMarks}]".toRegex(), "").replace("[^a-zA-Z0-9 *]".toRegex(), "")
    }

    private fun crc16(payload: String): String {
        var crc = 0xFFFF
        for (i in payload.indices) {
            crc = crc xor (payload[i].code shl 8)
            for (j in 0 until 8) {
                if ((crc and 0x8000) != 0) {
                    crc = (crc shl 1) xor 0x1021
                } else {
                    crc = crc shl 1
                }
            }
        }
        crc = crc and 0xFFFF
        return String.format(Locale.US, "%04X", crc)
    }
}
