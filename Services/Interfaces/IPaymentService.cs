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

        Task<PaymentLogic.ClientGlobalStatement> GetClientGlobalStatementAsync(int clientId);
        Task<PaymentLogic.PayableFinancialStatement> GetFinancialStatementAsync(int installmentId, DateTime? referenceTime = null);
        Task<bool> DeleteInstallmentAsync(int installmentId);

        // Estorno de Pagamento com Rastreabilidade
        Task<RefundResult> RefundInstallmentPaymentAsync(int paymentId, decimal? refundAmount = null, string? reason = null);
        Task<RefundResult> RefundMonthlyPaymentAsync(int monthlyPaymentId, decimal? refundAmount = null, string? reason = null);
    }

    /// <summary>
    /// Resultado de uma operação de estorno financeiro de pagamento.
    /// </summary>
    public class RefundResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public decimal RefundedAmount { get; set; }
        public InstallmentPayment? RefundPayment { get; set; }
        public MonthlyPayment? RefundMonthlyPayment { get; set; }
        public Installment? UpdatedInstallment { get; set; }
        public PaymentLogic.ClientGlobalStatement? UpdatedGlobalStatement { get; set; }

        public static RefundResult Ok(decimal amount, InstallmentPayment payment, Installment installment, PaymentLogic.ClientGlobalStatement statement) =>
            new RefundResult
            {
                Success = true,
                RefundedAmount = amount,
                RefundPayment = payment,
                UpdatedInstallment = installment,
                UpdatedGlobalStatement = statement
            };

        public static RefundResult OkMonthly(decimal amount, MonthlyPayment payment, PaymentLogic.ClientGlobalStatement statement) =>
            new RefundResult
            {
                Success = true,
                RefundedAmount = amount,
                RefundMonthlyPayment = payment,
                UpdatedGlobalStatement = statement
            };

        public static RefundResult Fail(string error) =>
            new RefundResult
            {
                Success = false,
                ErrorMessage = error
            };
    }
}
