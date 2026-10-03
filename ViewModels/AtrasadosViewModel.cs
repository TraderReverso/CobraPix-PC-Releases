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
    public enum AtrasadosTypeFilter
    {
        All,
        Parcelas,
        Mensalidades
    }

    /// <summary>
    /// ViewModel do módulo Inadimplentes e Atrasados.
    /// Monitora parcelas e mensalidades vencidas com cálculo de dias em atraso e saldo devedor.
    /// </summary>
    public class AtrasadosViewModel : ModuleViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly IPaymentService _paymentService;
        private readonly IMonthlyCustomerService _monthlyService;
        private readonly Action<int>? _onOpenPayment;
        private readonly Action<int>? _onOpenMensalista;

        private readonly List<AtrasadoItemViewModel> _allAtrasados = new();
        private AtrasadoItemViewModel? _selectedAtrasado;
        private AtrasadosTypeFilter _currentFilter = AtrasadosTypeFilter.All;
        private string _searchText = string.Empty;

        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;

        // Contadores
        private int _totalCount;
        private decimal _totalOverdueAmount;
        private int _parcelasCount;
        private int _mensalidadesCount;
        private int _maxDaysOverdue;
        private int _uniqueClientsCount;

        public override string Title => "Inadimplentes e Atrasados";
        public override string Description => "Monitoramento de parcelas vencidas e pendências financeiras.";
        public override string Icon => "⚠️";
        public override string Category => "RECORRÊNCIA & COBRANÇA";

        public ObservableCollection<AtrasadoItemViewModel> FilteredAtrasados { get; } = new();

        public AtrasadoItemViewModel? SelectedAtrasado
        {
            get => _selectedAtrasado;
            set
            {
                if (SetProperty(ref _selectedAtrasado, value))
                {
                    OnPropertyChanged(nameof(HasSelectedAtrasado));
                }
            }
        }

        public bool HasSelectedAtrasado => SelectedAtrasado != null;

        public AtrasadosTypeFilter CurrentFilter
        {
            get => _currentFilter;
            set
            {
                if (SetProperty(ref _currentFilter, value))
                {
                    ApplyFilterAndSearch();
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterParcelas));
                    OnPropertyChanged(nameof(IsFilterMensalidades));
                }
            }
        }

        public bool IsFilterAll => CurrentFilter == AtrasadosTypeFilter.All;
        public bool IsFilterParcelas => CurrentFilter == AtrasadosTypeFilter.Parcelas;
        public bool IsFilterMensalidades => CurrentFilter == AtrasadosTypeFilter.Mensalidades;

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

        public bool IsEmpty => !IsLoading && _allAtrasados.Count == 0;
        public bool IsSearchEmpty => !IsLoading && _allAtrasados.Count > 0 && FilteredAtrasados.Count == 0;

        // Métricas / Indicadores
        public int TotalCount
        {
            get => _totalCount;
            private set => SetProperty(ref _totalCount, value);
        }

        public decimal TotalOverdueAmount
        {
            get => _totalOverdueAmount;
            private set
            {
                if (SetProperty(ref _totalOverdueAmount, value))
                {
                    OnPropertyChanged(nameof(FormattedTotalOverdueAmount));
                }
            }
        }

        public string FormattedTotalOverdueAmount => TotalOverdueAmount.ToString("C2", PtBr);

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

        public int MaxDaysOverdue
        {
            get => _maxDaysOverdue;
            private set => SetProperty(ref _maxDaysOverdue, value);
        }

        public int UniqueClientsCount
        {
            get => _uniqueClientsCount;
            private set => SetProperty(ref _uniqueClientsCount, value);
        }

        // Comandos
        public ICommand LoadDataCommand { get; }
        public ICommand SetTypeFilterCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand OpenPaymentCommand { get; }
        public ICommand OpenMensalistaCommand { get; }
        public ICommand OpenWhatsAppCommand { get; }

        private readonly IWhatsAppService _whatsAppService;
        public WhatsAppPreviewViewModel WhatsAppPreview { get; }

        public AtrasadosViewModel()
            : this(
                App.Services?.GetService<IPaymentService>() ?? new PaymentService(new CobraPixDbContext()),
                App.Services?.GetService<IMonthlyCustomerService>() ?? new MonthlyCustomerService(new CobraPixDbContext()),
                null,
                null,
                App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService())
        {
        }

        public AtrasadosViewModel(
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

            OpenWhatsAppCommand = new RelayCommand(param =>
            {
                var target = param as AtrasadoItemViewModel ?? SelectedAtrasado;
                if (target == null) return;

                string msg = target.IsParcela
                    ? _whatsAppService.GenerateParcelaAtrasadaMessage(target.ClientName, target.Reference, target.DueDate, target.RemainingBalance)
                    : _whatsAppService.GenerateMensalidadeAtrasadaMessage(target.ClientName, target.Reference, target.DueDate, target.RemainingBalance);

                WhatsAppPreview.Open(target.ClientName, target.ClientPhone, target.TipoBadge, target.FormattedRemainingBalance, msg);
            });

            LoadDataCommand = new RelayCommand(async () => await LoadDataAsync());
            SetTypeFilterCommand = new RelayCommand(param =>
            {
                if (param is AtrasadosTypeFilter filter)
                {
                    CurrentFilter = filter;
                }
                else if (param is string str && Enum.TryParse<AtrasadosTypeFilter>(str, true, out var parsed))
                {
                    CurrentFilter = parsed;
                }
            });
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

            OpenPaymentCommand = new RelayCommand(param =>
            {
                var target = param as AtrasadoItemViewModel ?? SelectedAtrasado;
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
                var target = param as AtrasadoItemViewModel ?? SelectedAtrasado;
                if (target != null && target.MonthlyCustomerId.HasValue)
                {
                    _onOpenMensalista?.Invoke(target.MonthlyCustomerId.Value);
                }
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
                DateTime today = referenceDate?.Date ?? DateTime.Today;

                // 1. Sincroniza o status de parcelas vencidas no banco
                await _paymentService.UpdateOverdueInstallmentsAsync();

                // 2. Carrega parcelas
                var installments = await _paymentService.GetAllInstallmentsAsync();

                // 3. Carrega clientes mensalistas ativos
                var monthlyCustomers = await _monthlyService.GetActiveMonthlyCustomersAsync();

                int? currentSelectedId = SelectedAtrasado?.Id;
                AtrasadoItemType? currentSelectedType = SelectedAtrasado?.ItemType;

                _allAtrasados.Clear();

                // Adiciona parcelas vencidas que possuem saldo devedor
                foreach (var inst in installments)
                {
                    if (inst.DueDate.Date < today && inst.Status != InstallmentStatus.Paid)
                    {
                        var item = new AtrasadoItemViewModel(inst);
                        if (item.RemainingBalance > 0m)
                        {
                            _allAtrasados.Add(item);
                        }
                    }
                }

                // Adiciona mensalidades ativas da competência atual que já passaram do dia de vencimento e não foram pagas
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

                        // Considera atrasada se a data de vencimento é anterior a hoje
                        // ou no início do mês quando dueDay coincide ou contrato anterior sem pagamento
                        if (dueDate.Date < today || (today.Day <= 2 && dueDay <= today.Day) || (today.Day <= dueDay && customer.StartDate.Date <= today.AddMonths(-1).Date))
                        {
                            string refComp = $"{today.Month:D2}/{today.Year}";
                            _allAtrasados.Add(new AtrasadoItemViewModel(customer, dueDate.Date < today ? dueDate : today.AddDays(-1), refComp));
                        }
                    }
                }

                // Ordenação oficial: Maior quantidade de dias em atraso (decrescente); em caso de empate, nome do cliente
                _allAtrasados.Sort((a, b) =>
                {
                    int daysComp = b.DaysOverdue.CompareTo(a.DaysOverdue);
                    if (daysComp != 0) return daysComp;
                    return string.Compare(a.ClientName, b.ClientName, StringComparison.CurrentCultureIgnoreCase);
                });

                UpdateCounters();
                ApplyFilterAndSearch();

                if (currentSelectedId.HasValue && currentSelectedType.HasValue)
                {
                    SelectedAtrasado = _allAtrasados.FirstOrDefault(a =>
                        a.Id == currentSelectedId.Value && a.ItemType == currentSelectedType.Value);
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Erro ao carregar atrasados: " + ex.Message;
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
            IEnumerable<AtrasadoItemViewModel> query = _allAtrasados;

            // Filtro por tipo de registro
            switch (CurrentFilter)
            {
                case AtrasadosTypeFilter.Parcelas:
                    query = query.Where(a => a.IsParcela);
                    break;
                case AtrasadosTypeFilter.Mensalidades:
                    query = query.Where(a => a.IsMensalidade);
                    break;
                case AtrasadosTypeFilter.All:
                default:
                    break;
            }

            // Pesquisa por nome do cliente ou telefone
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(a =>
                    (a.ClientName != null && a.ClientName.ToLowerInvariant().Contains(term)) ||
                    (a.ClientPhone != null && a.ClientPhone.ToLowerInvariant().Contains(term)));
            }

            FilteredAtrasados.Clear();
            foreach (var item in query)
            {
                FilteredAtrasados.Add(item);
            }

            if (SelectedAtrasado != null && !FilteredAtrasados.Contains(SelectedAtrasado))
            {
                SelectedAtrasado = FilteredAtrasados.FirstOrDefault();
            }
            else if (SelectedAtrasado == null)
            {
                SelectedAtrasado = FilteredAtrasados.FirstOrDefault();
            }

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        private void UpdateCounters()
        {
            TotalCount = _allAtrasados.Count;
            TotalOverdueAmount = _allAtrasados.Sum(a => a.RemainingBalance);
            ParcelasCount = _allAtrasados.Count(a => a.IsParcela);
            MensalidadesCount = _allAtrasados.Count(a => a.IsMensalidade);

            MaxDaysOverdue = _allAtrasados.Count > 0 ? _allAtrasados.Max(a => a.DaysOverdue) : 0;
            UniqueClientsCount = _allAtrasados.Select(a => a.ClientName).Distinct().Count();
        }
    }
}
