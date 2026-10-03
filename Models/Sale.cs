using System;
using System.Collections.Generic;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa uma venda associada a um cliente.
    /// </summary>
    public class Sale
    {
        public int Id { get; set; }

        public int ClientId { get; set; }

        public Client Client { get; set; } = null!;

        public decimal TotalAmount { get; set; }

        public int InstallmentCount { get; set; }

        public DateTime SaleDate { get; set; } = DateTime.Today;

        public string? Description { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Relacionamentos de navegação
        public ICollection<Installment> Installments { get; set; } = new List<Installment>();
    }
}
