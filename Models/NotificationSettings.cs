namespace CobraPix_PC.Models
{
    /// <summary>
    /// Configurações do sistema local de notificações e lembretes.
    /// Valores são persistidos localmente no banco SQLite através da tabela AppSettings.
    /// </summary>
    public class NotificationSettings
    {
        /// <summary>
        /// Define se o sistema deve gerar notificações locais. Padrão: Ativado (true).
        /// </summary>
        public bool NotificationsEnabled { get; set; } = true;

        /// <summary>
        /// Define se sons nativos do Windows devem ser reproduzidos ao notificar. Padrão: Ativado (true).
        /// </summary>
        public bool SoundEnabled { get; set; } = true;

        /// <summary>
        /// Dias de antecedência para avisar sobre vencimentos (0 = no dia, 1 = 1 dia antes, 2, 3, 5). Padrão: 1.
        /// </summary>
        public int AdvanceDays { get; set; } = 1;

        /// <summary>
        /// Define se alertas de parcelas em atraso devem ser gerados. Padrão: Ativado (true).
        /// </summary>
        public bool OverdueAlertsEnabled { get; set; } = true;

        /// <summary>
        /// Define se alertas de clientes mensalistas devem ser gerados. Padrão: Ativado (true).
        /// </summary>
        public bool MonthlyAlertsEnabled { get; set; } = true;

        /// <summary>
        /// Define se alertas relativos ao 5º dia útil devem ser gerados. Padrão: Ativado (true).
        /// </summary>
        public bool FifthBusinessDayAlertEnabled { get; set; } = true;

        /// <summary>
        /// Tipo de som nativo do Windows a ser reproduzido. Padrão: "Asterisk".
        /// Opções: Asterisk, Beep, Exclamation, Hand, Question.
        /// </summary>
        public string SoundType { get; set; } = "Asterisk";

        /// <summary>
        /// Caminho completo do arquivo de som personalizado (.wav), se configurado pelo usuário.
        /// Armazenado em %LOCALAPPDATA%\CobraPix\sounds\
        /// </summary>
        public string? CustomSoundPath { get; set; }

        /// <summary>
        /// Os 7 horários diários configuráveis para verificação automática de notificações e cobranças (padrão CobraPix Android).
        /// Formato: "HH:mm" (24h: 00:00 a 23:59).
        /// </summary>
        public string HourSlot1 { get; set; } = "08:00";
        public string HourSlot2 { get; set; } = "10:00";
        public string HourSlot3 { get; set; } = "12:00";
        public string HourSlot4 { get; set; } = "14:00";
        public string HourSlot5 { get; set; } = "16:00";
        public string HourSlot6 { get; set; } = "18:00";
        public string HourSlot7 { get; set; } = "20:00";

        public System.Collections.Generic.List<string> GetHourSlots() => new()
        {
            HourSlot1,
            HourSlot2,
            HourSlot3,
            HourSlot4,
            HourSlot5,
            HourSlot6,
            HourSlot7
        };

        public System.Collections.Generic.List<System.TimeSpan> GetValidTimeSpans()
        {
            var list = new System.Collections.Generic.List<System.TimeSpan>();
            foreach (var slot in GetHourSlots())
            {
                if (TryParseTime(slot, out var ts))
                {
                    list.Add(ts);
                }
            }
            list.Sort();
            return list;
        }

        public static bool TryParseTime(string? timeStr, out System.TimeSpan timeSpan)
        {
            timeSpan = System.TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(timeStr)) return false;

            timeStr = timeStr.Trim();
            if (System.TimeSpan.TryParseExact(timeStr, @"h\:mm", System.Globalization.CultureInfo.InvariantCulture, out timeSpan) ||
                System.TimeSpan.TryParseExact(timeStr, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out timeSpan))
            {
                if (timeSpan >= System.TimeSpan.Zero && timeSpan < System.TimeSpan.FromDays(1))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsValidTime(string? timeStr) => TryNormalizeTime(timeStr, out _);

        /// <summary>
        /// Normaliza entradas de horário para o formato padrão 24h "HH:mm" (00:00 a 23:59).
        /// Formatos aceitos:
        /// - Com separadores: "21:30", "21h30", "21H30", "21 30", "8:00", "8h00", "8H00", "8 00", "08:00"
        /// - Dígitos contínuos: "2130" -> "21:30", "0800" -> "08:00", "830" -> "08:30", "0000" -> "00:00", "2359" -> "23:59"
        /// Retorna true e o valor normalizado apenas se hora e minuto estiverem nas faixas 00-23 e 00-59.
        /// Retorna false para entradas inválidas (ex: "24:00", "24h00", "2400", "23:60", "2360", "9999", "abcd").
        /// </summary>
        public static bool TryNormalizeTime(string? input, out string normalizedTime)
        {
            normalizedTime = string.Empty;
            if (string.IsNullOrWhiteSpace(input)) return false;

            string trimmed = input.Trim();

            // Caso 1: Separadores explícitos: ':', 'h', 'H' ou ' ' (espaço)
            // Exemplos: "21:30", "21h30", "21H30", "21 30", "8:00", "8h00", "8 00"
            char[] separators = { ':', 'h', 'H', ' ' };
            int sepIndex = trimmed.IndexOfAny(separators);
            if (sepIndex >= 0)
            {
                string[] parts = trimmed.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    // A hora deve ter 1 ou 2 dígitos e os minutos devem ter exatamente 2 dígitos (ex: "8:00", "08:00", "21:30", mas não "12:3")
                    if (parts[0].Length >= 1 && parts[0].Length <= 2 && parts[1].Length == 2)
                    {
                        if (int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
                        {
                            if (h >= 0 && h <= 23 && m >= 0 && m <= 59)
                            {
                                normalizedTime = $"{h:D2}:{m:D2}";
                                return true;
                            }
                        }
                    }
                    return false;
                }
                else if (parts.Length == 1 && trimmed.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                {
                    // Exemplo: "8h" -> "08:00"
                    if (int.TryParse(parts[0], out int h) && h >= 0 && h <= 23)
                    {
                        normalizedTime = $"{h:D2}:00";
                        return true;
                    }
                }
                return false;
            }

            // Caso 2: Digitação contínua somente com dígitos (sem separador)
            // Exemplos: "2130", "0800", "0830", "830", "0000", "2359"
            string digitsOnly = string.Concat(trimmed.Where(char.IsDigit));
            if (digitsOnly.Length != trimmed.Length)
            {
                return false;
            }

            if (digitsOnly.Length == 4)
            {
                string hhStr = digitsOnly.Substring(0, 2);
                string mmStr = digitsOnly.Substring(2, 2);
                if (int.TryParse(hhStr, out int hh) && int.TryParse(mmStr, out int mm))
                {
                    if (hh >= 0 && hh <= 23 && mm >= 0 && mm <= 59)
                    {
                        normalizedTime = $"{hh:D2}:{mm:D2}";
                        return true;
                    }
                }
                return false;
            }

            if (digitsOnly.Length == 3)
            {
                string hhStr = digitsOnly.Substring(0, 1);
                string mmStr = digitsOnly.Substring(1, 2);
                if (int.TryParse(hhStr, out int hh) && int.TryParse(mmStr, out int mm))
                {
                    if (hh >= 0 && hh <= 23 && mm >= 0 && mm <= 59)
                    {
                        normalizedTime = $"{hh:D2}:{mm:D2}";
                        return true;
                    }
                }
                return false;
            }

            return false;
        }

        public NotificationSettings Clone()
        {
            return new NotificationSettings
            {
                NotificationsEnabled = NotificationsEnabled,
                SoundEnabled = SoundEnabled,
                AdvanceDays = AdvanceDays,
                OverdueAlertsEnabled = OverdueAlertsEnabled,
                MonthlyAlertsEnabled = MonthlyAlertsEnabled,
                FifthBusinessDayAlertEnabled = FifthBusinessDayAlertEnabled,
                SoundType = SoundType,
                CustomSoundPath = CustomSoundPath,
                HourSlot1 = HourSlot1,
                HourSlot2 = HourSlot2,
                HourSlot3 = HourSlot3,
                HourSlot4 = HourSlot4,
                HourSlot5 = HourSlot5,
                HourSlot6 = HourSlot6,
                HourSlot7 = HourSlot7
            };
        }
    }
}
