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

            foreach (var p in installmentPayments)
            {
                var client = p.Installment?.Sale?.Client;
                int clientId = client?.Id ?? (p.Installment?.Sale?.ClientId ?? 0);
                string clientName = client?.Name ?? "Cliente";
                string? phone = client?.Phone;

                string desc = p.Installment != null
                    ? $"Parcela {p.Installment.InstallmentNumber} · Venda #{p.Installment.SaleId}"
                    : "Pagamento de Parcela";

                string reference = p.Installment?.Sale?.Description ?? $"Venda #{p.Installment?.SaleId}";

                decimal origAmount = p.Installment?.Amount ?? p.AmountPaid;
                decimal totalPaid = totalPaidByInstallment.TryGetValue(p.InstallmentId, out var tp) ? tp : p.AmountPaid;
                decimal remainingBalance = Math.Max(0m, origAmount - totalPaid);

                list.Add(new FinancialTransaction
                {
                    Id = p.Id,
                    TransactionType = FinancialTransactionType.Installment,
                    ClientId = clientId,
                    ClientName = clientName,
                    ClientPhone = phone,
                    Amount = p.AmountPaid,
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
                    RemainingBalance = remainingBalance
                });
            }

            // 2. Pagamentos de Mensalistas
            var monthlyPayments = await _context.MonthlyPayments
                .AsNoTracking()
                .Include(p => p.MonthlyCustomer)
                    .ThenInclude(m => m.Client)
                .ToListAsync();

            foreach (var m in monthlyPayments)
            {
                var client = m.MonthlyCustomer?.Client;
                int clientId = client?.Id ?? (m.MonthlyCustomer?.ClientId ?? 0);
                string clientName = client?.Name ?? "Mensalista";
                string? phone = client?.Phone;

                string refText = !string.IsNullOrWhiteSpace(m.MonthlyCustomer?.Notes)
                    ? m.MonthlyCustomer.Notes!
                    : "Contrato de Mensalista";

                decimal origAmount = m.MonthlyCustomer?.MonthlyAmount ?? m.Amount;
                decimal totalPaid = m.Amount;
                decimal remainingBalance = 0m;

                list.Add(new FinancialTransaction
                {
                    Id = m.Id,
                    TransactionType = FinancialTransactionType.Monthly,
                    ClientId = clientId,
                    ClientName = clientName,
                    ClientPhone = phone,
                    Amount = m.Amount,
                    PaymentDate = m.PaymentDate,
                    PaymentMethod = m.PaymentMethod,
                    Description = $"Mensalidade {m.ReferenceMonth:D2}/{m.ReferenceYear}",
                    Notes = m.Notes,
                    ReferenceMonth = m.ReferenceMonth,
                    ReferenceYear = m.ReferenceYear,
                    Reference = refText,
                    OriginalAmount = origAmount,
                    TotalPaid = totalPaid,
                    RemainingBalance = remainingBalance
                });
            }

            return list.OrderByDescending(t => t.PaymentDate).ToList();
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

            return new FinancialSummary
            {
                TotalReceived = all.Sum(t => t.Amount),
                InstallmentPaymentsTotal = installmentPayments.Sum(t => t.Amount),
                InstallmentPaymentsCount = installmentPayments.Count,
                MonthlyPaymentsTotal = monthlyPayments.Sum(t => t.Amount),
                MonthlyPaymentsCount = monthlyPayments.Count,
                TotalTransactionsCount = all.Count
            };
        }
    }
}
