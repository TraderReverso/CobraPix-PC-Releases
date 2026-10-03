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
    public enum CobrancasFilterType
    {
        All,
        Parcelas,
        Mensalidades,
        FifthBusinessDay,
        Atrasadas,
        Vencendo
    }

    /// <summary>
    /// ViewModel da Central de Cobranças.
    /// Gerencia cobranças preventivas e atrasadas, integrando parcelas, mensalidades e a regra do 5º dia útil.
    /// </summary>
    public class CobrancasViewModel : ModuleViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly IPaymentService _paymentService;
        private readonly IMonthlyCustomerService _monthlyService;
        private readonly Action<int>? _onOpenPayment;
        private readonly Action<int>? _onOpenMensalista;

        private readonly List<CobrancaItemViewModel> _allCobrancas = new();
        private CobrancaItemViewModel? _selectedCobranca;
        private CobrancasFilterType _currentFilter = CobrancasFilterType.All;
        private string _searchText = string.Empty;

        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;

        // Contadores / Indicadores do Resumo
        private decimal _totalParaCobrar;
        private decimal _totalEmAtraso;
        private int _parcelasAtrasadasCount;
        private int _mensalidadesAtrasadasCount;
        private int _clientesEnvolvidosCount;
        private string _proximoQuintoDiaUtilText = string.Empty;
        private string _fifthBusinessDayStatusText = string.Empty;
        private int _fifthBusinessDayObligationsCount;

        public override string Title => "Central de Cobranças";
        public override string Description => "Gestão centralizada de cobranças, clientes em atraso e regra do 5º dia útil.";
        public override string Icon => "💬";
        public override string Category => "RECORRÊNCIA & COBRANÇA";

        public ObservableCollection<CobrancaItemViewModel> FilteredCobrancas { get; } = new();

        public CobrancaItemViewModel? SelectedCobranca
        {
            get => _selectedCobranca;
            set
            {
                if (SetProperty(ref _selectedCobranca, value))
                {
                    OnPropertyChanged(nameof(HasSelectedCobranca));
                }
            }
        }

        public bool HasSelectedCobranca => SelectedCobranca != null;

        public CobrancasFilterType CurrentFilter
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
                    OnPropertyChanged(nameof(IsFilterFifthBusinessDay));
                    OnPropertyChanged(nameof(IsFilterAtrasadas));
                    OnPropertyChanged(nameof(IsFilterVencendo));
                }
            }
        }

        public bool IsFilterAll => CurrentFilter == CobrancasFilterType.All;
        public bool IsFilterParcelas => CurrentFilter == CobrancasFilterType.Parcelas;
        public bool IsFilterMensalidades => CurrentFilter == CobrancasFilterType.Mensalidades;
        public bool IsFilterFifthBusinessDay => CurrentFilter == CobrancasFilterType.FifthBusinessDay;
        public bool IsFilterAtrasadas => CurrentFilter == CobrancasFilterType.Atrasadas;
        public bool IsFilterVencendo => CurrentFilter == CobrancasFilterType.Vencendo;

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

        public bool IsEmpty => !IsLoading && _allCobrancas.Count == 0;
        public bool IsSearchEmpty => !IsLoading && _allCobrancas.Count > 0 && FilteredCobrancas.Count == 0;

        // Métricas do Resumo
        public decimal TotalParaCobrar
        {
            get => _totalParaCobrar;
            private set
            {
                if (SetProperty(ref _totalParaCobrar, value))
                {
                    OnPropertyChanged(nameof(FormattedTotalParaCobrar));
                }
            }
        }

        public string FormattedTotalParaCobrar => TotalParaCobrar.ToString("C2", PtBr);

        public decimal TotalEmAtraso
        {
            get => _totalEmAtraso;
            private set
            {
                if (SetProperty(ref _totalEmAtraso, value))
                {
                    OnPropertyChanged(nameof(FormattedTotalEmAtraso));
                }
            }
        }

        public string FormattedTotalEmAtraso => TotalEmAtraso.ToString("C2", PtBr);

        public int ParcelasAtrasadasCount
        {
            get => _parcelasAtrasadasCount;
            private set => SetProperty(ref _parcelasAtrasadasCount, value);
        }

        public int MensalidadesAtrasadasCount
        {
            get => _mensalidadesAtrasadasCount;
            private set => SetProperty(ref _mensalidadesAtrasadasCount, value);
        }

        public int ClientesEnvolvidosCount
        {
            get => _clientesEnvolvidosCount;
            private set => SetProperty(ref _clientesEnvolvidosCount, value);
        }

        public string ProximoQuintoDiaUtilText
        {
            get => _proximoQuintoDiaUtilText;
            private set => SetProperty(ref _proximoQuintoDiaUtilText, value);
        }

        public string FifthBusinessDayStatusText
        {
            get => _fifthBusinessDayStatusText;
            private set => SetProperty(ref _fifthBusinessDayStatusText, value);
        }

        public int FifthBusinessDayObligationsCount
        {
            get => _fifthBusinessDayObligationsCount;
            private set => SetProperty(ref _fifthBusinessDayObligationsCount, value);
        }

        private readonly IWhatsAppService _whatsAppService;

        // Comandos
        public ICommand LoadDataCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand OpenPaymentCommand { get; }
        public ICommand OpenMensalistaCommand { get; }
        public ICommand ExecuteItemActionCommand { get; }
        public ICommand OpenWhatsAppCommand { get; }

        public WhatsAppPreviewViewModel WhatsAppPreview { get; }

        public CobrancasViewModel()
            : this(
                App.Services?.GetService<IPaymentService>() ?? new PaymentService(new CobraPixDbContext()),
                App.Services?.GetService<IMonthlyCustomerService>() ?? new MonthlyCustomerService(new CobraPixDbContext()),
                null,
                null,
                App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService())
        {
        }

        public CobrancasViewModel(
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
                var target = param as CobrancaItemViewModel ?? SelectedCobranca;
                if (target == null) return;

                string msg;
                if (target.Status == "Atrasada")
                {
                    if (target.IsParcela)
                    {
                        msg = _whatsAppService.GenerateParcelaAtrasadaMessage(target.ClientName, target.Reference, target.DueDate, target.RemainingBalance);
                    }
                    else
                    {
                        msg = _whatsAppService.GenerateMensalidadeAtrasadaMessage(target.ClientName, target.Reference, target.DueDate, target.RemainingBalance);
                    }
                }
                else
                {
                    msg = _whatsAppService.GenerateVencimentoProximoMessage(target.ClientName, target.Reference, target.DueDate, target.RemainingBalance);
                }

                WhatsAppPreview.Open(target.ClientName, target.ClientPhone, target.TipoBadge, target.FormattedRemainingBalance, msg);
            });

            LoadDataCommand = new RelayCommand(async () => await LoadDataAsync());
            SetFilterCommand = new RelayCommand(param =>
            {
                if (param is CobrancasFilterType filter)
                {
                    CurrentFilter = filter;
                }
                else if (param is string str && Enum.TryParse<CobrancasFilterType>(str, true, out var parsed))
                {
                    CurrentFilter = parsed;
                }
            });
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

            OpenPaymentCommand = new RelayCommand(param =>
            {
                var target = param as CobrancaItemViewModel ?? SelectedCobranca;
                if (target != null && target.InstallmentId.HasValue)
                {
                    _onOpenPayment?.Invoke(target.InstallmentId.Value);
                }
            });

            OpenMensalistaCommand = new RelayCommand(param =>
            {
                var target = param as CobrancaItemViewModel ?? SelectedCobranca;
                if (target != null && target.MonthlyCustomerId.HasValue)
                {
                    _onOpenMensalista?.Invoke(target.MonthlyCustomerId.Value);
                }
            });

            ExecuteItemActionCommand = new RelayCommand(param =>
            {
                var target = param as CobrancaItemViewModel ?? SelectedCobranca;
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

            _ = LoadDataAsync();
        }

        public async Task LoadDataAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            try
            {
                DateTime today = DateTime.Today;

                // 1. Sincroniza o status de parcelas vencidas no banco
                await _paymentService.UpdateOverdueInstallmentsAsync();

                // 2. Carrega parcelas
                var installments = await _paymentService.GetAllInstallmentsAsync();

                // 3. Carrega clientes mensalistas ativos
                var monthlyCustomers = await _monthlyService.GetActiveMonthlyCustomersAsync();

                // 4. Calcula 5º dia útil do mês atual e subsequente
                DateTime currentFifthBusinessDay = BusinessDayHelper.GetFifthBusinessDay(today.Year, today.Month);
                DateTime nextFifthBusinessDay = BusinessDayHelper.GetNextFifthBusinessDay(today);
                DateTime targetFifth = (today <= currentFifthBusinessDay) ? currentFifthBusinessDay : nextFifthBusinessDay;

                ProximoQuintoDiaUtilText = targetFifth.ToString("dd/MM/yyyy");
                FifthBusinessDayStatusText = BusinessDayHelper.GetFifthBusinessDayStatusText(today, targetFifth);

                int? currentSelectedId = SelectedCobranca?.Id;
                string? currentSelectedTipo = SelectedCobranca?.Tipo;

                _allCobrancas.Clear();

                // 5. Processa Parcelas
                foreach (var inst in installments)
                {
                    // Não incluir parcelas quitadas
                    if (inst.Status == InstallmentStatus.Paid) continue;

                    // Calcular saldo devedor
                    decimal paid = 0m;
                    if (inst.Payments != null)
                    {
                        foreach (var p in inst.Payments) paid += p.AmountPaid;
                    }
                    decimal balance = inst.Amount - paid;
                    if (balance <= 0m) continue;

                    bool isFifthBusinessDay = (inst.DueDate.Date == currentFifthBusinessDay.Date) ||
                                              (inst.DueDate.Date == nextFifthBusinessDay.Date) ||
                                              (inst.Notes != null && (inst.Notes.Contains("5º dia útil", StringComparison.OrdinalIgnoreCase) ||
                                                                      inst.Notes.Contains("5º dia util", StringComparison.OrdinalIgnoreCase) ||
                                                                      inst.Notes.Contains("quinto dia util", StringComparison.OrdinalIgnoreCase)));

                    bool isOverdue = inst.DueDate.Date < today;
                    bool isUpcoming = inst.DueDate.Date >= today && inst.DueDate.Date <= today.AddDays(3);

                    // Inclui se estiver em atraso, vencendo nos próximos 3 dias, ou atrelada ao 5º dia útil
                    if (isOverdue || isUpcoming || isFifthBusinessDay)
                    {
                        var item = new CobrancaItemViewModel(inst, isFifthBusinessDay: isFifthBusinessDay, referenceDate: today);
                        _allCobrancas.Add(item);
                    }
                }

                // 6. Processa Mensalidades
                foreach (var customer in monthlyCustomers)
                {
                    // Mensalista deve estar ativo
                    if (!customer.IsActive) continue;

                    // Verificar se já pagou a competência atual
                    bool alreadyPaidCurrentMonth = customer.Payments != null && customer.Payments.Any(p =>
                        p.ReferenceMonth == today.Month && p.ReferenceYear == today.Year);

                    if (alreadyPaidCurrentMonth) continue;

                    bool isFifthBusinessDay = (customer.DueDay == currentFifthBusinessDay.Day) ||
                                              (customer.Notes != null && (customer.Notes.Contains("5º dia útil", StringComparison.OrdinalIgnoreCase) ||
                                                                          customer.Notes.Contains("5º dia util", StringComparison.OrdinalIgnoreCase) ||
                                                                          customer.Notes.Contains("quinto dia util", StringComparison.OrdinalIgnoreCase)));

                    DateTime dueDate;
                    if (isFifthBusinessDay)
                    {
                        dueDate = currentFifthBusinessDay;
                    }
                    else
                    {
                        int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
                        int dueDay = Math.Min(customer.DueDay, daysInMonth);
                        dueDate = new DateTime(today.Year, today.Month, dueDay);
                    }

                    bool isOverdue = dueDate.Date < today;
                    bool isUpcoming = dueDate.Date >= today && dueDate.Date <= today.AddDays(3);

                    if (isOverdue || isUpcoming || isFifthBusinessDay)
                    {
                        string refComp = $"{today.Month:D2}/{today.Year}";
                        var item = new CobrancaItemViewModel(customer, dueDate, refComp, isFifthBusinessDay: isFifthBusinessDay, referenceDate: today);
                        _allCobrancas.Add(item);
                    }
                }

                // 7. Ordenação: primeiro os atrasados com maior tempo em atraso, depois próximos vencimentos e ordem alfabética
                _allCobrancas.Sort((a, b) =>
                {
                    bool aOverdue = a.Status == "Atrasada";
                    bool bOverdue = b.Status == "Atrasada";

                    if (aOverdue && bOverdue)
                    {
                        int daysComp = b.DaysOverdue.CompareTo(a.DaysOverdue);
                        if (daysComp != 0) return daysComp;
                    }
                    else if (aOverdue && !bOverdue)
                    {
                        return -1;
                    }
                    else if (!aOverdue && bOverdue)
                    {
                        return 1;
                    }

                    int dateComp = a.DueDate.Date.CompareTo(b.DueDate.Date);
                    if (dateComp != 0) return dateComp;

                    return string.Compare(a.ClientName, b.ClientName, StringComparison.CurrentCultureIgnoreCase);
                });

                UpdateCounters();
                ApplyFilterAndSearch();

                if (currentSelectedId.HasValue && currentSelectedTipo != null)
                {
                    SelectedCobranca = _allCobrancas.FirstOrDefault(c =>
                        c.Id == currentSelectedId.Value && c.Tipo == currentSelectedTipo);
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Erro ao carregar central de cobranças: " + ex.Message;
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
            IEnumerable<CobrancaItemViewModel> query = _allCobrancas;

            // Filtros de visualização
            switch (CurrentFilter)
            {
                case CobrancasFilterType.Parcelas:
                    query = query.Where(c => c.Tipo == "Parcela");
                    break;
                case CobrancasFilterType.Mensalidades:
                    query = query.Where(c => c.Tipo == "Mensalidade");
                    break;
                case CobrancasFilterType.FifthBusinessDay:
                    query = query.Where(c => c.Tipo == "5º Dia Útil" || c.IsFifthBusinessDay);
                    break;
                case CobrancasFilterType.Atrasadas:
                    query = query.Where(c => c.Status == "Atrasada");
                    break;
                case CobrancasFilterType.Vencendo:
                    query = query.Where(c => c.Status == "Vencendo");
                    break;
                case CobrancasFilterType.All:
                default:
                    break;
            }

            // Busca por Nome ou Telefone
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim();
                query = query.Where(c =>
                    c.ClientName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                    c.ClientPhone.Contains(term, StringComparison.CurrentCultureIgnoreCase));
            }

            FilteredCobrancas.Clear();
            foreach (var item in query)
            {
                FilteredCobrancas.Add(item);
            }

            if (SelectedCobranca != null && !FilteredCobrancas.Contains(SelectedCobranca))
            {
                SelectedCobranca = FilteredCobrancas.FirstOrDefault();
            }

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        public void UpdateCounters()
        {
            TotalParaCobrar = _allCobrancas.Sum(c => c.RemainingBalance);
            TotalEmAtraso = _allCobrancas.Where(c => c.Status == "Atrasada").Sum(c => c.RemainingBalance);

            ParcelasAtrasadasCount = _allCobrancas.Count(c => c.IsParcela && c.Status == "Atrasada");
            MensalidadesAtrasadasCount = _allCobrancas.Count(c => c.IsMensalidade && c.Status == "Atrasada");

            ClientesEnvolvidosCount = _allCobrancas.Select(c => c.ClientName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            FifthBusinessDayObligationsCount = _allCobrancas.Count(c => c.IsFifthBusinessDay || c.Tipo == "5º Dia Útil");
        }

        /// <summary>
        /// Método auxiliar para testes e injeção controlada de itens em memória.
        /// </summary>
        public void SetItemsForTesting(IEnumerable<CobrancaItemViewModel> items)
        {
            _allCobrancas.Clear();
            if (items != null)
            {
                _allCobrancas.AddRange(items);
            }
            UpdateCounters();
            ApplyFilterAndSearch();
        }
    }
}
