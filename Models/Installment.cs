using System;
using System.Collections.Generic;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa uma parcela de uma venda.
    /// </summary>
    public class Installment
    {
        public int Id { get; set; }

        public int SaleId { get; set; }

        public Sale Sale { get; set; } = null!;

        public int InstallmentNumber { get; set; }

        public decimal Amount { get; set; }

        public DateTime DueDate { get; set; }

        public DateTime? PaidDate { get; set; }

        public InstallmentStatus Status { get; set; } = InstallmentStatus.Pending;

        public string? Notes { get; set; }

        // Relacionamentos de navegação
        public ICollection<InstallmentPayment> Payments { get; set; } = new List<InstallmentPayment>();
    }
}
