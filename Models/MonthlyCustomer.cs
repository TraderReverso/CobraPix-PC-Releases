using System;
using System.Collections.Generic;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa a configuração de mensalidade de um cliente mensalista.
    /// </summary>
    public class MonthlyCustomer
    {
        public int Id { get; set; }

        public int ClientId { get; set; }

        public Client Client { get; set; } = null!;

        public decimal MonthlyAmount { get; set; }

        public int DueDay { get; set; } = 5;

        public bool IsActive { get; set; } = true;

        public DateTime StartDate { get; set; } = DateTime.Today;

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Relacionamentos de navegação
        public ICollection<MonthlyPayment> Payments { get; set; } = new List<MonthlyPayment>();
    }
}
