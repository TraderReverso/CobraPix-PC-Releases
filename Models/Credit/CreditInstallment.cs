using System;
using System.Collections.Generic;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Parcela fixa individual de um contrato de crédito com juros pré-fixados.
    /// Respeita a decomposição exata em centavos: Total = NominalPrincipal + NominalInterest.
    /// </summary>
    public class CreditInstallment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ContractId { get; set; } = string.Empty;
        public CreditContract? Contract { get; set; }

        public int InstallmentNumber { get; set; }

        public DateTime DueDate { get; set; }

        public long NominalPrincipalCents { get; set; }

        public long NominalInterestCents { get; set; }

        public long TotalAmountCents { get; set; }

        public long PaidAmountCents { get; set; }

        public CreditInstallmentStatus Status { get; set; } = CreditInstallmentStatus.Pending;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Navegações
        public ICollection<CreditPaymentInstallmentAlloc> Allocations { get; set; } = new List<CreditPaymentInstallmentAlloc>();
        public ICollection<CreditSurplusConsumption> SurplusConsumptions { get; set; } = new List<CreditSurplusConsumption>();
    }
}
