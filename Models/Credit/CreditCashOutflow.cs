using System;
using CobraPix_PC.Models;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Registro único e imutável de saída financeira de caixa gerada exatamente uma vez
    /// no momento da liquidação (Settled) de uma devolução.
    /// </summary>
    public class CreditCashOutflow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string DevolutionId { get; set; } = string.Empty;
        public CreditDevolution? Devolution { get; set; }

        public string PaymentId { get; set; } = string.Empty;
        public CreditPayment? Payment { get; set; }

        public string ContractId { get; set; } = string.Empty;
        public CreditContract? Contract { get; set; }

        public long AmountCents { get; set; }

        public DateTime OutflowDate { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Pix;

        public string Description { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
