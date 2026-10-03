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
            string? notes = null)
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

            // Geração consistente das parcelas com divisão precisa de centavos
            var generatedInstallments = GenerateInstallments(totalAmount, installmentCount, firstDueDate);

            var sale = new Sale
            {
                ClientId = clientId,
                TotalAmount = totalAmount,
                InstallmentCount = installmentCount,
                SaleDate = DateTime.Today,
                Description = description?.Trim(),
                Notes = notes?.Trim(),
                CreatedAt = DateTime.Now
            };

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

                return sale;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new BusinessRuleException($"Erro ao salvar venda parcelada: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Divide o valor total entre as parcelas com distribuição exata dos centavos restantes
        /// e calcula os vencimentos mensais tratando meses com 28, 29, 30 ou 31 dias.
        /// </summary>
        public static List<Installment> GenerateInstallments(decimal totalAmount, int installmentCount, DateTime firstDueDate)
        {
            if (totalAmount <= 0)
                throw new BusinessRuleException("Valor total deve ser maior que zero.");

            if (installmentCount <= 0)
                throw new BusinessRuleException("Quantidade de parcelas deve ser maior que zero.");

            // Truncamento na 2ª casa decimal para o valor base de cada parcela
            decimal baseAmount = Math.Floor((totalAmount / installmentCount) * 100m) / 100m;
            decimal remainder = totalAmount - (baseAmount * installmentCount);
            int remainderCents = (int)Math.Round(remainder * 100m);

            var list = new List<Installment>(installmentCount);

            for (int i = 1; i <= installmentCount; i++)
            {
                // Os centavos residuais são distribuídos 1 a 1 nas primeiras parcelas
                decimal amount = baseAmount + (i <= remainderCents ? 0.01m : 0.00m);
                DateTime dueDate = CalculateMonthlyDueDate(firstDueDate, i - 1);

                list.Add(new Installment
                {
                    InstallmentNumber = i,
                    Amount = amount,
                    DueDate = dueDate,
                    Status = InstallmentStatus.Pending
                });
            }

            return list;
        }

        /// <summary>
        /// Calcula o vencimento da n-ésima parcela mensal, preservando o dia original
        /// e ajustando para o último dia válido em meses com menor quantidade de dias (ex: 31/01 -> 28/02).
        /// </summary>
        public static DateTime CalculateMonthlyDueDate(DateTime firstDueDate, int installmentIndex)
        {
            if (installmentIndex == 0)
                return firstDueDate;

            int targetDay = firstDueDate.Day;
            int totalMonths = (firstDueDate.Year * 12 + (firstDueDate.Month - 1)) + installmentIndex;
            int year = totalMonths / 12;
            int month = (totalMonths % 12) + 1;
            int daysInMonth = DateTime.DaysInMonth(year, month);
            int day = Math.Min(targetDay, daysInMonth);

            return new DateTime(year, month, day, firstDueDate.Hour, firstDueDate.Minute, firstDueDate.Second);
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

        public List<Installment> PreviewInstallments(decimal totalAmount, int installmentCount, DateTime firstDueDate)
        {
            return GenerateInstallments(totalAmount, installmentCount, firstDueDate);
        }

        public async Task<List<Installment>> GetInstallmentsBySaleIdAsync(int saleId)
        {
            return await _context.Installments
                .Where(i => i.SaleId == saleId)
                .Include(i => i.Payments)
                .OrderBy(i => i.InstallmentNumber)
                .ToListAsync();
        }
    }
}
