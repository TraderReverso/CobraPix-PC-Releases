using System;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Registro de estorno de pagamento em duas etapas.
    /// Etapa 1: Authorized (reabre a dívida imediatamente).
    /// Etapa 2: Settled (saída de caixa e devolução efetiva consumada) ou Cancelled.
    /// </summary>
    public class CreditPaymentRefund
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string PaymentId { get; set; } = string.Empty;
        public CreditPayment? Payment { get; set; }

        public long AuthorizedAmountCents { get; set; }

        public CreditRefundStatus Status { get; set; } = CreditRefundStatus.Authorized;

        public string Reason { get; set; } = string.Empty;

        public DateTime AuthorizedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? SettledAtUtc { get; set; }

        public DateTime? CancelledAtUtc { get; set; }
        public CreditDevolution? Devolution { get; set; }
    }
}
