using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CobraPix_PC.Models;

namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// Item de histórico de pagamento associado a uma mensalidade recorrente.
    /// </summary>
    public class MonthlyPaymentHistoryItemViewModel : ViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        public int Id { get; }
        public int MonthlyCustomerId { get; }
        public decimal Amount { get; }
        public DateTime PaymentDate { get; }
        public int ReferenceMonth { get; }
        public int ReferenceYear { get; }
        public PaymentMethod PaymentMethod { get; }
        public string? Notes { get; }

        public string FormattedReference => $"{ReferenceMonth:D2}/{ReferenceYear}";
        public string FormattedAmount => Amount.ToString("C2", PtBr);
        public string FormattedDate => PaymentDate.ToString("dd/MM/yyyy HH:mm");

        public string MethodText => PaymentMethod switch
        {
            PaymentMethod.Pix => "PIX",
            PaymentMethod.Cash => "Dinheiro",
            PaymentMethod.Card => "Cartão",
            PaymentMethod.Other => "Outro",
            _ => "Nenhum"
        };

        public string MethodBadge => PaymentMethod switch
        {
            PaymentMethod.Pix => "⚡ PIX",
            PaymentMethod.Cash => "💵 Dinheiro",
            PaymentMethod.Card => "💳 Cartão",
            PaymentMethod.Other => "📄 Outro",
            _ => "Nenhum"
        };

        public MonthlyPaymentHistoryItemViewModel(MonthlyPayment payment)
        {
            if (payment == null) throw new ArgumentNullException(nameof(payment));

            Id = payment.Id;
            MonthlyCustomerId = payment.MonthlyCustomerId;
            Amount = payment.Amount;
            PaymentDate = payment.PaymentDate;
            ReferenceMonth = payment.ReferenceMonth;
            ReferenceYear = payment.ReferenceYear;
            PaymentMethod = payment.PaymentMethod;
            Notes = payment.Notes;
        }
    }

    /// <summary>
    /// Modelo de exibição para clientes mensalistas com dados financeiros e situação da competência atual.
    /// </summary>
    public class MonthlyCustomerItemViewModel : ViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private decimal _monthlyAmount;
        private int _dueDay;
        private bool _isActive;
        private string? _notes;
        private List<MonthlyPaymentHistoryItemViewModel> _payments = new();

        public int Id { get; }
        public int ClientId { get; }
        public string ClientName { get; }
        public string ClientPhone { get; }
        public DateTime StartDate { get; }

        public decimal MonthlyAmount
        {
            get => _monthlyAmount;
            private set
            {
                if (SetProperty(ref _monthlyAmount, value))
                {
                    OnPropertyChanged(nameof(FormattedMonthlyAmount));
                }
            }
        }

        public double ContractInterestRate { get; private set; }

        public decimal OriginalMonthlyAmount { get; private set; }

        public int DueDay
        {
            get => _dueDay;
            private set
            {
                if (SetProperty(ref _dueDay, value))
                {
                    OnPropertyChanged(nameof(FormattedDueDay));
                    RefreshCurrentMonthStatus();
                }
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(IsInactive));
                    RefreshCurrentMonthStatus();
                }
            }
        }

        public bool IsInactive => !IsActive;

        public string StatusText => IsActive ? "Ativo" : "Inativo";

        public string? Notes
        {
            get => _notes;
            private set => SetProperty(ref _notes, value);
        }

        public List<MonthlyPaymentHistoryItemViewModel> Payments
        {
            get => _payments;
            private set
            {
                if (SetProperty(ref _payments, value))
                {
                    OnPropertyChanged(nameof(HasPayments));
                    OnPropertyChanged(nameof(LastPaymentText));
                    RefreshCurrentMonthStatus();
                }
            }
        }

        public bool HasPayments => Payments.Count > 0;

        public string FormattedMonthlyAmount => MonthlyAmount.ToString("C2", PtBr);
        public string FormattedDueDay => $"Dia {DueDay}";
        public string FormattedStartDate => StartDate.ToString("dd/MM/yyyy");

        // Situação do Mês Atual (Competência Atual)
        public bool IsCurrentMonthPaid => CurrentMonthPayment != null;

        public MonthlyPaymentHistoryItemViewModel? CurrentMonthPayment
        {
            get
            {
                int curMonth = DateTime.Today.Month;
                int curYear = DateTime.Today.Year;
                return Payments.FirstOrDefault(p => p.ReferenceMonth == curMonth && p.ReferenceYear == curYear);
            }
        }

        public string CurrentMonthStatusText
        {
            get
            {
                if (!IsActive) return "Inativo";
                if (IsCurrentMonthPaid) return "Pago";
                if (DateTime.Today.Day > DueDay) return "Atrasado";
                return "Pendente";
            }
        }

        public bool IsCurrentMonthPending => IsActive && !IsCurrentMonthPaid && DateTime.Today.Day <= DueDay;
        public bool IsCurrentMonthOverdue => IsActive && !IsCurrentMonthPaid && DateTime.Today.Day > DueDay;

        public string LastPaymentText
        {
            get
            {
                var latest = Payments.OrderByDescending(p => p.PaymentDate).FirstOrDefault();
                if (latest == null) return "Nenhum pagamento registrado";
                return $"{latest.FormattedDate} ({latest.FormattedReference} - {latest.FormattedAmount})";
            }
        }

        public MonthlyCustomerItemViewModel(MonthlyCustomer customer)
        {
            if (customer == null) throw new ArgumentNullException(nameof(customer));

            Id = customer.Id;
            ClientId = customer.ClientId;
            ClientName = customer.Client?.Name ?? "Cliente";
            ClientPhone = string.IsNullOrWhiteSpace(customer.Client?.Phone) ? "Sem telefone" : customer.Client.Phone;
            StartDate = customer.StartDate;
            _monthlyAmount = customer.MonthlyAmount;
            _dueDay = customer.DueDay;
            _isActive = customer.IsActive;
            _notes = customer.Notes;
            ContractInterestRate = customer.ContractInterestRate;
            OriginalMonthlyAmount = customer.OriginalMonthlyAmount > 0 ? customer.OriginalMonthlyAmount : customer.MonthlyAmount;

            if (customer.Payments != null && customer.Payments.Count > 0)
            {
                _payments = customer.Payments
                    .OrderByDescending(p => p.ReferenceYear)
                    .ThenByDescending(p => p.ReferenceMonth)
                    .ThenByDescending(p => p.PaymentDate)
                    .Select(p => new MonthlyPaymentHistoryItemViewModel(p))
                    .ToList();
            }
        }

        public void UpdateFrom(MonthlyCustomer customer)
        {
            if (customer == null) return;

            MonthlyAmount = customer.MonthlyAmount;
            DueDay = customer.DueDay;
            IsActive = customer.IsActive;
            Notes = customer.Notes;
            ContractInterestRate = customer.ContractInterestRate;
            OriginalMonthlyAmount = customer.OriginalMonthlyAmount > 0 ? customer.OriginalMonthlyAmount : customer.MonthlyAmount;

            if (customer.Payments != null)
            {
                SetPayments(customer.Payments);
            }
        }

        public void SetPayments(IEnumerable<MonthlyPayment> payments)
        {
            Payments = (payments ?? Enumerable.Empty<MonthlyPayment>())
                .OrderByDescending(p => p.ReferenceYear)
                .ThenByDescending(p => p.ReferenceMonth)
                .ThenByDescending(p => p.PaymentDate)
                .Select(p => new MonthlyPaymentHistoryItemViewModel(p))
                .ToList();

            RefreshCurrentMonthStatus();
        }

        private void RefreshCurrentMonthStatus()
        {
            OnPropertyChanged(nameof(CurrentMonthPayment));
            OnPropertyChanged(nameof(IsCurrentMonthPaid));
            OnPropertyChanged(nameof(CurrentMonthStatusText));
            OnPropertyChanged(nameof(IsCurrentMonthPending));
            OnPropertyChanged(nameof(IsCurrentMonthOverdue));
            OnPropertyChanged(nameof(LastPaymentText));
        }
    }
}
