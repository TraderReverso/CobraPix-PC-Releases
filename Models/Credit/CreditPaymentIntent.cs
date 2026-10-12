using System;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Intenção de pagamento gravada na Fase 1 do Two-Phase Intent Capture,
    /// sobrevivendo a crashes antes do commit da transação de negócio principal.
    /// </summary>
    public class CreditPaymentIntent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string IdempotencyKey { get; set; } = string.Empty;

        public string ContractId { get; set; } = string.Empty;

        public long AmountCents { get; set; }

        public string RequestPayloadHash { get; set; } = string.Empty;

        public CreditIntentStatus Status { get; set; } = CreditIntentStatus.InFlight;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAtUtc { get; set; }
    }
}
