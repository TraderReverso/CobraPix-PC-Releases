using System;
using System.Collections.Generic;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Saldo excedente gerado por um pagamento cujo valor superou as obrigações exigíveis imediatas.
    /// Restrito ao contrato de origem na V1 (sem compensação intercontratos).
    /// </summary>
    public class CreditSurplusBalance
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ContractId { get; set; } = string.Empty;
        public CreditContract? Contract { get; set; }

        public string SourcePaymentId { get; set; } = string.Empty;
        public CreditPayment? SourcePayment { get; set; }

        public long OriginalSurplusCents { get; set; }

        public long RemainingSurplusCents { get; set; }

        public string FormattedOriginalSurplus => (OriginalSurplusCents / 100.0).ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        public string FormattedRemainingSurplus => (RemainingSurplusCents / 100.0).ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        public CreditSurplusStatus Status { get; set; } = CreditSurplusStatus.Available;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Navegações
        public ICollection<CreditSurplusConsumption> Consumptions { get; set; } = new List<CreditSurplusConsumption>();
    }
}
