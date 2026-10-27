using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CobraPix_PC.Helpers;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.ViewModels.Modules
{
    /// <summary>
    /// ViewModel completo do módulo de Lembretes e Tarefas.
    /// Gerencia listagem com abas inteligentes, busca, filtros de prioridade e categoria,
    /// cadastro/edição, conclusão com cálculo automático de recorrência e adiamento (snooze).
    /// </summary>
    public class LembretesViewModel : ModuleViewModelBase
    {
        private readonly IReminderService _reminderService;
        private readonly INotificationService? _notificationService;

        public override string Title => "Lembretes";
        public override string Description => "Gestão de compromissos, tarefas, prazos, prioridades e notificações recorrentes";
        public override string Icon => "📝";
        public override string Category => "ORGANIZAÇÃO";

        // Coleções observáveis
        public ObservableCollection<Reminder> Reminders { get; } = new();
        public ObservableCollection<Reminder> FilteredReminders { get; } = new();
        public ObservableCollection<string> AvailableCategories { get; } = new();

        public List<ReminderPriority> PrioritiesList { get; } = new()
        {
            ReminderPriority.Low,
            ReminderPriority.Normal,
            ReminderPriority.High,
            ReminderPriority.Urgent
        };

        public List<ReminderRecurrence> RecurrencesList { get; } = new()
        {
            ReminderRecurrence.None,
            ReminderRecurrence.Daily,
            ReminderRecurrence.Weekly,
            ReminderRecurrence.Monthly,
            ReminderRecurrence.CustomDays
        };

        // Estado do formulário
        private bool _isEditing;
        private int? _editingReminderId;
        private string _formTitle = "Novo Lembrete";
        private string _titleInput = string.Empty;
        private string? _descriptionInput;
        private DateTime? _dueDateInput = DateTime.Today;
        private string _dueTimeInput = "09:00";
        private bool _hasDueDate = true;
        private ReminderPriority _selectedPriority = ReminderPriority.Normal;
        private string _categoryInput = "Geral";
        private ReminderRecurrence _selectedRecurrence = ReminderRecurrence.None;
        private int? _customIntervalDays = 1;

        // Filtros e busca
        private string _activeTab = "Hoje"; // Hoje, Próximos, Atrasados, Concluídos, Todos
        private string _searchText = string.Empty;
        private string _selectedFilterCategory = "Todas";
        private string _selectedFilterPriority = "Todas";

        // Indicadores e Contadores
        private bool _isBusy;
        private string? _statusMessage;
        private string? _errorMessage;
        private string? _successMessage;

        private int _totalPendingCount;
        private int _overdueCount;
        private int _todayCount;
        private int _completedCount;

        // Propriedades do Formulário
        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        public string FormTitle
        {
            get => _formTitle;
            set => SetProperty(ref _formTitle, value);
        }

        public string TitleInput
        {
            get => _titleInput;
            set => SetProperty(ref _titleInput, value);
        }

        public string? DescriptionInput
        {
            get => _descriptionInput;
            set => SetProperty(ref _descriptionInput, value);
        }

        public DateTime? DueDateInput
        {
            get => _dueDateInput;
            set => SetProperty(ref _dueDateInput, value);
        }

        public string DueTimeInput
        {
            get => _dueTimeInput;
            set => SetProperty(ref _dueTimeInput, value);
        }

        public bool HasDueDate
        {
            get => _hasDueDate;
            set => SetProperty(ref _hasDueDate, value);
        }

        public ReminderPriority SelectedPriority
        {
            get => _selectedPriority;
            set => SetProperty(ref _selectedPriority, value);
        }

        public string CategoryInput
        {
            get => _categoryInput;
            set => SetProperty(ref _categoryInput, value);
        }

        public ReminderRecurrence SelectedRecurrence
        {
            get => _selectedRecurrence;
            set
            {
                if (SetProperty(ref _selectedRecurrence, value))
                {
                    OnPropertyChanged(nameof(IsCustomRecurrence));
                }
            }
        }

        public bool IsCustomRecurrence => SelectedRecurrence == ReminderRecurrence.CustomDays;

        public int? CustomIntervalDays
        {
            get => _customIntervalDays;
            set => SetProperty(ref _customIntervalDays, value);
        }

        // Propriedades de Filtro
        public string ActiveTab
        {
            get => _activeTab;
            set
            {
                if (SetProperty(ref _activeTab, value))
                {
                    ApplyFilters();
                    OnPropertyChanged(nameof(IsTabHoje));
                    OnPropertyChanged(nameof(IsTabProximos));
                    OnPropertyChanged(nameof(IsTabAtrasados));
                    OnPropertyChanged(nameof(IsTabConcluidos));
                    OnPropertyChanged(nameof(IsTabTodos));
                }
            }
        }

        public bool IsTabHoje => ActiveTab == "Hoje";
        public bool IsTabProximos => ActiveTab == "Próximos";
        public bool IsTabAtrasados => ActiveTab == "Atrasados";
        public bool IsTabConcluidos => ActiveTab == "Concluídos";
        public bool IsTabTodos => ActiveTab == "Todos";

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilters();
                }
            }
        }

        public string SelectedFilterCategory
        {
            get => _selectedFilterCategory;
            set
            {
                if (SetProperty(ref _selectedFilterCategory, value))
                {
                    ApplyFilters();
                }
            }
        }

        public string SelectedFilterPriority
        {
            get => _selectedFilterPriority;
            set
            {
                if (SetProperty(ref _selectedFilterPriority, value))
                {
                    ApplyFilters();
                }
            }
        }

        // KPIs
        public int TotalPendingCount
        {
            get => _totalPendingCount;
            private set => SetProperty(ref _totalPendingCount, value);
        }

        public int OverdueCount
        {
            get => _overdueCount;
            private set => SetProperty(ref _overdueCount, value);
        }

        public int TodayCount
        {
            get => _todayCount;
            private set => SetProperty(ref _todayCount, value);
        }

        public int CompletedCount
        {
            get => _completedCount;
            private set => SetProperty(ref _completedCount, value);
        }

        // Estado da UI
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public string? StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public string? SuccessMessage
        {
            get => _successMessage;
            set => SetProperty(ref _successMessage, value);
        }

        // Comandos
        public ICommand SaveReminderCommand { get; }
        public ICommand NewReminderCommand { get; }
        public ICommand EditReminderCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand DeleteReminderCommand { get; }
        public ICommand CompleteReminderCommand { get; }
        public ICommand SnoozeReminderCommand { get; }
        public ICommand SelectTabCommand { get; }
        public ICommand RefreshCommand { get; }

        public LembretesViewModel(
            IReminderService reminderService,
            INotificationService? notificationService = null)
        {
            _reminderService = reminderService ?? throw new ArgumentNullException(nameof(reminderService));
            _notificationService = notificationService;

            SaveReminderCommand = new RelayCommand(async () => await SaveReminderAsync());
            NewReminderCommand = new RelayCommand(ResetForm);
            EditReminderCommand = new RelayCommand<Reminder>(EditReminder);
            CancelEditCommand = new RelayCommand(ResetForm);
            DeleteReminderCommand = new RelayCommand<Reminder>(async r => await DeleteReminderAsync(r));
            CompleteReminderCommand = new RelayCommand<Reminder>(async r => await CompleteReminderAsync(r));
            SnoozeReminderCommand = new RelayCommand<object>(async param => await HandleSnoozeAsync(param));
            SelectTabCommand = new RelayCommand<string>(tab => { if (tab != null) ActiveTab = tab; });
            RefreshCommand = new RelayCommand(async () => await LoadDataAsync());
        }

        public async Task LoadDataAsync()
        {
            try
            {
                IsBusy = true;

                var items = await _reminderService.GetAllRemindersAsync();
                var summary = await _reminderService.GetSummaryAsync();

                TotalPendingCount = summary.TotalPending;
                OverdueCount = summary.OverdueCount;
                TodayCount = summary.TodayCount;
                CompletedCount = summary.CompletedCount;

                Reminders.Clear();
                foreach (var item in items)
                {
                    Reminders.Add(item);
                }

                // Categorias disponíveis
                var currentCat = SelectedFilterCategory;
                AvailableCategories.Clear();
                AvailableCategories.Add("Todas");

                var distinctCats = items
                    .Select(r => r.Category)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .OrderBy(c => c);

                foreach (var cat in distinctCats)
                {
                    AvailableCategories.Add(cat);
                }

                // Garante opções padrão se vazias
                if (!AvailableCategories.Contains("Geral")) AvailableCategories.Add("Geral");
                if (!AvailableCategories.Contains("Cobrança")) AvailableCategories.Add("Cobrança");
                if (!AvailableCategories.Contains("Financeiro")) AvailableCategories.Add("Financeiro");

                SelectedFilterCategory = AvailableCategories.Contains(currentCat) ? currentCat : "Todas";

                ApplyFilters();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erro ao carregar lembretes: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyFilters()
        {
            DateTime now = DateTime.Now;
            DateTime today = now.Date;

            IEnumerable<Reminder> query = Reminders;

            // Filtro por Aba
            query = ActiveTab switch
            {
                "Hoje" => query.Where(r => r.Status == ReminderStatus.Pending && r.DueDate.HasValue && r.DueDate.Value.Date == today),
                "Próximos" => query.Where(r => r.Status == ReminderStatus.Pending && r.DueDate.HasValue && r.DueDate.Value.Date > today),
                "Atrasados" => query.Where(r => r.Status == ReminderStatus.Pending && r.DueDate.HasValue && r.DueDate.Value < now),
                "Concluídos" => query.Where(r => r.Status == ReminderStatus.Completed),
                _ => query // "Todos"
            };

            // Filtro por Categoria
            if (!string.IsNullOrWhiteSpace(SelectedFilterCategory) && SelectedFilterCategory != "Todas")
            {
                query = query.Where(r => string.Equals(r.Category, SelectedFilterCategory, StringComparison.OrdinalIgnoreCase));
            }

            // Filtro por Prioridade
            if (!string.IsNullOrWhiteSpace(SelectedFilterPriority) && SelectedFilterPriority != "Todas")
            {
                query = SelectedFilterPriority switch
                {
                    "Baixa" => query.Where(r => r.Priority == ReminderPriority.Low),
                    "Normal" => query.Where(r => r.Priority == ReminderPriority.Normal),
                    "Alta" => query.Where(r => r.Priority == ReminderPriority.High),
                    "Urgente" => query.Where(r => r.Priority == ReminderPriority.Urgent),
                    _ => query
                };
            }

            // Busca por texto
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string searchLower = SearchText.Trim().ToLower();
                query = query.Where(r =>
                    r.Title.ToLower().Contains(searchLower) ||
                    (r.Description != null && r.Description.ToLower().Contains(searchLower)) ||
                    r.Category.ToLower().Contains(searchLower));
            }

            FilteredReminders.Clear();
            foreach (var item in query)
            {
                FilteredReminders.Add(item);
            }
        }

        public async Task SaveReminderAsync()
        {
            if (string.IsNullOrWhiteSpace(TitleInput))
            {
                ErrorMessage = "O título do lembrete é obrigatório.";
                return;
            }

            try
            {
                IsBusy = true;
                ClearMessages();

                DateTime? computedDueDate = null;
                if (HasDueDate && DueDateInput.HasValue)
                {
                    TimeSpan parsedTime = TimeSpan.FromHours(9); // padrão 09:00
                    if (!string.IsNullOrWhiteSpace(DueTimeInput) && TimeSpan.TryParse(DueTimeInput, out var timeVal))
                    {
                        parsedTime = timeVal;
                    }

                    computedDueDate = DueDateInput.Value.Date.Add(parsedTime);
                }

                if (IsEditing && _editingReminderId.HasValue)
                {
                    var reminderToUpdate = new Reminder
                    {
                        Id = _editingReminderId.Value,
                        Title = TitleInput.Trim(),
                        Description = string.IsNullOrWhiteSpace(DescriptionInput) ? null : DescriptionInput.Trim(),
                        DueDate = computedDueDate,
                        Priority = SelectedPriority,
                        Category = string.IsNullOrWhiteSpace(CategoryInput) ? "Geral" : CategoryInput.Trim(),
                        Recurrence = SelectedRecurrence,
                        CustomIntervalDays = SelectedRecurrence == ReminderRecurrence.CustomDays ? CustomIntervalDays : null,
                        Status = ReminderStatus.Pending
                    };

                    await _reminderService.UpdateReminderAsync(reminderToUpdate);
                    SuccessMessage = $"Lembrete '{reminderToUpdate.Title}' atualizado com sucesso!";
                }
                else
                {
                    var newReminder = new Reminder
                    {
                        Title = TitleInput.Trim(),
                        Description = string.IsNullOrWhiteSpace(DescriptionInput) ? null : DescriptionInput.Trim(),
                        DueDate = computedDueDate,
                        Priority = SelectedPriority,
                        Category = string.IsNullOrWhiteSpace(CategoryInput) ? "Geral" : CategoryInput.Trim(),
                        Recurrence = SelectedRecurrence,
                        CustomIntervalDays = SelectedRecurrence == ReminderRecurrence.CustomDays ? CustomIntervalDays : null,
                        Status = ReminderStatus.Pending
                    };

                    await _reminderService.CreateReminderAsync(newReminder);
                    SuccessMessage = $"Lembrete '{newReminder.Title}' criado com sucesso!";
                }

                ResetForm();
                await LoadDataAsync();
                _ = _notificationService?.CheckScheduledNotificationsAsync();
            }
            catch (BusinessRuleException brex)
            {
                ErrorMessage = brex.Message;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erro ao salvar lembrete: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void EditReminder(Reminder? reminder)
        {
            if (reminder == null) return;

            ClearMessages();
            IsEditing = true;
            _editingReminderId = reminder.Id;
            FormTitle = $"Editar Lembrete #{reminder.Id}";

            TitleInput = reminder.Title;
            DescriptionInput = reminder.Description;
            SelectedPriority = reminder.Priority;
            CategoryInput = reminder.Category;
            SelectedRecurrence = reminder.Recurrence;
            CustomIntervalDays = reminder.CustomIntervalDays ?? 1;

            if (reminder.DueDate.HasValue)
            {
                HasDueDate = true;
                DueDateInput = reminder.DueDate.Value.Date;
                DueTimeInput = reminder.DueDate.Value.ToString("HH:mm");
            }
            else
            {
                HasDueDate = false;
                DueDateInput = DateTime.Today;
                DueTimeInput = "09:00";
            }
        }

        public void ResetForm()
        {
            IsEditing = false;
            _editingReminderId = null;
            FormTitle = "Novo Lembrete";
            TitleInput = string.Empty;
            DescriptionInput = null;
            DueDateInput = DateTime.Today;
            DueTimeInput = "09:00";
            HasDueDate = true;
            SelectedPriority = ReminderPriority.Normal;
            CategoryInput = "Geral";
            SelectedRecurrence = ReminderRecurrence.None;
            CustomIntervalDays = 1;
        }

        public async Task CompleteReminderAsync(Reminder? reminder)
        {
            if (reminder == null) return;

            try
            {
                IsBusy = true;
                ClearMessages();

                var completed = await _reminderService.CompleteReminderAsync(reminder.Id);
                reminder.Status = ReminderStatus.Completed;
                reminder.CompletedAt = completed.CompletedAt;
                SuccessMessage = $"Lembrete '{reminder.Title}' marcado como Concluído!";

                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erro ao concluir lembrete: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task DeleteReminderAsync(Reminder? reminder)
        {
            if (reminder == null) return;

            try
            {
                IsBusy = true;
                ClearMessages();

                bool deleted = await _reminderService.DeleteReminderAsync(reminder.Id);
                if (deleted)
                {
                    SuccessMessage = $"Lembrete '{reminder.Title}' removido com sucesso.";
                }

                if (IsEditing && _editingReminderId == reminder.Id)
                {
                    ResetForm();
                }

                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erro ao excluir lembrete: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task HandleSnoozeAsync(object? param)
        {
            if (param is Reminder r)
            {
                // Padrão: adiar por 60 minutos
                await SnoozeReminderAsync(r, TimeSpan.FromMinutes(60));
            }
            else if (param is (Reminder rem, int minutes))
            {
                await SnoozeReminderAsync(rem, TimeSpan.FromMinutes(minutes));
            }
        }

        public async Task SnoozeReminderAsync(Reminder? reminder, TimeSpan delay)
        {
            if (reminder == null) return;

            try
            {
                IsBusy = true;
                ClearMessages();

                var updated = await _reminderService.SnoozeReminderAsync(reminder.Id, delay);
                SuccessMessage = $"Lembrete '{reminder.Title}' adiado para {updated.FormattedDueDate}.";

                await LoadDataAsync();
                _ = _notificationService?.CheckScheduledNotificationsAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erro ao adiar lembrete: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void ClearMessages()
        {
            ErrorMessage = null;
            SuccessMessage = null;
            StatusMessage = null;
        }
    }
}
