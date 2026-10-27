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
    public enum VencimentosPeriodFilter
    {
        All,
        Today,
        Next7Days,
        Next15Days,
        Next30Days
    }

    /// <summary>
    /// ViewModel do módulo Próximos Vencimentos.
    /// Funciona como central de acompanhamento preventivo de parcelas e mensalidades a vencer.
    /// </summary>
    public class VencimentosViewModel : ModuleViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly IPaymentService _paymentService;
        private readonly IMonthlyCustomerService _monthlyService;
        private readonly IWhatsAppService _whatsAppService;
        private readonly Action<int>? _onOpenPayment;
        private readonly Action<int>? _onOpenMensalista;

        public WhatsAppPreviewViewModel WhatsAppPreview { get; }

        private readonly List<VencimentoItemViewModel> _allVencimentos = new();
        private VencimentoItemViewModel? _selectedVencimento;
        private VencimentosPeriodFilter _currentFilter = VencimentosPeriodFilter.All;
        private string _searchText = string.Empty;

        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;

        // Contadores
        private int _totalCount;
        private decimal _totalAmount;
        private int _parcelasCount;
        private int _mensalidadesCount;
        private int _todayCount;
        private int _next7DaysCount;
        private int _next15DaysCount;
        private int _next30DaysCount;
        private DateTime? _referenceDate;

        public DateTime? ReferenceDate
        {
            get => _referenceDate;
            set
            {
                if (SetProperty(ref _referenceDate, value))
                {
                    ApplyFilterAndSearch();
                }
            }
        }

        public override string Title => "Próximos Vencimentos";
        public override string Description => "Acompanhamento preventivo de contas que vencem nos próximos dias.";
        public override string Icon => "⏰";
        public override string Category => "RECORRÊNCIA & COBRANÇA";

        public ObservableCollection<VencimentoItemViewModel> FilteredVencimentos { get; } = new();

        public VencimentoItemViewModel? SelectedVencimento
        {
            get => _selectedVencimento;
            set
            {
                if (SetProperty(ref _selectedVencimento, value))
                {
                    OnPropertyChanged(nameof(HasSelectedVencimento));
                }
            }
        }

        public bool HasSelectedVencimento => SelectedVencimento != null;

        public VencimentosPeriodFilter CurrentFilter
        {
            get => _currentFilter;
            set
            {
                if (SetProperty(ref _currentFilter, value))
                {
                    ApplyFilterAndSearch();
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterToday));
                    OnPropertyChanged(nameof(IsFilterNext7Days));
                    OnPropertyChanged(nameof(IsFilterNext15Days));
                    OnPropertyChanged(nameof(IsFilterNext30Days));
                }
            }
        }

        public bool IsFilterAll => CurrentFilter == VencimentosPeriodFilter.All;
        public bool IsFilterToday => CurrentFilter == VencimentosPeriodFilter.Today;
        public bool IsFilterNext7Days => CurrentFilter == VencimentosPeriodFilter.Next7Days;
        public bool IsFilterNext15Days => CurrentFilter == VencimentosPeriodFilter.Next15Days;
        public bool IsFilterNext30Days => CurrentFilter == VencimentosPeriodFilter.Next30Days;

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

        public bool IsEmpty => !IsLoading && _allVencimentos.Count == 0;
        public bool IsSearchEmpty => !IsLoading && _allVencimentos.Count > 0 && FilteredVencimentos.Count == 0;

        // Métricas / Indicadores
        public int TotalCount
        {
            get => _totalCount;
            private set => SetProperty(ref _totalCount, value);
        }

        public decimal TotalAmount
        {
            get => _totalAmount;
            private set
            {
                if (SetProperty(ref _totalAmount, value))
                {
                    OnPropertyChanged(nameof(FormattedTotalAmount));
                }
            }
        }

        public string FormattedTotalAmount => TotalAmount.ToString("C2", PtBr);

        public int ParcelasCount
        {
            get => _parcelasCount;
            private set => SetProperty(ref _parcelasCount, value);
        }

        public int MensalidadesCount
        {
            get => _mensalidadesCount;
            private set => SetProperty(ref _mensalidadesCount, value);
        }

        public int TodayCount
        {
            get => _todayCount;
            private set => SetProperty(ref _todayCount, value);
        }

        public int Next7DaysCount
        {
            get => _next7DaysCount;
            private set => SetProperty(ref _next7DaysCount, value);
        }

        public int Next15DaysCount
        {
            get => _next15DaysCount;
            private set => SetProperty(ref _next15DaysCount, value);
        }

        public int Next30DaysCount
        {
            get => _next30DaysCount;
            private set => SetProperty(ref _next30DaysCount, value);
        }

        // Comandos
        public ICommand LoadDataCommand { get; }
        public ICommand SetPeriodFilterCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand OpenPaymentCommand { get; }
        public ICommand OpenMensalistaCommand { get; }
        public ICommand OpenWhatsAppCommand { get; }

        public VencimentosViewModel()
            : this(
                App.Services?.GetService<IPaymentService>() ?? new PaymentService(new CobraPixDbContext()),
                App.Services?.GetService<IMonthlyCustomerService>() ?? new MonthlyCustomerService(new CobraPixDbContext()),
                null,
                null,
                App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService())
        {
        }

        public VencimentosViewModel(
            IPaymentService paymentService,
            IMonthlyCustomerService monthlyService,
            Action<int>? onOpenPayment = null,
            Action<int>? onOpenMensalista = null,
            IWhatsAppService? whatsAppService = null)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _monthlyService = monthlyService ?? throw new ArgumentNullException(nameof(monthlyService));
            _onOpenPayment = onOpenPayment;
            _onOpenMensalista = onOpenMensalista;
            _whatsAppService = whatsAppService ?? App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService();

            WhatsAppPreview = new WhatsAppPreviewViewModel(_whatsAppService);

            LoadDataCommand = new RelayCommand(async () => await LoadDataAsync());
            SetPeriodFilterCommand = new RelayCommand(param =>
            {
                if (param is VencimentosPeriodFilter filter)
                {
                    CurrentFilter = filter;
                }
                else if (param is string str && Enum.TryParse<VencimentosPeriodFilter>(str, true, out var parsed))
                {
                    CurrentFilter = parsed;
                }
            });
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

            OpenPaymentCommand = new RelayCommand(param =>
            {
                var target = param as VencimentoItemViewModel ?? SelectedVencimento;
                if (target == null) return;

                if (target.IsParcela && target.InstallmentId.HasValue)
                {
                    _onOpenPayment?.Invoke(target.InstallmentId.Value);
                }
                else if (target.IsMensalidade && target.MonthlyCustomerId.HasValue)
                {
                    _onOpenMensalista?.Invoke(target.MonthlyCustomerId.Value);
                }
            });

            OpenMensalistaCommand = new RelayCommand(param =>
            {
                var target = param as VencimentoItemViewModel ?? SelectedVencimento;
                if (target != null && target.MonthlyCustomerId.HasValue)
                {
                    _onOpenMensalista?.Invoke(target.MonthlyCustomerId.Value);
                }
            });

            OpenWhatsAppCommand = new RelayCommand(param =>
            {
                var target = param as VencimentoItemViewModel ?? SelectedVencimento;
                if (target == null) return;

                string defaultMsg = _whatsAppService.GenerateVencimentoProximoMessage(
                    target.ClientName,
                    target.Reference,
                    target.DueDate,
                    target.OriginalAmount,
                    target.TotalPaid,
                    target.RemainingBalance);

                WhatsAppPreview.Open(
                    target.ClientName,
                    target.ClientPhone,
                    target.TipoBadge,
                    target.RemainingBalance,
                    defaultMsg,
                    _whatsAppService.PixKey);
            });

            _ = LoadDataAsync();
        }

        public async Task LoadDataAsync(DateTime? referenceDate = null)
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            try
            {
                if (referenceDate.HasValue)
                {
                    _referenceDate = referenceDate.Value;
                }
                DateTime today = _referenceDate ?? DateTime.Today;

                // 1. Sincroniza o status de parcelas vencidas no banco
                await _paymentService.UpdateOverdueInstallmentsAsync();

                // 2. Carrega parcelas que ainda não venceram
                var installments = await _paymentService.GetAllInstallmentsAsync();

                // 3. Carrega clientes mensalistas ativos
                var monthlyCustomers = await _monthlyService.GetActiveMonthlyCustomersAsync();

                int? currentSelectedId = SelectedVencimento?.Id;
                VencimentoItemType? currentSelectedType = SelectedVencimento?.ItemType;

                _allVencimentos.Clear();

                // Adiciona parcelas que ainda não venceram e possuem saldo devedor
                foreach (var inst in installments)
                {
                    if (inst.DueDate.Date >= today && inst.Status != InstallmentStatus.Paid)
                    {
                        var item = new VencimentoItemViewModel(inst);
                        if (item.RemainingBalance > 0m)
                        {
                            _allVencimentos.Add(item);
                        }
                    }
                }

                // Adiciona mensalidades ativas da competência atual que ainda não venceram e não foram pagas
                foreach (var customer in monthlyCustomers)
                {
                    if (!customer.IsActive) continue;

                    bool alreadyPaidCurrentMonth = customer.Payments != null && customer.Payments.Any(p =>
                        p.ReferenceMonth == today.Month && p.ReferenceYear == today.Year);

                    if (!alreadyPaidCurrentMonth)
                    {
                        int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
                        int dueDay = Math.Min(customer.DueDay, daysInMonth);
                        DateTime dueDate = new DateTime(today.Year, today.Month, dueDay);

                        // Considera próximo vencimento se a data é hoje ou no futuro
                        if (dueDate.Date >= today)
                        {
                            string refComp = $"{today.Month:D2}/{today.Year}";
                            _allVencimentos.Add(new VencimentoItemViewModel(customer, dueDate, refComp));
                        }
                    }
                }

                // Ordenação oficial: Data de vencimento mais próxima, em caso de empate, nome do cliente
                _allVencimentos.Sort((a, b) =>
                {
                    int dateComp = a.DueDate.Date.CompareTo(b.DueDate.Date);
                    if (dateComp != 0) return dateComp;
                    return string.Compare(a.ClientName, b.ClientName, StringComparison.CurrentCultureIgnoreCase);
                });

                UpdateCounters();
                ApplyFilterAndSearch();

                if (currentSelectedId.HasValue && currentSelectedType.HasValue)
                {
                    SelectedVencimento = _allVencimentos.FirstOrDefault(v =>
                        v.Id == currentSelectedId.Value && v.ItemType == currentSelectedType.Value);
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Erro ao carregar próximos vencimentos: " + ex.Message;
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
            DateTime today = _referenceDate ?? DateTime.Today;
            IEnumerable<VencimentoItemViewModel> query = _allVencimentos;

            // Filtro de período por data de vencimento
            switch (CurrentFilter)
            {
                case VencimentosPeriodFilter.Today:
                    query = query.Where(v => v.DueDate.Date == today);
                    break;
                case VencimentosPeriodFilter.Next7Days:
                    query = query.Where(v => v.DueDate.Date >= today && v.DueDate.Date <= today.AddDays(7));
                    break;
                case VencimentosPeriodFilter.Next15Days:
                    query = query.Where(v => v.DueDate.Date >= today && v.DueDate.Date <= today.AddDays(15));
                    break;
                case VencimentosPeriodFilter.Next30Days:
                    query = query.Where(v => v.DueDate.Date >= today && v.DueDate.Date <= today.AddDays(30));
                    break;
                case VencimentosPeriodFilter.All:
                default:
                    break;
            }

            // Pesquisa por nome do cliente ou telefone
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(v =>
                    (v.ClientName != null && v.ClientName.ToLowerInvariant().Contains(term)) ||
                    (v.ClientPhone != null && v.ClientPhone.ToLowerInvariant().Contains(term)));
            }

            FilteredVencimentos.Clear();
            foreach (var item in query)
            {
                FilteredVencimentos.Add(item);
            }

            if (SelectedVencimento != null && !FilteredVencimentos.Contains(SelectedVencimento))
            {
                SelectedVencimento = FilteredVencimentos.FirstOrDefault();
            }
            else if (SelectedVencimento == null)
            {
                SelectedVencimento = FilteredVencimentos.FirstOrDefault();
            }

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        private void UpdateCounters()
        {
            DateTime today = DateTime.Today;

            TotalCount = _allVencimentos.Count;
            TotalAmount = _allVencimentos.Sum(v => v.RemainingBalance);
            ParcelasCount = _allVencimentos.Count(v => v.IsParcela);
            MensalidadesCount = _allVencimentos.Count(v => v.IsMensalidade);

            TodayCount = _allVencimentos.Count(v => v.DueDate.Date == today);
            Next7DaysCount = _allVencimentos.Count(v => v.DueDate.Date >= today && v.DueDate.Date <= today.AddDays(7));
            Next15DaysCount = _allVencimentos.Count(v => v.DueDate.Date >= today && v.DueDate.Date <= today.AddDays(15));
            Next30DaysCount = _allVencimentos.Count(v => v.DueDate.Date >= today && v.DueDate.Date <= today.AddDays(30));
        }
    }
}
