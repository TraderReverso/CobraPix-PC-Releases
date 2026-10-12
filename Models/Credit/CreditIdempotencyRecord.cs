using System;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Registro persistente de idempotência para garantia de processamento exatamente-uma-vez.
    /// Chave primária lógica: (Scope, IdempotencyKey) UNIQUE.
    /// </summary>
    public class CreditIdempotencyRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Scope { get; set; } = string.Empty;

        public string IdempotencyKey { get; set; } = string.Empty;

        public string RequestPayloadHash { get; set; } = string.Empty;

        public string ResponsePayloadJson { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
