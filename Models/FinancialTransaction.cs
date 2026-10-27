using System;

namespace CobraPix_PC.Models
{
    public enum FinancialTransactionType
    {
        Installment = 1,
        Monthly = 2
    }

    /// <summary>
    /// Modelo unificado que representa uma transação financeira de entrada (recebimento efetivo),
    /// seja ela originada de pagamento de parcela ou de mensalidade.
    /// Inclui campos de paridade funcional para exibição de demonstrativo global e juros no recibo.
    /// </summary>
    public class FinancialTransaction
    {
        public int Id { get; set; }

        public FinancialTransactionType TransactionType { get; set; }

        public int ClientId { get; set; }

        public string ClientName { get; set; } = string.Empty;

        public string? ClientPhone { get; set; }

        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; }

        public PaymentMethod PaymentMethod { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? Notes { get; set; }

        public int? SaleId { get; set; }

        public int? InstallmentNumber { get; set; }

        public int? TotalInstallments { get; set; }

        public int? ReferenceMonth { get; set; }

        public int? ReferenceYear { get; set; }

        public string Reference { get; set; } = string.Empty;

        public decimal? OriginalAmount { get; set; }

        public decimal? TotalPaid { get; set; }

        public decimal? RemainingBalance { get; set; }

        // Campos de Paridade Funcional Android para Comprovantes e Recibos
        public decimal? GlobalInitialDebt { get; set; }

        public decimal? GlobalTotalPaid { get; set; }

        public decimal? GlobalRemainingBalance { get; set; }

        public decimal? ContractInterest { get; set; }

        public decimal? OverdueInterest { get; set; }

        public decimal? OriginalPrincipal { get; set; }

        public decimal? UnpaidPrincipal { get; set; }

        public string? ReceiverName { get; set; }

        public string? PixKey { get; set; }

        // Campos de Estorno e Rastreabilidade Financeira
        public bool IsRefund { get; set; }

        public bool IsRefunded { get; set; }

        public decimal? RefundedAmount { get; set; }

        public int? OriginalPaymentId { get; set; }
    }

    /// <summary>
    /// Resumo consolidado de recebimentos financeiros em um período determinado.
    /// </summary>
    public class FinancialSummary
    {
        public decimal TotalReceived { get; set; }

        public decimal InstallmentPaymentsTotal { get; set; }

        public int InstallmentPaymentsCount { get; set; }

        public decimal MonthlyPaymentsTotal { get; set; }

        public int MonthlyPaymentsCount { get; set; }

        public int TotalTransactionsCount { get; set; }
    }
}
