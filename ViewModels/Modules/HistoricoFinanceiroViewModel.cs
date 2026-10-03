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

namespace CobraPix_PC.ViewModels.Modules
{
    public enum HistoryPeriodPreset
    {
        Month,
        Today,
        Last7Days,
        Last30Days,
        CurrentYear,
        All,
        Custom
    }

    public enum HistoryTypeFilter
    {
        All,
        Installments,
        Monthly
    }

    public enum HistoryMethodFilter
    {
        All,
        Pix,
        Cash,
        Card,
        Other,
        None
    }

    /// <summary>
    /// ViewModel do módulo de Histórico Financeiro / Livro Caixa (Entradas).
    /// Consolida todos os recebimentos efetivos de parcelas e mensalidades com filtros avançados de período,
    /// tipo e método de pagamento, com comportamento idêntico ao CashFlowScreen do CobraPix Android.
    /// </summary>
    public class HistoricoFinanceiroViewModel : ModuleViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly IFinancialHistoryService _historyService;
        private readonly IWhatsAppService _whatsAppService;
        private readonly IReceiptPdfService _pdfService;
        private readonly List<FinancialTransactionItemViewModel> _allTransactions = new();

        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;

        // Feedback de Geração de PDF
        private bool _isPdfSuccessOpen;
        private string _generatedPdfPath = string.Empty;
        private string _pdfFeedbackTitle = string.Empty;
        private string _pdfFeedbackMessage = string.Empty;

        // Filtro de Mês/Ano (Navegação Android)
        private int _selectedMonth = DateTime.Today.Month;
        private int _selectedYear = DateTime.Today.Year;

        // Presets e Período Personalizado
        private HistoryPeriodPreset _periodPreset = HistoryPeriodPreset.Month;
        private DateTime _customStartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime _customEndDate = DateTime.Today;

        // Filtros de Tipo e Método
        private HistoryTypeFilter _typeFilter = HistoryTypeFilter.All;
        private HistoryMethodFilter _methodFilter = HistoryMethodFilter.All;
        private string _searchText = string.Empty;

        // Métricas Calculadas
        private decimal _totalReceived;
        private decimal _installmentsTotal;
        private int _installmentsCount;
        private decimal _monthlyTotal;
        private int _monthlyCount;
        private int _totalTransactionsCount;

        public override string Title => "Histórico Financeiro";
        public override string Description => "Registro detalhado de entradas e valores recebidos de parcelas e mensalidades.";
        public override string Icon => "📜";
        public override string Category => "CONSULTAS";

        public ObservableCollection<FinancialTransactionItemViewModel> FilteredTransactions { get; } = new();

        public WhatsAppPreviewViewModel WhatsAppPreview { get; }

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

        public bool IsEmpty => !IsLoading && _allTransactions.Count == 0;
        public bool IsSearchEmpty => !IsLoading && _allTransactions.Count > 0 && FilteredTransactions.Count == 0;

        // Mês e Ano Selecionado
        public int SelectedMonth
        {
            get => _selectedMonth;
            set
            {
                if (SetProperty(ref _selectedMonth, value))
                {
                    OnPropertyChanged(nameof(MonthYearDisplayName));
                    ApplyFilters();
                }
            }
        }

        public int SelectedYear
        {
            get => _selectedYear;
            set
            {
                if (SetProperty(ref _selectedYear, value))
                {
                    OnPropertyChanged(nameof(MonthYearDisplayName));
                    ApplyFilters();
                }
            }
        }

        public string MonthYearDisplayName => $"{GetMonthName(SelectedMonth)} {SelectedYear}";

        // Preset de Período
        public HistoryPeriodPreset PeriodPreset
        {
            get => _periodPreset;
            set
            {
                if (SetProperty(ref _periodPreset, value))
                {
                    OnPropertyChanged(nameof(IsPeriodMonth));
                    OnPropertyChanged(nameof(IsPeriodToday));
                    OnPropertyChanged(nameof(IsPeriod7Days));
                    OnPropertyChanged(nameof(IsPeriod30Days));
                    OnPropertyChanged(nameof(IsPeriodCurrentYear));
                    OnPropertyChanged(nameof(IsPeriodAll));
                    OnPropertyChanged(nameof(IsPeriodCustom));
                    ApplyFilters();
                }
            }
        }

