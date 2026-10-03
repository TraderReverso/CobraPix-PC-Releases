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
    public enum MonthlyCustomerFilterType
    {
        All,
        Active,
        Inactive
    }

    /// <summary>
    /// ViewModel do módulo de Clientes Mensalistas com gestão de cobrança recorrente,
    /// histórico de mensalidades e quitação do mês vigente.
    /// </summary>
    public class MensalistasViewModel : ModuleViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly IMonthlyCustomerService _monthlyService;
        private readonly IClientService _clientService;
        private readonly IWhatsAppService _whatsAppService;

        public WhatsAppPreviewViewModel WhatsAppPreview { get; }

        private readonly List<MonthlyCustomerItemViewModel> _allCustomers = new();
        private MonthlyCustomerItemViewModel? _selectedMonthlyCustomer;
        private MonthlyCustomerFilterType _currentFilter = MonthlyCustomerFilterType.All;
        private string _searchText = string.Empty;

        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;

        // Contadores
        private int _totalCount;
        private int _activeCount;
        private int _inactiveCount;
        private int _currentMonthPaidCount;
        private int _currentMonthPendingCount;
        private int _currentMonthOverdueCount;
        private decimal _totalMonthlyRevenue;

        // Pagamento do mês atual
        private string _paymentAmountText = string.Empty;
        private PaymentMethod _selectedPaymentMethod = PaymentMethod.Pix;
        private string? _paymentNotes;
        private bool _isProcessingPayment;
        private bool _hasPaymentError;
        private string? _paymentErrorMessage;
        private bool _hasPaymentSuccess;
        private string? _paymentSuccessMessage;
        private string? _paymentSuccessSummary;

        // Formulário (Novo / Editar)
        private bool _isFormOpen;
        private bool _isEditing;
        private bool _hasFormError;
        private string? _formErrorMessage;
        private Client? _selectedFormClient;
        private string _formClientNameText = string.Empty;
        private string _formMonthlyAmountText = string.Empty;
        private int _formDueDay = 5;
        private DateTime _formStartDate = DateTime.Today;
        private string? _formNotes;
        private bool _isSavingForm;

        public override string Title => "Clientes Mensalistas";
        public override string Description => "Controle de clientes com mensalidades recorrentes e vencimento fixo.";
        public override string Icon => "⭐";
        public override string Category => "RECORRÊNCIA & COBRANÇA";

        public ObservableCollection<MonthlyCustomerItemViewModel> FilteredMonthlyCustomers { get; } = new();
        public ObservableCollection<Client> ActiveClients { get; } = new();

        public MonthlyCustomerItemViewModel? SelectedMonthlyCustomer
        {
            get => _selectedMonthlyCustomer;
            set
            {
                if (SetProperty(ref _selectedMonthlyCustomer, value))
                {
                    OnPropertyChanged(nameof(HasSelectedMonthlyCustomer));
                    OnSelectedCustomerChanged();
                }
            }
        }

        public bool HasSelectedMonthlyCustomer => SelectedMonthlyCustomer != null;

        public MonthlyCustomerFilterType CurrentFilter
        {
            get => _currentFilter;
            set
            {
                if (SetProperty(ref _currentFilter, value))
                {
                    ApplyFilterAndSearch();
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(IsFilterInactive));
                }
            }
        }

        public bool IsFilterAll => CurrentFilter == MonthlyCustomerFilterType.All;
        public bool IsFilterActive => CurrentFilter == MonthlyCustomerFilterType.Active;
        public bool IsFilterInactive => CurrentFilter == MonthlyCustomerFilterType.Inactive;

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

        public bool IsEmpty => !IsLoading && _allCustomers.Count == 0;
        public bool IsSearchEmpty => !IsLoading && _allCustomers.Count > 0 && FilteredMonthlyCustomers.Count == 0;

        // Contadores
        public int TotalCount
        {
            get => _totalCount;
            private set => SetProperty(ref _totalCount, value);
        }

        public int ActiveCount
        {
            get => _activeCount;
            private set => SetProperty(ref _activeCount, value);
        }

        public int InactiveCount
        {
            get => _inactiveCount;
            private set => SetProperty(ref _inactiveCount, value);
        }

        public int CurrentMonthPaidCount
        {
            get => _currentMonthPaidCount;
            private set => SetProperty(ref _currentMonthPaidCount, value);
        }

        public int CurrentMonthPendingCount
        {
            get => _currentMonthPendingCount;
            private set => SetProperty(ref _currentMonthPendingCount, value);
        }

        public int CurrentMonthOverdueCount
        {
            get => _currentMonthOverdueCount;
            private set => SetProperty(ref _currentMonthOverdueCount, value);
        }

        public decimal TotalMonthlyRevenue
        {
            get => _totalMonthlyRevenue;
            private set
            {
                if (SetProperty(ref _totalMonthlyRevenue, value))
                {
                    OnPropertyChanged(nameof(FormattedTotalMonthlyRevenue));
                }
            }
        }

        public string FormattedTotalMonthlyRevenue => TotalMonthlyRevenue.ToString("C2", PtBr);

        // Competência Atual
        public int CurrentReferenceMonth => DateTime.Today.Month;
        public int CurrentReferenceYear => DateTime.Today.Year;
        public string FormattedCurrentCompetence => $"{CurrentReferenceMonth:D2}/{CurrentReferenceYear}";

        // Campos de Pagamento do Mês Atual
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

        // Formulário (Novo / Editar)
        public bool IsFormOpen
        {
            get => _isFormOpen;
            set => SetProperty(ref _isFormOpen, value);
        }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (SetProperty(ref _isEditing, value))
                {
                    OnPropertyChanged(nameof(FormTitle));
                }
            }
        }

        public string FormTitle => IsEditing ? "Editar Mensalista" : "Novo Mensalista";

        public bool HasFormError
        {
            get => _hasFormError;
            set => SetProperty(ref _hasFormError, value);
        }

        public string? FormErrorMessage
        {
            get => _formErrorMessage;
            set => SetProperty(ref _formErrorMessage, value);
        }

        public bool HasActiveClients => ActiveClients.Count > 0;

        public Client? SelectedFormClient
        {
            get => _selectedFormClient;
            set => SetProperty(ref _selectedFormClient, value);
        }

        public string FormClientNameText
        {
            get => _formClientNameText;
            set => SetProperty(ref _formClientNameText, value);
        }

        public string FormMonthlyAmountText
        {
            get => _formMonthlyAmountText;
            set => SetProperty(ref _formMonthlyAmountText, value);
        }

        public int FormDueDay
        {
            get => _formDueDay;
            set => SetProperty(ref _formDueDay, value);
        }

        public DateTime FormStartDate
        {
            get => _formStartDate;
            set => SetProperty(ref _formStartDate, value);
        }

        public string? FormNotes
        {
            get => _formNotes;
            set => SetProperty(ref _formNotes, value);
        }

        public bool IsSavingForm
        {
            get => _isSavingForm;
            set => SetProperty(ref _isSavingForm, value);
        }

        // Comandos
        public ICommand LoadCustomersCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand OpenNewFormCommand { get; }
        public ICommand OpenEditFormCommand { get; }
        public ICommand CancelFormCommand { get; }
        public ICommand SaveFormCommand { get; }
        public ICommand ToggleStatusCommand { get; }
        public ICommand SetPaymentMethodCommand { get; }
        public ICommand RegisterPaymentCommand { get; }
        public ICommand OpenWhatsAppCommand { get; }

        public MensalistasViewModel()
            : this(
                App.Services?.GetService<IMonthlyCustomerService>() ?? new MonthlyCustomerService(new CobraPixDbContext()),
                App.Services?.GetService<IClientService>() ?? new ClientService(new CobraPixDbContext()),
                App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService())
        {
        }

        public MensalistasViewModel(IMonthlyCustomerService monthlyService, IClientService clientService)
            : this(
                monthlyService,
                clientService,
                App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService())
        {
        }

        public MensalistasViewModel(IMonthlyCustomerService monthlyService, IClientService clientService, IWhatsAppService whatsAppService)
        {
            _monthlyService = monthlyService ?? throw new ArgumentNullException(nameof(monthlyService));
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _whatsAppService = whatsAppService ?? throw new ArgumentNullException(nameof(whatsAppService));

            WhatsAppPreview = new WhatsAppPreviewViewModel(_whatsAppService);

            LoadCustomersCommand = new RelayCommand(async () => await LoadMonthlyCustomersAsync());
            SetFilterCommand = new RelayCommand(param =>
            {
                if (param is MonthlyCustomerFilterType filter)
                    CurrentFilter = filter;
                else if (param is string str && Enum.TryParse<MonthlyCustomerFilterType>(str, out var parsed))
                    CurrentFilter = parsed;
            });
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

            OpenNewFormCommand = new RelayCommand(async () => await OpenNewFormAsync());
            OpenEditFormCommand = new RelayCommand(() => OpenEditForm());
            CancelFormCommand = new RelayCommand(() => CloseForm());
            SaveFormCommand = new RelayCommand(async () => await SaveFormAsync());

            ToggleStatusCommand = new RelayCommand(async () => await ToggleStatusAsync());
            SetPaymentMethodCommand = new RelayCommand(param =>
            {
                if (param is PaymentMethod method)
                    SelectedPaymentMethod = method;
                else if (param is string str && Enum.TryParse<PaymentMethod>(str, out var parsed))
                    SelectedPaymentMethod = parsed;
            });
            RegisterPaymentCommand = new RelayCommand(async () => await RegisterPaymentAsync());

            OpenWhatsAppCommand = new RelayCommand(param => OpenWhatsApp(param as MonthlyCustomerItemViewModel ?? SelectedMonthlyCustomer));

            // Carregamento inicial assíncrono
            _ = LoadMonthlyCustomersAsync();
        }

        private void OpenWhatsApp(MonthlyCustomerItemViewModel? customer)
        {
            if (customer == null) return;

            DateTime today = DateTime.Today;
            int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
            int dueDay = Math.Min(customer.DueDay, daysInMonth);
            DateTime dueDate = new DateTime(today.Year, today.Month, dueDay);
            string refMes = $"{today.Month:D2}/{today.Year}";

            string defaultMsg;
            if (customer.IsCurrentMonthOverdue)
            {
                defaultMsg = _whatsAppService.GenerateMensalidadeAtrasadaMessage(
                    customer.ClientName,
                    refMes,
                    dueDate,
                    customer.MonthlyAmount);
            }
            else if (customer.IsCurrentMonthPending)
            {
                defaultMsg = _whatsAppService.GenerateVencimentoProximoMessage(
                    customer.ClientName,
                    $"Mensalidade {refMes}",
                    dueDate,
                    customer.MonthlyAmount);
            }
            else
            {
                defaultMsg = _whatsAppService.GenerateSimpleContactMessage(customer.ClientName);
            }

            WhatsAppPreview.Open(
                customer.ClientName,
                customer.ClientPhone,
                "⭐ Mensalidade",
                customer.MonthlyAmount,
                defaultMsg);
        }

        public async Task LoadMonthlyCustomersAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            try
            {
                var customers = await _monthlyService.GetAllMonthlyCustomersAsync();

                int? currentSelectedId = SelectedMonthlyCustomer?.Id;

                _allCustomers.Clear();
                foreach (var c in customers)
                {
                    _allCustomers.Add(new MonthlyCustomerItemViewModel(c));
                }

                UpdateCounters();
                ApplyFilterAndSearch();

                if (currentSelectedId.HasValue)
                {
                    SelectedMonthlyCustomer = _allCustomers.FirstOrDefault(c => c.Id == currentSelectedId.Value);
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Erro ao carregar mensalistas: " + ex.Message;
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
            IEnumerable<MonthlyCustomerItemViewModel> query = _allCustomers;

            switch (CurrentFilter)
            {
                case MonthlyCustomerFilterType.Active:
                    query = query.Where(c => c.IsActive);
                    break;
                case MonthlyCustomerFilterType.Inactive:
                    query = query.Where(c => !c.IsActive);
                    break;
                case MonthlyCustomerFilterType.All:
                default:
                    break;
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(c =>
                    (c.ClientName != null && c.ClientName.ToLowerInvariant().Contains(term)) ||
                    (c.ClientPhone != null && c.ClientPhone.ToLowerInvariant().Contains(term)));
            }

            FilteredMonthlyCustomers.Clear();
            foreach (var item in query.OrderBy(c => c.ClientName))
            {
                FilteredMonthlyCustomers.Add(item);
            }

            if (SelectedMonthlyCustomer != null && !FilteredMonthlyCustomers.Contains(SelectedMonthlyCustomer))
            {
                SelectedMonthlyCustomer = FilteredMonthlyCustomers.FirstOrDefault();
            }
            else if (SelectedMonthlyCustomer == null)
            {
                SelectedMonthlyCustomer = FilteredMonthlyCustomers.FirstOrDefault();
            }

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        private void UpdateCounters()
        {
            TotalCount = _allCustomers.Count;
            ActiveCount = _allCustomers.Count(c => c.IsActive);
            InactiveCount = _allCustomers.Count(c => !c.IsActive);

            CurrentMonthPaidCount = _allCustomers.Count(c => c.IsActive && c.IsCurrentMonthPaid);
            CurrentMonthPendingCount = _allCustomers.Count(c => c.IsActive && c.IsCurrentMonthPending);
            CurrentMonthOverdueCount = _allCustomers.Count(c => c.IsActive && c.IsCurrentMonthOverdue);

            TotalMonthlyRevenue = _allCustomers.Where(c => c.IsActive).Sum(c => c.MonthlyAmount);
        }

        private void OnSelectedCustomerChanged()
        {
            HasPaymentError = false;
            PaymentErrorMessage = null;
            HasPaymentSuccess = false;
            PaymentSuccessMessage = null;
            PaymentSuccessSummary = null;

            if (SelectedMonthlyCustomer != null)
            {
                PaymentAmountText = SelectedMonthlyCustomer.MonthlyAmount.ToString("N2", PtBr);
                SelectedPaymentMethod = PaymentMethod.Pix;
                PaymentNotes = string.Empty;
            }
            else
            {
                PaymentAmountText = string.Empty;
                PaymentNotes = string.Empty;
            }
        }

        public async Task RegisterPaymentAsync()
        {
            if (SelectedMonthlyCustomer == null)
            {
                HasPaymentError = true;
                PaymentErrorMessage = "Selecione um mensalista para registrar o pagamento.";
                return;
            }

            if (!SelectedMonthlyCustomer.IsActive)
            {
                HasPaymentError = true;
                PaymentErrorMessage = "Não é possível registrar pagamentos para mensalistas inativos.";
                return;
            }

            if (SelectedMonthlyCustomer.IsCurrentMonthPaid)
            {
                HasPaymentError = true;
                PaymentErrorMessage = $"A mensalidade da competência {FormattedCurrentCompetence} já está quitada.";
                return;
            }

            string cleanText = PaymentAmountText?.Trim() ?? string.Empty;
            if (!decimal.TryParse(cleanText, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, PtBr, out decimal amount) &&
                !decimal.TryParse(cleanText, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            {
                HasPaymentError = true;
                PaymentErrorMessage = "Informe um valor de pagamento válido (ex: 150,00).";
                return;
            }

            if (amount <= 0m)
            {
                HasPaymentError = true;
                PaymentErrorMessage = "O valor do pagamento da mensalidade deve ser maior que zero.";
                return;
            }

            IsProcessingPayment = true;
            HasPaymentError = false;
            PaymentErrorMessage = null;
            HasPaymentSuccess = false;

            try
            {
                DateTime paymentDate = DateTime.Now;

                await _monthlyService.RegisterPaymentAsync(
                    SelectedMonthlyCustomer.Id,
                    amount,
                    paymentDate,
                    CurrentReferenceMonth,
                    CurrentReferenceYear,
                    SelectedPaymentMethod,
                    PaymentNotes);

                // Recarrega o histórico de pagamentos do mensalista
                var updatedPayments = await _monthlyService.GetPaymentsByMonthlyCustomerAsync(SelectedMonthlyCustomer.Id);
                SelectedMonthlyCustomer.SetPayments(updatedPayments);

                UpdateCounters();
                ApplyFilterAndSearch();

                HasPaymentSuccess = true;
                PaymentSuccessMessage = $"Mensalidade de {FormattedCurrentCompetence} quitada com sucesso.";
                PaymentSuccessSummary = $"Valor: {amount.ToString("C2", PtBr)}  |  Método: {SelectedPaymentMethod}  |  Data: {paymentDate:dd/MM/yyyy HH:mm}";

                PaymentNotes = string.Empty;
            }
            catch (BusinessRuleException ex)
            {
                HasPaymentError = true;
                PaymentErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                HasPaymentError = true;
                PaymentErrorMessage = "Erro ao registrar pagamento da mensalidade: " + ex.Message;
            }
            finally
            {
                IsProcessingPayment = false;
            }
        }

        public async Task ToggleStatusAsync()
        {
            if (SelectedMonthlyCustomer == null) return;

            bool newStatus = !SelectedMonthlyCustomer.IsActive;

            try
            {
                await _monthlyService.SetStatusAsync(SelectedMonthlyCustomer.Id, newStatus);
                SelectedMonthlyCustomer.IsActive = newStatus;

                UpdateCounters();
                ApplyFilterAndSearch();
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Erro ao alterar status do mensalista: " + ex.Message;
            }
        }

        public async Task OpenNewFormAsync()
        {
            IsEditing = false;
            HasFormError = false;
            FormErrorMessage = null;

            try
            {
                var activeClients = await _clientService.GetActiveClientsAsync();

                // Filtra clientes que já possuem cadastro de mensalista
                var existingClientIds = _allCustomers.Select(c => c.ClientId).ToHashSet();
                var availableClients = activeClients.Where(c => !existingClientIds.Contains(c.Id)).ToList();

                ActiveClients.Clear();
                foreach (var client in availableClients)
                {
                    ActiveClients.Add(client);
                }

                OnPropertyChanged(nameof(HasActiveClients));

                SelectedFormClient = ActiveClients.FirstOrDefault();
                FormClientNameText = string.Empty;
                FormMonthlyAmountText = string.Empty;
                FormDueDay = 5;
                FormStartDate = DateTime.Today;
                FormNotes = string.Empty;

                IsFormOpen = true;
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Erro ao preparar cadastro de mensalista: " + ex.Message;
            }
        }

        public void OpenEditForm()
        {
            if (SelectedMonthlyCustomer == null) return;

            IsEditing = true;
            HasFormError = false;
            FormErrorMessage = null;

            FormClientNameText = SelectedMonthlyCustomer.ClientName;
            FormMonthlyAmountText = SelectedMonthlyCustomer.MonthlyAmount.ToString("N2", PtBr);
            FormDueDay = SelectedMonthlyCustomer.DueDay;
            FormStartDate = SelectedMonthlyCustomer.StartDate;
            FormNotes = SelectedMonthlyCustomer.Notes;

            IsFormOpen = true;
        }

        public void CloseForm()
        {
            IsFormOpen = false;
            HasFormError = false;
            FormErrorMessage = null;
        }

        public async Task SaveFormAsync()
        {
            HasFormError = false;
            FormErrorMessage = null;

            // Validações básicas de entrada
            if (!IsEditing && SelectedFormClient == null)
            {
                HasFormError = true;
                FormErrorMessage = "Selecione um cliente para vincular à mensalidade.";
                return;
            }

            string cleanAmount = FormMonthlyAmountText?.Trim() ?? string.Empty;
            if (!decimal.TryParse(cleanAmount, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, PtBr, out decimal monthlyAmount) &&
                !decimal.TryParse(cleanAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out monthlyAmount))
            {
                HasFormError = true;
                FormErrorMessage = "Informe um valor de mensalidade válido (ex: 150,00).";
                return;
            }

            if (monthlyAmount <= 0)
            {
                HasFormError = true;
                FormErrorMessage = "O valor da mensalidade deve ser maior que zero.";
                return;
            }

            if (FormDueDay < 1 || FormDueDay > 31)
            {
                HasFormError = true;
                FormErrorMessage = "O dia de vencimento deve estar entre 1 e 31.";
                return;
            }

            IsSavingForm = true;

            try
            {
                if (IsEditing)
                {
                    if (SelectedMonthlyCustomer == null)
                    {
                        throw new InvalidOperationException("Nenhum mensalista selecionado para edição.");
                    }

                    var updated = await _monthlyService.UpdateMonthlyCustomerAsync(
                        SelectedMonthlyCustomer.Id,
                        monthlyAmount,
                        FormDueDay,
                        FormNotes);

                    SelectedMonthlyCustomer.UpdateFrom(updated);
                    UpdateCounters();
                    ApplyFilterAndSearch();
                }
                else
                {
                    var created = await _monthlyService.CreateMonthlyCustomerAsync(
                        SelectedFormClient!.Id,
                        monthlyAmount,
                        FormDueDay,
                        FormStartDate,
                        FormNotes);

                    await LoadMonthlyCustomersAsync();
                    SelectedMonthlyCustomer = _allCustomers.FirstOrDefault(c => c.Id == created.Id);
                }

                CloseForm();
            }
            catch (BusinessRuleException ex)
            {
                HasFormError = true;
                FormErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                HasFormError = true;
                FormErrorMessage = "Erro ao salvar mensalista: " + ex.Message;
            }
            finally
            {
                IsSavingForm = false;
            }
        }
    }
}
