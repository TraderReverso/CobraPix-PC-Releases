using System;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Consumo de saldo excedente aplicado para amortizar uma obrigação.
    /// Respeita a Regra A: juros primeiro (InterestConsumed), principal depois (PrincipalConsumed).
    /// </summary>
    public class CreditSurplusConsumption
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string SurplusBalanceId { get; set; } = string.Empty;
        public CreditSurplusBalance? SurplusBalance { get; set; }

        public string? TargetInstallmentId { get; set; }
        public CreditInstallment? TargetInstallment { get; set; }

        public string? TargetCycleId { get; set; }
        public CreditCycle? TargetCycle { get; set; }

        public long ConsumedAmount { get; set; }

        public long InterestConsumed { get; set; }

        public long PrincipalConsumed { get; set; }

        public bool IsReversed { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Navegação de reversão 1:1
        public CreditSurplusConsumptionReversal? Reversal { get; set; }
    }
}
