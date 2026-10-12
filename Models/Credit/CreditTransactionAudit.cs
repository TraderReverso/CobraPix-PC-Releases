using System;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Log estruturado de auditoria de operações financeiras do módulo de crédito.
    /// </summary>
    public class CreditTransactionAudit
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string? ContractId { get; set; }

        public string OperationType { get; set; } = string.Empty;

        public string DetailsJson { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
