using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Gerador de payload Pix no padrão EMVCo BR Code (Pix Copia e Cola) estabelecido pelo Banco Central do Brasil.
    /// Porta fielmente o PixGenerator.kt da versão Android, incluindo validações de tamanho,
    /// normalização de caracteres e cálculo determinístico de CRC16-CCITT.
    /// </summary>
    public static class PixGenerator
    {
        public static string GeneratePayload(
            string pixKey,
            decimal amount,
            string receiverName,
            string city,
            string txid = "***")
        {
            if (amount <= 0m)
            {
                throw new ArgumentException("O valor da cobrança Pix deve ser maior que zero.", nameof(amount));
            }

            if (string.IsNullOrWhiteSpace(pixKey))
            {
                throw new ArgumentException("A chave Pix está vazia. Configure-a na tela de Ajustes.", nameof(pixKey));
            }

            string cleanName = RemoveAccents(receiverName).Trim();
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                throw new ArgumentException("O nome do recebedor está vazio ou possui caracteres inválidos. Configure-o na tela de Ajustes.", nameof(receiverName));
            }
            if (cleanName.Length > 25)
            {
                throw new ArgumentException("O nome do recebedor possui mais de 25 caracteres permitidos. Abrevie o nome na tela de Ajustes.", nameof(receiverName));
            }

            string cleanCity = RemoveAccents(city).Trim();
            if (string.IsNullOrWhiteSpace(cleanCity))
            {
                throw new ArgumentException("A cidade do recebedor está vazia ou possui caracteres inválidos. Configure-a na tela de Ajustes.", nameof(city));
            }
            if (cleanCity.Length > 15)
            {
                throw new ArgumentException("A cidade possui mais de 15 caracteres permitidos. Abrevie a cidade na tela de Ajustes.", nameof(city));
            }

            string cleanTxid = RemoveAccents(txid).Trim();
            if (cleanTxid.Length > 25)
            {
                cleanTxid = cleanTxid.Substring(0, 25).Trim();
            }
            if (string.IsNullOrEmpty(cleanTxid))
            {
                cleanTxid = "***";
            }

            string amountStr = amount.ToString("F2", CultureInfo.InvariantCulture);

            string gui = FormatField("00", "br.gov.bcb.pix");
            string key = FormatField("01", pixKey.Trim());
            string merchantAccountInfo = FormatField("26", gui + key);

            string txidField = FormatField("05", cleanTxid);
            string additionalDataField = FormatField("62", txidField);

            string payload = FormatField("00", "01") +
                             merchantAccountInfo +
                             FormatField("52", "0000") +
                             FormatField("53", "986") +
                             FormatField("54", amountStr) +
                             FormatField("58", "BR") +
                             FormatField("59", cleanName) +
                             FormatField("60", cleanCity) +
                             additionalDataField +
                             "6304";

            return payload + CalculateCrc16(payload);
        }

        private static string FormatField(string id, string value)
        {
            string len = value.Length.ToString("D2");
            return $"{id}{len}{value}";
        }

        public static string RemoveAccents(string? str)
        {
            if (string.IsNullOrWhiteSpace(str)) return string.Empty;

            string normalized = str.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (char c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            string clean = Regex.Replace(sb.ToString(), @"[^a-zA-Z0-9 *]", "");
            return clean;
        }

        public static string CalculateCrc16(string payload)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < payload.Length; i++)
            {
                crc = (ushort)(crc ^ ((ushort)payload[i] << 8));
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                    {
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    }
                    else
                    {
                        crc = (ushort)(crc << 1);
                    }
                }
            }
            return crc.ToString("X4", CultureInfo.InvariantCulture);
        }
    }
}
