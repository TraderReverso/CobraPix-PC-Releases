using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using CobraPix_PC.Data;
using CobraPix_PC.Helpers;
using CobraPix_PC.Models;
using CobraPix_PC.Services;
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// ViewModel do módulo de Pagamentos.
    /// Gerencia a consulta de parcelas, registro de quitações totais e parciais,
    /// histórico financeiro de pagamentos e sincronização de saldo e status.
    /// </summary>
    public class PagamentosViewModel : ModuleViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly IPaymentService _paymentService;
        private readonly Action? _onPaymentRegistered;
        private readonly List<InstallmentItemViewModel> _allInstallments = new();

        private InstallmentFilterType _currentFilter = InstallmentFilterType.All;
        private string _searchText = string.Empty;
        private InstallmentItemViewModel? _selectedInstallment;

        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;

        // Formulário de Registro de Pagamento
        private string _paymentAmountText = string.Empty;
        private PaymentMethod _selectedPaymentMethod = PaymentMethod.Pix;
        private string? _paymentNotes;
        private bool _isProcessingPayment;
        private bool _hasPaymentError;
        private string? _paymentErrorMessage;
        private bool _hasPaymentSuccess;
        private string? _paymentSuccessMessage;
        private string? _paymentSuccessSummary;

        public override string Title => "Gestão de Pagamentos";
        public override string Description => "Registro de quitações, amortizações parciais e consulta de histórico financeiro de parcelas.";
        public override string Icon => "💳";
        public override string Category => "PRINCIPAL";

        public ObservableCollection<InstallmentItemViewModel> FilteredInstallments { get; } = new();

        public InstallmentItemViewModel? SelectedInstallment
        {
            get => _selectedInstallment;
            set
            {
                if (SetProperty(ref _selectedInstallment, value))
                {
                    OnPropertyChanged(nameof(HasSelectedInstallment));
                    OnSelectedInstallmentChanged();
                }
            }
        }

        public bool HasSelectedInstallment => SelectedInstallment != null;

        public InstallmentFilterType CurrentFilter
        {
            get => _currentFilter;
            set
            {
                if (SetProperty(ref _currentFilter, value))
                {
                    ApplyFilterAndSearch();
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterPending));
                    OnPropertyChanged(nameof(IsFilterOverdue));
                    OnPropertyChanged(nameof(IsFilterPaid));
                }
            }
        }

        public bool IsFilterAll => CurrentFilter == InstallmentFilterType.All;
        public bool IsFilterPending => CurrentFilter == InstallmentFilterType.Pending;
        public bool IsFilterOverdue => CurrentFilter == InstallmentFilterType.Overdue;
        public bool IsFilterPaid => CurrentFilter == InstallmentFilterType.Paid;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilterAndSearch();
                    OnPropertyChanged(nameof(HasSearchText));
                }
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool IsEmpty => !IsLoading && _allInstallments.Count == 0;
        public bool IsSearchEmpty => !IsLoading && _allInstallments.Count > 0 && FilteredInstallments.Count == 0;

        public int TotalCount => _allInstallments.Count;
        public int PendingCount => _allInstallments.Count(i => i.IsPending);
        public int OverdueCount => _allInstallments.Count(i => i.IsOverdue);
        public int PaidCount => _allInstallments.Count(i => i.IsPaid);

        public string FormattedTotalFilteredAmount => FilteredInstallments.Sum(i => i.Amount).ToString("C2", PtBr);

        // Propriedades do Formulário de Pagamento
        public string PaymentAmountText
        {
            get => _paymentAmountText;
            set => SetProperty(ref _paymentAmountText, value);
        }

        public PaymentMethod SelectedPaymentMethod
        {
            get => _selectedPaymentMethod;
            set
            {
                if (SetProperty(ref _selectedPaymentMethod, value))
                {
                    OnPropertyChanged(nameof(IsPixSelected));
                    OnPropertyChanged(nameof(IsCashSelected));
                    OnPropertyChanged(nameof(IsCardSelected));
                    OnPropertyChanged(nameof(IsOtherSelected));
                }
            }
        }

        public bool IsPixSelected => SelectedPaymentMethod == PaymentMethod.Pix;
        public bool IsCashSelected => SelectedPaymentMethod == PaymentMethod.Cash;
        public bool IsCardSelected => SelectedPaymentMethod == PaymentMethod.Card;
        public bool IsOtherSelected => SelectedPaymentMethod == PaymentMethod.Other;

        public string? PaymentNotes
        {
            get => _paymentNotes;
            set => SetProperty(ref _paymentNotes, value);
        }

        public bool IsProcessingPayment
        {
            get => _isProcessingPayment;
            set => SetProperty(ref _isProcessingPayment, value);
        }

        public bool HasPaymentError
        {
            get => _hasPaymentError;
            set => SetProperty(ref _hasPaymentError, value);
        }

        public string? PaymentErrorMessage
        {
            get => _paymentErrorMessage;
            set => SetProperty(ref _paymentErrorMessage, value);
        }

        public bool HasPaymentSuccess
        {
            get => _hasPaymentSuccess;
            set => SetProperty(ref _hasPaymentSuccess, value);
        }

        public string? PaymentSuccessMessage
        {
            get => _paymentSuccessMessage;
            set => SetProperty(ref _paymentSuccessMessage, value);
        }

        public string? PaymentSuccessSummary
        {
            get => _paymentSuccessSummary;
            set => SetProperty(ref _paymentSuccessSummary, value);
        }

        // Comandos
        public ICommand LoadInstallmentsCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand SetPaymentMethodCommand { get; }
        public ICommand FillFullBalanceCommand { get; }
        public ICommand RegisterPaymentCommand { get; }

        public PagamentosViewModel()
            : this(App.Services?.GetService<IPaymentService>() ?? new PaymentService(new CobraPixDbContext()))
        {
        }

        public PagamentosViewModel(IPaymentService paymentService, Action? onPaymentRegistered = null)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _onPaymentRegistered = onPaymentRegistered;

            LoadInstallmentsCommand = new RelayCommand(async () => await LoadInstallmentsAsync());
            SetFilterCommand = new RelayCommand<string>(filter =>
            {
                if (Enum.TryParse<InstallmentFilterType>(filter, true, out var result))
                {
                    CurrentFilter = result;
                }
            });
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

            SetPaymentMethodCommand = new RelayCommand<string>(methodStr =>
            {
                if (Enum.TryParse<PaymentMethod>(methodStr, true, out var method))
                {
                    SelectedPaymentMethod = method;
                }
            });

            FillFullBalanceCommand = new RelayCommand(() =>
            {
                if (SelectedInstallment != null && SelectedInstallment.RemainingBalance > 0)
                {
                    PaymentAmountText = SelectedInstallment.RemainingBalance.ToString("N2", PtBr);
                }
            });

            RegisterPaymentCommand = new RelayCommand(async () => await RegisterPaymentAsync());

            _ = LoadInstallmentsAsync();
        }

        public async Task LoadInstallmentsAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            try
            {
                // Sincroniza status de parcelas vencidas
                await _paymentService.UpdateOverdueInstallmentsAsync();

                var installments = await _paymentService.GetAllInstallmentsAsync();

                int? currentSelectedId = SelectedInstallment?.Id;

                _allInstallments.Clear();
                foreach (var inst in installments)
                {
                    _allInstallments.Add(new InstallmentItemViewModel(inst));
                }

                UpdateCounters();
                ApplyFilterAndSearch();

                if (currentSelectedId.HasValue)
                {
                    SelectedInstallment = _allInstallments.FirstOrDefault(i => i.Id == currentSelectedId.Value);
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Não foi possível carregar as parcelas para pagamentos: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsSearchEmpty));
            }
        }

        public void ApplyFilterAndSearch()
        {
            IEnumerable<InstallmentItemViewModel> query = _allInstallments;

            switch (CurrentFilter)
            {
                case InstallmentFilterType.Pending:
                    query = query.Where(i => i.IsPending);
                    break;
                case InstallmentFilterType.Overdue:
                    query = query.Where(i => i.IsOverdue);
                    break;
                case InstallmentFilterType.Paid:
                    query = query.Where(i => i.IsPaid);
                    break;
                case InstallmentFilterType.All:
                default:
                    break;
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(i =>
                    (i.ClientName != null && i.ClientName.ToLowerInvariant().Contains(term)) ||
                    (i.SaleDescription != null && i.SaleDescription.ToLowerInvariant().Contains(term)) ||
                    i.InstallmentNumber.ToString().Contains(term));
            }

            FilteredInstallments.Clear();
            foreach (var inst in query.OrderBy(i => i.DueDate).ThenBy(i => i.InstallmentNumber))
            {
                FilteredInstallments.Add(inst);
            }

            if (SelectedInstallment != null && !FilteredInstallments.Contains(SelectedInstallment))
            {
                SelectedInstallment = FilteredInstallments.FirstOrDefault();
            }
            else if (SelectedInstallment == null)
            {
                SelectedInstallment = FilteredInstallments.FirstOrDefault();
            }

            OnPropertyChanged(nameof(FormattedTotalFilteredAmount));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        private void OnSelectedInstallmentChanged()
        {
            HasPaymentError = false;
            PaymentErrorMessage = null;
            HasPaymentSuccess = false;
            PaymentSuccessMessage = null;
            PaymentSuccessSummary = null;

            if (SelectedInstallment != null && SelectedInstallment.CanPay)
            {
                PaymentAmountText = SelectedInstallment.RemainingBalance.ToString("N2", PtBr);
                SelectedPaymentMethod = PaymentMethod.Pix;
                PaymentNotes = string.Empty;
            }
            else
            {
                PaymentAmountText = string.Empty;
                PaymentNotes = string.Empty;
            }
        }

        /// <summary>
        /// Seleciona uma parcela por Id e ajusta o filtro para garantir que ela esteja visível.
        /// </summary>
        public async Task SelectInstallmentByIdAsync(int installmentId)
        {
            if (_allInstallments.Count == 0)
            {
                await LoadInstallmentsAsync();
            }

            var target = _allInstallments.FirstOrDefault(i => i.Id == installmentId);
            if (target != null)
            {
                // Se a parcela não atende ao filtro atual, muda para "Todas"
                if (CurrentFilter != InstallmentFilterType.All)
                {
                    CurrentFilter = InstallmentFilterType.All;
                }

                SearchText = string.Empty;
                SelectedInstallment = target;
            }
        }

        /// <summary>
        /// Processa o registro de pagamento da parcela selecionada.
        /// </summary>
        public async Task RegisterPaymentAsync()
        {
            if (SelectedInstallment == null)
            {
                HasPaymentError = true;
                PaymentErrorMessage = "Selecione uma parcela para registrar pagamento.";
                return;
            }

            if (!SelectedInstallment.CanPay)
            {
                HasPaymentError = true;
                PaymentErrorMessage = "Esta parcela já está quitada.";
                return;
            }

            // Validação de entrada monetária
            string cleanText = PaymentAmountText?.Trim() ?? string.Empty;
            if (!decimal.TryParse(cleanText, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, PtBr, out decimal amount) &&
                !decimal.TryParse(cleanText, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            {
                HasPaymentError = true;
                PaymentErrorMessage = "Informe um valor de pagamento válido (ex: 50,00).";
                return;
            }

            if (amount <= 0m)
            {
                HasPaymentError = true;
                PaymentErrorMessage = "O valor do pagamento deve ser maior que zero.";
                return;
            }

            if (amount > SelectedInstallment.RemainingBalance)
            {
                HasPaymentError = true;
                PaymentErrorMessage = $"Valor do pagamento ({amount.ToString("C2", PtBr)}) não pode exceder o saldo restante ({SelectedInstallment.FormattedRemainingBalance}).";
                return;
            }

            IsProcessingPayment = true;
            HasPaymentError = false;
            PaymentErrorMessage = null;
            HasPaymentSuccess = false;

            try
            {
                // Data do pagamento é estritamente a data/hora local atual do sistema
                DateTime paymentDate = DateTime.Now;

                var payment = await _paymentService.RegisterPaymentAsync(
                    SelectedInstallment.Id,
                    amount,
                    paymentDate,
                    SelectedPaymentMethod,
                    PaymentNotes);

                // Carrega histórico atualizado de pagamentos da parcela
                var updatedPayments = await _paymentService.GetPaymentsByInstallmentIdAsync(SelectedInstallment.Id);

                decimal newTotalPaid = updatedPayments.Sum(p => p.AmountPaid);
                decimal newBalance = Math.Max(0m, SelectedInstallment.Amount - newTotalPaid);
                InstallmentStatus newStatus = newBalance == 0
                    ? InstallmentStatus.Paid
                    : (SelectedInstallment.DueDate.Date < DateTime.Today.Date ? InstallmentStatus.Overdue : InstallmentStatus.Pending);
                DateTime? newPaidDate = newBalance == 0 ? paymentDate : null;

                // Atualiza o item de apresentação no ViewModel
                SelectedInstallment.SetPayments(updatedPayments, newStatus, newPaidDate);

                // Atualiza contadores e filtro
                UpdateCounters();
                ApplyFilterAndSearch();

                // Mensagem amigável de sucesso
                HasPaymentSuccess = true;
                if (newBalance == 0)
                {
                    PaymentSuccessMessage = "Parcela quitada com sucesso.";
                }
                else
                {
                    PaymentSuccessMessage = "Pagamento registrado com sucesso.";
                }

                PaymentSuccessSummary = $"Pago: {amount.ToString("C2", PtBr)}  |  Saldo: {newBalance.ToString("C2", PtBr)}  |  Status: {SelectedInstallment.StatusText}";

                // Prepara campos para novo pagamento se ainda houver saldo
                if (SelectedInstallment.CanPay)
                {
                    PaymentAmountText = SelectedInstallment.RemainingBalance.ToString("N2", PtBr);
                    PaymentNotes = string.Empty;
                }
                else
                {
                    PaymentAmountText = string.Empty;
                    PaymentNotes = string.Empty;
                }

                // Notifica listeners externos (ex: sincronização de Parcelas e Vendas)
                _onPaymentRegistered?.Invoke();
            }
            catch (BusinessRuleException ex)
            {
                HasPaymentError = true;
                PaymentErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                HasPaymentError = true;
                PaymentErrorMessage = "Erro ao registrar pagamento: " + ex.Message;
            }
            finally
            {
                IsProcessingPayment = false;
            }
        }

        private void UpdateCounters()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(OverdueCount));
            OnPropertyChanged(nameof(PaidCount));
        }
    }
}
