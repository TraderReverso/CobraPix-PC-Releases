using System;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa o pagamento de uma mensalidade de um cliente mensalista.
    /// </summary>
    public class MonthlyPayment
    {
        public int Id { get; set; }

        public int MonthlyCustomerId { get; set; }

        public MonthlyCustomer MonthlyCustomer { get; set; } = null!;

        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.Now;

        public int ReferenceMonth { get; set; }

        public int ReferenceYear { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.None;

        public string? Notes { get; set; }
    }
}
