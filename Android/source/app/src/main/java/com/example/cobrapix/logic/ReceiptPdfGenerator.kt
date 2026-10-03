package com.example.cobrapix.logic

import android.content.Context
import android.content.Intent
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import android.util.Log
import android.widget.Toast
import androidx.core.content.FileProvider
import com.example.cobrapix.data.PaymentMethod
import java.io.File
import java.io.FileOutputStream
import java.text.SimpleDateFormat
import java.util.*

object ReceiptPdfGenerator {
    fun generateAndShareReceipt(
        context: Context,
        clientName: String,
        clientPhone: String,
        installmentNumber: Int,
        originalValue: Long,
        paidNow: Long,
        totalPaid: Long,
        remaining: Long,
        paymentMethod: PaymentMethod,
        paymentDate: Long,
        receiverName: String,
        pixKey: String
    ) {
        try {
            val document = PdfDocument()
            // A4 page size
            val pageInfo = PdfDocument.PageInfo.Builder(595, 842, 1).create()
            val page = document.startPage(pageInfo)
            val canvas = page.canvas

            val normalPaint = Paint().apply {
                color = Color.BLACK
                textSize = 14f
            }
            val titlePaint = Paint().apply {
                color = Color.BLACK
                textSize = 20f
                typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD)
                textAlign = Paint.Align.CENTER
            }
            val boldPaint = Paint().apply {
                color = Color.BLACK
                textSize = 14f
                typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD)
            }
            val statusPaint = Paint().apply {
                color = if (remaining <= 0) android.graphics.Color.parseColor("#388E3C") else android.graphics.Color.parseColor("#F57C00") // Verde ou Laranja
                textSize = 16f
                typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD)
            }

            var yPos = 80f
            val leftMargin = 60f
            val valueMargin = 220f
            val rightMargin = 60f
            val centerX = pageInfo.pageWidth / 2f

            fun splitTextIntoLines(text: String, paint: Paint, maxWidth: Float): List<String> {
                if (text.isBlank()) return listOf("")
                val words = text.split(" ")
                val lines = mutableListOf<String>()
                var currentLine = ""

                for (word in words) {
                    if (paint.measureText(word) > maxWidth) {
                        if (currentLine.isNotEmpty()) {
                            lines.add(currentLine)
                            currentLine = ""
                        }
                        var tempWord = word
                        while (paint.measureText(tempWord) > maxWidth && tempWord.isNotEmpty()) {
                            var len = tempWord.length
                            while (len > 0 && paint.measureText(tempWord.substring(0, len)) > maxWidth) {
                                len--
                            }
                            if (len == 0) len = 1
                            lines.add(tempWord.substring(0, len))
                            tempWord = tempWord.substring(len)
                        }
                        if (tempWord.isNotEmpty()) {
                            currentLine = tempWord
                        }
                    } else {
                        val testLine = if (currentLine.isEmpty()) word else "$currentLine $word"
                        if (paint.measureText(testLine) <= maxWidth) {
                            currentLine = testLine
                        } else {
                            lines.add(currentLine)
                            currentLine = word
                        }
                    }
                }
                if (currentLine.isNotEmpty()) {
                    lines.add(currentLine)
                }
                return lines
            }

            canvas.drawText("COBRAPIX", centerX, yPos, titlePaint)
            yPos += 30f
            canvas.drawText("COMPROVANTE DE PAGAMENTO", centerX, yPos, titlePaint)
            yPos += 60f

            fun drawRow(label: String, value: String, isValueBold: Boolean = true) {
                canvas.drawText(label, leftMargin, yPos, normalPaint)
                
                val paintToUse = if (isValueBold) boldPaint else normalPaint
                val maxWidth = pageInfo.pageWidth - (leftMargin + valueMargin) - rightMargin
                val lines = splitTextIntoLines(value, paintToUse, maxWidth)
                
                if (lines.isEmpty()) {
                    yPos += 30f
                    return
                }

                for ((index, line) in lines.withIndex()) {
                    canvas.drawText(line, leftMargin + valueMargin, yPos + (index * 20f), paintToUse)
                }
                yPos += (lines.size * 20f) + 10f
            }

            drawRow("Cliente:", clientName, false)
            drawRow("Telefone:", clientPhone, false)
            drawRow("Parcela:", "$installmentNumber", false)

            yPos += 20f
            canvas.drawLine(leftMargin, yPos, pageInfo.pageWidth - leftMargin, yPos, normalPaint)
            yPos += 40f

            drawRow("Valor original:", CurrencyUtils.formatCurrency(originalValue))
            drawRow("Pagamento recebido:", CurrencyUtils.formatCurrency(paidNow))
            drawRow("Total pago na parcela:", CurrencyUtils.formatCurrency(totalPaid))
            drawRow("Saldo restante:", CurrencyUtils.formatCurrency(remaining))

            yPos += 20f
            canvas.drawLine(leftMargin, yPos, pageInfo.pageWidth - leftMargin, yPos, normalPaint)
            yPos += 40f

            val methodStr = when(paymentMethod) {
                PaymentMethod.PIX -> "PIX"
                PaymentMethod.CASH -> "Dinheiro"
                PaymentMethod.CARD -> "Cartão"
                PaymentMethod.OTHER -> "Outro"
                PaymentMethod.NONE -> "Não especificado"
            }
            drawRow("Forma de pagamento:", methodStr)

            val dateStr = SimpleDateFormat("dd/MM/yyyy", Locale.getDefault()).format(Date(paymentDate))
            val timeStr = SimpleDateFormat("HH:mm", Locale.getDefault()).format(Date(paymentDate))
            drawRow("Data:", dateStr, false)
            drawRow("Hora:", timeStr, false)

            yPos += 10f
            val statusStr = if (remaining <= 0) "PARCELA QUITADA" else "PAGAMENTO PARCIAL"
            canvas.drawText("Status:", leftMargin, yPos, normalPaint)
            canvas.drawText(statusStr, leftMargin + valueMargin, yPos, statusPaint)
            yPos += 30f

            if (receiverName.isNotBlank() || pixKey.isNotBlank()) {
                yPos += 20f
                canvas.drawLine(leftMargin, yPos, pageInfo.pageWidth - leftMargin, yPos, normalPaint)
                yPos += 40f
                
                if (receiverName.isNotBlank()) drawRow("Recebedor:", receiverName, false)
                if (pixKey.isNotBlank()) drawRow("Chave PIX:", pixKey, false)
            }

            document.finishPage(page)

            val safeName = clientName.replace(Regex("[^a-zA-Z0-9]"), "_")
            val fileName = "CobraPix_Recibo_${safeName}_${paymentDate}.pdf"
            val file = File(context.cacheDir, fileName)
            
            document.writeTo(FileOutputStream(file))
            document.close()

            val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
            val intent = Intent(Intent.ACTION_SEND).apply {
                type = "application/pdf"
                putExtra(Intent.EXTRA_STREAM, uri)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            context.startActivity(Intent.createChooser(intent, "Compartilhar Recibo").apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            })

        } catch (e: Exception) {
            Log.e("CobraPix", "Erro ao gerar PDF do recibo", e)
            Toast.makeText(context, "Não foi possível gerar o recibo no momento.", Toast.LENGTH_SHORT).show()
        }
    }
}
