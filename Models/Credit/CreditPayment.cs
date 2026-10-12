using System;
using System.Collections.Generic;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Registro de pagamento em dinheiro efetuado contra um contrato de crédito.
    /// </summary>
    public class CreditPayment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ContractId { get; set; } = string.Empty;
        public CreditContract? Contract { get; set; }

        public DateTime PaymentDate { get; set; }

        public long AmountCents { get; set; }

        public CreditPaymentStatus Status { get; set; } = CreditPaymentStatus.Active;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Navegações
        public ICollection<CreditPaymentInstallmentAlloc> Allocations { get; set; } = new List<CreditPaymentInstallmentAlloc>();
        public ICollection<CreditSurplusBalance> SurplusBalances { get; set; } = new List<CreditSurplusBalance>();
        public CreditPaymentRefund? Refund { get; set; }
        public ICollection<CreditDevolution> Devolutions { get; set; } = new List<CreditDevolution>();
        public ICollection<CreditCashOutflow> CashOutflows { get; set; } = new List<CreditCashOutflow>();
    }
}
