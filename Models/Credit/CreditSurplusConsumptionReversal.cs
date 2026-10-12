using System;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Reversão de consumo de excedente com garantia relacional 1:1.
    /// Restaura o saldo consumido à bolsa de excedente e os componentes financeiros.
    /// </summary>
    public class CreditSurplusConsumptionReversal
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string CreditSurplusConsumptionId { get; set; } = string.Empty;
        public CreditSurplusConsumption? Consumption { get; set; }

        public long ReversedAmountCents { get; set; }

        public long ReversedInterestCents { get; set; }

        public long ReversedPrincipalCents { get; set; }

        public DateTime ReversalDateUtc { get; set; } = DateTime.UtcNow;
    }
}
