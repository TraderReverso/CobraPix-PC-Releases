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
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.ViewModels
{
    public enum InstallmentFilterType
    {
        All,
        Pending,
        Overdue,
        Paid
    }

    /// <summary>
    /// ViewModel do módulo de Controle de Parcelas.
    /// Permite consulta de todas as parcelas geradas no sistema com filtros por status e pesquisa rápida por cliente.
    /// </summary>
    public class ParcelasViewModel : ModuleViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly IPaymentService _paymentService;
        private readonly List<InstallmentItemViewModel> _allInstallments = new();

        private InstallmentFilterType _currentFilter = InstallmentFilterType.All;
        private string _searchText = string.Empty;
        private InstallmentItemViewModel? _selectedInstallment;

        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;

        public override string Title => "Controle de Parcelas";
        public override string Description => "Listagem geral de parcelas geradas, datas de vencimento, valores e filtros por status.";
        public override string Icon => "📅";
        public override string Category => "PRINCIPAL";

        public ObservableCollection<InstallmentItemViewModel> FilteredInstallments { get; } = new();

        public InstallmentItemViewModel? SelectedInstallment
        {
            get => _selectedInstallment;
            set => SetProperty(ref _selectedInstallment, value);
        }

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

        private readonly Action<int>? _onOpenPayment;

        public ICommand LoadInstallmentsCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand OpenPaymentCommand { get; }
        public ICommand DeleteInstallmentCommand { get; }

        public ParcelasViewModel()
            : this(App.Services?.GetService<IPaymentService>() ?? new PaymentService(new CobraPixDbContext()), null)
        {
        }

        public ParcelasViewModel(IPaymentService paymentService, Action<int>? onOpenPayment = null)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _onOpenPayment = onOpenPayment;

            LoadInstallmentsCommand = new RelayCommand(async () => await LoadInstallmentsAsync());
            SetFilterCommand = new RelayCommand<string>(filter =>
            {
                if (Enum.TryParse<InstallmentFilterType>(filter, true, out var result))
                {
                    CurrentFilter = result;
                }
            });
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

            OpenPaymentCommand = new RelayCommand<InstallmentItemViewModel>(item =>
            {
                var target = item ?? SelectedInstallment;
                if (target != null)
                {
                    _onOpenPayment?.Invoke(target.Id);
                }
            });

            DeleteInstallmentCommand = new RelayCommand<InstallmentItemViewModel>(async item =>
            {
                var target = item ?? SelectedInstallment;
                if (target == null) return;

                if (target.IsPaid || target.TotalPaid > 0)
                {
                    HasError = true;
                    ErrorMessage = "Não é possível excluir uma parcela com pagamentos registrados. Estorne os pagamentos no histórico primeiro.";
                    return;
                }

                try
                {
                    await _paymentService.DeleteInstallmentAsync(target.Id);
                    await LoadInstallmentsAsync();
                }
                catch (Exception ex)
                {
                    HasError = true;
                    ErrorMessage = ex.Message;
                }
            });

            _ = LoadInstallmentsAsync();
        }

        public async Task LoadInstallmentsAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            try
            {
                // Sincroniza status de parcelas vencidas através da regra de negócio oficial
                await _paymentService.UpdateOverdueInstallmentsAsync();

                var installments = await _paymentService.GetAllInstallmentsAsync();

                _allInstallments.Clear();
                foreach (var inst in installments)
                {
                    _allInstallments.Add(new InstallmentItemViewModel(inst));
                }

                UpdateCounters();
                ApplyFilterAndSearch();
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Não foi possível carregar as parcelas: " + ex.Message;
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

        private void UpdateCounters()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(OverdueCount));
            OnPropertyChanged(nameof(PaidCount));
        }
    }
}
