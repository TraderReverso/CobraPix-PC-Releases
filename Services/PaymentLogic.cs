using System;
using System.Collections.Generic;
using System.Linq;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Núcleo determinístico de regras financeiras e cálculos do CobraPix.
    /// Porta fielmente a lógica de negócios da versão Android (PaymentLogic.kt),
    /// garantindo 100% de paridade em juros, amortização (Código Civil Art. 354),
    /// agendamentos, recálculos e demonstrativos globais.
    /// </summary>
    public static class PaymentLogic
    {
        public sealed class ValidationResult
        {
            public bool IsValid { get; }
            public string? Reason { get; }

            private ValidationResult(bool isValid, string? reason)
            {
                IsValid = isValid;
                Reason = reason;
            }

            public static ValidationResult Valid() => new(true, null);
            public static ValidationResult Invalid(string reason) => new(false, reason);
        }

        public class ContractInterestConfig
        {
            public bool Enabled { get; set; }
            public double DefaultRate { get; set; }

            public ContractInterestConfig(bool enabled = false, double defaultRate = 0.0)
            {
                Enabled = enabled;
                DefaultRate = defaultRate;
            }
        }

        public class OverdueInterestConfig
        {
            public bool Enabled { get; set; }
            public double MonthlyRate { get; set; }
            public int ToleranceDays { get; set; }
            public int StartMinuteOfDay { get; set; }

            public OverdueInterestConfig(
                bool enabled = false,
                double monthlyRate = 0.0,
                int toleranceDays = 0,
                int startMinuteOfDay = 540)
            {
                Enabled = enabled;
                MonthlyRate = monthlyRate;
                ToleranceDays = toleranceDays;
                StartMinuteOfDay = startMinuteOfDay;
            }
        }

        public class ContractInterestCalculation
        {
            public decimal OriginalPrincipal { get; set; }
            public double RatePercent { get; set; }
            public decimal InterestAmount { get; set; }
            public decimal TotalContracted { get; set; }
            public List<decimal> InstallmentValues { get; set; } = new();

            public ContractInterestCalculation(
                decimal originalPrincipal,
                double ratePercent,
                decimal interestAmount,
                decimal totalContracted,
                List<decimal> installmentValues)
            {
                OriginalPrincipal = originalPrincipal;
                RatePercent = ratePercent;
                InterestAmount = interestAmount;
                TotalContracted = totalContracted;
                InstallmentValues = installmentValues;
            }
        }

        public class OverdueInterestCalculation
        {
            public int OverdueDays { get; set; }
            public bool IsEligible { get; set; }
            public decimal OverdueInterestAmount { get; set; }
            public string CalculationDetails { get; set; } = string.Empty;

            public OverdueInterestCalculation(
                int overdueDays,
                bool isEligible,
                decimal overdueInterestAmount,
                string calculationDetails)
            {
                OverdueDays = overdueDays;
                IsEligible = isEligible;
                OverdueInterestAmount = overdueInterestAmount;
                CalculationDetails = calculationDetails;
            }
        }

        public class PayableFinancialStatement
        {
            public int InstallmentNumber { get; set; }
            public int TotalInstallments { get; set; }
            public decimal OriginalPrincipalInstallment { get; set; }
            public decimal ContractInterestInstallment { get; set; }
            public decimal ContractedNominalValue { get; set; }
            public decimal PaidAmount { get; set; }
            public decimal PaidContractInterest { get; set; }
            public decimal PaidPrincipal { get; set; }
            public decimal UnpaidPrincipal { get; set; }
            public decimal UnpaidContractInterest { get; set; }
            public decimal NominalRemaining { get; set; }
            public int OverdueDays { get; set; }
            public bool IsOverdueEligible { get; set; }
            public decimal OverdueInterestAmount { get; set; }
            public decimal TotalPayableNow { get; set; }
            public decimal PrincipalRemaining => UnpaidPrincipal;
        }

        public class ClientGlobalStatement
        {
            public decimal TotalInitialDebt { get; set; }
            public decimal TotalPaidAccumulated { get; set; }
            public decimal RemainingGlobalDebt { get; set; }
            public int TotalInstallmentsCount { get; set; }
            public int PaidInstallmentsCount { get; set; }
            public int PendingInstallmentsCount { get; set; }

            public decimal TotalPaid => TotalPaidAccumulated;
            public decimal RemainingBalance => RemainingGlobalDebt;

            public ClientGlobalStatement(
                decimal totalInitialDebt,
                decimal totalPaidAccumulated,
                decimal remainingGlobalDebt,
                int totalInstallmentsCount = 0,
                int paidInstallmentsCount = 0,
                int pendingInstallmentsCount = 0)
            {
                TotalInitialDebt = totalInitialDebt;
                TotalPaidAccumulated = totalPaidAccumulated;
                RemainingGlobalDebt = remainingGlobalDebt;
                TotalInstallmentsCount = totalInstallmentsCount;
                PaidInstallmentsCount = paidInstallmentsCount;
                PendingInstallmentsCount = pendingInstallmentsCount;
            }
        }

        /// <summary>
        /// Valida se uma tentativa de pagamento parcial ou total é válida.
        /// </summary>
        public static ValidationResult ValidatePayment(decimal installmentAmount, decimal currentPaidAmount, decimal amountToPay)
        {
            if (amountToPay <= 0m)
            {
                return ValidationResult.Invalid("O valor do pagamento deve ser maior que zero.");
            }

            decimal remaining = installmentAmount - currentPaidAmount;
            if (remaining <= 0m)
            {
                return ValidationResult.Invalid("A parcela já se encontra totalmente quitada.");
            }

            if (amountToPay > remaining)
            {
                return ValidationResult.Invalid(
                    $"O valor informado ({amountToPay:C2}) excede o saldo devedor restante ({remaining:C2}).");
            }

            return ValidationResult.Valid();
        }

        /// <summary>
        /// Valida se uma operação de estorno financeiro é matematicamente e funcionalmente admissível.
        /// Impede estorno superior ao valor original pago e estorno duplicado.
        /// </summary>
        public static ValidationResult ValidateRefund(decimal originalPaidAmount, decimal alreadyRefundedAmount, decimal amountToRefund)
        {
            if (amountToRefund <= 0m)
            {
                return ValidationResult.Invalid("O valor do estorno deve ser maior que zero.");
            }

            if (originalPaidAmount <= 0m)
            {
                return ValidationResult.Invalid("Não é possível estornar um pagamento com valor zerado ou negativo.");
            }

            decimal remainingRefundable = originalPaidAmount - alreadyRefundedAmount;
            if (remainingRefundable <= 0m)
            {
                return ValidationResult.Invalid("Este pagamento já foi totalmente estornado.");
            }

            if (amountToRefund > remainingRefundable)
            {
                return ValidationResult.Invalid(
                    $"O valor solicitado para estorno ({amountToRefund:C2}) excede o saldo disponível para estorno deste pagamento ({remainingRefundable:C2}).");
            }

            return ValidationResult.Valid();
        }

        /// <summary>
        /// Recalcula o status e valores da parcela após o processamento de um estorno financeiro (paridade Android PaymentLogic.applyRefund).
        /// </summary>
        public static (decimal newPaidAmount, InstallmentStatus newStatus, DateTime? newPaidDate) ApplyRefund(
            decimal installmentAmount,
            DateTime installmentDueDate,
            decimal currentTotalPaid,
            decimal refundAmount,
            DateTime? currentPaymentDate,
            DateTime? referenceDate = null)
        {
            var today = (referenceDate ?? DateTime.Today).Date;
            decimal newPaidAmount = Math.Max(0m, currentTotalPaid - refundAmount);
            bool isFullyPaid = newPaidAmount >= installmentAmount;

            InstallmentStatus newStatus;
            DateTime? newPaidDate;

            if (isFullyPaid)
            {
                newStatus = InstallmentStatus.Paid;
                newPaidDate = currentPaymentDate ?? today;
            }
            else
            {
                if (installmentDueDate.Date < today)
                {
                    newStatus = InstallmentStatus.Overdue;
                }
                else
                {
                    newStatus = InstallmentStatus.Pending;
                }
                newPaidDate = null;
            }

            return (newPaidAmount, newStatus, newPaidDate);
        }

        /// <summary>
        /// Calcula o acréscimo de juros contratuais sobre o principal original em venda parcelada,
        /// distribuindo os centavos residuais deterministicamente nas primeiras parcelas.
        /// </summary>
        public static ContractInterestCalculation CalculateContractInterest(
            decimal originalPrincipal,
            double ratePercent,
            int count)
        {
            if (originalPrincipal <= 0m || count <= 0)
            {
                return new ContractInterestCalculation(0m, 0.0, 0m, 0m, new List<decimal>());
            }

            double safeRate = (ratePercent > 0.0 && !double.IsNaN(ratePercent) && !double.IsInfinity(ratePercent))
                ? ratePercent
                : 0.0;

            decimal interestAmount = safeRate > 0.0
                ? Math.Round(originalPrincipal * (decimal)(safeRate / 100.0), 2, MidpointRounding.AwayFromZero)
                : 0m;

            decimal totalContracted = originalPrincipal + interestAmount;

            // Base truncada em centavos
            decimal baseValue = Math.Floor((totalContracted / count) * 100m) / 100m;
            decimal remainder = totalContracted - (baseValue * count);
            int remainderCents = (int)Math.Round(remainder * 100m, MidpointRounding.AwayFromZero);

            var installmentValues = new List<decimal>(count);
            for (int index = 0; index < count; index++)
            {
                decimal value = baseValue + (index < remainderCents ? 0.01m : 0.00m);
                installmentValues.Add(value);
            }

            return new ContractInterestCalculation(
                originalPrincipal,
                safeRate,
                interestAmount,
                totalContracted,
                installmentValues);
        }

        /// <summary>
        /// Calcula os juros contratuais para cliente na modalidade Mensalista.
        /// </summary>
        public static (decimal interestAmount, decimal finalMonthlyValue) CalculateMonthlyContractInterest(
            decimal originalMonthlyValue,
            double ratePercent)
        {
            if (originalMonthlyValue <= 0m)
            {
                return (0m, 0m);
            }

            double safeRate = (ratePercent > 0.0 && !double.IsNaN(ratePercent) && !double.IsInfinity(ratePercent))
                ? ratePercent
                : 0.0;

            decimal interestAmount = safeRate > 0.0
                ? Math.Round(originalMonthlyValue * (decimal)(safeRate / 100.0), 2, MidpointRounding.AwayFromZero)
                : 0m;

            decimal finalMonthly = originalMonthlyValue + interestAmount;
            return (interestAmount, finalMonthly);
        }

        /// <summary>
        /// Calcula os dias de atraso elegíveis e os juros de mora.
        /// Convenção financeira fixa: base 30 dias por mês.
        /// Juros = saldoPrincipal × (taxaMensal / 100) × (diasElegíveis / 30.0).
        /// A base é estritamente o principal não amortizado (unpaidPrincipal).
        /// </summary>
        public static OverdueInterestCalculation CalculateOverdueDaysAndInterest(
            DateTime dueDate,
            decimal principalRemaining,
            OverdueInterestConfig config,
            DateTime? referenceTime = null)
        {
            if (config == null || !config.Enabled || config.MonthlyRate <= 0.0 || principalRemaining <= 0m)
            {
                return new OverdueInterestCalculation(0, false, 0m, "Sem encargos de atraso ativos ou saldo quitado.");
            }

            DateTime refTime = referenceTime ?? DateTime.Now;

            // Fim do dia do vencimento: 23:59:59.999
            DateTime dueEndOfDay = dueDate.Date.AddDays(1).AddMilliseconds(-1);

            if (refTime <= dueEndOfDay)
            {
                return new OverdueInterestCalculation(0, false, 0m, "Parcela em dia.");
            }

            // Ponto de corte da tolerância: dueDate + toleranceDays + 1 dia no horário configurado
            DateTime toleranceThreshold = dueDate.Date
                .AddDays(config.ToleranceDays + 1)
                .AddMinutes(config.StartMinuteOfDay);

            if (refTime < toleranceThreshold)
            {
                return new OverdueInterestCalculation(0, false, 0m, "Dentro do período de tolerância.");
            }

            DateTime dayAfterDue = dueDate.Date.AddDays(1);
            DateTime refDay = refTime.Date;

            int elapsedDays = Math.Max(0, (int)(refDay - dayAfterDue).TotalDays) + 1;
            int eligibleDays = Math.Max(1, elapsedDays - config.ToleranceDays);

            double dailyRate = (config.MonthlyRate / 100.0) / 30.0;
            decimal interest = Math.Round(principalRemaining * (decimal)(dailyRate * eligibleDays), 2, MidpointRounding.AwayFromZero);

            return new OverdueInterestCalculation(
                eligibleDays,
                true,
                Math.Max(0m, interest),
                $"Base: {principalRemaining:C2}, {eligibleDays} dias a {config.MonthlyRate:F2}% a.m. (base 30d)");
        }

        /// <summary>
        /// Retorna o demonstrativo financeiro discriminado de uma parcela com base na regra de amortização (Art. 354 CC).
        /// </summary>
        public static PayableFinancialStatement GenerateFinancialStatement(
            decimal installmentAmount,
            int installmentNumber,
            DateTime dueDate,
            decimal paidAmount,
            Sale? sale,
            OverdueInterestConfig config,
            DateTime? referenceTime = null)
        {
            int totalInstallments = sale?.InstallmentCount ?? 1;
            decimal originalPrincipalInstallment;

            if (sale != null && sale.OriginalPrincipal > 0m && totalInstallments > 0)
            {
                decimal baseP = Math.Floor((sale.OriginalPrincipal / totalInstallments) * 100m) / 100m;
                decimal remP = sale.OriginalPrincipal - (baseP * totalInstallments);
                int remPCents = (int)Math.Round(remP * 100m, MidpointRounding.AwayFromZero);
                originalPrincipalInstallment = ((installmentNumber - 1) < remPCents) ? baseP + 0.01m : baseP;
            }
            else
            {
                originalPrincipalInstallment = installmentAmount;
            }

            decimal contractInterestInstallment = Math.Max(0m, installmentAmount - originalPrincipalInstallment);

            // Política de Amortização: Código Civil (Art. 354)
            // Havendo capital e juros, o pagamento parcial quita primeiramente os juros de contrato vencidos da parcela, e o excedente amortiza o principal.
            decimal paidContractInterest = Math.Min(contractInterestInstallment, paidAmount);
            decimal unpaidContractInterest = Math.Max(0m, contractInterestInstallment - paidContractInterest);

            decimal paidPrincipal = Math.Max(0m, paidAmount - contractInterestInstallment);
            decimal unpaidPrincipal = Math.Max(0m, originalPrincipalInstallment - paidPrincipal);

            decimal nominalRemaining = Math.Max(0m, installmentAmount - paidAmount);

            // A base para os juros de mora é estritamente o saldo de principal original ainda não amortizado (unpaidPrincipal):
            var overdueCalc = CalculateOverdueDaysAndInterest(
                dueDate,
                unpaidPrincipal,
                config,
                referenceTime);

            decimal totalPayableNow = nominalRemaining + overdueCalc.OverdueInterestAmount;

            return new PayableFinancialStatement
            {
                InstallmentNumber = installmentNumber,
                TotalInstallments = totalInstallments,
                OriginalPrincipalInstallment = originalPrincipalInstallment,
                ContractInterestInstallment = contractInterestInstallment,
                ContractedNominalValue = installmentAmount,
                PaidAmount = paidAmount,
                PaidContractInterest = paidContractInterest,
                PaidPrincipal = paidPrincipal,
                UnpaidPrincipal = unpaidPrincipal,
                UnpaidContractInterest = unpaidContractInterest,
                NominalRemaining = nominalRemaining,
                OverdueDays = overdueCalc.OverdueDays,
                IsOverdueEligible = overdueCalc.IsEligible,
                OverdueInterestAmount = overdueCalc.OverdueInterestAmount,
                TotalPayableNow = totalPayableNow
            };
        }

        /// <summary>
        /// Calcula o demonstrativo financeiro da dívida global do cliente a partir de todas as parcelas contratadas.
        /// </summary>
        public static ClientGlobalStatement CalculateClientGlobalStatement(
            IEnumerable<(decimal value, decimal paidAmount, InstallmentStatus status, int id)> clientInstallments,
            int? targetInstallmentId = null,
            decimal operationAmount = 0m,
            bool isRefund = false,
            bool isAlreadyPersisted = true)
        {
            var list = clientInstallments.ToList();
            if (list.Count == 0)
            {
                return new ClientGlobalStatement(0m, 0m, 0m);
            }

            decimal totalInitialDebt = list.Sum(i => i.value);
            decimal totalPaid;
            decimal remaining;

            if (isAlreadyPersisted)
            {
                totalPaid = list.Sum(i => i.paidAmount);
                remaining = list.Sum(i => Math.Max(0m, i.value - i.paidAmount));
            }
            else
            {
                decimal simulatedPaid = 0m;
                decimal simulatedRem = 0m;

                foreach (var inst in list)
                {
                    if (targetInstallmentId.HasValue && inst.id == targetInstallmentId.Value)
                    {
                        decimal newPaid = isRefund
                            ? Math.Max(0m, inst.paidAmount - operationAmount)
                            : Math.Min(inst.value, inst.paidAmount + operationAmount);

                        simulatedPaid += newPaid;
                        simulatedRem += Math.Max(0m, inst.value - newPaid);
                    }
                    else
                    {
                        simulatedPaid += inst.paidAmount;
                        simulatedRem += Math.Max(0m, inst.value - inst.paidAmount);
                    }
                }

                totalPaid = simulatedPaid;
                remaining = simulatedRem;
            }

            int paidCount = list.Count(i => i.status == InstallmentStatus.Paid || i.paidAmount >= i.value);
            int pendingCount = list.Count - paidCount;

            return new ClientGlobalStatement(
                totalInitialDebt,
                totalPaid,
                remaining,
                list.Count,
                paidCount,
                pendingCount);
        }

        /// <summary>
        /// Recalcula o estado da venda após a exclusão de uma de suas parcelas não pagas.
        /// </summary>
        public static (decimal newTotalAmount, int newInstallmentCount) RecalculateSaleAfterInstallmentDeletion(
            IEnumerable<decimal> remainingInstallmentValues)
        {
            var values = remainingInstallmentValues.ToList();
            return (values.Sum(), values.Count);
        }

        /// <summary>
        /// Calcula a data exata do 5º dia útil de um mês e ano (segunda a sexta-feira).
        /// </summary>
        public static DateTime CalculateFifthBusinessDay(int month, int year)
        {
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), "O mês deve estar entre 1 e 12.");

            int count = 0;
            int daysInMonth = DateTime.DaysInMonth(year, month);

            for (int day = 1; day <= daysInMonth; day++)
            {
                var dt = new DateTime(year, month, day);
                if (dt.DayOfWeek != DayOfWeek.Saturday && dt.DayOfWeek != DayOfWeek.Sunday)
                {
                    count++;
                    if (count == 5)
                    {
                        return dt;
                    }
                }
            }

            throw new InvalidOperationException($"Não foi possível determinar o 5º dia útil de {month:D2}/{year}.");
        }

        /// <summary>
        /// Determina a data do 1º vencimento no 5º dia útil com base em uma data de referência.
        /// Se automático e o 5º dia útil do mês atual já passou, avança para o 5º dia útil do mês subsequente.
        /// </summary>
        public static DateTime CalculateInitialFifthBusinessDay(
            DateTime referenceDate,
            bool isExplicitDate = false,
            DateTime? now = null)
        {
            var fifthOfRefMonth = CalculateFifthBusinessDay(referenceDate.Month, referenceDate.Year);

            if (isExplicitDate)
            {
                return fifthOfRefMonth;
            }

            DateTime today = (now ?? DateTime.Today).Date;
            if (fifthOfRefMonth < today)
            {
                var nextMonth = referenceDate.AddMonths(1);
                return CalculateFifthBusinessDay(nextMonth.Month, nextMonth.Year);
            }

            return fifthOfRefMonth;
        }

        /// <summary>
        /// Gera a lista ordenada de datas de vencimento para as parcelas de uma venda,
        /// com suporte a mensal, semanal (incluindo dia da semana fixo de segunda a domingo), quinzenal e 5º dia útil.
        /// </summary>
        public static List<DateTime> CalculateInstallmentDueDates(
            int count,
            DateTime firstDate,
            bool useFifthBusinessDay,
            int periodicity, // 0: Mensal, 1: Semanal, 2: Quinzenal
            DayOfWeek? targetWeekday = null,
            bool isExplicitDate = false,
            DateTime? now = null)
        {
            var list = new List<DateTime>();
            if (count <= 0) return list;

            if (useFifthBusinessDay && periodicity == 0)
            {
                DateTime firstDue = CalculateInitialFifthBusinessDay(firstDate, isExplicitDate, now);
                list.Add(firstDue);

                for (int i = 1; i < count; i++)
                {
                    var targetMonth = firstDue.AddMonths(i);
                    list.Add(CalculateFifthBusinessDay(targetMonth.Month, targetMonth.Year));
                }
            }
            else if (periodicity == 1) // Semanal
            {
                DateTime start = firstDate;
                if (targetWeekday.HasValue)
                {
                    // Ajusta para o dia da semana alvo na mesma data ou subsequente
                    int diff = ((int)targetWeekday.Value - (int)firstDate.DayOfWeek + 7) % 7;
                    start = firstDate.AddDays(diff);
                }

                for (int i = 0; i < count; i++)
                {
                    list.Add(start.AddDays(7 * i));
                }
            }
            else if (periodicity == 2) // Quinzenal
            {
                for (int i = 0; i < count; i++)
                {
                    list.Add(firstDate.AddDays(14 * i));
                }
            }
            else // Mensal (periodicity == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    list.Add(CalculateMonthlyDueDate(firstDate, i));
                }
            }

            return list;
        }

        /// <summary>
        /// Calcula a data mensal mantendo o dia original e ajustando para o último dia válido em meses mais curtos.
        /// </summary>
        public static DateTime CalculateMonthlyDueDate(DateTime firstDueDate, int installmentIndex)
        {
            if (installmentIndex == 0) return firstDueDate;

            int targetDay = firstDueDate.Day;
            int totalMonths = (firstDueDate.Year * 12 + (firstDueDate.Month - 1)) + installmentIndex;
            int year = totalMonths / 12;
            int month = (totalMonths % 12) + 1;
            int daysInMonth = DateTime.DaysInMonth(year, month);
            int day = Math.Min(targetDay, daysInMonth);

            return new DateTime(year, month, day, firstDueDate.Hour, firstDueDate.Minute, firstDueDate.Second);
        }
    }
}
