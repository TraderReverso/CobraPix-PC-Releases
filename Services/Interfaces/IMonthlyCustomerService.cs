using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    public interface IMonthlyCustomerService
    {
        Task<MonthlyCustomer> CreateMonthlyCustomerAsync(
            int clientId,
            decimal monthlyAmount,
            int dueDay,
            DateTime startDate,
            string? notes = null);

        Task<MonthlyCustomer> UpdateMonthlyCustomerAsync(
            int id,
            decimal monthlyAmount,
            int dueDay,
            string? notes = null);

        Task<bool> SetStatusAsync(int id, bool isActive);

        Task<MonthlyPayment> RegisterPaymentAsync(
            int monthlyCustomerId,
            decimal amount,
            DateTime paymentDate,
            int referenceMonth,
            int referenceYear,
            PaymentMethod paymentMethod,
            string? notes = null);

        Task<MonthlyCustomer?> GetByIdAsync(int id);
        Task<MonthlyCustomer?> GetByClientIdAsync(int clientId);
        Task<List<MonthlyCustomer>> GetAllMonthlyCustomersAsync();
        Task<List<MonthlyCustomer>> GetActiveMonthlyCustomersAsync();
        Task<List<MonthlyPayment>> GetPaymentsByMonthlyCustomerAsync(int monthlyCustomerId);
    }
}