        public bool IsPeriodMonth => PeriodPreset == HistoryPeriodPreset.Month;
        public bool IsPeriodToday => PeriodPreset == HistoryPeriodPreset.Today;
        public bool IsPeriod7Days => PeriodPreset == HistoryPeriodPreset.Last7Days;
        public bool IsPeriod30Days => PeriodPreset == HistoryPeriodPreset.Last30Days;
        public bool IsPeriodCurrentYear => PeriodPreset == HistoryPeriodPreset.CurrentYear;
        public bool IsPeriodAll => PeriodPreset == HistoryPeriodPreset.All;
        public bool IsPeriodCustom => PeriodPreset == HistoryPeriodPreset.Custom;

        public DateTime CustomStartDate
        {
            get => _customStartDate;
            set
            {
                if (SetProperty(ref _customStartDate, value))
                {
                    if (PeriodPreset == HistoryPeriodPreset.Custom)
                        ApplyFilters();
                }
            }
        }

        public DateTime CustomEndDate
        {
            get => _customEndDate;
            set
            {
                if (SetProperty(ref _customEndDate, value))
                {
                    if (PeriodPreset == HistoryPeriodPreset.Custom)
                        ApplyFilters();
                }
            }
        }

        // Filtro de Tipo
        public HistoryTypeFilter TypeFilter
        {
            get => _typeFilter;
            set
            {
                if (SetProperty(ref _typeFilter, value))
                {
                    OnPropertyChanged(nameof(IsTypeAll));
                    OnPropertyChanged(nameof(IsTypeInstallments));
                    OnPropertyChanged(nameof(IsTypeMonthly));
                    ApplyFilters();
                }
            }
        }

        public bool IsTypeAll => TypeFilter == HistoryTypeFilter.All;
        public bool IsTypeInstallments => TypeFilter == HistoryTypeFilter.Installments;
        public bool IsTypeMonthly => TypeFilter == HistoryTypeFilter.Monthly;

        // Filtro de Método
        public HistoryMethodFilter MethodFilter
        {
            get => _methodFilter;
            set
            {
                if (SetProperty(ref _methodFilter, value))
                {
                    OnPropertyChanged(nameof(IsMethodAll));
                    OnPropertyChanged(nameof(IsMethodPix));
                    OnPropertyChanged(nameof(IsMethodCash));
                    OnPropertyChanged(nameof(IsMethodCard));
                    OnPropertyChanged(nameof(IsMethodOther));
                    ApplyFilters();
                }
            }
        }

        public bool IsMethodAll => MethodFilter == HistoryMethodFilter.All;
        public bool IsMethodPix => MethodFilter == HistoryMethodFilter.Pix;
        public bool IsMethodCash => MethodFilter == HistoryMethodFilter.Cash;
        public bool IsMethodCard => MethodFilter == HistoryMethodFilter.Card;
        public bool IsMethodOther => MethodFilter == HistoryMethodFilter.Other;

