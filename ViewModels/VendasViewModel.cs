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
    /// ViewModel do módulo de Vendas Parceladas.
    /// Gerencia listagem, busca por cliente, visualização de detalhes e modal de criação com prévia de parcelas.
    /// </summary>
    public class VendasViewModel : ModuleViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly ISaleService _saleService;
        private readonly IClientService _clientService;
        private readonly IPaymentService _paymentService;

        private readonly List<SaleItemViewModel> _allSales = new();

        private string _searchText = string.Empty;
        private SaleItemViewModel? _selectedSale;

        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;

        // Estado do Formulário de Nova Venda
        private bool _isFormOpen;
        private Client? _selectedFormClient;
        private string _formAmountText = "100,00";
        private int _formInstallmentCount = 3;
        private DateTime _formFirstDueDate = DateTime.Today.AddMonths(1);
        private string? _formDescription;
        private string? _formNotes;
        private string? _formErrorMessage;
        private bool _hasFormError;

        public override string Title => "Vendas Parceladas";
        public override string Description => "Lançamento de novas vendas, cálculo de parcelas e divisão matemática de centavos.";
        public override string Icon => "🛒";
        public override string Category => "PRINCIPAL";

        public ObservableCollection<SaleItemViewModel> FilteredSales { get; } = new();
        public ObservableCollection<Client> ActiveClients { get; } = new();
        public ObservableCollection<InstallmentItemViewModel> PreviewInstallmentsList { get; } = new();

        public SaleItemViewModel? SelectedSale
        {
            get => _selectedSale;
            set
            {
                if (SetProperty(ref _selectedSale, value))
                {
                    OnPropertyChanged(nameof(HasSelectedSale));
                }
            }
        }

        public bool HasSelectedSale => SelectedSale != null;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplySearch();
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

        public bool IsEmpty => !IsLoading && _allSales.Count == 0;
        public bool IsSearchEmpty => !IsLoading && _allSales.Count > 0 && FilteredSales.Count == 0;

        public int TotalSalesCount => _allSales.Count;
        public string FormattedTotalSalesAmount => _allSales.Sum(s => s.TotalAmount).ToString("C2", PtBr);

        // Propriedades do Formulário de Nova Venda
        public bool IsFormOpen
        {
            get => _isFormOpen;
            set => SetProperty(ref _isFormOpen, value);
        }

        public bool HasActiveClients => ActiveClients.Count > 0;

        public Client? SelectedFormClient
        {
            get => _selectedFormClient;
            set => SetProperty(ref _selectedFormClient, value);
        }

        public string FormAmountText
        {
            get => _formAmountText;
            set
            {
                if (SetProperty(ref _formAmountText, value))
                {
                    UpdatePreview();
                }
            }
        }

        public int FormInstallmentCount
        {
            get => _formInstallmentCount;
            set
            {
                if (SetProperty(ref _formInstallmentCount, value))
                {
                    UpdatePreview();
                }
            }
        }

        public DateTime FormFirstDueDate
        {
            get => _formFirstDueDate;
            set
            {
                if (SetProperty(ref _formFirstDueDate, value))
                {
                    UpdatePreview();
                }
            }
        }

        public string? FormDescription
        {
            get => _formDescription;
            set => SetProperty(ref _formDescription, value);
        }

        public string? FormNotes
        {
            get => _formNotes;
            set => SetProperty(ref _formNotes, value);
        }

        public string? FormErrorMessage
        {
            get => _formErrorMessage;
            set => SetProperty(ref _formErrorMessage, value);
        }

        public bool HasFormError
        {
            get => _hasFormError;
            set => SetProperty(ref _hasFormError, value);
        }

        public bool HasPreview => PreviewInstallmentsList.Count > 0;
        public string FormattedPreviewTotal => PreviewInstallmentsList.Sum(p => p.Amount).ToString("C2", PtBr);

        private readonly Action<int>? _onOpenPayment;

        // Comandos
        public ICommand LoadSalesCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand OpenNewSaleCommand { get; }
        public ICommand SaveSaleCommand { get; }
        public ICommand CancelFormCommand { get; }
        public ICommand OpenPaymentCommand { get; }

        public VendasViewModel()
            : this(
                App.Services?.GetService<ISaleService>() ?? new SaleService(new CobraPixDbContext()),
                App.Services?.GetService<IClientService>() ?? new ClientService(new CobraPixDbContext()),
                App.Services?.GetService<IPaymentService>() ?? new PaymentService(new CobraPixDbContext()),
                null)
        {
        }

        public VendasViewModel(ISaleService saleService, IClientService clientService, IPaymentService paymentService, Action<int>? onOpenPayment = null)
        {
            _saleService = saleService ?? throw new ArgumentNullException(nameof(saleService));
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _onOpenPayment = onOpenPayment;

            LoadSalesCommand = new RelayCommand(async () => await LoadSalesAsync());
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            OpenNewSaleCommand = new RelayCommand(async () => await OpenNewSaleAsync());
            SaveSaleCommand = new RelayCommand(async () => await SaveSaleAsync());
            CancelFormCommand = new RelayCommand(() => IsFormOpen = false);

            OpenPaymentCommand = new RelayCommand<InstallmentItemViewModel>(item =>
            {
                if (item != null)
                {
                    _onOpenPayment?.Invoke(item.Id);
                }
            });

            _ = LoadSalesAsync();
        }

        public async Task LoadSalesAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            try
            {
                // Garante que vencimentos expirados sejam sincronizados
                await _paymentService.UpdateOverdueInstallmentsAsync();

                var sales = await _saleService.GetAllSalesAsync();

                _allSales.Clear();
                foreach (var sale in sales)
                {
                    _allSales.Add(new SaleItemViewModel(sale));
                }

                OnPropertyChanged(nameof(TotalSalesCount));
                OnPropertyChanged(nameof(FormattedTotalSalesAmount));
                ApplySearch();
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Não foi possível carregar as vendas locais: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsSearchEmpty));
            }
        }

        public void ApplySearch()
        {
            IEnumerable<SaleItemViewModel> query = _allSales;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(s =>
                    (s.ClientName != null && s.ClientName.ToLowerInvariant().Contains(term)) ||
                    (s.Description != null && s.Description.ToLowerInvariant().Contains(term)));
            }

            FilteredSales.Clear();
            foreach (var sale in query.OrderByDescending(s => s.SaleDate).ThenByDescending(s => s.Id))
            {
                FilteredSales.Add(sale);
            }

            if (SelectedSale != null && !FilteredSales.Contains(SelectedSale))
            {
                SelectedSale = FilteredSales.FirstOrDefault();
            }
            else if (SelectedSale == null)
            {
                SelectedSale = FilteredSales.FirstOrDefault();
            }

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        public async Task OpenNewSaleForClientAsync(int clientId)
        {
            await OpenNewSaleAsync();
            var target = ActiveClients.FirstOrDefault(c => c.Id == clientId);
            if (target != null)
            {
                SelectedFormClient = target;
            }
        }

        private async Task OpenNewSaleAsync()
        {
            HasFormError = false;
            FormErrorMessage = null;

            try
            {
                // Carrega somente clientes ativos para a seleção
                var clients = await _clientService.GetActiveClientsAsync();
                ActiveClients.Clear();
                foreach (var c in clients)
                {
                    ActiveClients.Add(c);
                }

                OnPropertyChanged(nameof(HasActiveClients));

                SelectedFormClient = ActiveClients.FirstOrDefault();
                FormAmountText = "100,00";
                FormInstallmentCount = 3;
                FormFirstDueDate = DateTime.Today.AddMonths(1);
                FormDescription = string.Empty;
                FormNotes = string.Empty;

                UpdatePreview();
                IsFormOpen = true;
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Erro ao carregar lista de clientes ativos: " + ex.Message;
            }
        }

        public void UpdatePreview()
        {
            PreviewInstallmentsList.Clear();

            if (!TryParseAmount(FormAmountText, out decimal totalAmount) || totalAmount <= 0 || FormInstallmentCount <= 0)
            {
                OnPropertyChanged(nameof(HasPreview));
                OnPropertyChanged(nameof(FormattedPreviewTotal));
                return;
            }

            try
            {
                // Utiliza o serviço oficial para gerar a prévia com a regra exata de centavos e datas
                var installments = _saleService.PreviewInstallments(totalAmount, FormInstallmentCount, FormFirstDueDate);

                foreach (var inst in installments)
                {
                    PreviewInstallmentsList.Add(new InstallmentItemViewModel(inst, SelectedFormClient?.Name, FormDescription));
                }
            }
            catch
            {
                // Silencia prévia caso inputs estejam em digitação
            }

            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(FormattedPreviewTotal));
        }

        public async Task SaveSaleAsync()
        {
            HasFormError = false;
            FormErrorMessage = null;

            if (SelectedFormClient == null)
            {
                HasFormError = true;
                FormErrorMessage = "Selecione um cliente ativo para criar a venda.";
                return;
            }

            if (!TryParseAmount(FormAmountText, out decimal totalAmount) || totalAmount <= 0)
            {
                HasFormError = true;
                FormErrorMessage = "Informe um valor total válido maior que zero (ex: 150,00).";
                return;
            }

            if (FormInstallmentCount <= 0)
            {
                HasFormError = true;
                FormErrorMessage = "A quantidade de parcelas deve ser de pelo menos 1.";
                return;
            }

            try
            {
                var createdSale = await _saleService.CreateSaleAsync(
                    SelectedFormClient.Id,
                    totalAmount,
                    FormInstallmentCount,
                    FormFirstDueDate,
                    FormDescription,
                    FormNotes);

                // Carrega a venda completa com relacionamentos para exibição imediata
                var loadedSale = await _saleService.GetSaleByIdAsync(createdSale.Id);
                if (loadedSale != null)
                {
                    var newVm = new SaleItemViewModel(loadedSale);
                    _allSales.Insert(0, newVm);
                    SelectedSale = newVm;
                }

                OnPropertyChanged(nameof(TotalSalesCount));
                OnPropertyChanged(nameof(FormattedTotalSalesAmount));
                ApplySearch();
                IsFormOpen = false;
            }
            catch (BusinessRuleException ex)
            {
                HasFormError = true;
                FormErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                HasFormError = true;
                FormErrorMessage = "Erro ao registrar venda: " + ex.Message;
            }
        }

        private static bool TryParseAmount(string text, out decimal amount)
        {
            amount = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var cleanText = text.Trim().Replace("R$", "").Trim();
            if (decimal.TryParse(cleanText, NumberStyles.Currency, PtBr, out amount))
                return true;

            return decimal.TryParse(cleanText, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        }
    }
}
