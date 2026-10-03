using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CobraPix_PC.Models;

namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// Item de histórico de pagamento associado a uma parcela.
    /// </summary>
    public class PaymentHistoryItemViewModel : ViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        public int Id { get; }
        public decimal AmountPaid { get; }
        public DateTime PaymentDate { get; }
        public PaymentMethod PaymentMethod { get; }
        public string? Notes { get; }

        public string FormattedDate => PaymentDate.ToString("dd/MM/yyyy HH:mm");
        public string FormattedAmount => AmountPaid.ToString("C2", PtBr);
        public string MethodText => PaymentMethod switch
        {
            PaymentMethod.Pix => "PIX",
            PaymentMethod.Cash => "Dinheiro",
            PaymentMethod.Card => "Cartão",
            PaymentMethod.Other => "Outro",
            _ => "Nenhum"
        };

        public PaymentHistoryItemViewModel(InstallmentPayment payment)
        {
            if (payment == null) throw new ArgumentNullException(nameof(payment));

            Id = payment.Id;
            AmountPaid = payment.AmountPaid;
            PaymentDate = payment.PaymentDate;
            PaymentMethod = payment.PaymentMethod;
            Notes = payment.Notes;
        }
    }

    /// <summary>
    /// Modelo de exibição para parcelas, utilizado nos módulos de Vendas, Parcelas e Pagamentos.
    /// </summary>
    public class InstallmentItemViewModel : ViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private InstallmentStatus _status;
        private DateTime? _paidDate;
        private decimal _totalPaid;
        private decimal _remainingBalance;
        private List<PaymentHistoryItemViewModel> _paymentsHistory = new();

        public int Id { get; }
        public int SaleId { get; }
        public int InstallmentNumber { get; }
        public decimal Amount { get; }
        public DateTime DueDate { get; }
        public string? Notes { get; }

        public string ClientName { get; }
        public string SaleDescription { get; }

        public InstallmentStatus Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public DateTime? PaidDate
        {
            get => _paidDate;
            private set => SetProperty(ref _paidDate, value);
        }

        public decimal TotalPaid
        {
            get => _totalPaid;
            private set => SetProperty(ref _totalPaid, value);
        }

        public decimal RemainingBalance
        {
            get => _remainingBalance;
            private set => SetProperty(ref _remainingBalance, value);
        }

        public List<PaymentHistoryItemViewModel> PaymentsHistory
        {
            get => _paymentsHistory;
            private set => SetProperty(ref _paymentsHistory, value);
        }

        public string FormattedNumber => $"Parcela {InstallmentNumber}";
        public string FormattedAmount => Amount.ToString("C2", PtBr);
        public string FormattedTotalPaid => TotalPaid.ToString("C2", PtBr);
        public string FormattedRemainingBalance => RemainingBalance.ToString("C2", PtBr);
        public string FormattedDueDate => DueDate.ToString("dd/MM/yyyy");
        public string FormattedPaidDate => PaidDate.HasValue ? PaidDate.Value.ToString("dd/MM/yyyy") : "-";

        public string StatusText => Status switch
        {
            InstallmentStatus.Paid => "Paga",
            InstallmentStatus.Overdue => "Vencida",
            _ => "Pendente"
        };

        public bool IsPending => Status == InstallmentStatus.Pending;
        public bool IsOverdue => Status == InstallmentStatus.Overdue;
        public bool IsPaid => Status == InstallmentStatus.Paid;
        public bool CanPay => Status != InstallmentStatus.Paid && RemainingBalance > 0m;

        public InstallmentItemViewModel(Installment installment, string? clientName = null, string? saleDescription = null)
        {
            if (installment == null) throw new ArgumentNullException(nameof(installment));

            Id = installment.Id;
            SaleId = installment.SaleId;
            InstallmentNumber = installment.InstallmentNumber;
            Amount = installment.Amount;
            DueDate = installment.DueDate;
            _paidDate = installment.PaidDate;
            _status = installment.Status;
            Notes = installment.Notes;

            ClientName = clientName ?? installment.Sale?.Client?.Name ?? "Cliente";
            SaleDescription = saleDescription ?? installment.Sale?.Description ?? $"Venda #{installment.SaleId}";

            if (installment.Payments != null && installment.Payments.Count > 0)
            {
                _totalPaid = installment.Payments.Sum(p => p.AmountPaid);
                _paymentsHistory = installment.Payments
                    .OrderByDescending(p => p.PaymentDate)
                    .Select(p => new PaymentHistoryItemViewModel(p))
                    .ToList();
            }
            else
            {
                _totalPaid = 0m;
                _paymentsHistory = new List<PaymentHistoryItemViewModel>();
            }

            _remainingBalance = Math.Max(0m, Amount - _totalPaid);
        }

        /// <summary>
        /// Atualiza os valores, histórico de pagamentos e status a partir da entidade atualizada.
        /// </summary>
        public void UpdateFrom(Installment installment)
        {
            if (installment == null) return;

            Status = installment.Status;
            PaidDate = installment.PaidDate;

            if (installment.Payments != null)
            {
                TotalPaid = installment.Payments.Sum(p => p.AmountPaid);
                PaymentsHistory = installment.Payments
                    .OrderByDescending(p => p.PaymentDate)
                    .Select(p => new PaymentHistoryItemViewModel(p))
                    .ToList();
            }
            else
            {
                TotalPaid = 0m;
                PaymentsHistory = new List<PaymentHistoryItemViewModel>();
            }

            RemainingBalance = Math.Max(0m, Amount - TotalPaid);

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsOverdue));
            OnPropertyChanged(nameof(IsPaid));
            OnPropertyChanged(nameof(CanPay));
            OnPropertyChanged(nameof(FormattedPaidDate));
            OnPropertyChanged(nameof(FormattedTotalPaid));
            OnPropertyChanged(nameof(FormattedRemainingBalance));
        }

        /// <summary>
        /// Atualiza a lista de pagamentos e recalcula total pago e saldo.
        /// </summary>
        public void SetPayments(IEnumerable<InstallmentPayment> payments, InstallmentStatus newStatus, DateTime? newPaidDate)
        {
            var list = payments?.ToList() ?? new List<InstallmentPayment>();
            TotalPaid = list.Sum(p => p.AmountPaid);
            RemainingBalance = Math.Max(0m, Amount - TotalPaid);
            Status = newStatus;
            PaidDate = newPaidDate;

            PaymentsHistory = list
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => new PaymentHistoryItemViewModel(p))
                .ToList();

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsOverdue));
            OnPropertyChanged(nameof(IsPaid));
            OnPropertyChanged(nameof(CanPay));
            OnPropertyChanged(nameof(FormattedPaidDate));
            OnPropertyChanged(nameof(FormattedTotalPaid));
            OnPropertyChanged(nameof(FormattedRemainingBalance));
        }
    }
}
