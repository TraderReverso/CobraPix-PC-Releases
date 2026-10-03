using System;
using System.Globalization;
using CobraPix_PC.Models;

namespace CobraPix_PC.ViewModels
{
    public enum AtrasadoItemType
    {
        Parcela,
        Mensalidade
    }

    /// <summary>
    /// Item de exibição para a central de Inadimplentes e Atrasados.
    /// Exibe parcelas e mensalidades vencidas com cálculo preciso de dias em atraso e saldo devedor.
    /// </summary>
    public class AtrasadoItemViewModel : ViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        public int Id { get; }
        public int? InstallmentId { get; }
        public int? MonthlyCustomerId { get; }
        public AtrasadoItemType ItemType { get; }

        public string ClientName { get; }
        public string ClientPhone { get; }
        public string Origem { get; }
        public string Reference { get; }
        public DateTime DueDate { get; }

        public decimal OriginalAmount { get; }
        public decimal TotalPaid { get; }
        public decimal RemainingBalance { get; }
        public int DaysOverdue { get; }
        public string Status { get; }

        public bool IsParcela => ItemType == AtrasadoItemType.Parcela;
        public bool IsMensalidade => ItemType == AtrasadoItemType.Mensalidade;

        public string TipoText => IsParcela ? "Parcela" : "Mensalidade";
        public string TipoBadge => IsParcela ? "📦 Parcela" : "⭐ Mensalidade";

        public string FormattedDueDate => DueDate.ToString("dd/MM/yyyy");
        public string FormattedOriginalAmount => OriginalAmount.ToString("C2", PtBr);
        public string FormattedTotalPaid => TotalPaid.ToString("C2", PtBr);
        public string FormattedRemainingBalance => RemainingBalance.ToString("C2", PtBr);

        public string DaysOverdueText => DaysOverdue == 1 ? "1 dia em atraso" : $"{DaysOverdue} dias em atraso";
        public string DaysOverdueBadge => $"{DaysOverdue}d";

        public string StatusBadge => Status switch
        {
            "Atrasada (Parcial)" => "🌓 Parcial",
            _ => "⚠️ Vencida"
        };

        /// <summary>
        /// Construtor para Parcela Vencida.
        /// </summary>
        public AtrasadoItemViewModel(Installment installment)
        {
            if (installment == null) throw new ArgumentNullException(nameof(installment));

            Id = installment.Id;
            InstallmentId = installment.Id;
            MonthlyCustomerId = null;
            ItemType = AtrasadoItemType.Parcela;

            ClientName = installment.Sale?.Client?.Name ?? "Cliente";
            ClientPhone = installment.Sale?.Client?.Phone ?? string.Empty;
            Origem = $"Venda #{installment.SaleId}";
            Reference = installment.Sale?.InstallmentCount > 0
                ? $"Parcela {installment.InstallmentNumber}/{installment.Sale.InstallmentCount}"
                : $"Parcela {installment.InstallmentNumber}";

            DueDate = installment.DueDate.Date;
            OriginalAmount = installment.Amount;

            if (installment.Payments != null && installment.Payments.Count > 0)
            {
                TotalPaid = 0m;
                foreach (var p in installment.Payments)
                {
                    TotalPaid += p.AmountPaid;
                }
            }
            else
            {
                TotalPaid = 0m;
            }

            RemainingBalance = Math.Max(0m, OriginalAmount - TotalPaid);
            DaysOverdue = Math.Max(0, (DateTime.Today.Date - installment.DueDate.Date).Days);
            Status = TotalPaid > 0m ? "Atrasada (Parcial)" : "Atrasada";
        }

        /// <summary>
        /// Construtor para Mensalidade Vencida.
        /// </summary>
        public AtrasadoItemViewModel(MonthlyCustomer customer, DateTime dueDate, string referenceCompetence)
        {
            if (customer == null) throw new ArgumentNullException(nameof(customer));

            Id = customer.Id;
            InstallmentId = null;
            MonthlyCustomerId = customer.Id;
            ItemType = AtrasadoItemType.Mensalidade;

            ClientName = customer.Client?.Name ?? "Cliente Mensalista";
            ClientPhone = customer.Client?.Phone ?? string.Empty;
            Origem = "Mensalidade";
            Reference = referenceCompetence;

            DueDate = dueDate.Date;
            OriginalAmount = customer.MonthlyAmount;
            TotalPaid = 0m;
            RemainingBalance = customer.MonthlyAmount;
            DaysOverdue = Math.Max(0, (DateTime.Today.Date - dueDate.Date).Days);
            Status = "Atrasada";
        }
    }
}
