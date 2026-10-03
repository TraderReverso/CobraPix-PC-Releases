using System;
using System.Collections.Generic;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa um cliente no sistema.
    /// Preparado para histórico financeiro e arquivamento lógico.
    /// </summary>
    public class Client
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string? Notes { get; set; }

        /// <summary>
        /// Indica se o cliente está arquivado (arquivamento lógico, preservando histórico financeiro).
        /// </summary>
        public bool IsArchived { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }

        // Relacionamentos de navegação
        public ICollection<Sale> Sales { get; set; } = new List<Sale>();

        public MonthlyCustomer? MonthlyCustomer { get; set; }
    }
}
