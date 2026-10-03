using System;
using System.Globalization;
using CobraPix_PC.Models;

namespace CobraPix_PC.ViewModels
{
    public enum VencimentoItemType
    {
        Parcela,
        Mensalidade
    }

    /// <summary>
    /// Item de exibição para a central de Próximos Vencimentos.
    /// Unifica parcelas de vendas parceladas e mensalidades recorrentes que ainda não venceram.
    /// </summary>
    public class VencimentoItemViewModel : ViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        public int Id { get; }
        public int? InstallmentId { get; }
        public int? MonthlyCustomerId { get; }
        public VencimentoItemType ItemType { get; }

        public string ClientName { get; }
        public string ClientPhone { get; }
        public string Origem { get; }
        public string Reference { get; }
        public DateTime DueDate { get; }

        public decimal OriginalAmount { get; }
        public decimal TotalPaid { get; }
        public decimal RemainingBalance { get; }
        public string Situacao { get; }

        public bool IsParcela => ItemType == VencimentoItemType.Parcela;
        public bool IsMensalidade => ItemType == VencimentoItemType.Mensalidade;

        public string TipoText => IsParcela ? "Parcela" : "Mensalidade";
        public string TipoBadge => IsParcela ? "📦 Parcela" : "⭐ Mensalidade";

        public string FormattedDueDate => DueDate.ToString("dd/MM/yyyy");
        public string FormattedOriginalAmount => OriginalAmount.ToString("C2", PtBr);
        public string FormattedTotalPaid => TotalPaid.ToString("C2", PtBr);
        public string FormattedRemainingBalance => RemainingBalance.ToString("C2", PtBr);

        public int DaysUntilDue => Math.Max(0, (DueDate.Date - DateTime.Today.Date).Days);
        public bool IsDueToday => DueDate.Date == DateTime.Today.Date;
        public bool IsDueTomorrow => DueDate.Date == DateTime.Today.AddDays(1).Date;

        public string DueCountdownText
        {
            get
            {
                if (IsDueToday) return "Vence Hoje";
                if (IsDueTomorrow) return "Vence Amanhã";
                return $"Em {DaysUntilDue} dias";
            }
        }

        public string SituacaoBadge => Situacao switch
        {
            "Parcial" => "🌓 Parcial",
            _ => "⏳ Pendente"
        };

        /// <summary>
        /// Construtor para Parcela de Venda.
        /// </summary>
        public VencimentoItemViewModel(Installment installment)
        {
            if (installment == null) throw new ArgumentNullException(nameof(installment));

            Id = installment.Id;
            InstallmentId = installment.Id;
            MonthlyCustomerId = null;
            ItemType = VencimentoItemType.Parcela;

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
            Situacao = TotalPaid > 0m ? "Parcial" : "Pendente";
        }

        /// <summary>
        /// Construtor para Mensalidade Recorrente.
        /// </summary>
        public VencimentoItemViewModel(MonthlyCustomer customer, DateTime dueDate, string referenceCompetence)
        {
            if (customer == null) throw new ArgumentNullException(nameof(customer));

            Id = customer.Id;
            InstallmentId = null;
            MonthlyCustomerId = customer.Id;
            ItemType = VencimentoItemType.Mensalidade;

            ClientName = customer.Client?.Name ?? "Cliente Mensalista";
            ClientPhone = customer.Client?.Phone ?? string.Empty;
            Origem = "Mensalidade";
            Reference = referenceCompetence;

            DueDate = dueDate.Date;
            OriginalAmount = customer.MonthlyAmount;
            TotalPaid = 0m;
            RemainingBalance = customer.MonthlyAmount;
            Situacao = "Pendente";
        }
    }
}
