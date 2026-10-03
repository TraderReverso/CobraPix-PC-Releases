using System;

namespace CobraPix_PC.Helpers
{
    /// <summary>
    /// Utilitário para cálculo e validação de dias úteis e determinação da regra do 5º dia útil.
    /// Considera segunda a sexta-feira como dias úteis, e sábado e domingo como dias não úteis.
    /// </summary>
    public static class BusinessDayHelper
    {
        /// <summary>
        /// Determina se uma data corresponde a um dia útil (segunda a sexta-feira).
        /// </summary>
        public static bool IsBusinessDay(DateTime date)
        {
            return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
        }

        /// <summary>
        /// Calcula a data exata do 5º dia útil de um mês e ano específicos.
        /// </summary>
        public static DateTime GetFifthBusinessDay(int year, int month)
        {
            if (month < 1 || month > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(month), "O mês deve estar entre 1 e 12.");
            }

            if (year < 1 || year > 9999)
            {
                throw new ArgumentOutOfRangeException(nameof(year), "Ano inválido.");
            }

            int businessDayCount = 0;
            int daysInMonth = DateTime.DaysInMonth(year, month);

            for (int day = 1; day <= daysInMonth; day++)
            {
                var candidate = new DateTime(year, month, day);
                if (IsBusinessDay(candidate))
                {
                    businessDayCount++;
                    if (businessDayCount == 5)
                    {
                        return candidate;
                    }
                }
            }

            throw new InvalidOperationException($"Não foi possível encontrar o 5º dia útil para o mês {month:D2}/{year}.");
        }

        /// <summary>
        /// Retorna a data do próximo 5º dia útil a partir de uma data de referência.
        /// Se o 5º dia útil do mês atual já passou, retorna o 5º dia útil do mês subsequente.
        /// </summary>
        public static DateTime GetNextFifthBusinessDay(DateTime referenceDate)
        {
            var currentFifth = GetFifthBusinessDay(referenceDate.Year, referenceDate.Month);
            if (referenceDate.Date <= currentFifth.Date)
            {
                return currentFifth;
            }

            var nextMonth = referenceDate.AddMonths(1);
            return GetFifthBusinessDay(nextMonth.Year, nextMonth.Month);
        }

        /// <summary>
        /// Retorna um texto descritivo do status do 5º dia útil para a data atual.
        /// Ex: "Hoje", "Faltam X dias", "Ocorreu há X dias".
        /// </summary>
        public static string GetFifthBusinessDayStatusText(DateTime referenceDate, DateTime fifthBusinessDay)
        {
            int diff = (fifthBusinessDay.Date - referenceDate.Date).Days;
            if (diff == 0) return "Hoje";
            if (diff > 0) return diff == 1 ? "Falta 1 dia" : $"Faltam {diff} dias";
            int past = Math.Abs(diff);
            return past == 1 ? "Ocorreu ontem" : $"Ocorreu há {past} dias";
        }
    }
}
