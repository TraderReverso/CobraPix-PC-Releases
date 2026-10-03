using System;
using System.Globalization;
using CobraPix_PC.Models;

namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// Item de exibição para a Central de Cobranças.
    /// Representa uma cobrança de Parcela, Mensalidade ou obrigação do 5º Dia Útil.
    /// </summary>
    public class CobrancaItemViewModel : ViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        public int Id { get; }
        public int? InstallmentId { get; }
        public int? MonthlyCustomerId { get; }

        public string ClientName { get; }
        public string ClientPhone { get; }
        public string Tipo { get; } // Strictly: "Parcela", "Mensalidade", "5º Dia Útil"
        public string Reference { get; }
        public DateTime DueDate { get; }
        public string FormattedDueDate => DueDate.ToString("dd/MM/yyyy");

        public int DaysOverdue { get; }
        public string DaysOverdueText { get; }

        public decimal OriginalAmount { get; }
        public decimal TotalPaid { get; }
        public decimal RemainingBalance { get; }

        public string FormattedOriginalAmount => OriginalAmount.ToString("C2", PtBr);
        public string FormattedTotalPaid => TotalPaid.ToString("C2", PtBr);
        public string FormattedRemainingBalance => RemainingBalance.ToString("C2", PtBr);

        public string Motivo { get; } // Strictly: "Parcela vencida", "Mensalidade vencida", "Cobrança 5º dia útil"
        public string Status { get; } // Strictly: "Pendente", "Atrasada", "Vencendo", "Em dia"

        public bool IsParcela => InstallmentId.HasValue;
        public bool IsMensalidade => MonthlyCustomerId.HasValue && !InstallmentId.HasValue;
        public bool IsFifthBusinessDay => Tipo == "5º Dia Útil";

        public string TipoBadge => Tipo switch
        {
            "5º Dia Útil" => "💼 5º Dia Útil",
            "Mensalidade" => "⭐ Mensalidade",
            _ => "📦 Parcela"
        };

        public string StatusBadge => Status switch
        {
            "Atrasada" => "⚠️ Atrasada",
            "Vencendo" => "⏳ Vencendo",
            "Pendente" => "📅 Pendente",
            "Em dia" => "✅ Em dia",
            _ => Status
        };

        public string ActionButtonText => IsParcela ? "Pagar" : "Mensalista";
        public string ActionButtonIcon => IsParcela ? "💳" : "👤";

        /// <summary>
        /// Construtor para Parcela de Venda.
        /// </summary>
        public CobrancaItemViewModel(Installment installment, bool isFifthBusinessDay = false, DateTime? referenceDate = null)
        {
            if (installment == null) throw new ArgumentNullException(nameof(installment));

            DateTime refDate = referenceDate ?? DateTime.Today;

            Id = installment.Id;
            InstallmentId = installment.Id;
            MonthlyCustomerId = null;

            ClientName = installment.Sale?.Client?.Name ?? "Cliente";
            ClientPhone = installment.Sale?.Client?.Phone ?? string.Empty;

            Reference = installment.Sale?.InstallmentCount > 0
                ? $"Parc. {installment.InstallmentNumber}/{installment.Sale.InstallmentCount} - Venda #{installment.SaleId}"
                : $"Parc. {installment.InstallmentNumber} - Venda #{installment.SaleId}";

            DueDate = installment.DueDate.Date;
            OriginalAmount = installment.Amount;

            if (installment.Payments != null && installment.Payments.Count > 0)
            {
                decimal paid = 0m;
                foreach (var p in installment.Payments)
                {
                    paid += p.AmountPaid;
                }
                TotalPaid = paid;
            }
            else
            {
                TotalPaid = 0m;
            }

            RemainingBalance = Math.Max(0m, OriginalAmount - TotalPaid);

            if (isFifthBusinessDay)
            {
                Tipo = "5º Dia Útil";
                Motivo = "Cobrança 5º dia útil";
            }
            else
            {
                Tipo = "Parcela";
                Motivo = "Parcela vencida";
            }

            // Cálculo preciso de dias em atraso e status de apresentação
            if (DueDate < refDate.Date)
            {
                DaysOverdue = (refDate.Date - DueDate).Days;
                DaysOverdueText = DaysOverdue == 1 ? "1 dia em atraso" : $"{DaysOverdue} dias em atraso";
                Status = RemainingBalance > 0m ? "Atrasada" : "Em dia";
            }
            else if (DueDate == refDate.Date)
            {
                DaysOverdue = 0;
                DaysOverdueText = "Vence hoje";
                Status = RemainingBalance > 0m ? "Vencendo" : "Em dia";
            }
            else
            {
                int diff = (DueDate - refDate.Date).Days;
                DaysOverdue = 0;
                DaysOverdueText = diff == 1 ? "Vence amanhã" : $"Vence em {diff} dias";
                if (RemainingBalance == 0m)
                {
                    Status = "Em dia";
                }
                else
                {
                    Status = diff <= 3 ? "Vencendo" : "Pendente";
                }
            }
        }

        /// <summary>
        /// Construtor para Mensalidade de Cliente Mensalista.
        /// </summary>
        public CobrancaItemViewModel(MonthlyCustomer customer, DateTime dueDate, string referenceCompetence, bool isFifthBusinessDay = false, DateTime? referenceDate = null)
        {
            if (customer == null) throw new ArgumentNullException(nameof(customer));

            DateTime refDate = referenceDate ?? DateTime.Today;

            Id = customer.Id;
            InstallmentId = null;
            MonthlyCustomerId = customer.Id;

            ClientName = customer.Client?.Name ?? "Cliente Mensalista";
            ClientPhone = customer.Client?.Phone ?? string.Empty;

            Reference = $"Mensalidade {referenceCompetence}";
            DueDate = dueDate.Date;
            OriginalAmount = customer.MonthlyAmount;
            TotalPaid = 0m;
            RemainingBalance = customer.MonthlyAmount;

            if (isFifthBusinessDay)
            {
                Tipo = "5º Dia Útil";
                Motivo = "Cobrança 5º dia útil";
            }
            else
            {
                Tipo = "Mensalidade";
                Motivo = "Mensalidade vencida";
            }

            if (DueDate < refDate.Date)
            {
                DaysOverdue = (refDate.Date - DueDate).Days;
                DaysOverdueText = DaysOverdue == 1 ? "1 dia em atraso" : $"{DaysOverdue} dias em atraso";
                Status = RemainingBalance > 0m ? "Atrasada" : "Em dia";
            }
            else if (DueDate == refDate.Date)
            {
                DaysOverdue = 0;
                DaysOverdueText = "Vence hoje";
                Status = RemainingBalance > 0m ? "Vencendo" : "Em dia";
            }
            else
            {
                int diff = (DueDate - refDate.Date).Days;
                DaysOverdue = 0;
                DaysOverdueText = diff == 1 ? "Vence amanhã" : $"Vence em {diff} dias";
                if (RemainingBalance == 0m)
                {
                    Status = "Em dia";
                }
                else
                {
                    Status = diff <= 3 ? "Vencendo" : "Pendente";
                }
            }
        }

        /// <summary>
        /// Construtor explícito para testes unitários ou construção direta de itens.
        /// </summary>
        public CobrancaItemViewModel(
            int id,
            int? installmentId,
            int? monthlyCustomerId,
            string clientName,
            string clientPhone,
            string tipo,
            string reference,
            DateTime dueDate,
            decimal originalAmount,
            decimal totalPaid,
            string motivo,
            string status,
            int daysOverdue = 0)
        {
            Id = id;
            InstallmentId = installmentId;
            MonthlyCustomerId = monthlyCustomerId;
            ClientName = clientName ?? "Cliente";
            ClientPhone = clientPhone ?? string.Empty;
            Tipo = tipo;
            Reference = reference;
            DueDate = dueDate.Date;
            OriginalAmount = originalAmount;
            TotalPaid = totalPaid;
            RemainingBalance = Math.Max(0m, originalAmount - totalPaid);
            Motivo = motivo;
            Status = status;
            DaysOverdue = daysOverdue;
            DaysOverdueText = daysOverdue > 0 ? (daysOverdue == 1 ? "1 dia em atraso" : $"{daysOverdue} dias em atraso") : "Em dia";
        }
    }
}
