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
    public class MonthlyCustomerService : IMonthlyCustomerService
    {
        private readonly CobraPixDbContext _context;

        public MonthlyCustomerService(CobraPixDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<MonthlyCustomer> CreateMonthlyCustomerAsync(
            int clientId,
            decimal monthlyAmount,
            int dueDay,
            DateTime startDate,
            string? notes = null)
        {
            if (clientId <= 0)
            {
                throw new BusinessRuleException("Cliente inválido.");
            }

            var clientExists = await _context.Clients.AnyAsync(c => c.Id == clientId);
            if (!clientExists)
            {
                throw new BusinessRuleException($"Cliente com ID {clientId} não encontrado.");
            }

            var alreadyMonthly = await _context.MonthlyCustomers.AnyAsync(m => m.ClientId == clientId);
            if (alreadyMonthly)
            {
                throw new BusinessRuleException($"O cliente com ID {clientId} já possui cadastro de mensalista.");
            }

            if (monthlyAmount <= 0)
            {
                throw new BusinessRuleException("O valor da mensalidade deve ser maior que zero.");
            }

            if (dueDay < 1 || dueDay > 31)
            {
                throw new BusinessRuleException("O dia de vencimento deve estar entre 1 e 31.");
            }

            var monthlyCustomer = new MonthlyCustomer
            {
                ClientId = clientId,
                MonthlyAmount = monthlyAmount,
                DueDay = dueDay,
                StartDate = startDate.Date,
                IsActive = true,
                Notes = notes?.Trim(),
                CreatedAt = DateTime.Now
            };

            _context.MonthlyCustomers.Add(monthlyCustomer);
            await _context.SaveChangesAsync();
            return monthlyCustomer;
        }

        public async Task<MonthlyCustomer> UpdateMonthlyCustomerAsync(
            int id,
            decimal monthlyAmount,
            int dueDay,
            string? notes = null)
        {
            var customer = await _context.MonthlyCustomers.FirstOrDefaultAsync(m => m.Id == id);
            if (customer == null)
            {
                throw new BusinessRuleException($"Mensalista com ID {id} não encontrado.");
            }

            if (monthlyAmount <= 0)
            {
                throw new BusinessRuleException("O valor da mensalidade deve ser maior que zero.");
            }

            if (dueDay < 1 || dueDay > 31)
            {
                throw new BusinessRuleException("O dia de vencimento deve estar entre 1 e 31.");
            }

            customer.MonthlyAmount = monthlyAmount;
            customer.DueDay = dueDay;
            customer.Notes = notes?.Trim();

            await _context.SaveChangesAsync();
            return customer;
        }

        public async Task<bool> SetStatusAsync(int id, bool isActive)
        {
            var customer = await _context.MonthlyCustomers.FirstOrDefaultAsync(m => m.Id == id);
            if (customer == null)
            {
                throw new BusinessRuleException($"Mensalista com ID {id} não encontrado.");
            }

            customer.IsActive = isActive;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<MonthlyPayment> RegisterPaymentAsync(
            int monthlyCustomerId,
            decimal amount,
            DateTime paymentDate,
            int referenceMonth,
            int referenceYear,
            PaymentMethod paymentMethod,
            string? notes = null)
        {
            if (monthlyCustomerId <= 0)
            {
                throw new BusinessRuleException("Identificador de mensalista inválido.");
            }

            var customer = await _context.MonthlyCustomers.FirstOrDefaultAsync(m => m.Id == monthlyCustomerId);
            if (customer == null)
            {
                throw new BusinessRuleException($"Mensalista com ID {monthlyCustomerId} não encontrado.");
            }

            if (amount <= 0)
            {
                throw new BusinessRuleException("O valor do pagamento da mensalidade deve ser maior que zero.");
            }

            if (referenceMonth < 1 || referenceMonth > 12)
            {
                throw new BusinessRuleException("O mês de referência deve estar entre 1 e 12.");
            }

            if (referenceYear < 2000 || referenceYear > 2100)
            {
                throw new BusinessRuleException("O ano de referência é inválido.");
            }

            var alreadyPaid = await _context.MonthlyPayments.AnyAsync(p =>
                p.MonthlyCustomerId == monthlyCustomerId &&
                p.ReferenceMonth == referenceMonth &&
                p.ReferenceYear == referenceYear);
            if (alreadyPaid)
            {
                throw new BusinessRuleException($"Já existe um pagamento registrado para este mensalista no mês {referenceMonth:D2}/{referenceYear}.");
            }

            var payment = new MonthlyPayment
            {
                MonthlyCustomerId = monthlyCustomerId,
                Amount = amount,
                PaymentDate = paymentDate,
                ReferenceMonth = referenceMonth,
                ReferenceYear = referenceYear,
                PaymentMethod = paymentMethod,
                Notes = notes?.Trim()
            };

            _context.MonthlyPayments.Add(payment);
            await _context.SaveChangesAsync();
            return payment;
        }

        public async Task<MonthlyCustomer?> GetByIdAsync(int id)
        {
            return await _context.MonthlyCustomers
                .Include(m => m.Client)
                .Include(m => m.Payments.OrderByDescending(p => p.PaymentDate))
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<MonthlyCustomer?> GetByClientIdAsync(int clientId)
        {
            return await _context.MonthlyCustomers
                .Include(m => m.Client)
                .Include(m => m.Payments.OrderByDescending(p => p.PaymentDate))
                .FirstOrDefaultAsync(m => m.ClientId == clientId);
        }

        public async Task<List<MonthlyCustomer>> GetAllMonthlyCustomersAsync()
        {
            return await _context.MonthlyCustomers
                .AsNoTracking()
                .Include(m => m.Client)
                .Include(m => m.Payments)
                .OrderBy(m => m.Client.Name)
                .ToListAsync();
        }

        public async Task<List<MonthlyCustomer>> GetActiveMonthlyCustomersAsync()
        {
            return await _context.MonthlyCustomers
                .AsNoTracking()
                .Where(m => m.IsActive)
                .Include(m => m.Client)
                .Include(m => m.Payments)
                .OrderBy(m => m.Client.Name)
                .ToListAsync();
        }

        public async Task<List<MonthlyPayment>> GetPaymentsByMonthlyCustomerAsync(int monthlyCustomerId)
        {
            return await _context.MonthlyPayments
                .AsNoTracking()
                .Where(p => p.MonthlyCustomerId == monthlyCustomerId)
                .OrderByDescending(p => p.ReferenceYear)
                .ThenByDescending(p => p.ReferenceMonth)
                .ThenByDescending(p => p.PaymentDate)
                .ToListAsync();
        }
    }
}
