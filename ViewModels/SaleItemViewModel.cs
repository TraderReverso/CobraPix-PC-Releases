using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CobraPix_PC.Models;

namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// Modelo de exibição para itens de vendas na listagem e detalhes com resumo de parcelas.
    /// </summary>
    public class SaleItemViewModel : ViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        public int Id { get; }
        public int ClientId { get; }
        public string ClientName { get; }
        public decimal TotalAmount { get; }
        public int InstallmentCount { get; }
        public DateTime SaleDate { get; }
        public string? Description { get; }
        public string? Notes { get; }
        public DateTime CreatedAt { get; }

        public List<InstallmentItemViewModel> Installments { get; }

        public string FormattedTotalAmount => TotalAmount.ToString("C2", PtBr);
        public string FormattedSaleDate => SaleDate.ToString("dd/MM/yyyy");
        public string DisplayDescription => string.IsNullOrWhiteSpace(Description) ? $"Venda #{Id}" : Description;

        public int PaidCount => Installments.Count(i => i.Status == InstallmentStatus.Paid);
        public int OverdueCount => Installments.Count(i => i.Status == InstallmentStatus.Overdue);
        public int PendingCount => Installments.Count(i => i.Status == InstallmentStatus.Pending);

        public bool IsFullyPaid => InstallmentCount > 0 && PaidCount == InstallmentCount;
        public bool HasOverdue => OverdueCount > 0;

        public string ProgressText => $"{PaidCount} de {InstallmentCount} parcelas pagas";

        public string StatusSummaryText
        {
            get
            {
                if (IsFullyPaid) return "Quitada";
                if (HasOverdue) return $"{OverdueCount} vencida(s)";
                return "Em dia";
            }
        }

        public SaleItemViewModel(Sale sale)
        {
            if (sale == null) throw new ArgumentNullException(nameof(sale));

            Id = sale.Id;
            ClientId = sale.ClientId;
            ClientName = sale.Client?.Name ?? "Cliente";
            TotalAmount = sale.TotalAmount;
            InstallmentCount = sale.InstallmentCount;
            SaleDate = sale.SaleDate;
            Description = sale.Description;
            Notes = sale.Notes;
            CreatedAt = sale.CreatedAt;

            Installments = sale.Installments != null
                ? sale.Installments.OrderBy(i => i.InstallmentNumber)
                                   .Select(i => new InstallmentItemViewModel(i, ClientName, Description))
                                   .ToList()
                : new List<InstallmentItemViewModel>();
        }
    }
}
