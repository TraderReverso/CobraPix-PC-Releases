using System;
using CobraPix_PC.Models;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Registro de desembolso financeiro associado ao contrato.
    /// Exatamente um desembolso por contrato (relação 1:1 estrita).
    /// </summary>
    public class CreditDisbursement
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ContractId { get; set; } = string.Empty;
        public CreditContract? Contract { get; set; }

        public long DisbursedAmountCents { get; set; }

        public DateTime DisbursementDate { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Pix;

        public string? Notes { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
