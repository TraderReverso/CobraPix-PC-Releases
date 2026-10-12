using System;
using System.Collections.Generic;
using CobraPix_PC.Models;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Contrato de crédito ou capital de giro emitido para um cliente.
    /// Todos os valores monetários são expressos em centavos inteiros (long).
    /// </summary>
    public class CreditContract
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public int ClientId { get; set; }
        public Client? Client { get; set; }

        public string ContractNumber { get; set; } = string.Empty;

        public CreditContractType ContractType { get; set; }

        public CreditContractStatus Status { get; set; } = CreditContractStatus.Draft;

        /// <summary>
        /// Principal desembolsado contratual em centavos.
        /// </summary>
        public long DisbursedPrincipalCents { get; set; }

        /// <summary>
        /// Juros nominais totais contratados em centavos (para parcelas fixas).
        /// </summary>
        public long TotalNominalInterestCents { get; set; }

        /// <summary>
        /// Valor total do contrato em centavos (Principal + Juros).
        /// </summary>
        public long TotalAmountCents { get; set; }

        /// <summary>
        /// Data civil do contrato / desembolso planejado.
        /// </summary>
        public DateTime ContractDate { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? CancelledAtUtc { get; set; }
        public string? CancellationReason { get; set; }

        // Navegações
        public CreditDisbursement? Disbursement { get; set; }
        public ICollection<CreditInstallment> Installments { get; set; } = new List<CreditInstallment>();
        public ICollection<CreditCycle> Cycles { get; set; } = new List<CreditCycle>();
        public ICollection<CreditPayment> Payments { get; set; } = new List<CreditPayment>();
        public ICollection<CreditSurplusBalance> SurplusBalances { get; set; } = new List<CreditSurplusBalance>();
    }
}
