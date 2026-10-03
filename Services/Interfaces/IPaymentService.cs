using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    public interface IPaymentService
    {
        Task<InstallmentPayment> RegisterPaymentAsync(
            int installmentId,
            decimal amountPaid,
            DateTime paymentDate,
            PaymentMethod paymentMethod,
            string? notes = null);

        Task<int> UpdateOverdueInstallmentsAsync(DateTime? referenceDate = null);

        Task<List<Installment>> GetAllInstallmentsAsync();
        Task<List<Installment>> GetPendingInstallmentsAsync();
        Task<List<Installment>> GetOverdueInstallmentsAsync();
        Task<List<Installment>> GetPaidInstallmentsAsync();
        Task<List<InstallmentPayment>> GetPaymentsByInstallmentIdAsync(int installmentId);
        Task<decimal> GetTotalPaidForInstallmentAsync(int installmentId);
        Task<decimal> GetRemainingBalanceForInstallmentAsync(int installmentId);
    }
}