        // Busca
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    OnPropertyChanged(nameof(HasSearchText));
                    ApplyFilters();
                }
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

        // Métricas
        public decimal TotalReceived
        {
            get => _totalReceived;
            private set
            {
                if (SetProperty(ref _totalReceived, value))
                    OnPropertyChanged(nameof(FormattedTotalReceived));
            }
        }

        public string FormattedTotalReceived => TotalReceived.ToString("C2", PtBr);

        public decimal InstallmentsTotal
        {
            get => _installmentsTotal;
            private set
            {
                if (SetProperty(ref _installmentsTotal, value))
                    OnPropertyChanged(nameof(FormattedInstallmentsTotal));
            }
        }

        public string FormattedInstallmentsTotal => InstallmentsTotal.ToString("C2", PtBr);

        public int InstallmentsCount
        {
            get => _installmentsCount;
            private set => SetProperty(ref _installmentsCount, value);
        }

        public decimal MonthlyTotal
        {
            get => _monthlyTotal;
            private set
            {
                if (SetProperty(ref _monthlyTotal, value))
                    OnPropertyChanged(nameof(FormattedMonthlyTotal));
            }
        }

        public string FormattedMonthlyTotal => MonthlyTotal.ToString("C2", PtBr);

        public int MonthlyCount
        {
            get => _monthlyCount;
            private set => SetProperty(ref _monthlyCount, value);
        }

        public int TotalTransactionsCount
        {
            get => _totalTransactionsCount;
            private set => SetProperty(ref _totalTransactionsCount, value);
        }

        // Comandos
        // Feedback de Geração de PDF
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
        public ICommand LoadTransactionsCommand { get; }
        public ICommand PreviousMonthCommand { get; }
        public ICommand NextMonthCommand { get; }
        public ICommand SetPeriodPresetCommand { get; }
        public ICommand SetTypeFilterCommand { get; }
        public ICommand SetMethodFilterCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand OpenWhatsAppReceiptCommand { get; }
        public ICommand GeneratePdfReceiptCommand { get; }
        public ICommand OpenPdfCommand { get; }
        public ICommand ClosePdfFeedbackCommand { get; }

        public HistoricoFinanceiroViewModel()
            : this(
                App.Services?.GetService<IFinancialHistoryService>() ?? new FinancialHistoryService(new CobraPixDbContext()),
                App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService(),
                App.Services?.GetService<IReceiptPdfService>() ?? new ReceiptPdfService())
        {
        }

        public HistoricoFinanceiroViewModel(
            IFinancialHistoryService historyService,
            IWhatsAppService? whatsAppService = null,
            IReceiptPdfService? pdfService = null)
        {
            _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
            _whatsAppService = whatsAppService ?? App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService();
            _pdfService = pdfService ?? App.Services?.GetService<IReceiptPdfService>() ?? new ReceiptPdfService();

            WhatsAppPreview = new WhatsAppPreviewViewModel(_whatsAppService);

            LoadTransactionsCommand = new RelayCommand(async () => await LoadTransactionsAsync());
            PreviousMonthCommand = new RelayCommand(NavigatePreviousMonth);
            NextMonthCommand = new RelayCommand(NavigateNextMonth);

            SetPeriodPresetCommand = new RelayCommand<string>(presetStr =>
            {
                if (Enum.TryParse<HistoryPeriodPreset>(presetStr, true, out var preset))
                {
                    PeriodPreset = preset;
                }
            });

            SetTypeFilterCommand = new RelayCommand<string>(typeStr =>
            {
                if (Enum.TryParse<HistoryTypeFilter>(typeStr, true, out var type))
                {
                    TypeFilter = type;
                }
            });

            SetMethodFilterCommand = new RelayCommand<string>(methodStr =>
            {
                if (Enum.TryParse<HistoryMethodFilter>(methodStr, true, out var method))
                {
                    MethodFilter = method;
                }
            });

            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            ResetFiltersCommand = new RelayCommand(ResetFilters);
            OpenWhatsAppReceiptCommand = new RelayCommand<FinancialTransactionItemViewModel>(OpenWhatsAppReceipt);
            GeneratePdfReceiptCommand = new RelayCommand<FinancialTransactionItemViewModel>(item => GeneratePdfReceipt(item));
            OpenPdfCommand = new RelayCommand(OpenGeneratedPdf);
            ClosePdfFeedbackCommand = new RelayCommand(ClosePdfFeedback);

            _ = LoadTransactionsAsync();
        }

        public void NavigatePreviousMonth()
        {
            if (SelectedMonth == 1)
            {
                _selectedMonth = 12;
                _selectedYear--;
            }
            else
            {
                _selectedMonth--;
            }

            // Garante que o preset esteja em Mês
            if (PeriodPreset != HistoryPeriodPreset.Month)
            {
                _periodPreset = HistoryPeriodPreset.Month;
                OnPropertyChanged(nameof(PeriodPreset));
                OnPropertyChanged(nameof(IsPeriodMonth));
                OnPropertyChanged(nameof(IsPeriodToday));
                OnPropertyChanged(nameof(IsPeriod7Days));
                OnPropertyChanged(nameof(IsPeriod30Days));
                OnPropertyChanged(nameof(IsPeriodCurrentYear));
                OnPropertyChanged(nameof(IsPeriodAll));
                OnPropertyChanged(nameof(IsPeriodCustom));
            }

            OnPropertyChanged(nameof(SelectedMonth));
            OnPropertyChanged(nameof(SelectedYear));
            OnPropertyChanged(nameof(MonthYearDisplayName));
            ApplyFilters();
        }

        public void NavigateNextMonth()
        {
            if (SelectedMonth == 12)
            {
                _selectedMonth = 1;
                _selectedYear++;
            }
            else
            {
                _selectedMonth++;
            }

            // Garante que o preset esteja em Mês
            if (PeriodPreset != HistoryPeriodPreset.Month)
            {
                _periodPreset = HistoryPeriodPreset.Month;
                OnPropertyChanged(nameof(PeriodPreset));
                OnPropertyChanged(nameof(IsPeriodMonth));
                OnPropertyChanged(nameof(IsPeriodToday));
                OnPropertyChanged(nameof(IsPeriod7Days));
                OnPropertyChanged(nameof(IsPeriod30Days));
                OnPropertyChanged(nameof(IsPeriodCurrentYear));
                OnPropertyChanged(nameof(IsPeriodAll));
                OnPropertyChanged(nameof(IsPeriodCustom));
            }

            OnPropertyChanged(nameof(SelectedMonth));
            OnPropertyChanged(nameof(SelectedYear));
            OnPropertyChanged(nameof(MonthYearDisplayName));
            ApplyFilters();
        }

        public void ResetFilters()
        {
            _selectedMonth = DateTime.Today.Month;
            _selectedYear = DateTime.Today.Year;
            _periodPreset = HistoryPeriodPreset.Month;
            _typeFilter = HistoryTypeFilter.All;
            _methodFilter = HistoryMethodFilter.All;
            _searchText = string.Empty;

            OnPropertyChanged(nameof(SelectedMonth));
            OnPropertyChanged(nameof(SelectedYear));
            OnPropertyChanged(nameof(MonthYearDisplayName));
            OnPropertyChanged(nameof(PeriodPreset));
            OnPropertyChanged(nameof(IsPeriodMonth));
            OnPropertyChanged(nameof(IsPeriodToday));
            OnPropertyChanged(nameof(IsPeriod7Days));
            OnPropertyChanged(nameof(IsPeriod30Days));
            OnPropertyChanged(nameof(IsPeriodCurrentYear));
            OnPropertyChanged(nameof(IsPeriodAll));
            OnPropertyChanged(nameof(IsPeriodCustom));
            OnPropertyChanged(nameof(TypeFilter));
            OnPropertyChanged(nameof(IsTypeAll));
            OnPropertyChanged(nameof(IsTypeInstallments));
            OnPropertyChanged(nameof(IsTypeMonthly));
            OnPropertyChanged(nameof(MethodFilter));
            OnPropertyChanged(nameof(IsMethodAll));
            OnPropertyChanged(nameof(IsMethodPix));
            OnPropertyChanged(nameof(IsMethodCash));
            OnPropertyChanged(nameof(IsMethodCard));
            OnPropertyChanged(nameof(IsMethodOther));
            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(HasSearchText));

            ApplyFilters();
        }

        public async Task LoadTransactionsAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            try
            {
                var transactions = await _historyService.GetAllTransactionsAsync();

                _allTransactions.Clear();
                foreach (var tx in transactions)
                {
                    _allTransactions.Add(new FinancialTransactionItemViewModel(tx));
                }

                ApplyFilters();
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Não foi possível carregar o histórico financeiro: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsSearchEmpty));
            }
        }

        public void ApplyFilters()
        {
            IEnumerable<FinancialTransactionItemViewModel> query = _allTransactions;

            // 1. Filtro de Período
            var today = DateTime.Today;
            switch (PeriodPreset)
            {
                case HistoryPeriodPreset.Month:
                    var startMonth = new DateTime(SelectedYear, SelectedMonth, 1);
                    var endMonth = startMonth.AddMonths(1).AddTicks(-1);
                    query = query.Where(t => t.PaymentDate >= startMonth && t.PaymentDate <= endMonth);
                    break;

                case HistoryPeriodPreset.Today:
                    var startToday = today;
                    var endToday = today.AddDays(1).AddTicks(-1);
                    query = query.Where(t => t.PaymentDate >= startToday && t.PaymentDate <= endToday);
                    break;

                case HistoryPeriodPreset.Last7Days:
                    var start7 = today.AddDays(-6);
                    var end7 = today.AddDays(1).AddTicks(-1);
                    query = query.Where(t => t.PaymentDate >= start7 && t.PaymentDate <= end7);
                    break;

                case HistoryPeriodPreset.Last30Days:
                    var start30 = today.AddDays(-29);
                    var end30 = today.AddDays(1).AddTicks(-1);
                    query = query.Where(t => t.PaymentDate >= start30 && t.PaymentDate <= end30);
                    break;

                case HistoryPeriodPreset.CurrentYear:
                    var startYear = new DateTime(today.Year, 1, 1);
                    var endYear = new DateTime(today.Year, 12, 31, 23, 59, 59, 999);
                    query = query.Where(t => t.PaymentDate >= startYear && t.PaymentDate <= endYear);
                    break;

                case HistoryPeriodPreset.Custom:
                    var startCustom = CustomStartDate.Date;
                    var endCustom = CustomEndDate.Date.AddDays(1).AddTicks(-1);
                    query = query.Where(t => t.PaymentDate >= startCustom && t.PaymentDate <= endCustom);
                    break;

                case HistoryPeriodPreset.All:
                default:
                    break;
            }

            // 2. Filtro de Tipo
            switch (TypeFilter)
            {
                case HistoryTypeFilter.Installments:
                    query = query.Where(t => t.IsInstallment);
                    break;
                case HistoryTypeFilter.Monthly:
                    query = query.Where(t => t.IsMonthly);
                    break;
                case HistoryTypeFilter.All:
                default:
                    break;
            }

            // 3. Filtro de Método
            switch (MethodFilter)
            {
                case HistoryMethodFilter.Pix:
                    query = query.Where(t => t.PaymentMethod == PaymentMethod.Pix);
                    break;
                case HistoryMethodFilter.Cash:
                    query = query.Where(t => t.PaymentMethod == PaymentMethod.Cash);
                    break;
                case HistoryMethodFilter.Card:
                    query = query.Where(t => t.PaymentMethod == PaymentMethod.Card);
                    break;
                case HistoryMethodFilter.Other:
                    query = query.Where(t => t.PaymentMethod == PaymentMethod.Other);
                    break;
                case HistoryMethodFilter.None:
                    query = query.Where(t => t.PaymentMethod == PaymentMethod.None);
                    break;
                case HistoryMethodFilter.All:
                default:
                    break;
            }

            // 4. Busca por Texto (Cliente, Descrição, Notas)
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(t =>
                    (t.ClientName != null && t.ClientName.ToLowerInvariant().Contains(term)) ||
                    (t.Description != null && t.Description.ToLowerInvariant().Contains(term)) ||
                    (t.Notes != null && t.Notes.ToLowerInvariant().Contains(term)) ||
                    (t.Reference != null && t.Reference.ToLowerInvariant().Contains(term)));
            }

            var filteredList = query.OrderByDescending(t => t.PaymentDate).ToList();

            FilteredTransactions.Clear();
            foreach (var item in filteredList)
            {
                FilteredTransactions.Add(item);
            }

            // 5. Atualização de Totais e Contadores
            TotalReceived = filteredList.Sum(t => t.Amount);
            var instItems = filteredList.Where(t => t.IsInstallment).ToList();
            InstallmentsTotal = instItems.Sum(t => t.Amount);
            InstallmentsCount = instItems.Count;

            var monthItems = filteredList.Where(t => t.IsMonthly).ToList();
            MonthlyTotal = monthItems.Sum(t => t.Amount);
            MonthlyCount = monthItems.Count;

            TotalTransactionsCount = filteredList.Count;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        private void OpenWhatsAppReceipt(FinancialTransactionItemViewModel? item)
        {
            if (item == null) return;

            string clientName = item.ClientName;
            string? phone = item.ClientPhone;
            string subject = item.IsMonthly ? "⭐ Mensalidade Quitada" : "💰 Parcela Quitada";
            string msg = $"Olá, {clientName}! Confirmamos o recebimento de {item.FormattedAmount} referente a {item.Description} em {item.FormattedDate}. Agradecemos o pagamento!";

            WhatsAppPreview.Open(
                clientName,
                phone,
                subject,
                item.Amount,
                msg);
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

        private static string GetMonthName(int month) => month switch
        {
            1 => "Janeiro",
            2 => "Fevereiro",
            3 => "Março",
            4 => "Abril",
            5 => "Maio",
            6 => "Junho",
            7 => "Julho",
            8 => "Agosto",
            9 => "Setembro",
            10 => "Outubro",
            11 => "Novembro",
            12 => "Dezembro",
            _ => "Mês"
        };
    }
}
