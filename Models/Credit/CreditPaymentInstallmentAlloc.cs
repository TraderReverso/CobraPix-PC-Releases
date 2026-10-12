using System;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Alocação detalhada de um pagamento em uma parcela fixa.
    /// Registra explicitamente os componentes financeiros segundo a Regra A (Juros Primeiro, Principal Depois).
    /// AllocatedAmount = InterestAllocated + PrincipalAllocated.
    /// </summary>
    public class CreditPaymentInstallmentAlloc
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string PaymentId { get; set; } = string.Empty;
        public CreditPayment? Payment { get; set; }

        public string InstallmentId { get; set; } = string.Empty;
        public CreditInstallment? Installment { get; set; }

        public long AllocatedAmount { get; set; }

        public long InterestAllocated { get; set; }

        public long PrincipalAllocated { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
