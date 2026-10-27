using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CobraPix_PC.Data;
using CobraPix_PC.Helpers;
using CobraPix_PC.Models;
using CobraPix_PC.Services;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.ViewModels.Modules
{
    public class RecentPaymentItemViewModel
    {
        public int Id { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public PaymentMethod Method { get; set; }
        public string Description { get; set; } = string.Empty;

        public string FormattedAmount => Amount.ToString("C2");
        public string FormattedDate => PaymentDate.ToString("dd/MM/yyyy HH:mm");
        public string MethodBadge => Method switch
        {
            PaymentMethod.Pix => "PIX",
            PaymentMethod.Cash => "DINHEIRO",
            PaymentMethod.Card => "CARTÃO",
            PaymentMethod.Other => "OUTRO",
            _ => "NENHUM"
        };
    }

    public class UpcomingCobrancaItemViewModel
    {
        public string ClientName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public bool IsOverdue => DueDate.Date < DateTime.Today;
        public bool IsToday => DueDate.Date == DateTime.Today;
        public string FormattedAmount => Amount.ToString("C2");
        public string FormattedDueDate => DueDate.Date == DateTime.Today
            ? "Hoje"
            : (DueDate.Date == DateTime.Today.AddDays(1) ? "Amanhã" : DueDate.ToString("dd/MM/yyyy"));
        public string BadgeText => IsOverdue ? "ATRASADO" : (IsToday ? "HOJE" : "A VENCER");
        public string BadgeBackground => IsOverdue ? "#2A1515" : (IsToday ? "#2A2315" : "#14253D");
        public string BadgeForeground => IsOverdue ? "#EF4444" : (IsToday ? "#F59E0B" : "#38BDF8");
    }

    public class ReminderItemSummaryViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime? DueDate { get; set; }
        public ReminderPriority Priority { get; set; }
        public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateTime.Now;
        public bool IsToday => DueDate.HasValue && DueDate.Value.Date == DateTime.Today;
        public string FormattedDueDate => DueDate.HasValue ? DueDate.Value.ToString("dd/MM HH:mm") : "Sem prazo";
        public string PriorityLabel => Priority switch
        {
            ReminderPriority.Low => "Baixa",
            ReminderPriority.Normal => "Normal",
            ReminderPriority.High => "Alta",
            ReminderPriority.Urgent => "Urgente",
            _ => "Normal"
        };
        public string PriorityColor => Priority switch
        {
            ReminderPriority.Low => "#10B981",
            ReminderPriority.Normal => "#3B82F6",
            ReminderPriority.High => "#F59E0B",
            ReminderPriority.Urgent => "#EF4444",
            _ => "#3B82F6"
        };
    }

    /// <summary>
    /// ViewModel do Dashboard Geral do CobraPix PC.
    /// Consolida métricas de clientes, vendas, parcelas abertas, atrasadas, mensalistas e pagamentos recentes.
    /// </summary>
    public class DashboardViewModel : ModuleViewModelBase
    {
        private readonly CobraPixDbContext _context;
        private readonly IProService _proService;
        private readonly IReminderService? _reminderService;
        private readonly Action<string>? _navigateToModule;

        private bool _isLoading;
        private bool _isEmpty;
        private bool _hasError;
        private string? _errorMessage;
        private DateTime _lastUpdated = DateTime.Now;

        // Métricas de Clientes
        private int _totalClientsCount;
        private int _activeClientsCount;
        private int _archivedClientsCount;
        private int _quitadosClientsCount;

        // Métricas de Parcelas e Saldos
        private int _openInstallmentsCount;
        private decimal _totalOpenAmount;
        private int _overdueInstallmentsCount;
        private decimal _totalOverdueAmount;
        private int _paidInstallmentsCount;
        private decimal _totalPaidAmount;

        // Métricas de Mensalistas
        private int _activeMonthlyCount;
        private int _pendingMonthlyCount;
        private decimal _pendingMonthlyAmount;

        // Métricas de Vencimentos Relevantes
        private int _dueTodayCount;
        private decimal _dueTodayAmount;
        private int _dueNext7DaysCount;
        private decimal _dueNext7DaysAmount;

        public override string Title => "Dashboard Geral";
        public override string Description => "Visão consolidada de recebíveis, faturamento, parcelas do dia e indicadores financeiros.";
        public override string Icon => "📊";
        public override string Category => "PRINCIPAL";

        public ObservableCollection<RecentPaymentItemViewModel> RecentPayments { get; } = new();

        // Propriedades observáveis
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsEmpty
        {
            get => _isEmpty;
            set => SetProperty(ref _isEmpty, value);
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

        public DateTime LastUpdated
        {
            get => _lastUpdated;
            set
            {
                if (SetProperty(ref _lastUpdated, value))
                {
                    OnPropertyChanged(nameof(FormattedLastUpdated));
                }
            }
        }

        public string FormattedLastUpdated => $"Atualizado em {LastUpdated:dd/MM/yyyy HH:mm:ss}";

        // Clientes
        public int TotalClientsCount
        {
            get => _totalClientsCount;
            set => SetProperty(ref _totalClientsCount, value);
        }

        public int ActiveClientsCount
        {
            get => _activeClientsCount;
            set => SetProperty(ref _activeClientsCount, value);
        }

        public int ArchivedClientsCount
        {
            get => _archivedClientsCount;
            set => SetProperty(ref _archivedClientsCount, value);
        }

        public int QuitadosClientsCount
        {
            get => _quitadosClientsCount;
            set => SetProperty(ref _quitadosClientsCount, value);
        }

        // Parcelas
        public int OpenInstallmentsCount
        {
            get => _openInstallmentsCount;
            set => SetProperty(ref _openInstallmentsCount, value);
        }

        public decimal TotalOpenAmount
        {
            get => _totalOpenAmount;
            set
            {
                if (SetProperty(ref _totalOpenAmount, value))
                {
                    OnPropertyChanged(nameof(FormattedTotalOpenAmount));
                }
            }
        }

        public string FormattedTotalOpenAmount => TotalOpenAmount.ToString("C2");

        public int OverdueInstallmentsCount
        {
            get => _overdueInstallmentsCount;
            set => SetProperty(ref _overdueInstallmentsCount, value);
        }

        public decimal TotalOverdueAmount
        {
            get => _totalOverdueAmount;
            set
            {
                if (SetProperty(ref _totalOverdueAmount, value))
                {
                    OnPropertyChanged(nameof(FormattedTotalOverdueAmount));
                }
            }
        }

        public string FormattedTotalOverdueAmount => TotalOverdueAmount.ToString("C2");

        public int PaidInstallmentsCount
        {
            get => _paidInstallmentsCount;
            set => SetProperty(ref _paidInstallmentsCount, value);
        }

        public decimal TotalPaidAmount
        {
            get => _totalPaidAmount;
            set
            {
                if (SetProperty(ref _totalPaidAmount, value))
                {
                    OnPropertyChanged(nameof(FormattedTotalPaidAmount));
                }
            }
        }

        public string FormattedTotalPaidAmount => TotalPaidAmount.ToString("C2");

        // Mensalistas
        public int ActiveMonthlyCount
        {
            get => _activeMonthlyCount;
            set => SetProperty(ref _activeMonthlyCount, value);
        }

        public int PendingMonthlyCount
        {
            get => _pendingMonthlyCount;
            set => SetProperty(ref _pendingMonthlyCount, value);
        }

        public decimal PendingMonthlyAmount
        {
            get => _pendingMonthlyAmount;
            set
            {
                if (SetProperty(ref _pendingMonthlyAmount, value))
                {
                    OnPropertyChanged(nameof(FormattedPendingMonthlyAmount));
                }
            }
        }

        public string FormattedPendingMonthlyAmount => PendingMonthlyAmount.ToString("C2");

        // Vencimentos Relevantes
        public int DueTodayCount
        {
            get => _dueTodayCount;
            set => SetProperty(ref _dueTodayCount, value);
        }

        public decimal DueTodayAmount
        {
            get => _dueTodayAmount;
            set
            {
                if (SetProperty(ref _dueTodayAmount, value))
                {
                    OnPropertyChanged(nameof(FormattedDueTodayAmount));
                }
            }
        }

        public string FormattedDueTodayAmount => DueTodayAmount.ToString("C2");

        public int DueNext7DaysCount
        {
            get => _dueNext7DaysCount;
            set => SetProperty(ref _dueNext7DaysCount, value);
        }

        public decimal DueNext7DaysAmount
        {
            get => _dueNext7DaysAmount;
            set
            {
                if (SetProperty(ref _dueNext7DaysAmount, value))
                {
                    OnPropertyChanged(nameof(FormattedDueNext7DaysAmount));
                }
            }
        }

        public string FormattedDueNext7DaysAmount => DueNext7DaysAmount.ToString("C2");

        public bool HasRecentPayments => RecentPayments.Count > 0;

        // Métricas de Lembretes & Tarefas
        private int _todayRemindersCount;
        public int TodayRemindersCount
        {
            get => _todayRemindersCount;
            set => SetProperty(ref _todayRemindersCount, value);
        }

        private int _overdueRemindersCount;
        public int OverdueRemindersCount
        {
            get => _overdueRemindersCount;
            set => SetProperty(ref _overdueRemindersCount, value);
        }

        public ObservableCollection<UpcomingCobrancaItemViewModel> UpcomingCobrancas { get; } = new();
        public bool HasUpcomingCobrancas => UpcomingCobrancas.Count > 0;

        public ObservableCollection<ReminderItemSummaryViewModel> UrgentReminders { get; } = new();
        public bool HasUrgentReminders => UrgentReminders.Count > 0;

        // Comandos
        public ICommand LoadDataCommand { get; }
        public ICommand NavigateToClientsCommand { get; }
        public ICommand NavigateToSalesCommand { get; }
        public ICommand NavigateToInstallmentsCommand { get; }
        public ICommand NavigateToPaymentsCommand { get; }
        public ICommand NavigateToDueDatesCommand { get; }
        public ICommand NavigateToOverdueCommand { get; }
        public ICommand NavigateToCollectionsCommand { get; }
        public ICommand NavigateToQuitadosCommand { get; }
        public ICommand NavigateToArquivadosCommand { get; }
        public ICommand NavigateToRankingCommand { get; }
        public ICommand NavigateToHistoricoCommand { get; }
        public ICommand NavigateToRemindersCommand { get; }
        public ICommand NavigateToConfigCommand { get; }

        public DashboardViewModel()
            : this(
                App.Services?.GetService<CobraPixDbContext>() ?? new CobraPixDbContext(),
                App.Services?.GetService<IProService>() ?? new ProService(new CobraPixDbContext()),
                null,
                App.Services?.GetService<IReminderService>())
        {
        }

        public DashboardViewModel(
            CobraPixDbContext context,
            IProService proService,
            Action<string>? navigateToModule = null,
            IReminderService? reminderService = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _proService = proService ?? throw new ArgumentNullException(nameof(proService));
            _navigateToModule = navigateToModule;
            _reminderService = reminderService ?? App.Services?.GetService<IReminderService>();

            LoadDataCommand = new RelayCommand(async () => await LoadDataAsync());
            NavigateToClientsCommand = new RelayCommand(() => _navigateToModule?.Invoke("Clientes"));
            NavigateToSalesCommand = new RelayCommand(() => _navigateToModule?.Invoke("Vendas"));
            NavigateToInstallmentsCommand = new RelayCommand(() => _navigateToModule?.Invoke("Parcelas"));
            NavigateToPaymentsCommand = new RelayCommand(() => _navigateToModule?.Invoke("Pagamentos"));
            NavigateToDueDatesCommand = new RelayCommand(() => _navigateToModule?.Invoke("Vencimentos"));
            NavigateToOverdueCommand = new RelayCommand(() => _navigateToModule?.Invoke("Atrasados"));
            NavigateToCollectionsCommand = new RelayCommand(() => _navigateToModule?.Invoke("Cobranças"));
            NavigateToQuitadosCommand = new RelayCommand(() => _navigateToModule?.Invoke("Quitados"));
            NavigateToArquivadosCommand = new RelayCommand(() => _navigateToModule?.Invoke("Arquivados"));
            NavigateToRankingCommand = new RelayCommand(() => _navigateToModule?.Invoke("Ranqueamento"));
            NavigateToHistoricoCommand = new RelayCommand(() => _navigateToModule?.Invoke("Histórico Financeiro"));
            NavigateToRemindersCommand = new RelayCommand(() => _navigateToModule?.Invoke("Lembretes"));
            NavigateToConfigCommand = new RelayCommand(() => _navigateToModule?.Invoke("Configurações"));

            _ = LoadDataAsync();
        }

        public async Task LoadDataAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            try
            {
                var today = DateTime.Today;

                // 1. Clientes e status comercial
                var limitStatus = await _proService.CheckClientLimitAsync();
                TotalClientsCount = limitStatus.TotalClientsCount;
                ActiveClientsCount = limitStatus.ActiveClientsCount;
                ArchivedClientsCount = limitStatus.ArchivedClientsCount;
                QuitadosClientsCount = limitStatus.QuitadosClientsCount;

                // 2. Parcelas
                var allInstallments = await _context.Installments
                    .AsNoTracking()
                    .Include(i => i.Payments)
                    .Include(i => i.Sale)
                        .ThenInclude(s => s.Client)
                    .ToListAsync();

                // Parcelas pagas (quantidade de parcelas com status Paid)
                var paidInst = allInstallments.Where(i => i.Status == InstallmentStatus.Paid).ToList();
                PaidInstallmentsCount = paidInst.Count;

                // Parcelas em aberto (Pending ou PartiallyPaid)
                var openInst = allInstallments.Where(i => i.Status != InstallmentStatus.Paid).ToList();
                OpenInstallmentsCount = openInst.Count;
                TotalOpenAmount = openInst.Sum(i => Math.Max(0m, i.Amount - (i.Payments?.Sum(p => p.AmountPaid) ?? 0m)));

                // Parcelas atrasadas (vencimento anterior a hoje e com saldo)
                var overdueInst = openInst.Where(i => i.DueDate.Date < today).ToList();
                OverdueInstallmentsCount = overdueInst.Count;
                TotalOverdueAmount = overdueInst.Sum(i => Math.Max(0m, i.Amount - (i.Payments?.Sum(p => p.AmountPaid) ?? 0m)));

                // Parcelas vencendo hoje
                var dueTodayInst = openInst.Where(i => i.DueDate.Date == today).ToList();
                DueTodayCount = dueTodayInst.Count;
                DueTodayAmount = dueTodayInst.Sum(i => Math.Max(0m, i.Amount - (i.Payments?.Sum(p => p.AmountPaid) ?? 0m)));

                // Parcelas vencendo nos próximos 7 dias (inclusive hoje)
                var due7DaysInst = openInst.Where(i => i.DueDate.Date >= today && i.DueDate.Date <= today.AddDays(7)).ToList();
                DueNext7DaysCount = due7DaysInst.Count;
                DueNext7DaysAmount = due7DaysInst.Sum(i => Math.Max(0m, i.Amount - (i.Payments?.Sum(p => p.AmountPaid) ?? 0m)));

                // 3. Mensalistas
                var allMonthly = await _context.MonthlyCustomers
                    .AsNoTracking()
                    .Include(m => m.Payments)
                    .Include(m => m.Client)
                    .ToListAsync();

                var activeMonthly = allMonthly.Where(m => m.IsActive).ToList();
                ActiveMonthlyCount = activeMonthly.Count;

                // Total efetivamente recebido de parcelas e mensalidades (todas as amortizações e quitações reais)
                decimal allInstallmentsPaid = allInstallments.Sum(i => i.Payments?.Sum(p => p.AmountPaid) ?? 0m);
                decimal allMonthlyPaid = allMonthly.Sum(m => m.Payments?.Sum(p => p.Amount) ?? 0m);
                TotalPaidAmount = allInstallmentsPaid + allMonthlyPaid;

                // Mensalidades pendentes do mês atual
                var pendingMonthly = activeMonthly.Where(m =>
                    m.Payments == null || !m.Payments.Any(p => p.ReferenceMonth == today.Month && p.ReferenceYear == today.Year)).ToList();

                PendingMonthlyCount = pendingMonthly.Count;
                PendingMonthlyAmount = pendingMonthly.Sum(m => m.MonthlyAmount);

                // 4. Pagamentos recentes (combina pagamentos de parcelas e mensalidades ordenados por data decrescente)
                var recentInstallmentPayments = await _context.InstallmentPayments
                    .AsNoTracking()
                    .Include(p => p.Installment)
                        .ThenInclude(i => i.Sale)
                            .ThenInclude(s => s.Client)
                    .OrderByDescending(p => p.PaymentDate)
                    .Take(5)
                    .ToListAsync();

                var recentMonthlyPayments = await _context.MonthlyPayments
                    .AsNoTracking()
                    .Include(p => p.MonthlyCustomer)
                        .ThenInclude(m => m.Client)
                    .OrderByDescending(p => p.PaymentDate)
                    .Take(5)
                    .ToListAsync();

                var combinedRecent = new List<RecentPaymentItemViewModel>();
                foreach (var p in recentInstallmentPayments)
                {
                    string clientName = p.Installment?.Sale?.Client?.Name ?? "Cliente";
                    string desc = p.Installment != null
                        ? $"Parcela {p.Installment.InstallmentNumber} - Venda #{p.Installment.SaleId}"
                        : "Pagamento de Parcela";

                    combinedRecent.Add(new RecentPaymentItemViewModel
                    {
                        Id = p.Id,
                        ClientName = clientName,
                        Amount = p.AmountPaid,
                        PaymentDate = p.PaymentDate,
                        Method = p.PaymentMethod,
                        Description = desc
                    });
                }

                foreach (var m in recentMonthlyPayments)
                {
                    string clientName = m.MonthlyCustomer?.Client?.Name ?? "Mensalista";
                    string desc = $"Mensalidade {m.ReferenceMonth:D2}/{m.ReferenceYear}";

                    combinedRecent.Add(new RecentPaymentItemViewModel
                    {
                        Id = m.Id,
                        ClientName = clientName,
                        Amount = m.Amount,
                        PaymentDate = m.PaymentDate,
                        Method = m.PaymentMethod,
                        Description = desc
                    });
                }

                RecentPayments.Clear();
                foreach (var item in combinedRecent.OrderByDescending(x => x.PaymentDate).Take(5))
                {
                    RecentPayments.Add(item);
                }

                OnPropertyChanged(nameof(HasRecentPayments));

                // 5. Próximas cobranças (parcelas e mensalidades a vencer ou hoje)
                UpcomingCobrancas.Clear();
                var upcomingList = new List<UpcomingCobrancaItemViewModel>();

                foreach (var inst in openInst)
                {
                    decimal remaining = Math.Max(0m, inst.Amount - (inst.Payments?.Sum(p => p.AmountPaid) ?? 0m));
                    if (remaining > 0)
                    {
                        string clientName = inst.Sale?.Client?.Name ?? "Cliente";
                        upcomingList.Add(new UpcomingCobrancaItemViewModel
                        {
                            ClientName = clientName,
                            Description = $"Parcela {inst.InstallmentNumber} - Venda #{inst.SaleId}",
                            DueDate = inst.DueDate,
                            Amount = remaining
                        });
                    }
                }

                foreach (var m in pendingMonthly)
                {
                    string clientName = m.Client?.Name ?? "Mensalista";
                    DateTime dueThisMonth = new DateTime(today.Year, today.Month, Math.Min(m.DueDay, DateTime.DaysInMonth(today.Year, today.Month)));
                    upcomingList.Add(new UpcomingCobrancaItemViewModel
                    {
                        ClientName = clientName,
                        Description = $"Mensalidade {today.Month:D2}/{today.Year}",
                        DueDate = dueThisMonth,
                        Amount = m.MonthlyAmount
                    });
                }

                foreach (var item in upcomingList.OrderBy(u => u.DueDate).Take(5))
                {
                    UpcomingCobrancas.Add(item);
                }
                OnPropertyChanged(nameof(HasUpcomingCobrancas));

                // 6. Lembretes & Tarefas de Hoje e Atrasados
                UrgentReminders.Clear();
                if (_reminderService != null)
                {
                    var reminderSummary = await _reminderService.GetSummaryAsync();
                    TodayRemindersCount = reminderSummary.TodayCount;
                    OverdueRemindersCount = reminderSummary.OverdueCount;

                    var pendingReminders = await _reminderService.GetAllRemindersAsync(status: ReminderStatus.Pending);
                    var urgent = pendingReminders
                        .Where(r => r.IsOverdue || r.IsToday)
                        .OrderByDescending(r => r.Priority)
                        .ThenBy(r => r.DueDate ?? DateTime.MaxValue)
                        .Take(5)
                        .ToList();

                    foreach (var r in urgent)
                    {
                        UrgentReminders.Add(new ReminderItemSummaryViewModel
                        {
                            Id = r.Id,
                            Title = r.Title,
                            DueDate = r.DueDate,
                            Priority = r.Priority
                        });
                    }
                }
                OnPropertyChanged(nameof(HasUrgentReminders));

                // Indicador de banco vazio
                IsEmpty = TotalClientsCount == 0 && allInstallments.Count == 0 && allMonthly.Count == 0;
                LastUpdated = DateTime.Now;
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Não foi possível carregar os dados do Dashboard: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
