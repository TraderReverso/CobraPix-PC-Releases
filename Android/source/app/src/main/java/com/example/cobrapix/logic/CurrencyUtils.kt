package com.example.cobrapix.logic

import java.text.NumberFormat
import java.util.Locale

object CurrencyUtils {
    private val ptBr = Locale("pt", "BR")

    /**
     * Formata o valor em centavos para o padrão brasileiro (R$ 0,00).
     * Cria uma nova instância de NumberFormat para garantir thread-safety.
     */
    fun formatCurrency(cents: Long): String {
        return try {
            val currencyFormat = NumberFormat.getCurrencyInstance(ptBr)
            currencyFormat.format(cents / 100.0)
        } catch (e: Exception) {
            "R$ 0,00"
        }
    }

    /**
     * Converte texto formatado para Long (centavos).
     */
    fun parseBrazilianCurrency(text: String): Long {
        return try {
            val clean = text.replace("[R$\\.\\s]".toRegex(), "").replace(",", ".")
            val value = clean.toDoubleOrNull() ?: 0.0
            (value * 100).toLong()
        } catch (e: Exception) {
            0L
        }
    }
}
