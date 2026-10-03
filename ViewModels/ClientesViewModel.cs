using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

using System.Globalization;

namespace CobraPix_PC.ViewModels
{
    public enum ClientFilterType
    {
        All,
        Active,
        Archived
    }

    /// <summary>
    /// ViewModel do módulo de Gestão de Clientes.
    /// Gerencia listagem, busca, filtros de status, cadastro, edição, arquivamento e reativação.
    /// </summary>
    public class ClientesViewModel : ModuleViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly IClientService _clientService;
        private readonly IWhatsAppService _whatsAppService;
        private readonly IProService _proService;
        private readonly IFinancialHistoryService _historyService;
        private readonly IReceiptPdfService _pdfService;
        private readonly List<ClientItemViewModel> _allClients = new();

        public WhatsAppPreviewViewModel WhatsAppPreview { get; }

        private bool _isPlanPro;
        private ClientFilterType _currentFilter = ClientFilterType.Active;
        private string _searchText = string.Empty;
        private ClientItemViewModel? _selectedClient;

        // Feedback de Geração de PDF de Comprovante
        private bool _isPdfSuccessOpen;
        private string _generatedPdfPath = string.Empty;
        private string _pdfFeedbackTitle = string.Empty;
        private string _pdfFeedbackMessage = string.Empty;

        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;

        // Histórico Financeiro do Cliente Selecionado
        private decimal _clientTotalPaid;
        private bool _isLoadingClientHistory;

        // Controle do Formulário (Criar / Editar)
        private bool _isFormOpen;
        private bool _isEditing;
        private int _formEditingId;
        private string _formName = string.Empty;
        private string? _formPhone;
        private string? _formNotes;
        private string? _formErrorMessage;
        private bool _hasFormError;

        // Controle do Diálogo de Confirmação
        private bool _isConfirmOpen;
        private string _confirmTitle = string.Empty;
        private string _confirmMessage = string.Empty;
        private Func<Task>? _onConfirmAction;

        public override string Title => "Gestão de Clientes";
        public override string Description => "Cadastro, consulta, histórico financeiro e controle de clientes ativos.";
        public override string Icon => "👥";
        public override string Category => "PRINCIPAL";

        public ObservableCollection<ClientItemViewModel> FilteredClients { get; } = new();
        public ObservableCollection<FinancialTransactionItemViewModel> ClientFinancialHistory { get; } = new();

        public decimal ClientTotalPaid
        {
            get => _clientTotalPaid;
            private set
            {
                if (SetProperty(ref _clientTotalPaid, value))
                {
                    OnPropertyChanged(nameof(FormattedClientTotalPaid));
                }
            }
        }

        public string FormattedClientTotalPaid => ClientTotalPaid.ToString("C2", PtBr);

        public bool HasClientFinancialHistory => ClientFinancialHistory.Count > 0;

        public bool IsLoadingClientHistory
        {
            get => _isLoadingClientHistory;
            private set => SetProperty(ref _isLoadingClientHistory, value);
        }

        public ClientItemViewModel? SelectedClient
        {
            get => _selectedClient;
            set
            {
                if (SetProperty(ref _selectedClient, value))
                {
                    OnPropertyChanged(nameof(HasSelectedClient));
                    OnPropertyChanged(nameof(CanArchiveSelected));
                    OnPropertyChanged(nameof(CanReactivateSelected));
                    _ = LoadClientFinancialHistoryAsync();
                }
            }
        }

        public bool HasSelectedClient => SelectedClient != null;
        public bool CanArchiveSelected => SelectedClient != null && !SelectedClient.IsArchived;
        public bool CanReactivateSelected => SelectedClient != null && SelectedClient.IsArchived;

