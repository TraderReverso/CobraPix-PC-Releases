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
    public class SaleService : ISaleService
    {
        private readonly CobraPixDbContext _context;

        public SaleService(CobraPixDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<Sale> CreateSaleAsync(
            int clientId,
            decimal totalAmount,
            int installmentCount,
            DateTime firstDueDate,
            string? description = null,
            string? notes = null,
            double contractInterestRate = 0.0,
            bool useFifthBusinessDay = false,
            int periodicity = 0,
            DayOfWeek? targetWeekday = null)
        {
            // Validações
            if (clientId <= 0)
            {
                throw new BusinessRuleException("Cliente inválido.");
            }

            var clientExists = await _context.Clients.AnyAsync(c => c.Id == clientId);
            if (!clientExists)
            {
                throw new BusinessRuleException($"Cliente com ID {clientId} não encontrado.");
            }

            if (totalAmount <= 0)
            {
                throw new BusinessRuleException("O valor total da venda deve ser maior que zero.");
            }

            if (installmentCount <= 0)
            {
                throw new BusinessRuleException("A quantidade de parcelas deve ser de pelo menos 1.");
            }

            // Geração consistente das parcelas com divisão precisa de centavos e juros
            var generatedInstallments = GenerateInstallments(
                totalAmount,
                installmentCount,
                firstDueDate,
                contractInterestRate,
                useFifthBusinessDay,
                periodicity,
                targetWeekday);

            decimal finalTotal = generatedInstallments.Sum(i => i.Amount);
            decimal interestAmount = Math.Max(0m, finalTotal - totalAmount);

            var sale = new Sale
            {
                ClientId = clientId,
                TotalAmount = finalTotal,
                OriginalPrincipal = totalAmount,
                ContractInterestRate = contractInterestRate > 0.0 ? contractInterestRate : 0.0,
                ContractInterestAmount = interestAmount,
                InstallmentCount = installmentCount,
                SaleDate = DateTime.Today,
                Description = description?.Trim(),
                Notes = notes?.Trim(),
                UseFifthBusinessDay = useFifthBusinessDay,
                Periodicity = periodicity,
                CreatedAt = DateTime.Now
            };
            sale.EmbedParityMetadata(totalAmount, contractInterestRate > 0.0 ? contractInterestRate : 0.0, interestAmount, useFifthBusinessDay, periodicity);

            // Utiliza transação para garantir que venda e todas as suas parcelas sejam gravadas atomicamente
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Sales.Add(sale);
                await _context.SaveChangesAsync();

                foreach (var installment in generatedInstallments)
                {
                    installment.SaleId = sale.Id;
                    _context.Installments.Add(installment);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                DiagnosticLogger.Instance.LogInformation("SaleService",
                    $"Venda #{sale.Id} criada para cliente #{clientId}. Total: {finalTotal:C2}, Principal: {totalAmount:C2}, Juros: {interestAmount:C2} ({contractInterestRate}%), Parcelas: {installmentCount}.");

                return sale;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new BusinessRuleException($"Erro ao salvar venda parcelada: {ex.Message}", ex);
            }
        }

        public static List<Installment> GenerateInstallments(
            decimal totalAmount,
            int installmentCount,
            DateTime firstDueDate,
            double contractInterestRate = 0.0,
            bool useFifthBusinessDay = false,
            int periodicity = 0,
            DayOfWeek? targetWeekday = null)
        {
            if (totalAmount <= 0)
                throw new BusinessRuleException("Valor total deve ser maior que zero.");

            if (installmentCount <= 0)
                throw new BusinessRuleException("Quantidade de parcelas deve ser maior que zero.");

            var calc = PaymentLogic.CalculateContractInterest(totalAmount, contractInterestRate, installmentCount);
            var dueDates = PaymentLogic.CalculateInstallmentDueDates(
                installmentCount,
                firstDueDate,
                useFifthBusinessDay,
                periodicity,
                targetWeekday);

            var list = new List<Installment>(installmentCount);

            for (int i = 0; i < installmentCount; i++)
            {
                decimal amount = (i < calc.InstallmentValues.Count)
                    ? calc.InstallmentValues[i]
                    : totalAmount / installmentCount;

                DateTime dueDate = (i < dueDates.Count)
                    ? dueDates[i]
                    : PaymentLogic.CalculateMonthlyDueDate(firstDueDate, i);

                list.Add(new Installment
                {
                    InstallmentNumber = i + 1,
                    Amount = amount,
                    DueDate = dueDate,
                    Status = InstallmentStatus.Pending
                });
            }

            return list;
        }

        public async Task<Sale?> GetSaleByIdAsync(int saleId)
        {
            return await _context.Sales
                .Include(s => s.Client)
                .Include(s => s.Installments.OrderBy(i => i.InstallmentNumber))
                    .ThenInclude(i => i.Payments)
                .FirstOrDefaultAsync(s => s.Id == saleId);
        }

        public async Task<List<Sale>> GetSalesByClientIdAsync(int clientId)
        {
            return await _context.Sales
                .AsNoTracking()
                .Where(s => s.ClientId == clientId)
                .Include(s => s.Installments.OrderBy(i => i.InstallmentNumber))
                    .ThenInclude(i => i.Payments)
                .OrderByDescending(s => s.SaleDate)
                .ToListAsync();
        }

        public async Task<List<Sale>> GetAllSalesAsync()
        {
            return await _context.Sales
                .AsNoTracking()
                .Include(s => s.Client)
                .Include(s => s.Installments.OrderBy(i => i.InstallmentNumber))
                    .ThenInclude(i => i.Payments)
                .OrderByDescending(s => s.SaleDate)
                .ThenByDescending(s => s.Id)
                .ToListAsync();
        }

        public List<Installment> PreviewInstallments(
            decimal totalAmount,
            int installmentCount,
            DateTime firstDueDate,
            double contractInterestRate = 0.0,
            bool useFifthBusinessDay = false,
            int periodicity = 0,
            DayOfWeek? targetWeekday = null)
        {
            return GenerateInstallments(
                totalAmount,
                installmentCount,
                firstDueDate,
                contractInterestRate,
                useFifthBusinessDay,
                periodicity,
                targetWeekday);
        }

        public async Task<List<Installment>> GetInstallmentsBySaleIdAsync(int saleId)
        {
            return await _context.Installments
                .Where(i => i.SaleId == saleId)
                .Include(i => i.Payments)
                .OrderBy(i => i.InstallmentNumber)
                .ToListAsync();
        }

        public async Task<bool> DeleteInstallmentAsync(int installmentId)
        {
            if (installmentId <= 0)
            {
                throw new BusinessRuleException("Identificador de parcela inválido.");
            }

            var installment = await _context.Installments
                .Include(i => i.Payments)
                .Include(i => i.Sale)
                .FirstOrDefaultAsync(i => i.Id == installmentId);

            if (installment == null)
            {
                throw new BusinessRuleException($"Parcela com ID {installmentId} não encontrada.");
            }

            // Regra mandatória de segurança financeira (Paridade com Android):
            // Não é permitido excluir uma parcela com pagamentos registrados ou já quitada.
            if (installment.Payments.Any(p => p.AmountPaid > 0) || installment.Status == InstallmentStatus.Paid)
            {
                throw new BusinessRuleException(
                    "Não é possível excluir uma parcela com pagamentos registrados. Estorne os pagamentos no histórico primeiro.");
            }

            int saleId = installment.SaleId;
            var sale = installment.Sale;

            _context.Installments.Remove(installment);
            await _context.SaveChangesAsync();

            // Recálculo imediato do valor total e contagem da venda após exclusão
            if (sale != null)
            {
                var remaining = await _context.Installments
                    .Where(i => i.SaleId == saleId)
                    .ToListAsync();

                var (newTotal, newCount) = PaymentLogic.RecalculateSaleAfterInstallmentDeletion(
                    remaining.Select(i => i.Amount));

                sale.TotalAmount = newTotal;
                sale.InstallmentCount = newCount;
                await _context.SaveChangesAsync();

                DiagnosticLogger.Instance.LogInformation("SaleService",
                    $"Parcela #{installmentId} excluída. Venda #{saleId} recalculada: {newCount} parcelas restantes, novo total: {newTotal:C2}.");
            }

            return true;
        }
    }
}
