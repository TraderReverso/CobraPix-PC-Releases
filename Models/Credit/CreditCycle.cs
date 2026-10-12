using System;
using System.Collections.Generic;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Ciclo de faturamento periódico para linhas de capital de giro rotativo.
    /// </summary>
    public class CreditCycle
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ContractId { get; set; } = string.Empty;
        public CreditContract? Contract { get; set; }

        public int CycleNumber { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public DateTime DueDate { get; set; }

        public long AccruedInterestCents { get; set; }

        public long PaidInterestCents { get; set; }

        public long MinimumAmountCents { get; set; }

        public long PaidAmountCents { get; set; }

        public CreditCycleStatus Status { get; set; } = CreditCycleStatus.Open;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Navegações
        public ICollection<CreditSurplusConsumption> SurplusConsumptions { get; set; } = new List<CreditSurplusConsumption>();
    }
}