        public ClientFilterType CurrentFilter
        {
            get => _currentFilter;
            set
            {
                if (SetProperty(ref _currentFilter, value))
                {
                    ApplyFilterAndSearch();
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(IsFilterArchived));
                }
            }
        }

        public bool IsFilterAll => CurrentFilter == ClientFilterType.All;
        public bool IsFilterActive => CurrentFilter == ClientFilterType.Active;
        public bool IsFilterArchived => CurrentFilter == ClientFilterType.Archived;

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

        public bool IsEmpty => !IsLoading && _allClients.Count == 0;
        public bool IsSearchEmpty => !IsLoading && _allClients.Count > 0 && FilteredClients.Count == 0;

        public int TotalCount => _allClients.Count;
        public int ActiveCount => _allClients.Count(c => !c.IsArchived);
        public int ArchivedCount => _allClients.Count(c => c.IsArchived);

        public bool IsPlanPro
        {
            get => _isPlanPro;
            set
            {
                if (SetProperty(ref _isPlanPro, value))
                {
                    OnPropertyChanged(nameof(ActiveClientsCounterText));
                }
            }
        }

        public string ActiveClientsCounterText => IsPlanPro
            ? $"Clientes ativos: {ActiveCount} · PRO ilimitado"
            : $"Clientes ativos: {ActiveCount} / {ProService.FreeMaxActiveClients}";

        // Propriedades do Formulário
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
                    OnPropertyChanged(nameof(FormButtonText));
                }
            }
        }

        public string FormTitle => IsEditing ? "Editar Cliente" : "Novo Cliente";
        public string FormButtonText => IsEditing ? "Salvar Alterações" : "Cadastrar Cliente";

        public string FormName
        {
            get => _formName;
            set => SetProperty(ref _formName, value);
        }

        public string? FormPhone
        {
            get => _formPhone;
            set => SetProperty(ref _formPhone, value);
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

        // Propriedades do Diálogo de Confirmação
        public bool IsConfirmOpen
        {
            get => _isConfirmOpen;
            set => SetProperty(ref _isConfirmOpen, value);
        }

        public string ConfirmTitle
        {
            get => _confirmTitle;
            set => SetProperty(ref _confirmTitle, value);
        }

        public string ConfirmMessage
        {
            get => _confirmMessage;
            set => SetProperty(ref _confirmMessage, value);
        }

        // Feedback de Geração de PDF de Comprovante
        public bool IsPdfSuccessOpen
        {
            get => _isPdfSuccessOpen;
            set => SetProperty(ref _isPdfSuccessOpen, value);
        }

        public string GeneratedPdfPath
        {
            get => _generatedPdfPath;
            set => SetProperty(ref _generatedPdfPath, value);
        }

        public string PdfFeedbackTitle
        {
            get => _pdfFeedbackTitle;
            set => SetProperty(ref _pdfFeedbackTitle, value);
        }

        public string PdfFeedbackMessage
        {
            get => _pdfFeedbackMessage;
            set => SetProperty(ref _pdfFeedbackMessage, value);
        }

        public bool CanOpenPdf => !string.IsNullOrWhiteSpace(GeneratedPdfPath);

        // Comandos
        public ICommand LoadClientsCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand OpenNewClientCommand { get; }
        public ICommand OpenEditClientCommand { get; }
        public ICommand SaveClientCommand { get; }
        public ICommand CancelFormCommand { get; }
        public ICommand RequestArchiveCommand { get; }
        public ICommand ReactivateCommand { get; }
        public ICommand ConfirmDialogCommand { get; }
        public ICommand CancelDialogCommand { get; }
        public ICommand OpenWhatsAppCommand { get; }
        public ICommand GeneratePdfReceiptCommand { get; }
        public ICommand OpenPdfCommand { get; }
        public ICommand ClosePdfFeedbackCommand { get; }

        public ClientesViewModel()
            : this(
                App.Services?.GetService<IClientService>() ?? new ClientService(new CobraPixDbContext()),
                App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService(),
                App.Services?.GetService<IProService>() ?? new ProService(new CobraPixDbContext()),
                App.Services?.GetService<IFinancialHistoryService>() ?? new FinancialHistoryService(new CobraPixDbContext()),
                App.Services?.GetService<IReceiptPdfService>() ?? new ReceiptPdfService())
        {
        }

        public ClientesViewModel(IClientService clientService)
            : this(
                clientService,
                App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService(),
                App.Services?.GetService<IProService>() ?? new ProService(new CobraPixDbContext()),
                null,
                null)
        {
        }

        public ClientesViewModel(IClientService clientService, IWhatsAppService whatsAppService)
            : this(
                clientService,
                whatsAppService,
                App.Services?.GetService<IProService>() ?? new ProService(new CobraPixDbContext()),
                null,
                null)
        {
        }

        public ClientesViewModel(IClientService clientService, IWhatsAppService whatsAppService, IProService? proService)
            : this(clientService, whatsAppService, proService, null, null)
        {
        }

        public ClientesViewModel(
            IClientService clientService,
            IWhatsAppService whatsAppService,
            IProService? proService,
            IFinancialHistoryService? historyService)
            : this(clientService, whatsAppService, proService, historyService, null)
        {
        }

        public ClientesViewModel(
            IClientService clientService,
            IWhatsAppService whatsAppService,
            IProService? proService,
            IFinancialHistoryService? historyService,
            IReceiptPdfService? pdfService)
        {
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _whatsAppService = whatsAppService ?? throw new ArgumentNullException(nameof(whatsAppService));
            _proService = proService ?? App.Services?.GetService<IProService>() ?? new ProService(new CobraPixDbContext());
            _historyService = historyService ?? App.Services?.GetService<IFinancialHistoryService>() ?? new FinancialHistoryService(new CobraPixDbContext());
            _pdfService = pdfService ?? App.Services?.GetService<IReceiptPdfService>() ?? new ReceiptPdfService();

            _proService.PlanChanged += (s, e) => { _ = UpdatePlanCountersAsync(); };

            WhatsAppPreview = new WhatsAppPreviewViewModel(_whatsAppService);

            LoadClientsCommand = new RelayCommand(async () => await LoadClientsAsync());
            SetFilterCommand = new RelayCommand<string>(filter =>
            {
                if (Enum.TryParse<ClientFilterType>(filter, true, out var result))
                {
                    CurrentFilter = result;
                }
            });
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

            OpenNewClientCommand = new RelayCommand(OpenNewClient);
            OpenEditClientCommand = new RelayCommand(OpenEditClient);
            SaveClientCommand = new RelayCommand(async () => await SaveClientAsync());
            CancelFormCommand = new RelayCommand(() => IsFormOpen = false);

            RequestArchiveCommand = new RelayCommand(RequestArchive);
            ReactivateCommand = new RelayCommand(async () => await ReactivateSelectedAsync());

            ConfirmDialogCommand = new RelayCommand(async () =>
            {
                if (_onConfirmAction != null)
                {
                    await _onConfirmAction.Invoke();
                }
                IsConfirmOpen = false;
            });
            CancelDialogCommand = new RelayCommand(() => IsConfirmOpen = false);

            OpenWhatsAppCommand = new RelayCommand(param => OpenWhatsApp(param as ClientItemViewModel ?? SelectedClient));
            GeneratePdfReceiptCommand = new RelayCommand<FinancialTransactionItemViewModel>(item => GeneratePdfReceipt(item));
            OpenPdfCommand = new RelayCommand(OpenGeneratedPdf);
            ClosePdfFeedbackCommand = new RelayCommand(ClosePdfFeedback);

            _ = LoadClientsAsync();
        }

        private void OpenWhatsApp(ClientItemViewModel? client)
        {
            if (client == null) return;

            string defaultMsg = _whatsAppService.GenerateSimpleContactMessage(client.Name);

            WhatsAppPreview.Open(
                client.Name,
                client.Phone,
                "👥 Contato",
                0m,
                defaultMsg);
        }

        public async Task LoadClientFinancialHistoryAsync()
        {
            if (SelectedClient == null)
            {
                ClientFinancialHistory.Clear();
                ClientTotalPaid = 0m;
                OnPropertyChanged(nameof(HasClientFinancialHistory));
                return;
            }

            IsLoadingClientHistory = true;
            try
            {
                var transactions = await _historyService.GetTransactionsByClientAsync(SelectedClient.Id);
                ClientFinancialHistory.Clear();
                foreach (var tx in transactions)
                {
                    ClientFinancialHistory.Add(new FinancialTransactionItemViewModel(tx));
                }

                ClientTotalPaid = transactions.Sum(t => t.Amount);
            }
            catch
            {
                ClientFinancialHistory.Clear();
                ClientTotalPaid = 0m;
            }
            finally
            {
                IsLoadingClientHistory = false;
                OnPropertyChanged(nameof(HasClientFinancialHistory));
            }
        }

        public async Task LoadClientsAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            try
            {
                var clients = await _clientService.GetAllClientsAsync();

                _allClients.Clear();
                foreach (var c in clients)
                {
                    _allClients.Add(new ClientItemViewModel(c));
                }

                UpdateCounters();
                await UpdatePlanCountersAsync();
                ApplyFilterAndSearch();
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Não foi possível carregar os clientes locais: " + ex.Message;
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
            IEnumerable<ClientItemViewModel> query = _allClients;

            // Filtro de status
            switch (CurrentFilter)
            {
                case ClientFilterType.Active:
                    query = query.Where(c => !c.IsArchived);
                    break;
                case ClientFilterType.Archived:
                    query = query.Where(c => c.IsArchived);
                    break;
                case ClientFilterType.All:
                default:
                    break;
            }

            // Filtro de busca textual (nome ou telefone)
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(c =>
                    (c.Name != null && c.Name.ToLowerInvariant().Contains(term)) ||
                    (c.Phone != null && c.Phone.Contains(term)));
            }

            FilteredClients.Clear();
            foreach (var client in query.OrderBy(c => c.Name))
            {
                FilteredClients.Add(client);
            }

            // Mantém ou ajusta seleção
            if (SelectedClient != null && !FilteredClients.Contains(SelectedClient))
            {
                SelectedClient = FilteredClients.FirstOrDefault();
            }
            else if (SelectedClient == null)
            {
                SelectedClient = FilteredClients.FirstOrDefault();
            }

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        public async Task UpdatePlanCountersAsync()
        {
            var isPro = await _proService.IsProAsync();
            IsPlanPro = isPro;
            OnPropertyChanged(nameof(IsPlanPro));
            OnPropertyChanged(nameof(ActiveClientsCounterText));
        }

        private void UpdateCounters()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(ArchivedCount));
            OnPropertyChanged(nameof(ActiveClientsCounterText));
            _ = UpdatePlanCountersAsync();
        }

        private void OpenNewClient()
        {
            IsEditing = false;
            _formEditingId = 0;
            FormName = string.Empty;
            FormPhone = string.Empty;
            FormNotes = string.Empty;
            HasFormError = false;
            FormErrorMessage = null;

            if (!IsPlanPro)
            {
                if (ActiveCount >= ProService.FreeMaxActiveClients)
                {
                    HasFormError = true;
                    FormErrorMessage = "Seu plano FREE permite até 10 clientes ativos. Assine o PRO para cadastrar mais clientes.";
                }
                else if ((ActiveCount + ArchivedCount) >= ProService.FreeMaxActiveClients)
                {
                    HasFormError = true;
                    FormErrorMessage = "Seu plano FREE atingiu a capacidade máxima de 10 clientes. Assine o PRO para cadastrar mais clientes ou reative seus clientes arquivados.";
                }
            }

            IsFormOpen = true;
        }

        private void OpenEditClient()
        {
            if (SelectedClient == null) return;

            IsEditing = true;
            _formEditingId = SelectedClient.Id;
            FormName = SelectedClient.Name;
            FormPhone = SelectedClient.Phone;
            FormNotes = SelectedClient.Notes;
            HasFormError = false;
            FormErrorMessage = null;
            IsFormOpen = true;
        }

        public async Task SaveClientAsync()
        {
            HasFormError = false;
            FormErrorMessage = null;

            if (string.IsNullOrWhiteSpace(FormName))
            {
                HasFormError = true;
                FormErrorMessage = "O nome do cliente é obrigatório.";
                return;
            }

            if (!IsEditing)
            {
                var limitStatus = await _proService.CheckClientLimitAsync();
                if (!limitStatus.CanAddClient)
                {
                    HasFormError = true;
                    FormErrorMessage = limitStatus.LimitMessage;
                    return;
                }
            }

            try
            {
                if (IsEditing)
                {
                    var updated = await _clientService.UpdateClientAsync(_formEditingId, FormName, FormPhone, FormNotes);
                    var item = _allClients.FirstOrDefault(c => c.Id == _formEditingId);
                    if (item != null)
                    {
                        item.UpdateFrom(updated);
                    }
                }
                else
                {
                    var created = await _clientService.CreateClientAsync(FormName, FormPhone, FormNotes);
                    var newItem = new ClientItemViewModel(created);
                    _allClients.Add(newItem);
                    SelectedClient = newItem;
                }

                UpdateCounters();
                ApplyFilterAndSearch();
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
                FormErrorMessage = "Erro ao salvar cliente: " + ex.Message;
            }
        }

        private void RequestArchive()
        {
            if (SelectedClient == null || SelectedClient.IsArchived) return;

            ConfirmTitle = "Confirmar Arquivamento";
            ConfirmMessage = $"Deseja arquivar o cliente '{SelectedClient.Name}'?\n\nO cliente ficará oculto da lista ativa, mas todo o histórico financeiro (vendas, parcelas e pagamentos) permanecerá integralmente preservado.";
            _onConfirmAction = async () =>
            {
                if (SelectedClient == null) return;
                try
                {
                    await _clientService.ArchiveClientAsync(SelectedClient.Id);
                    SelectedClient.IsArchived = true;
                    UpdateCounters();
                    ApplyFilterAndSearch();
                }
                catch (Exception ex)
                {
                    HasError = true;
                    ErrorMessage = "Erro ao arquivar cliente: " + ex.Message;
                }
            };
            IsConfirmOpen = true;
        }

        public async Task ReactivateSelectedAsync()
        {
            if (SelectedClient == null || !SelectedClient.IsArchived) return;

            var canReactivate = await _proService.CanReactivateClientAsync(SelectedClient.Id);
            if (!canReactivate)
            {
                HasError = true;
                ErrorMessage = "Seu plano FREE permite até 10 clientes ativos. Arquive outro cliente ou assine o PRO para reativar este cliente.";
                return;
            }

            try
            {
                await _clientService.ReactivateClientAsync(SelectedClient.Id);
                SelectedClient.IsArchived = false;
                UpdateCounters();
                ApplyFilterAndSearch();
            }
            catch (BusinessRuleException ex)
            {
                HasError = true;
                ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Erro ao reativar cliente: " + ex.Message;
            }
        }

        public string? GeneratePdfReceipt(FinancialTransactionItemViewModel? item, string? customDestinationPath = null)
        {
            if (item == null) return null;
            return GeneratePdfReceiptForTransaction(item.Transaction, customDestinationPath);
        }

        public string? GeneratePdfReceiptForTransaction(FinancialTransaction transaction, string? customDestinationPath = null)
        {
            if (transaction == null) return null;

            try
            {
                string targetPath;
                if (!string.IsNullOrWhiteSpace(customDestinationPath))
                {
                    targetPath = customDestinationPath;
                }
                else
                {
                    var sfd = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "Documento PDF (*.pdf)|*.pdf",
                        DefaultExt = ".pdf",
                        FileName = _pdfService.GetSuggestedFileName(transaction),
                        Title = "Salvar Comprovante de Pagamento em PDF"
                    };

                    bool? result = sfd.ShowDialog();
                    if (result != true)
                    {
                        return null;
                    }
                    targetPath = sfd.FileName;
                }

                string savedPath = _pdfService.GenerateAndSaveReceipt(transaction, targetPath);
                GeneratedPdfPath = savedPath;
                PdfFeedbackTitle = "Comprovante Gerado com Sucesso!";
                PdfFeedbackMessage = $"O comprovante em PDF foi gerado e salvo em:\n{savedPath}";
                IsPdfSuccessOpen = true;
                return savedPath;
            }
            catch (Exception ex)
            {
                PdfFeedbackTitle = "Erro ao Gerar Comprovante";
                PdfFeedbackMessage = $"Não foi possível gerar o comprovante em PDF: {ex.Message}";
                GeneratedPdfPath = string.Empty;
                IsPdfSuccessOpen = true;
                return null;
            }
        }

        public void OpenGeneratedPdf()
        {
            if (!string.IsNullOrWhiteSpace(GeneratedPdfPath) && System.IO.File.Exists(GeneratedPdfPath))
            {
                _pdfService.OpenPdf(GeneratedPdfPath);
            }
            IsPdfSuccessOpen = false;
        }

        public void ClosePdfFeedback()
        {
            IsPdfSuccessOpen = false;
        }
    }
}
