using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using CobraPix_PC.Data;
using CobraPix_PC.Helpers;
using CobraPix_PC.Services;
using CobraPix_PC.Services.Interfaces;
using CobraPix_PC.Services.Credit;
using CobraPix_PC.Services.Credit.Interfaces;
using CobraPix_PC.ViewModels.Modules;

namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// ViewModel principal que gerencia o Shell, a navegação entre módulos e os estados da Sidebar/Header.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly CobraPixDbContext _dbContext;
        private string _title = "CobraPix PC";
        private ModuleViewModelBase _currentViewModel;
        private NavigationItem? _selectedNavigationItem;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public ModuleViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set
            {
                if (SetProperty(ref _currentViewModel, value))
                {
                    OnPropertyChanged(nameof(CurrentTitle));
                    OnPropertyChanged(nameof(CurrentDescription));
                }
            }
        }

        public string CurrentTitle => _currentViewModel?.Title ?? "CobraPix PC";
        public string CurrentDescription => _currentViewModel?.Description ?? string.Empty;

        public NavigationItem? SelectedNavigationItem
        {
            get => _selectedNavigationItem;
            set => SetProperty(ref _selectedNavigationItem, value);
        }

        // Grupos de navegação para a Sidebar
        public ObservableCollection<NavigationItem> MainMenuItems { get; } = new();
        public ObservableCollection<NavigationItem> RecurrenceMenuItems { get; } = new();
        public ObservableCollection<NavigationItem> StatusMenuItems { get; } = new();
        public ObservableCollection<NavigationItem> FooterMenuItems { get; } = new();

        // Comandos de navegação
        public ICommand NavigateCommand { get; }
        public ICommand OpenConfigCommand { get; }
        public ICommand OpenProCommand { get; }

        public MainViewModel()
            : this(
                App.Services?.GetService<IClientService>(),
                App.Services?.GetService<ISaleService>(),
                App.Services?.GetService<IPaymentService>(),
                App.Services?.GetService<IMonthlyCustomerService>(),
                App.Services?.GetService<IWhatsAppService>(),
                App.Services?.GetService<INotificationService>(),
                App.Services?.GetService<IBackupService>(),
                App.Services?.GetService<IProService>(),
                App.Services?.GetService<IFinancialHistoryService>(),
                App.Services?.GetService<IThemeService>(),
                App.Services?.GetService<ICreditContractService>(),
                App.Services?.GetService<ICreditPaymentService>(),
                App.Services?.GetService<ICreditRefundService>(),
                App.Services?.GetService<IReminderService>())
        {
        }

        public MainViewModel(
            IClientService? clientService,
            ISaleService? saleService = null,
            IPaymentService? paymentService = null,
            IMonthlyCustomerService? monthlyCustomerService = null,
            IWhatsAppService? whatsAppService = null,
            INotificationService? notificationService = null,
            IBackupService? backupService = null,
            IProService? proService = null,
            IFinancialHistoryService? financialHistoryService = null,
            IThemeService? themeService = null,
            ICreditContractService? creditContractService = null,
            ICreditPaymentService? creditPaymentService = null,
            ICreditRefundService? creditRefundService = null,
            IReminderService? reminderService = null)
        {
            var injectedDbContext = (clientService as ClientService)?.DbContext
                ?? (notificationService as NotificationService)?.DbContext
                ?? (backupService as BackupService)?.DbContext
                ?? (proService as ProService)?.DbContext
                ?? (themeService as ThemeService)?.DbContext;

            var dbContext = injectedDbContext ?? App.Services?.GetService<CobraPixDbContext>() ?? new CobraPixDbContext();
            _dbContext = dbContext;
            var clientSvc = clientService ?? App.Services?.GetService<IClientService>() ?? new ClientService(dbContext);
            var saleSvc = saleService ?? App.Services?.GetService<ISaleService>() ?? new SaleService(dbContext);
            var paymentSvc = paymentService ?? App.Services?.GetService<IPaymentService>() ?? new PaymentService(dbContext);
            var monthlySvc = monthlyCustomerService ?? App.Services?.GetService<IMonthlyCustomerService>() ?? new MonthlyCustomerService(dbContext);
            var whatsAppSvc = whatsAppService ?? App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService(dbContext);
            var notificationSvc = notificationService ?? App.Services?.GetService<INotificationService>() ?? new NotificationService(dbContext, paymentSvc, monthlySvc);
            var backupSvc = backupService ?? App.Services?.GetService<IBackupService>() ?? new BackupService(dbContext);
            var proSvc = proService ?? App.Services?.GetService<IProService>() ?? new ProService(dbContext);
            var historySvc = financialHistoryService ?? App.Services?.GetService<IFinancialHistoryService>() ?? new FinancialHistoryService(dbContext);
            var themeSvc = themeService ?? App.Services?.GetService<IThemeService>() ?? new ThemeService(dbContext);
            var reminderSvc = reminderService ?? App.Services?.GetService<IReminderService>() ?? new ReminderService(dbContext);

            // Declaração dos ViewModels com interconexão de navegação e sincronização
            ClientesViewModel clientesVm = null!;
            VendasViewModel vendasVm = null!;
            ParcelasViewModel parcelasVm = null!;
            PagamentosViewModel pagamentosVm = null!;
            MensalistasViewModel mensalistasVm = null!;
            VencimentosViewModel vencimentosVm = null!;
            AtrasadosViewModel atrasadosVm = null!;
            CobrancasViewModel cobrancasVm = null!;

            DashboardViewModel dashboardVm = null!;
            QuitadosViewModel quitadosVm = null!;
            ArquivadosViewModel arquivadosVm = null!;
            RanqueamentoViewModel ranqueamentoVm = null!;
            HistoricoFinanceiroViewModel historicoFinanceiroVm = null!;
            LembretesViewModel lembretesVm = null!;

            void NavigateToModuleName(string moduleName)
            {
                var allItems = MainMenuItems
                    .Concat(RecurrenceMenuItems)
                    .Concat(StatusMenuItems)
                    .Concat(FooterMenuItems);

                var item = allItems.FirstOrDefault(m =>
                    m.Title.Equals(moduleName, StringComparison.OrdinalIgnoreCase) ||
                    m.ViewModel.Title.Equals(moduleName, StringComparison.OrdinalIgnoreCase));

                if (item != null)
                {
                    NavigateTo(item);
                }
            }

            void OnPaymentRegistered()
            {
                _ = parcelasVm.LoadInstallmentsAsync();
                _ = vendasVm.LoadSalesAsync();
                _ = vencimentosVm.LoadDataAsync();
                _ = atrasadosVm.LoadDataAsync();
                _ = cobrancasVm.LoadDataAsync();
                _ = dashboardVm.LoadDataAsync();
                _ = quitadosVm.LoadQuitadosAsync();
                _ = ranqueamentoVm.LoadDataAsync();
                _ = historicoFinanceiroVm.LoadTransactionsAsync();
            }

            void OpenPayment(int installmentId)
            {
                CurrentViewModel = pagamentosVm;
                SelectedNavigationItem = MainMenuItems.FirstOrDefault(m => m.ViewModel == pagamentosVm);
                _ = pagamentosVm.SelectInstallmentByIdAsync(installmentId);
            }

            void OpenMensalista(int monthlyCustomerId)
            {
                CurrentViewModel = mensalistasVm;
                SelectedNavigationItem = RecurrenceMenuItems.FirstOrDefault(m => m.ViewModel == mensalistasVm);
                mensalistasVm.CurrentFilter = MonthlyCustomerFilterType.All;
                mensalistasVm.SearchText = string.Empty;
                var target = mensalistasVm.FilteredMonthlyCustomers.FirstOrDefault(m => m.Id == monthlyCustomerId);
                if (target != null)
                {
                    mensalistasVm.SelectedMonthlyCustomer = target;
                }
            }

            void OpenNewSaleForClient(int clientId)
            {
                CurrentViewModel = vendasVm;
                SelectedNavigationItem = MainMenuItems.FirstOrDefault(m => m.ViewModel == vendasVm);
                _ = vendasVm.OpenNewSaleForClientAsync(clientId);
            }

            dashboardVm = new DashboardViewModel(dbContext, proSvc, NavigateToModuleName, reminderSvc);
            clientesVm = new ClientesViewModel(clientSvc, whatsAppSvc, proSvc, historySvc);
            vendasVm = new VendasViewModel(saleSvc, clientSvc, paymentSvc, OpenPayment);
            parcelasVm = new ParcelasViewModel(paymentSvc, OpenPayment);
            pagamentosVm = new PagamentosViewModel(paymentSvc, OnPaymentRegistered);
            mensalistasVm = new MensalistasViewModel(monthlySvc, clientSvc, whatsAppSvc);
            vencimentosVm = new VencimentosViewModel(paymentSvc, monthlySvc, OpenPayment, OpenMensalista, whatsAppSvc);
            atrasadosVm = new AtrasadosViewModel(paymentSvc, monthlySvc, OpenPayment, OpenMensalista, whatsAppSvc);
            cobrancasVm = new CobrancasViewModel(paymentSvc, monthlySvc, OpenPayment, OpenMensalista, whatsAppSvc);

            quitadosVm = new QuitadosViewModel(dbContext, clientSvc, OpenNewSaleForClient);
            arquivadosVm = new ArquivadosViewModel(dbContext, clientSvc);
            ranqueamentoVm = new RanqueamentoViewModel(dbContext);
            historicoFinanceiroVm = new HistoricoFinanceiroViewModel(historySvc, whatsAppSvc);

            lembretesVm = new LembretesViewModel(reminderSvc, notificationSvc);

            var androidImportSvc = App.Services?.GetService<IAndroidImportService>() ?? new AndroidImportService(dbContext);
            var configVm = new ConfiguracoesViewModel(notificationSvc, backupSvc, proSvc, themeSvc, androidImportSvc);
            var proVm = new ProViewModel(proSvc);

            void ReloadAllData()
            {
                try
                {
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    dbContext.ChangeTracker.Clear();
                }
                catch { }

                _ = clientesVm.LoadClientsAsync();
                _ = vendasVm.LoadSalesAsync();
                _ = parcelasVm.LoadInstallmentsAsync();
                _ = mensalistasVm.LoadMonthlyCustomersAsync();
                _ = pagamentosVm.LoadInstallmentsAsync();
                _ = vencimentosVm.LoadDataAsync();
                _ = atrasadosVm.LoadDataAsync();
                _ = cobrancasVm.LoadDataAsync();
                _ = dashboardVm.LoadDataAsync();
                _ = quitadosVm.LoadQuitadosAsync();
                _ = arquivadosVm.LoadArquivadosAsync();
                _ = ranqueamentoVm.LoadDataAsync();
                _ = historicoFinanceiroVm.LoadTransactionsAsync();
                _ = lembretesVm.LoadDataAsync();
            }

            configVm.OnDataImported = ReloadAllData;
            ConfiguracoesViewModel.DataRestoredOrImported += ReloadAllData;

            // Módulos Principais
            MainMenuItems.Add(new NavigationItem("Dashboard", "📊", dashboardVm));
            MainMenuItems.Add(new NavigationItem("Clientes", "👥", clientesVm));
            MainMenuItems.Add(new NavigationItem("Vendas", "🛒", vendasVm));
            MainMenuItems.Add(new NavigationItem("Parcelas", "📅", parcelasVm));
            MainMenuItems.Add(new NavigationItem("Pagamentos", "💳", pagamentosVm));

            // Recorrência e Cobrança
            RecurrenceMenuItems.Add(new NavigationItem("Mensalistas", "⭐", mensalistasVm));
            RecurrenceMenuItems.Add(new NavigationItem("Vencimentos", "⏰", vencimentosVm));
            RecurrenceMenuItems.Add(new NavigationItem("Atrasados", "⚠️", atrasadosVm));
            RecurrenceMenuItems.Add(new NavigationItem("Cobranças", "💬", cobrancasVm));
            RecurrenceMenuItems.Add(new NavigationItem("Lembretes", "📝", lembretesVm));

            // Consultas e Status
            StatusMenuItems.Add(new NavigationItem("Quitados", "✅", quitadosVm));
            StatusMenuItems.Add(new NavigationItem("Arquivados", "📁", arquivadosVm));
            StatusMenuItems.Add(new NavigationItem("Ranqueamento", "🏆", ranqueamentoVm));
            StatusMenuItems.Add(new NavigationItem("Histórico Financeiro", "📜", historicoFinanceiroVm));

            // Itens de Rodapé (Sistema e Upgrade)
            var proItem = new NavigationItem("CobraPix PRO", "👑", proVm, badge: "PRO", isPro: true);
            var configItem = new NavigationItem("Configurações", "⚙️", configVm);

            FooterMenuItems.Add(proItem);
            FooterMenuItems.Add(configItem);

            // Comandos
            NavigateCommand = new RelayCommand<NavigationItem>(NavigateTo);
            OpenConfigCommand = new RelayCommand(() => NavigateTo(configItem));
            OpenProCommand = new RelayCommand(() => NavigateTo(proItem));

            // Módulo inicial padrão: Dashboard
            var defaultItem = MainMenuItems[0];
            defaultItem.IsSelected = true;
            _selectedNavigationItem = defaultItem;
            _currentViewModel = defaultItem.ViewModel;
        }

        public void NavigateTo(NavigationItem? targetItem)
        {
            if (targetItem == null || targetItem == SelectedNavigationItem)
                return;

            // Desmarca todos os itens de todas as listas
            var allItems = MainMenuItems
                .Concat(RecurrenceMenuItems)
                .Concat(StatusMenuItems)
                .Concat(FooterMenuItems);

            foreach (var item in allItems)
            {
                item.IsSelected = false;
            }

            // Seleciona o item alvo
            targetItem.IsSelected = true;
            SelectedNavigationItem = targetItem;
            CurrentViewModel = targetItem.ViewModel;

            // Atualiza os dados do ViewModel que está sendo visualizado
            try
            {
                _dbContext?.ChangeTracker.Clear();
            }
            catch { }

            if (targetItem.ViewModel is ClientesViewModel cVm)
            {
                _ = cVm.LoadClientsAsync();
            }
            else if (targetItem.ViewModel is VendasViewModel vVm)
            {
                _ = vVm.LoadSalesAsync();
            }
            else if (targetItem.ViewModel is ParcelasViewModel pVm)
            {
                _ = pVm.LoadInstallmentsAsync();
            }
            else if (targetItem.ViewModel is MensalistasViewModel mVm)
            {
                _ = mVm.LoadMonthlyCustomersAsync();
            }
            else if (targetItem.ViewModel is PagamentosViewModel payVm)
            {
                _ = payVm.LoadInstallmentsAsync();
            }
            else if (targetItem.ViewModel is DashboardViewModel dVm)
            {
                _ = dVm.LoadDataAsync();
            }
            else if (targetItem.ViewModel is VencimentosViewModel vencVm)
            {
                _ = vencVm.LoadDataAsync();
            }
            else if (targetItem.ViewModel is AtrasadosViewModel atrVm)
            {
                _ = atrVm.LoadDataAsync();
            }
            else if (targetItem.ViewModel is CobrancasViewModel cobVm)
            {
                _ = cobVm.LoadDataAsync();
            }
            else if (targetItem.ViewModel is QuitadosViewModel qVm)
            {
                _ = qVm.LoadQuitadosAsync();
            }
            else if (targetItem.ViewModel is ArquivadosViewModel arqVm)
            {
                _ = arqVm.LoadArquivadosAsync();
            }
            else if (targetItem.ViewModel is RanqueamentoViewModel rVm)
            {
                _ = rVm.LoadDataAsync();
            }
            else if (targetItem.ViewModel is HistoricoFinanceiroViewModel hVm)
            {
                _ = hVm.LoadTransactionsAsync();
            }
            else if (targetItem.ViewModel is LembretesViewModel lemVm)
            {
                _ = lemVm.LoadDataAsync();
            }
        }
    }
}
