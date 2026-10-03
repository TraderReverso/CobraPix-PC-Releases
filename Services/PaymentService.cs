using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly CobraPixDbContext _context;

        public PaymentService(CobraPixDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<InstallmentPayment> RegisterPaymentAsync(
            int installmentId,
            decimal amountPaid,
            DateTime paymentDate,
            PaymentMethod paymentMethod,
            string? notes = null)
        {
            if (installmentId <= 0)
            {
                throw new BusinessRuleException("Identificador de parcela inválido.");
            }

            if (amountPaid <= 0)
            {
                throw new BusinessRuleException("O valor do pagamento deve ser maior que zero.");
            }

            var installment = await _context.Installments
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == installmentId);

            if (installment == null)
            {
                throw new BusinessRuleException($"Parcela com ID {installmentId} não encontrada.");
            }

            decimal currentTotalPaid = installment.Payments.Sum(p => p.AmountPaid);
            decimal remainingBalance = installment.Amount - currentTotalPaid;

            if (amountPaid > remainingBalance)
            {
                throw new BusinessRuleException(
                    $"Valor do pagamento (R$ {amountPaid:N2}) não pode exceder o saldo restante da parcela (R$ {remainingBalance:N2}).");
            }

            var payment = new InstallmentPayment
            {
                InstallmentId = installmentId,
                AmountPaid = amountPaid,
                PaymentDate = paymentDate,
                PaymentMethod = paymentMethod,
                Notes = notes?.Trim()
            };

            _context.InstallmentPayments.Add(payment);

            // Atualiza o status da parcela com base no novo saldo
            decimal newRemainingBalance = remainingBalance - amountPaid;

            if (newRemainingBalance == 0)
            {
                installment.Status = InstallmentStatus.Paid;
                installment.PaidDate = paymentDate;
            }
            else
            {
                // Parcela parcialmente paga: permanece PENDING se não venceu, ou OVERDUE se já venceu
                if (installment.DueDate.Date < DateTime.Today)
                {
                    installment.Status = InstallmentStatus.Overdue;
                }
                else
                {
                    installment.Status = InstallmentStatus.Pending;
                }

                installment.PaidDate = null;
            }

            await _context.SaveChangesAsync();
            return payment;
        }

        public async Task<int> UpdateOverdueInstallmentsAsync(DateTime? referenceDate = null)
        {
            var today = (referenceDate ?? DateTime.Today).Date;

            // Seleciona somente parcelas que estão pendentes e com vencimento anterior à data de referência
            // Parcelas PAID jamais são alteradas
            var overdueInstallments = await _context.Installments
                .Where(i => i.Status == InstallmentStatus.Pending && i.DueDate.Date < today)
                .ToListAsync();

            if (overdueInstallments.Count == 0)
            {
                return 0;
            }

            foreach (var installment in overdueInstallments)
            {
                installment.Status = InstallmentStatus.Overdue;
            }

            await _context.SaveChangesAsync();
            return overdueInstallments.Count;
        }

        public async Task<List<Installment>> GetAllInstallmentsAsync()
        {
            return await _context.Installments
                .AsNoTracking()
                .Include(i => i.Sale)
                    .ThenInclude(s => s.Client)
                .Include(i => i.Payments)
                .OrderBy(i => i.DueDate)
                .ThenBy(i => i.InstallmentNumber)
                .ToListAsync();
        }

        public async Task<List<Installment>> GetPendingInstallmentsAsync()
        {
            return await _context.Installments
                .AsNoTracking()
                .Where(i => i.Status == InstallmentStatus.Pending)
                .Include(i => i.Sale)
                    .ThenInclude(s => s.Client)
                .Include(i => i.Payments)
                .OrderBy(i => i.DueDate)
                .ToListAsync();
        }

        public async Task<List<Installment>> GetOverdueInstallmentsAsync()
        {
            return await _context.Installments
                .AsNoTracking()
                .Where(i => i.Status == InstallmentStatus.Overdue)
                .Include(i => i.Sale)
                    .ThenInclude(s => s.Client)
                .Include(i => i.Payments)
                .OrderBy(i => i.DueDate)
                .ToListAsync();
        }

        public async Task<List<Installment>> GetPaidInstallmentsAsync()
        {
            return await _context.Installments
                .AsNoTracking()
                .Where(i => i.Status == InstallmentStatus.Paid)
                .Include(i => i.Sale)
                    .ThenInclude(s => s.Client)
                .Include(i => i.Payments)
                .OrderByDescending(i => i.PaidDate)
                .ToListAsync();
        }

        public async Task<List<InstallmentPayment>> GetPaymentsByInstallmentIdAsync(int installmentId)
        {
            return await _context.InstallmentPayments
                .AsNoTracking()
                .Where(p => p.InstallmentId == installmentId)
                .OrderBy(p => p.PaymentDate)
                .ToListAsync();
        }

        public async Task<decimal> GetTotalPaidForInstallmentAsync(int installmentId)
        {
            return await _context.InstallmentPayments
                .Where(p => p.InstallmentId == installmentId)
                .SumAsync(p => p.AmountPaid);
        }

        public async Task<decimal> GetRemainingBalanceForInstallmentAsync(int installmentId)
        {
            var installment = await _context.Installments
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == installmentId);

            if (installment == null)
            {
                throw new BusinessRuleException($"Parcela com ID {installmentId} não encontrada.");
            }

            decimal totalPaid = installment.Payments.Sum(p => p.AmountPaid);
            return Math.Max(0m, installment.Amount - totalPaid);
        }
    }
}
