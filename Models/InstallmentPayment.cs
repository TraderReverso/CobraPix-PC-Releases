using System;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa um registro de pagamento associado a uma parcela,
    /// permitindo manter o histórico detalhado de quitações parciais ou totais.
    /// </summary>
    public class InstallmentPayment
    {
        public int Id { get; set; }

        public int InstallmentId { get; set; }

        public Installment Installment { get; set; } = null!;

        public decimal AmountPaid { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.Now;

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.None;

        public string? Notes { get; set; }
    }
}
