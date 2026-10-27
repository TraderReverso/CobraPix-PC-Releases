using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Serviço de histórico financeiro do CobraPix PC.
    /// Consolida todas as entradas e recebimentos reais originados de parcelas e mensalidades.
    /// Não utiliza valores de venda brutos; baseia-se estritamente nas movimentações registradas.
    /// </summary>
    public class FinancialHistoryService : IFinancialHistoryService
    {
        private readonly CobraPixDbContext _context;

        public FinancialHistoryService(CobraPixDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<List<FinancialTransaction>> GetAllTransactionsAsync()
        {
            var list = new List<FinancialTransaction>();

            // Pré-cálculo das dívidas globais reais dos clientes (paridade com Android)
            var allClientInstallments = await _context.Installments
                .AsNoTracking()
                .Include(i => i.Payments)
                .Include(i => i.Sale)
                .ToListAsync();

            var clientGlobalStatements = allClientInstallments
                .Where(i => i.Sale != null)
                .GroupBy(i => i.Sale!.ClientId)
                .ToDictionary(
                    g => g.Key,
                    g => PaymentLogic.CalculateClientGlobalStatement(
                        g.Select(i => (
                            value: i.Amount,
                            paidAmount: i.Payments.Sum(p => p.AmountPaid),
                            status: i.Status,
                            id: i.Id
                        ))
                    )
                );

            // 1. Pagamentos de Parcelas
            var installmentPayments = await _context.InstallmentPayments
                .AsNoTracking()
                .Include(p => p.Installment)
                    .ThenInclude(i => i.Sale)
                        .ThenInclude(s => s.Client)
                .ToListAsync();

            var installmentIds = installmentPayments.Select(p => p.InstallmentId).Distinct().ToList();
            var totalPaidByInstallment = await _context.InstallmentPayments
                .AsNoTracking()
                .Where(p => installmentIds.Contains(p.InstallmentId))
                .GroupBy(p => p.InstallmentId)
                .Select(g => new { InstallmentId = g.Key, TotalPaid = g.Sum(x => x.AmountPaid) })
                .ToDictionaryAsync(x => x.InstallmentId, x => x.TotalPaid);

            // Rastreamento de estornos vinculados a pagamentos originais
            var installmentRefunds = installmentPayments
                .Where(p => p.AmountPaid < 0m || (p.Notes != null && p.Notes.StartsWith("[ESTORNO:")))
                .ToList();

            var refundedAmountByOriginalId = new Dictionary<int, decimal>();
            foreach (var r in installmentRefunds)
            {
                int origId = ParseOriginalPaymentId(r.Notes, "OriginalPaymentId");
                if (origId > 0)
                {
                    decimal amt = Math.Abs(r.AmountPaid);
                    refundedAmountByOriginalId[origId] = refundedAmountByOriginalId.TryGetValue(origId, out var cur)
                        ? cur + amt
                        : amt;
                }
            }

            foreach (var p in installmentPayments)
            {
                var client = p.Installment?.Sale?.Client;
                int clientId = client?.Id ?? (p.Installment?.Sale?.ClientId ?? 0);
                string clientName = client?.Name ?? "Cliente";
                string? phone = client?.Phone;

                bool isRefund = p.AmountPaid < 0m || (p.Notes != null && p.Notes.StartsWith("[ESTORNO:"));
                int origPaymentId = isRefund ? ParseOriginalPaymentId(p.Notes, "OriginalPaymentId") : 0;
                decimal absAmount = Math.Abs(p.AmountPaid);

                string desc;
                string reference;

                if (isRefund)
                {
                    desc = p.Installment != null
                        ? $"Estorno Parcela {p.Installment.InstallmentNumber} · Venda #{p.Installment.SaleId}"
                        : "Estorno de Pagamento";
                    reference = origPaymentId > 0
                        ? $"Estorno ref. Pagamento #{origPaymentId}"
                        : (p.Installment?.Sale?.Description ?? $"Venda #{p.Installment?.SaleId}");
                }
                else
                {
                    desc = p.Installment != null
                        ? $"Parcela {p.Installment.InstallmentNumber} · Venda #{p.Installment.SaleId}"
                        : "Pagamento de Parcela";
                    reference = p.Installment?.Sale?.Description ?? $"Venda #{p.Installment?.SaleId}";
                }

                decimal origAmount = p.Installment?.Amount ?? absAmount;
                decimal totalPaid = totalPaidByInstallment.TryGetValue(p.InstallmentId, out var tp) ? tp : p.AmountPaid;
                decimal remainingBalance = Math.Max(0m, origAmount - totalPaid);

                var sale = p.Installment?.Sale;
                decimal? origPrincipal = null;
                decimal? contractInterest = null;
                decimal? unpaidPrincipal = null;

                if (sale != null && sale.OriginalPrincipal > 0 && sale.InstallmentCount > 0 && p.Installment != null)
                {
                    decimal baseP = Math.Floor((sale.OriginalPrincipal / sale.InstallmentCount) * 100m) / 100m;
                    decimal remP = sale.OriginalPrincipal - (baseP * sale.InstallmentCount);
                    int remPCents = (int)Math.Round(remP * 100m, MidpointRounding.AwayFromZero);
                    origPrincipal = ((p.Installment.InstallmentNumber - 1) < remPCents) ? baseP + 0.01m : baseP;
                    contractInterest = Math.Max(0m, origAmount - origPrincipal.Value);

                    decimal paidContractInterest = Math.Min(contractInterest.Value, totalPaid);
                    decimal paidPrincipal = Math.Max(0m, totalPaid - contractInterest.Value);
                    unpaidPrincipal = Math.Max(0m, origPrincipal.Value - paidPrincipal);
                }

                // Dívida global real calculada para o cliente
                decimal? gInitial = null;
                decimal? gPaid = null;
                decimal? gRemaining = null;

                if (clientId > 0 && clientGlobalStatements.TryGetValue(clientId, out var gStmt))
                {
                    gInitial = gStmt.TotalInitialDebt;
                    gPaid = gStmt.TotalPaid;
                    gRemaining = gStmt.RemainingBalance;
                }
                else if (sale != null)
                {
                    gInitial = sale.TotalAmount;
                    gPaid = totalPaid;
                    gRemaining = remainingBalance;
                }

                decimal refundedForThis = (!isRefund && refundedAmountByOriginalId.TryGetValue(p.Id, out var rTot)) ? rTot : 0m;
                bool isRefunded = !isRefund && refundedForThis >= p.AmountPaid;

                list.Add(new FinancialTransaction
                {
                    Id = p.Id,
                    TransactionType = FinancialTransactionType.Installment,
                    ClientId = clientId,
                    ClientName = clientName,
                    ClientPhone = phone,
                    Amount = absAmount,
                    PaymentDate = p.PaymentDate,
                    PaymentMethod = p.PaymentMethod,
                    Description = desc,
                    Notes = p.Notes,
                    SaleId = p.Installment?.SaleId,
                    InstallmentNumber = p.Installment?.InstallmentNumber,
                    TotalInstallments = p.Installment?.Sale?.InstallmentCount,
                    Reference = reference,
                    OriginalAmount = origAmount,
                    TotalPaid = totalPaid,
                    RemainingBalance = remainingBalance,
                    GlobalInitialDebt = gInitial,
                    GlobalTotalPaid = gPaid,
                    GlobalRemainingBalance = gRemaining,
                    OriginalPrincipal = origPrincipal,
                    ContractInterest = contractInterest,
                    UnpaidPrincipal = unpaidPrincipal,
                    IsRefund = isRefund,
                    IsRefunded = isRefunded,
                    RefundedAmount = refundedForThis > 0m ? refundedForThis : null,
                    OriginalPaymentId = origPaymentId > 0 ? origPaymentId : null
                });
            }

            // 2. Pagamentos de Mensalistas
            var monthlyPayments = await _context.MonthlyPayments
                .AsNoTracking()
                .Include(p => p.MonthlyCustomer)
                    .ThenInclude(m => m.Client)
                .ToListAsync();

            var monthlyRefunds = monthlyPayments
                .Where(p => p.Amount < 0m || (p.Notes != null && p.Notes.StartsWith("[ESTORNO:")))
                .ToList();

            var refundedMonthlyByOriginalId = new Dictionary<int, decimal>();
            foreach (var r in monthlyRefunds)
            {
                int origId = ParseOriginalPaymentId(r.Notes, "OriginalMonthlyPaymentId");
                if (origId > 0)
                {
                    decimal amt = Math.Abs(r.Amount);
                    refundedMonthlyByOriginalId[origId] = refundedMonthlyByOriginalId.TryGetValue(origId, out var cur)
                        ? cur + amt
                        : amt;
                }
            }

            foreach (var m in monthlyPayments)
            {
                var client = m.MonthlyCustomer?.Client;
                int clientId = client?.Id ?? (m.MonthlyCustomer?.ClientId ?? 0);
                string clientName = client?.Name ?? "Mensalista";
                string? phone = client?.Phone;

                bool isRefund = m.Amount < 0m || (m.Notes != null && m.Notes.StartsWith("[ESTORNO:"));
                int origPaymentId = isRefund ? ParseOriginalPaymentId(m.Notes, "OriginalMonthlyPaymentId") : 0;
                decimal absAmount = Math.Abs(m.Amount);

                string refText = !string.IsNullOrWhiteSpace(m.MonthlyCustomer?.Notes)
                    ? m.MonthlyCustomer.Notes!
                    : "Contrato de Mensalista";

                if (isRefund && origPaymentId > 0)
                {
                    refText = $"Estorno ref. Mensalidade #{origPaymentId}";
                }

                decimal origAmount = m.MonthlyCustomer?.MonthlyAmount ?? absAmount;
                decimal totalPaid = isRefund ? 0m : absAmount;
                decimal remainingBalance = 0m;

                decimal refundedForThis = (!isRefund && refundedMonthlyByOriginalId.TryGetValue(m.Id, out var rTot)) ? rTot : 0m;
                bool isRefunded = !isRefund && refundedForThis >= m.Amount;

                // Saldo global para mensalistas
                decimal? gInitial = null;
                decimal? gPaid = null;
                decimal? gRemaining = null;

                if (clientId > 0 && clientGlobalStatements.TryGetValue(clientId, out var gStmt))
                {
                    gInitial = gStmt.TotalInitialDebt;
                    gPaid = gStmt.TotalPaid;
                    gRemaining = gStmt.RemainingBalance;
                }
                else
                {
                    gInitial = origAmount;
                    gPaid = totalPaid;
                    gRemaining = 0m;
                }

                list.Add(new FinancialTransaction
                {
                    Id = m.Id,
                    TransactionType = FinancialTransactionType.Monthly,
                    ClientId = clientId,
                    ClientName = clientName,
                    ClientPhone = phone,
                    Amount = absAmount,
                    PaymentDate = m.PaymentDate,
                    PaymentMethod = m.PaymentMethod,
                    Description = isRefund
                        ? $"Estorno Mensalidade {m.ReferenceMonth:D2}/{m.ReferenceYear}"
                        : $"Mensalidade {m.ReferenceMonth:D2}/{m.ReferenceYear}",
                    Notes = m.Notes,
                    ReferenceMonth = m.ReferenceMonth,
                    ReferenceYear = m.ReferenceYear,
                    Reference = refText,
                    OriginalAmount = origAmount,
                    TotalPaid = totalPaid,
                    RemainingBalance = remainingBalance,
                    GlobalInitialDebt = gInitial,
                    GlobalTotalPaid = gPaid,
                    GlobalRemainingBalance = gRemaining,
                    IsRefund = isRefund,
                    IsRefunded = isRefunded,
                    RefundedAmount = refundedForThis > 0m ? refundedForThis : null,
                    OriginalPaymentId = origPaymentId > 0 ? origPaymentId : null
                });
            }

            return list.OrderByDescending(t => t.PaymentDate).ToList();
        }

        private static int ParseOriginalPaymentId(string? notes, string key)
        {
            if (string.IsNullOrWhiteSpace(notes)) return 0;
            string prefix = $"[{key}=";
            int idx = notes.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                prefix = $"[ESTORNO:{key}=";
                idx = notes.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            }
            if (idx < 0) return 0;

            int start = idx + prefix.Length;
            int end = notes.IndexOfAny(new[] { ';', ']', ' ' }, start);
            string token = (end >= 0) ? notes.Substring(start, end - start) : notes.Substring(start);
            return int.TryParse(token.Trim(), out int id) ? id : 0;
        }

        public async Task<List<FinancialTransaction>> GetTransactionsByClientAsync(int clientId)
        {
            var all = await GetAllTransactionsAsync();
            return all.Where(t => t.ClientId == clientId).OrderByDescending(t => t.PaymentDate).ToList();
        }

        public async Task<List<FinancialTransaction>> GetTransactionsByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            var start = startDate.Date;
            var end = endDate.Date.AddDays(1).AddTicks(-1);

            var all = await GetAllTransactionsAsync();
            return all
                .Where(t => t.PaymentDate >= start && t.PaymentDate <= end)
                .OrderByDescending(t => t.PaymentDate)
                .ToList();
        }

        public async Task<List<FinancialTransaction>> GetTransactionsByMonthAsync(int month, int year)
        {
            var start = new DateTime(year, month, 1, 0, 0, 0);
            var end = start.AddMonths(1).AddTicks(-1);

            var all = await GetAllTransactionsAsync();
            return all
                .Where(t => t.PaymentDate >= start && t.PaymentDate <= end)
                .OrderByDescending(t => t.PaymentDate)
                .ToList();
        }

        public async Task<FinancialSummary> GetFinancialSummaryAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var all = await GetAllTransactionsAsync();

            if (startDate.HasValue && endDate.HasValue)
            {
                var start = startDate.Value.Date;
                var end = endDate.Value.Date.AddDays(1).AddTicks(-1);
                all = all.Where(t => t.PaymentDate >= start && t.PaymentDate <= end).ToList();
            }

            var installmentPayments = all.Where(t => t.TransactionType == FinancialTransactionType.Installment).ToList();
            var monthlyPayments = all.Where(t => t.TransactionType == FinancialTransactionType.Monthly).ToList();

            decimal totalReceived = all.Where(t => !t.IsRefund).Sum(t => t.Amount) - all.Where(t => t.IsRefund).Sum(t => t.Amount);
            decimal instTotal = installmentPayments.Where(t => !t.IsRefund).Sum(t => t.Amount) - installmentPayments.Where(t => t.IsRefund).Sum(t => t.Amount);
            decimal monthlyTotal = monthlyPayments.Where(t => !t.IsRefund).Sum(t => t.Amount) - monthlyPayments.Where(t => t.IsRefund).Sum(t => t.Amount);

            return new FinancialSummary
            {
                TotalReceived = Math.Max(0m, totalReceived),
                InstallmentPaymentsTotal = Math.Max(0m, instTotal),
                InstallmentPaymentsCount = installmentPayments.Count(t => !t.IsRefund),
                MonthlyPaymentsTotal = Math.Max(0m, monthlyTotal),
                MonthlyPaymentsCount = monthlyPayments.Count(t => !t.IsRefund),
                TotalTransactionsCount = all.Count
            };
        }
    }
}
