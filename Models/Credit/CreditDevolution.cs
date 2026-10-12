using System;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Registro de devolução de caixa com ciclo de vida em duas fases:
    /// Authorized -> Settled ou Authorized -> Cancelled.
    /// </summary>
    public class CreditDevolution
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string? RefundId { get; set; }
        public CreditPaymentRefund? Refund { get; set; }

        public string PaymentId { get; set; } = string.Empty;
        public CreditPayment? Payment { get; set; }

        public string ContractId { get; set; } = string.Empty;
        public CreditContract? Contract { get; set; }

        public long AuthorizedAmountCents { get; set; }

        public CreditDevolutionStatus Status { get; set; } = CreditDevolutionStatus.Authorized;

        public string Reason { get; set; } = string.Empty;

        public DateTime AuthorizedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? SettledAtUtc { get; set; }

        public DateTime? CancelledAtUtc { get; set; }

        // Navegação para a saída de caixa gerada no momento da liquidação
        public CreditCashOutflow? CashOutflow { get; set; }
    }
}
