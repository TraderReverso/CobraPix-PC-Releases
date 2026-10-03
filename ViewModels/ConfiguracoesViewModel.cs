using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using CobraPix_PC.Data;
using CobraPix_PC.Helpers;
using CobraPix_PC.Models;
using CobraPix_PC.Services;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.ViewModels
{
    public class AdvanceDayOption
    {
        public int Days { get; }
        public string Label { get; }

        public AdvanceDayOption(int days, string label)
        {
            Days = days;
            Label = label;
        }

        public override string ToString() => Label;
    }

    /// <summary>
    /// ViewModel completo para o módulo de Configurações, gerenciando preferências de Notificações,
    /// Lembretes, Som e operações locais de Backup e Restauração (.cpb).
    /// </summary>
    public class ConfiguracoesViewModel : ModuleViewModelBase
    {
        private readonly INotificationService _notificationService;
        private readonly IBackupService? _backupService;
        private readonly IProService? _proService;
        private readonly IThemeService? _themeService;
        private readonly IAndroidImportService? _androidImportService;
        private readonly IUpdateService _updateService;
        private readonly IUpdateDownloadService _updateDownloadService;
        private readonly IExcelExportService? _excelExportService;

        // Propriedades de Exportação para Excel (Fase Excel 1)
        private bool _isBusyExportingExcel;
        private bool _showExcelSuccess;
        private bool _showExcelError;
        private string? _excelStatusMessage;

        // Propriedades de Atualização do CobraPix (Fase 8 e Fase Atualização 1/2)
        private bool _isCheckingUpdate;
        private string? _updateStatusMessage;
        private bool _isUpdateAvailable;
        private string? _remoteVersionText;
        private string? _remoteSha256;
        private string? _remoteDownloadUrl;
        private string? _remoteVersion;
        private bool _showUpdateSuccess;
        private bool _showUpdateError;
        private bool _showUpdateInfo;

        // Propriedades de Download de Atualização (Fase Atualização 2)
        private bool _isDownloadingUpdate;
        private double _downloadProgressPercentage;
        private string? _downloadProgressText;
        private string? _downloadPercentageText;
        private string? _downloadStatusText;
        private bool _isDownloadCompleted;
        private bool _isDownloadValid;
        private string? _downloadedFilePath;
        private CancellationTokenSource? _downloadCts;

        // Propriedades do Plano CobraPix (Fase 13)
        private PlanType _currentPlan = PlanType.Free;
        private int _activeClientsCount;
        private string? _proStatusMessage;

        // Propriedades de Tema (Escuro / Claro)
        private AppTheme _currentAppTheme = AppTheme.Dark;

        // Propriedades de Notificações e Áudio
        private bool _notificationsEnabled = true;
        private bool _soundEnabled = true;
        private int _advanceDays = 1;
        private bool _overdueAlertsEnabled = true;
        private bool _monthlyAlertsEnabled = true;
        private bool _fifthBusinessDayAlertEnabled = true;
        private string _soundType = "Asterisk";
        private SystemSoundOption? _selectedSoundOption;

        // 7 Horários de Notificação / Alarmes (Padrão CobraPix Android)
        private string _hourSlot1 = "08:00";
        private string _hourSlot2 = "10:00";
        private string _hourSlot3 = "12:00";
        private string _hourSlot4 = "14:00";
        private string _hourSlot5 = "16:00";
        private string _hourSlot6 = "18:00";
        private string _hourSlot7 = "20:00";
        private string _lastConfirmedSlot1 = "08:00";
        private string _lastConfirmedSlot2 = "10:00";
        private string _lastConfirmedSlot3 = "12:00";
        private string _lastConfirmedSlot4 = "14:00";
        private string _lastConfirmedSlot5 = "16:00";
        private string _lastConfirmedSlot6 = "18:00";
        private string _lastConfirmedSlot7 = "20:00";
        private bool _isLoadingSettings;
        private string? _hourSlotsErrorMessage;
        private string _nextScheduledAlarmText = "Calculando...";

        private AdvanceDayOption? _selectedAdvanceOption;
        private bool _isSaving;
        private bool _showSaveSuccess;
        private string? _statusMessage;

        // Propriedades de Backup e Restauração
        private string? _lastBackupInfo;
        private string? _backupStatusMessage;
        private bool _showBackupSuccess;
        private bool _showBackupError;
        private bool _isBusyBackup;

        public override string Title => "Configurações";
        public override string Description => "Preferências do sistema, notificações locais e backup/restauração de dados.";
        public override string Icon => "⚙️";
        public override string Category => "SISTEMA";

        public ObservableCollection<AdvanceDayOption> AdvanceDayOptions { get; } = new();
        public ObservableCollection<SystemSoundOption> AvailableSounds { get; } = new();

        public SystemSoundOption? SelectedSoundOption
        {
            get => _selectedSoundOption;
            set
            {
                if (SetProperty(ref _selectedSoundOption, value) && value != null)
                {
                    _soundType = value.Type;
                    OnPropertyChanged(nameof(SoundType));
                }
            }
        }

        public string SoundType
        {
            get => _soundType;
            set
            {
                if (SetProperty(ref _soundType, value))
                {
                    SyncSelectedSoundOption();
                    OnPropertyChanged(nameof(SoundStatusDisplayText));
                }
            }
        }

        private string? _customSoundPath;
        public string? CustomSoundPath
        {
            get => _customSoundPath;
            set
            {
                if (SetProperty(ref _customSoundPath, value))
                {
                    OnPropertyChanged(nameof(CustomSoundFileName));
                    OnPropertyChanged(nameof(IsCustomSoundActive));
                    OnPropertyChanged(nameof(SoundStatusDisplayText));
                }
            }
        }

        public string CustomSoundFileName => string.IsNullOrEmpty(CustomSoundPath)
            ? string.Empty
            : System.IO.Path.GetFileName(CustomSoundPath);

        public bool IsCustomSoundActive => !string.IsNullOrWhiteSpace(CustomSoundPath) && System.IO.File.Exists(CustomSoundPath);

        public string SoundStatusDisplayText => IsCustomSoundActive
            ? $"Som Personalizado: {CustomSoundFileName}"
            : $"Som do Sistema: {SelectedSoundOption?.DisplayName ?? SoundType}";

        public AppTheme CurrentAppTheme
        {
            get => _currentAppTheme;
            set
            {
                if (SetProperty(ref _currentAppTheme, value))
                {
                    OnPropertyChanged(nameof(IsDarkTheme));
                    OnPropertyChanged(nameof(IsLightTheme));
                    OnPropertyChanged(nameof(ThemeDescriptionText));
                }
            }
        }

        public bool IsDarkTheme => CurrentAppTheme == AppTheme.Dark;
        public bool IsLightTheme => CurrentAppTheme == AppTheme.Light;
        public string ThemeDescriptionText => IsDarkTheme
            ? "Escuro (Estética Solo Leveling)"
            : "Claro (Visual Comercial)";

        public ICommand SetDarkThemeCommand { get; }
        public ICommand SetLightThemeCommand { get; }
        public ICommand ToggleThemeCommand { get; }

        public bool NotificationsEnabled
        {
            get => _notificationsEnabled;
            set
            {
                if (SetProperty(ref _notificationsEnabled, value))
                {
                    OnPropertyChanged(nameof(SchedulerStatusText));
                    OnPropertyChanged(nameof(IsSchedulerActive));
                    UpdateNextScheduledAlarmText();
                    UpdateDiagnosticInfo();
                }
            }
        }

        public bool SoundEnabled
        {
            get => _soundEnabled;
            set => SetProperty(ref _soundEnabled, value);
        }

        public int AdvanceDays
        {
            get => _advanceDays;
            set
            {
                if (SetProperty(ref _advanceDays, value))
                {
                    SyncSelectedOption();
                }
            }
        }

        public AdvanceDayOption? SelectedAdvanceOption
        {
            get => _selectedAdvanceOption;
            set
            {
                if (SetProperty(ref _selectedAdvanceOption, value) && value != null)
                {
                    _advanceDays = value.Days;
                    OnPropertyChanged(nameof(AdvanceDays));
                }
            }
        }

        public bool OverdueAlertsEnabled
        {
            get => _overdueAlertsEnabled;
            set => SetProperty(ref _overdueAlertsEnabled, value);
        }

        public bool MonthlyAlertsEnabled
        {
            get => _monthlyAlertsEnabled;
            set => SetProperty(ref _monthlyAlertsEnabled, value);
        }

        public bool FifthBusinessDayAlertEnabled
        {
            get => _fifthBusinessDayAlertEnabled;
            set => SetProperty(ref _fifthBusinessDayAlertEnabled, value);
        }

        private void SetHourSlot(ref string field, ref string lastConfirmed, string value, string propertyName)
        {
            string val = NotificationSettings.TryNormalizeTime(value, out var norm) ? norm : value;
            if (SetProperty(ref field, val, propertyName))
            {
                if (NotificationSettings.IsValidTime(val))
                {
                    ValidateHourSlots(isDefinitive: true);
                }
                else
                {
                    ValidateHourSlots(isDefinitive: false);
                }

                UpdateNextScheduledAlarmText();
                UpdateDiagnosticInfo();

                // Feedback sonoro amigável de interação ao confirmar horário válido diferente do anterior
                if (!_isLoadingSettings && NotificationSettings.IsValidTime(val) && val != lastConfirmed)
                {
                    lastConfirmed = val;
                    PlayHourSlotFeedbackSound();
                }
            }
        }

        public void PlayHourSlotFeedbackSound()
        {
            if (!SoundEnabled || _isLoadingSettings) return;
            TestSound();
        }

        public void OnHourSlotLostFocus()
        {
            ValidateHourSlots(isDefinitive: true);
            UpdateDiagnosticInfo();
        }

        public string HourSlot1
        {
            get => _hourSlot1;
            set => SetHourSlot(ref _hourSlot1, ref _lastConfirmedSlot1, value, nameof(HourSlot1));
        }

        public string HourSlot2
        {
            get => _hourSlot2;
            set => SetHourSlot(ref _hourSlot2, ref _lastConfirmedSlot2, value, nameof(HourSlot2));
        }

        public string HourSlot3
        {
            get => _hourSlot3;
            set => SetHourSlot(ref _hourSlot3, ref _lastConfirmedSlot3, value, nameof(HourSlot3));
        }

        public string HourSlot4
        {
            get => _hourSlot4;
            set => SetHourSlot(ref _hourSlot4, ref _lastConfirmedSlot4, value, nameof(HourSlot4));
        }

        public string HourSlot5
        {
            get => _hourSlot5;
            set => SetHourSlot(ref _hourSlot5, ref _lastConfirmedSlot5, value, nameof(HourSlot5));
        }

        public string HourSlot6
        {
            get => _hourSlot6;
            set => SetHourSlot(ref _hourSlot6, ref _lastConfirmedSlot6, value, nameof(HourSlot6));
        }

        public string HourSlot7
        {
            get => _hourSlot7;
            set => SetHourSlot(ref _hourSlot7, ref _lastConfirmedSlot7, value, nameof(HourSlot7));
        }

        public string? HourSlotsErrorMessage
        {
            get => _hourSlotsErrorMessage;
            set
            {
                if (SetProperty(ref _hourSlotsErrorMessage, value))
                {
                    OnPropertyChanged(nameof(HasHourSlotsError));
                }
            }
        }

        public bool HasHourSlotsError => !string.IsNullOrEmpty(HourSlotsErrorMessage);

        public string NextScheduledAlarmText
        {
            get => _nextScheduledAlarmText;
            set => SetProperty(ref _nextScheduledAlarmText, value);
        }

        public bool IsSaving
        {
            get => _isSaving;
            set => SetProperty(ref _isSaving, value);
        }

        public bool ShowSaveSuccess
        {
            get => _showSaveSuccess;
            set => SetProperty(ref _showSaveSuccess, value);
        }

        public string? StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ==========================================
        // PROPRIEDADES DE BACKUP E RESTAURAÇÃO
        // ==========================================
        public string CurrentDatabasePath => _backupService?.GetCurrentDatabasePath() ?? string.Empty;

        public string? LastBackupInfo
        {
            get => _lastBackupInfo;
            set => SetProperty(ref _lastBackupInfo, value);
        }

        public string? BackupStatusMessage
        {
            get => _backupStatusMessage;
            set => SetProperty(ref _backupStatusMessage, value);
        }

        public bool ShowBackupSuccess
        {
            get => _showBackupSuccess;
            set => SetProperty(ref _showBackupSuccess, value);
        }

        public bool ShowBackupError
        {
            get => _showBackupError;
            set => SetProperty(ref _showBackupError, value);
        }

        public bool IsBusyBackup
        {
            get => _isBusyBackup;
            set => SetProperty(ref _isBusyBackup, value);
        }

        // Propriedades do Plano CobraPix (Fase 13)
        public PlanType CurrentPlan
        {
            get => _currentPlan;
            set
            {
                if (SetProperty(ref _currentPlan, value))
                {
                    OnPropertyChanged(nameof(IsPro));
                    OnPropertyChanged(nameof(PlanNameText));
                    OnPropertyChanged(nameof(ActiveClientsInfoText));
                    OnPropertyChanged(nameof(PlanUnlimitedText));
                }
            }
        }

        public bool IsPro => CurrentPlan == PlanType.Pro;
        public string PlanNameText => IsPro ? "PRO" : "FREE";

        public int ActiveClientsCount
        {
            get => _activeClientsCount;
            set
            {
                if (SetProperty(ref _activeClientsCount, value))
                {
                    OnPropertyChanged(nameof(ActiveClientsInfoText));
                }
            }
        }

        public string ActiveClientsInfoText => IsPro
            ? $"Clientes ativos: {ActiveClientsCount}"
            : $"Clientes ativos: {ActiveClientsCount} / {ProService.FreeMaxActiveClients}";

        public string PlanUnlimitedText => IsPro ? "Clientes ativos ilimitados" : string.Empty;

        public string MonthlyPriceText => "R$ 9,90 / mês";
        public string AnnualPriceText => "R$ 89,90 / ano";

        public string? ProStatusMessage
        {
            get => _proStatusMessage;
            set
            {
                if (SetProperty(ref _proStatusMessage, value))
                {
                    OnPropertyChanged(nameof(HasProStatusMessage));
                }
            }
        }

        public bool HasProStatusMessage => !string.IsNullOrEmpty(ProStatusMessage);

        public Action<string>? OpenBrowserAction { get; set; }

        // Funções delegates para injeção de caixas de diálogo (permite testes unitários desacoplados da UI)
        public Func<string?>? SaveFileDialogFunc { get; set; }
        public Func<string?>? OpenFileDialogFunc { get; set; }
        public Func<string, string, bool>? ConfirmDialogFunc { get; set; }
        public Func<string?>? SelectAudioFileDialogFunc { get; set; }
        public Action<string, string>? StartUpdaterProcessAction { get; set; }
        public Action? ShutdownApplicationAction { get; set; }

        /// <summary>
        /// Caminho opcional do banco de dados alvo para operações de backup/restauração (útil para testes isolados).
        /// </summary>
        public string? TargetDatabasePath { get; set; }

        /// <summary>
        /// Callback executado após uma restauração ou importação de backup bem-sucedida.
        /// </summary>
        public Action? OnDataImported { get; set; }

        /// <summary>
        /// Evento global disparado após qualquer restauração ou importação de backup bem-sucedida.
        /// </summary>
        public static event Action? DataRestoredOrImported;

        public ICommand SaveSettingsCommand { get; }
        public ICommand TestSoundCommand { get; }
        public ICommand SelectCustomSoundCommand { get; }
        public ICommand UseSystemSoundCommand { get; }
        public ICommand CheckNotificationsNowCommand { get; }
        public ICommand ResetDefaultsCommand { get; }
        public ICommand ResetDefaultHoursCommand { get; }
        public ICommand SortHourSlotsCommand { get; }

        public ICommand CreateBackupCommand { get; }
        public ICommand RestoreBackupCommand { get; }

        public ICommand OpenMonthlySubscriptionCommand { get; }
        public ICommand OpenAnnualSubscriptionCommand { get; }

        // Comandos de Atualização do CobraPix (Fase 8 e Fase Atualização 2)
        public ICommand CheckUpdateCommand { get; }
        public ICommand ApplyUpdateCommand { get; }
        public ICommand StartDownloadCommand { get; }
        public ICommand CancelDownloadCommand { get; }
        public ICommand DismissDownloadCommand { get; }

        // Comandos e Propriedades de Exportação para Excel (Fase Excel 1)
        public ICommand ExportToExcelCommand { get; }

        public bool IsBusyExportingExcel
        {
            get => _isBusyExportingExcel;
            set
            {
                if (SetProperty(ref _isBusyExportingExcel, value))
                {
                    (ExportToExcelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool ShowExcelSuccess
        {
            get => _showExcelSuccess;
            set => SetProperty(ref _showExcelSuccess, value);
        }

        public bool ShowExcelError
        {
            get => _showExcelError;
            set => SetProperty(ref _showExcelError, value);
        }

        public string? ExcelStatusMessage
        {
            get => _excelStatusMessage;
            set
            {
                if (SetProperty(ref _excelStatusMessage, value))
                {
                    OnPropertyChanged(nameof(HasExcelStatusMessage));
                }
            }
        }

        public bool HasExcelStatusMessage => !string.IsNullOrEmpty(ExcelStatusMessage);

        public Func<string?>? SaveExcelFileDialogFunc { get; set; }

        // Navegação por Abas (Fase Corretiva Maior)
        private int _selectedTabIndex = 0;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public ICommand SelectTabCommand { get; }

        // Painel de Diagnóstico em Tempo Real
        private string _currentLocalTimeText = DateTime.Now.ToString("HH:mm:ss");
        public string CurrentLocalTimeText
        {
            get => _currentLocalTimeText;
            private set => SetProperty(ref _currentLocalTimeText, value);
        }

        private string _currentTimeZoneText = TimeZoneInfo.Local.DisplayName;
        public string CurrentTimeZoneText
        {
            get => _currentTimeZoneText;
            private set => SetProperty(ref _currentTimeZoneText, value);
        }

        public string SchedulerStatusText => NotificationsEnabled ? "ATIVO" : "DESATIVADO";
        public bool IsSchedulerActive => NotificationsEnabled;

        private string _lastVerificationText = "Ainda não executado nesta sessão";
        public string LastVerificationText
        {
            get => _lastVerificationText;
            private set => SetProperty(ref _lastVerificationText, value);
        }

        public void UpdateDiagnosticInfo()
        {
            CurrentLocalTimeText = DateTime.Now.ToString("HH:mm:ss");
            CurrentTimeZoneText = TimeZoneInfo.Local.DisplayName;
            OnPropertyChanged(nameof(SchedulerStatusText));
            OnPropertyChanged(nameof(IsSchedulerActive));

            if (_notificationService.LastVerificationTime.HasValue)
            {
                var dt = _notificationService.LastVerificationTime.Value;
                string day = dt.Date == DateTime.Today ? "Hoje" : dt.ToString("dd/MM/yyyy");
                LastVerificationText = $"{day} às {dt:HH:mm:ss}";
            }
            else
            {
                LastVerificationText = "Ainda não executado nesta sessão";
            }

            UpdateNextScheduledAlarmText();
        }

        // Propriedades públicas de Atualização (Fase 8)
        public string InstalledVersion => _updateService.CurrentVersion;
        public string InstalledVersionText => $"Versão instalada: {_updateService.CurrentVersion}";
        public const string UpdateNoticeText = "Atualize somente se estiver enfrentando algum problema ou se quiser experimentar uma nova funcionalidade. Antes de atualizar, recomendamos fazer um backup dos seus dados.";

        public bool IsCheckingUpdate
        {
            get => _isCheckingUpdate;
            set
            {
                if (SetProperty(ref _isCheckingUpdate, value))
                {
                    (CheckUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string? UpdateStatusMessage
        {
            get => _updateStatusMessage;
            set
            {
                if (SetProperty(ref _updateStatusMessage, value))
                {
                    OnPropertyChanged(nameof(HasUpdateStatusMessage));
                }
            }
        }

        public bool HasUpdateStatusMessage => !string.IsNullOrEmpty(UpdateStatusMessage);

        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set
            {
                if (SetProperty(ref _isUpdateAvailable, value))
                {
                    (ApplyUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string? RemoteVersionText
        {
            get => _remoteVersionText;
            set => SetProperty(ref _remoteVersionText, value);
        }

        public string? RemoteSha256
        {
            get => _remoteSha256;
            set
            {
                if (SetProperty(ref _remoteSha256, value))
                {
                    OnPropertyChanged(nameof(HasRemoteSha256));
                }
            }
        }

        public bool HasRemoteSha256 => !string.IsNullOrWhiteSpace(RemoteSha256);

        public ObservableCollection<string> ReleaseNotes { get; } = new();

        public bool HasReleaseNotes => ReleaseNotes.Count > 0;

        public bool ShowUpdateSuccess
        {
            get => _showUpdateSuccess;
            set => SetProperty(ref _showUpdateSuccess, value);
        }

        public bool ShowUpdateError
        {
            get => _showUpdateError;
            set => SetProperty(ref _showUpdateError, value);
        }

        public bool ShowUpdateInfo
        {
            get => _showUpdateInfo;
            set => SetProperty(ref _showUpdateInfo, value);
        }

        // Propriedades de Download de Atualização (Fase Atualização 2)
        public bool IsDownloadingUpdate
        {
            get => _isDownloadingUpdate;
            set
            {
                if (SetProperty(ref _isDownloadingUpdate, value))
                {
                    OnPropertyChanged(nameof(CanStartDownload));
                    (StartDownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (CancelDownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public double DownloadProgressPercentage
        {
            get => _downloadProgressPercentage;
            set => SetProperty(ref _downloadProgressPercentage, value);
        }

        public string? DownloadProgressText
        {
            get => _downloadProgressText;
            set => SetProperty(ref _downloadProgressText, value);
        }

        public string? DownloadPercentageText
        {
            get => _downloadPercentageText;
            set => SetProperty(ref _downloadPercentageText, value);
        }

        public string? DownloadStatusText
        {
            get => _downloadStatusText;
            set => SetProperty(ref _downloadStatusText, value);
        }

        public bool IsDownloadCompleted
        {
            get => _isDownloadCompleted;
            set
            {
                if (SetProperty(ref _isDownloadCompleted, value))
                {
                    OnPropertyChanged(nameof(CanStartDownload));
                    OnPropertyChanged(nameof(CanApplyUpdate));
                    (StartDownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ApplyUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsDownloadValid
        {
            get => _isDownloadValid;
            set
            {
                if (SetProperty(ref _isDownloadValid, value))
                {
                    OnPropertyChanged(nameof(CanApplyUpdate));
                    (ApplyUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string? DownloadedFilePath
        {
            get => _downloadedFilePath;
            set
            {
                if (SetProperty(ref _downloadedFilePath, value))
                {
                    OnPropertyChanged(nameof(CanApplyUpdate));
                    (ApplyUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string? RemoteDownloadUrl
        {
            get => _remoteDownloadUrl;
            set => SetProperty(ref _remoteDownloadUrl, value);
        }

        public string? RemoteVersion
        {
            get => _remoteVersion;
            set => SetProperty(ref _remoteVersion, value);
        }

        public bool CanStartDownload => IsUpdateAvailable && !IsDownloadingUpdate && !IsDownloadCompleted;

        public bool CanApplyUpdate => IsUpdateAvailable && IsDownloadCompleted && IsDownloadValid && !string.IsNullOrWhiteSpace(DownloadedFilePath);

        public ConfiguracoesViewModel()
            : this(
                App.Services?.GetService<INotificationService>() ?? new NotificationService(new CobraPixDbContext(), new PaymentService(new CobraPixDbContext()), new MonthlyCustomerService(new CobraPixDbContext())),
                App.Services?.GetService<IBackupService>() ?? new BackupService(new CobraPixDbContext()),
                App.Services?.GetService<IProService>() ?? new ProService(new CobraPixDbContext()),
                App.Services?.GetService<IThemeService>() ?? new ThemeService(new CobraPixDbContext()),
                App.Services?.GetService<IAndroidImportService>() ?? new AndroidImportService(new CobraPixDbContext()),
                App.Services?.GetService<IUpdateService>() ?? new UpdateService(),
                App.Services?.GetService<IExcelExportService>() ?? new ExcelExportService(new CobraPixDbContext()),
                App.Services?.GetService<IUpdateDownloadService>() ?? new UpdateDownloadService())
        {
        }

        public ConfiguracoesViewModel(INotificationService notificationService, IBackupService? backupService = null)
            : this(notificationService, backupService, null, null, null, null, null, null)
        {
        }

        public ConfiguracoesViewModel(
            INotificationService notificationService,
            IBackupService? backupService,
            IProService? proService)
            : this(notificationService, backupService, proService, App.Services?.GetService<IThemeService>(), App.Services?.GetService<IAndroidImportService>(), App.Services?.GetService<IUpdateService>(), null, null)
        {
        }

        public ConfiguracoesViewModel(
            INotificationService notificationService,
            IBackupService? backupService,
            IProService? proService,
            IThemeService? themeService)
            : this(notificationService, backupService, proService, themeService, App.Services?.GetService<IAndroidImportService>(), App.Services?.GetService<IUpdateService>(), null, null)
        {
        }

        public ConfiguracoesViewModel(
            INotificationService notificationService,
            IBackupService? backupService,
            IProService? proService,
            IThemeService? themeService,
            IAndroidImportService? androidImportService)
            : this(notificationService, backupService, proService, themeService, androidImportService, App.Services?.GetService<IUpdateService>(), null, null)
        {
        }

        public ConfiguracoesViewModel(
            INotificationService notificationService,
            IBackupService? backupService,
            IProService? proService,
            IThemeService? themeService,
            IAndroidImportService? androidImportService,
            IUpdateService? updateService)
            : this(notificationService, backupService, proService, themeService, androidImportService, updateService, null, null)
        {
        }

        public ConfiguracoesViewModel(
            INotificationService notificationService,
            IUpdateService updateService,
            IUpdateDownloadService updateDownloadService)
            : this(notificationService, null, null, null, null, updateService, null, updateDownloadService)
        {
        }

        public ConfiguracoesViewModel(
            INotificationService notificationService,
            IBackupService? backupService,
            IProService? proService,
            IThemeService? themeService,
            IAndroidImportService? androidImportService,
            IUpdateService? updateService,
            IExcelExportService? excelExportService,
            IUpdateDownloadService? updateDownloadService = null)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _backupService = backupService;
            _proService = proService;
            _themeService = themeService ?? App.Services?.GetService<IThemeService>();
            _androidImportService = androidImportService ?? App.Services?.GetService<IAndroidImportService>();
            _updateService = updateService ?? App.Services?.GetService<IUpdateService>() ?? new UpdateService();
            _excelExportService = excelExportService ?? App.Services?.GetService<IExcelExportService>() ?? new ExcelExportService(new CobraPixDbContext());
            _updateDownloadService = updateDownloadService ?? App.Services?.GetService<IUpdateDownloadService>() ?? new UpdateDownloadService(_updateService);

            if (_proService != null)
            {
                _proService.PlanChanged += (s, e) => { _ = LoadPlanInfoAsync(); };
            }

            AdvanceDayOptions.Add(new AdvanceDayOption(0, "No dia do vencimento (0 dias)"));
            AdvanceDayOptions.Add(new AdvanceDayOption(1, "1 dia antes"));
            AdvanceDayOptions.Add(new AdvanceDayOption(2, "2 dias antes"));
            AdvanceDayOptions.Add(new AdvanceDayOption(3, "3 dias antes"));
            AdvanceDayOptions.Add(new AdvanceDayOption(5, "5 dias antes"));

            AvailableSounds.Add(new SystemSoundOption("Asterisk", "Asterisk (Padrão Windows)", "Som nativo discreto de notificação"));
            AvailableSounds.Add(new SystemSoundOption("Beep", "Beep (Alerta Rápido)", "Sinal sonoro clássico e curto"));
            AvailableSounds.Add(new SystemSoundOption("Exclamation", "Exclamation (Aviso Importante)", "Sinal de exclamação e atenção"));
            AvailableSounds.Add(new SystemSoundOption("Hand", "Hand (Alerta Enérgico)", "Alerta firme para prioridades"));
            AvailableSounds.Add(new SystemSoundOption("Question", "Question (Notificação Suave)", "Aviso suave e discreto"));

            SaveSettingsCommand = new RelayCommand(async () => await SaveSettingsAsync());
            TestSoundCommand = new RelayCommand(() => TestSound());
            SelectCustomSoundCommand = new RelayCommand(async () => await SelectCustomSoundAsync());
            UseSystemSoundCommand = new RelayCommand(async () => await UseSystemSoundAsync());
            CheckNotificationsNowCommand = new RelayCommand(async () => await CheckNotificationsNowAsync());
            ResetDefaultsCommand = new RelayCommand(async () => await ResetToDefaultsAsync());
            ResetDefaultHoursCommand = new RelayCommand(() => ResetDefaultHours());
            SortHourSlotsCommand = new RelayCommand(() => SortHourSlots());

            SetDarkThemeCommand = new RelayCommand(async () => await SetAppThemeAsync(AppTheme.Dark));
            SetLightThemeCommand = new RelayCommand(async () => await SetAppThemeAsync(AppTheme.Light));
            ToggleThemeCommand = new RelayCommand(async () => await SetAppThemeAsync(IsDarkTheme ? AppTheme.Light : AppTheme.Dark));

            CreateBackupCommand = new RelayCommand(async () => await CreateBackupAsync());
            RestoreBackupCommand = new RelayCommand(async () => await RestoreBackupAsync());
            ExportToExcelCommand = new RelayCommand(async () => await ExportToExcelAsync(), () => !IsBusyExportingExcel);

            OpenMonthlySubscriptionCommand = new RelayCommand(async () => await HandleSubscriptionClickAsync(isAnnual: false));
            OpenAnnualSubscriptionCommand = new RelayCommand(async () => await HandleSubscriptionClickAsync(isAnnual: true));

            CheckUpdateCommand = new RelayCommand(async () => await CheckForUpdatesAsync(), () => !IsCheckingUpdate);
            ApplyUpdateCommand = new RelayCommand(async () => await ApplyUpdateAsync(), () => IsUpdateAvailable);
            StartDownloadCommand = new RelayCommand(async () => await StartDownloadAsync(), () => CanStartDownload);
            CancelDownloadCommand = new RelayCommand(() => CancelDownload(), () => IsDownloadingUpdate);
            DismissDownloadCommand = new RelayCommand(() => DismissDownload());

            SelectTabCommand = new RelayCommand<object>(param =>
            {
                if (param is int idx) SelectedTabIndex = idx;
                else if (param != null && int.TryParse(param.ToString(), out int pIdx)) SelectedTabIndex = pIdx;
            });

            try
            {
                if (Application.Current != null && Application.Current.Dispatcher != null)
                {
                    var clockTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1)
                    };
                    clockTimer.Tick += (s, e) =>
                    {
                        CurrentLocalTimeText = DateTime.Now.ToString("HH:mm:ss");
                    };
                    clockTimer.Start();
                }
            }
            catch { }

            _ = LoadSettingsAsync();
            _ = LoadBackupInfoAsync();
            _ = LoadPlanInfoAsync();
            _ = LoadThemeInfoAsync();
        }

        private void SyncSelectedSoundOption()
        {
            SelectedSoundOption = AvailableSounds.FirstOrDefault(s => s.Type.Equals(_soundType, StringComparison.OrdinalIgnoreCase))
                                 ?? AvailableSounds.FirstOrDefault();
        }

        public async Task SetAppThemeAsync(AppTheme theme)
        {
            CurrentAppTheme = theme;
            var themeSvc = _themeService ?? App.Services?.GetService<IThemeService>();
            if (themeSvc != null)
            {
                await themeSvc.SetThemeAsync(theme);
            }
        }

        public async Task LoadThemeInfoAsync()
        {
            var themeSvc = _themeService ?? App.Services?.GetService<IThemeService>();
            if (themeSvc != null)
            {
                CurrentAppTheme = await themeSvc.GetThemeAsync();
            }
        }

        public async Task LoadSettingsAsync()
        {
            _isLoadingSettings = true;
            try
            {
                var settings = await _notificationService.GetSettingsAsync();

                NotificationsEnabled = settings.NotificationsEnabled;
                SoundEnabled = settings.SoundEnabled;
                AdvanceDays = settings.AdvanceDays;
                OverdueAlertsEnabled = settings.OverdueAlertsEnabled;
                MonthlyAlertsEnabled = settings.MonthlyAlertsEnabled;
                FifthBusinessDayAlertEnabled = settings.FifthBusinessDayAlertEnabled;
                SoundType = settings.SoundType;
                CustomSoundPath = settings.CustomSoundPath;

                HourSlot1 = settings.HourSlot1;
                HourSlot2 = settings.HourSlot2;
                HourSlot3 = settings.HourSlot3;
                HourSlot4 = settings.HourSlot4;
                HourSlot5 = settings.HourSlot5;
                HourSlot6 = settings.HourSlot6;
                HourSlot7 = settings.HourSlot7;

                _lastConfirmedSlot1 = HourSlot1;
                _lastConfirmedSlot2 = HourSlot2;
                _lastConfirmedSlot3 = HourSlot3;
                _lastConfirmedSlot4 = HourSlot4;
                _lastConfirmedSlot5 = HourSlot5;
                _lastConfirmedSlot6 = HourSlot6;
                _lastConfirmedSlot7 = HourSlot7;

                SyncSelectedOption();
                SyncSelectedSoundOption();
                ValidateHourSlots(isDefinitive: true);
                UpdateNextScheduledAlarmText();
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        public async Task LoadBackupInfoAsync()
        {
            if (_backupService != null)
            {
                var info = await _backupService.GetLastBackupInfoAsync();
                LastBackupInfo = !string.IsNullOrEmpty(info) ? info : "Nenhum backup realizado nesta máquina.";
            }
            else
            {
                LastBackupInfo = "Serviço de backup não inicializado.";
            }
        }

        public async Task SaveSettingsAsync()
        {
            IsSaving = true;
            ShowSaveSuccess = false;
            StatusMessage = null;

            // Normaliza entradas pendentes caso o usuário tenha clicado diretamente em Salvar
            if (NotificationSettings.TryNormalizeTime(_hourSlot1, out var n1)) _hourSlot1 = n1;
            if (NotificationSettings.TryNormalizeTime(_hourSlot2, out var n2)) _hourSlot2 = n2;
            if (NotificationSettings.TryNormalizeTime(_hourSlot3, out var n3)) _hourSlot3 = n3;
            if (NotificationSettings.TryNormalizeTime(_hourSlot4, out var n4)) _hourSlot4 = n4;
            if (NotificationSettings.TryNormalizeTime(_hourSlot5, out var n5)) _hourSlot5 = n5;
            if (NotificationSettings.TryNormalizeTime(_hourSlot6, out var n6)) _hourSlot6 = n6;
            if (NotificationSettings.TryNormalizeTime(_hourSlot7, out var n7)) _hourSlot7 = n7;
            OnPropertyChanged(nameof(HourSlot1));
            OnPropertyChanged(nameof(HourSlot2));
            OnPropertyChanged(nameof(HourSlot3));
            OnPropertyChanged(nameof(HourSlot4));
            OnPropertyChanged(nameof(HourSlot5));
            OnPropertyChanged(nameof(HourSlot6));
            OnPropertyChanged(nameof(HourSlot7));

            if (!ValidateHourSlots(isDefinitive: true))
            {
                IsSaving = false;
                StatusMessage = HourSlotsErrorMessage ?? "Corrija os horários de notificação antes de salvar.";
                return;
            }

            try
            {
                var settings = new NotificationSettings
                {
                    NotificationsEnabled = NotificationsEnabled,
                    SoundEnabled = SoundEnabled,
                    AdvanceDays = SelectedAdvanceOption?.Days ?? AdvanceDays,
                    OverdueAlertsEnabled = OverdueAlertsEnabled,
                    MonthlyAlertsEnabled = MonthlyAlertsEnabled,
                    FifthBusinessDayAlertEnabled = FifthBusinessDayAlertEnabled,
                    SoundType = SelectedSoundOption?.Type ?? SoundType,
                    CustomSoundPath = CustomSoundPath,
                    HourSlot1 = HourSlot1,
                    HourSlot2 = HourSlot2,
                    HourSlot3 = HourSlot3,
                    HourSlot4 = HourSlot4,
                    HourSlot5 = HourSlot5,
                    HourSlot6 = HourSlot6,
                    HourSlot7 = HourSlot7
                };

                await _notificationService.SaveSettingsAsync(settings);
                ShowSaveSuccess = true;
                StatusMessage = "Configurações de notificações salvas com sucesso!";
                UpdateNextScheduledAlarmText();
                UpdateDiagnosticInfo();
            }
            catch (Exception ex)
            {
                StatusMessage = "Erro ao salvar configurações: " + ex.Message;
            }
            finally
            {
                IsSaving = false;
            }
        }

        public void TestSound()
        {
            if (IsCustomSoundActive)
            {
                _notificationService.PlaySound(null, CustomSoundPath);
            }
            else
            {
                _notificationService.PlaySound(SelectedSoundOption?.Type ?? SoundType, null);
            }
        }

        public async Task SelectCustomSoundAsync()
        {
            StatusMessage = null;
            ShowSaveSuccess = false;

            string? chosen = (SelectAudioFileDialogFunc != null) ? SelectAudioFileDialogFunc() : DefaultAudioFileDialog();
            if (string.IsNullOrWhiteSpace(chosen))
            {
                return;
            }

            if (!chosen.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Apenas arquivos de áudio no formato .wav são suportados pelo sistema de alertas locais.";
                ShowSaveSuccess = false;
                return;
            }

            if (!System.IO.File.Exists(chosen))
            {
                StatusMessage = "Arquivo de áudio selecionado não foi encontrado no disco.";
                ShowSaveSuccess = false;
                return;
            }

            try
            {
                string localDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CobraPix",
                    "sounds");
                System.IO.Directory.CreateDirectory(localDir);

                string fileName = System.IO.Path.GetFileName(chosen);
                string destPath = System.IO.Path.Combine(localDir, fileName);

                if (!chosen.Equals(destPath, StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.File.Copy(chosen, destPath, overwrite: true);
                }

                CustomSoundPath = destPath;
                await SaveSettingsAsync();

                TestSound();

                StatusMessage = $"Som personalizado '{fileName}' configurado e testado com sucesso!";
                ShowSaveSuccess = true;
            }
            catch (Exception ex)
            {
                StatusMessage = "Erro ao configurar áudio personalizado: " + ex.Message;
                ShowSaveSuccess = false;
            }
        }

        public async Task UseSystemSoundAsync()
        {
            CustomSoundPath = null;
            await SaveSettingsAsync();
            TestSound();
            StatusMessage = "Sistema redefinido para usar sons nativos do Windows.";
            ShowSaveSuccess = true;
        }

        private static string? DefaultAudioFileDialog()
        {
            try
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Selecionar Som de Alerta Personalizado (.wav)",
                    Filter = "Arquivos de Áudio (*.wav)|*.wav",
                    DefaultExt = ".wav"
                };
                return ofd.ShowDialog() == true ? ofd.FileName : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task CheckNotificationsNowAsync()
        {
            StatusMessage = "Verificando notificações locais...";
            try
            {
                int count = await _notificationService.CheckAndNotifyAsync(DateTime.Today, force: true);
                UpdateDiagnosticInfo();
                StatusMessage = count > 0
                    ? $"{count} alerta(s) de cobrança identificado(s) e apresentado(s) com sucesso!"
                    : "Nenhum alerta pendente no momento (sistema em dia).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Falha ao verificar alertas: " + ex.Message;
            }
        }

        public async Task ResetToDefaultsAsync()
        {
            NotificationsEnabled = true;
            SoundEnabled = true;
            AdvanceDays = 1;
            OverdueAlertsEnabled = true;
            MonthlyAlertsEnabled = true;
            FifthBusinessDayAlertEnabled = true;
            CustomSoundPath = null;
            ResetDefaultHours();
            SyncSelectedOption();
            await SaveSettingsAsync();
        }

        public bool ValidateHourSlots() => ValidateHourSlots(true);

        public bool ValidateHourSlots(bool isDefinitive)
        {
            var slots = new[]
            {
                ("Aviso 1", _hourSlot1),
                ("Aviso 2", _hourSlot2),
                ("Aviso 3", _hourSlot3),
                ("Aviso 4", _hourSlot4),
                ("Aviso 5", _hourSlot5),
                ("Aviso 6", _hourSlot6),
                ("Aviso 7", _hourSlot7)
            };

            foreach (var (name, slot) in slots)
            {
                // Se a validação não for definitiva (usuário ainda construindo o texto no campo):
                if (!isDefinitive)
                {
                    if (string.IsNullOrWhiteSpace(slot)) continue;
                    string trimmed = slot.Trim();
                    bool hasSep = trimmed.Contains(':') || trimmed.Contains('h') || trimmed.Contains('H') || trimmed.Contains(' ');
                    if (trimmed.Length < 4 && !hasSep) continue;
                    if (trimmed.Length < 5 && hasSep) continue;
                }

                if (string.IsNullOrWhiteSpace(slot))
                {
                    HourSlotsErrorMessage = $"{name} não pode ficar em branco. Utilize o formato 24h HH:mm (00:00 a 23:59).";
                    return false;
                }

                if (!NotificationSettings.IsValidTime(slot))
                {
                    HourSlotsErrorMessage = $"O valor '{slot}' em {name} é inválido. Utilize o formato 24h HH:mm (00:00 a 23:59).";
                    return false;
                }
            }

            HourSlotsErrorMessage = null;
            return true;
        }

        public void UpdateNextScheduledAlarmText()
        {
            try
            {
                var settings = new NotificationSettings
                {
                    NotificationsEnabled = NotificationsEnabled,
                    HourSlot1 = HourSlot1,
                    HourSlot2 = HourSlot2,
                    HourSlot3 = HourSlot3,
                    HourSlot4 = HourSlot4,
                    HourSlot5 = HourSlot5,
                    HourSlot6 = HourSlot6,
                    HourSlot7 = HourSlot7
                };

                if (!settings.NotificationsEnabled)
                {
                    NextScheduledAlarmText = "Notificações desativadas";
                    return;
                }

                var next = _notificationService.GetNextScheduledTime(settings);
                string dayText = next.Date == DateTime.Today ? "Hoje" : "Amanhã";
                NextScheduledAlarmText = $"{dayText} às {next:HH:mm}";
            }
            catch
            {
                NextScheduledAlarmText = "--:--";
            }
        }

        public void ResetDefaultHours()
        {
            _hourSlot1 = "08:00";
            _hourSlot2 = "10:00";
            _hourSlot3 = "12:00";
            _hourSlot4 = "14:00";
            _hourSlot5 = "16:00";
            _hourSlot6 = "18:00";
            _hourSlot7 = "20:00";

            _lastConfirmedSlot1 = _hourSlot1;
            _lastConfirmedSlot2 = _hourSlot2;
            _lastConfirmedSlot3 = _hourSlot3;
            _lastConfirmedSlot4 = _hourSlot4;
            _lastConfirmedSlot5 = _hourSlot5;
            _lastConfirmedSlot6 = _hourSlot6;
            _lastConfirmedSlot7 = _hourSlot7;

            OnPropertyChanged(nameof(HourSlot1));
            OnPropertyChanged(nameof(HourSlot2));
            OnPropertyChanged(nameof(HourSlot3));
            OnPropertyChanged(nameof(HourSlot4));
            OnPropertyChanged(nameof(HourSlot5));
            OnPropertyChanged(nameof(HourSlot6));
            OnPropertyChanged(nameof(HourSlot7));

            ValidateHourSlots(true);
            UpdateNextScheduledAlarmText();
            PlayHourSlotFeedbackSound();
        }

        public void SortHourSlots()
        {
            var raw = new[] { _hourSlot1, _hourSlot2, _hourSlot3, _hourSlot4, _hourSlot5, _hourSlot6, _hourSlot7 };
            var list = new System.Collections.Generic.List<TimeSpan>();
            foreach (var item in raw)
            {
                if (NotificationSettings.TryParseTime(item, out var ts))
                {
                    list.Add(ts);
                }
            }

            if (list.Count == 7)
            {
                list.Sort();
                _hourSlot1 = $"{list[0].Hours:D2}:{list[0].Minutes:D2}";
                _hourSlot2 = $"{list[1].Hours:D2}:{list[1].Minutes:D2}";
                _hourSlot3 = $"{list[2].Hours:D2}:{list[2].Minutes:D2}";
                _hourSlot4 = $"{list[3].Hours:D2}:{list[3].Minutes:D2}";
                _hourSlot5 = $"{list[4].Hours:D2}:{list[4].Minutes:D2}";
                _hourSlot6 = $"{list[5].Hours:D2}:{list[5].Minutes:D2}";
                _hourSlot7 = $"{list[6].Hours:D2}:{list[6].Minutes:D2}";

                _lastConfirmedSlot1 = _hourSlot1;
                _lastConfirmedSlot2 = _hourSlot2;
                _lastConfirmedSlot3 = _hourSlot3;
                _lastConfirmedSlot4 = _hourSlot4;
                _lastConfirmedSlot5 = _hourSlot5;
                _lastConfirmedSlot6 = _hourSlot6;
                _lastConfirmedSlot7 = _hourSlot7;

                OnPropertyChanged(nameof(HourSlot1));
                OnPropertyChanged(nameof(HourSlot2));
                OnPropertyChanged(nameof(HourSlot3));
                OnPropertyChanged(nameof(HourSlot4));
                OnPropertyChanged(nameof(HourSlot5));
                OnPropertyChanged(nameof(HourSlot6));
                OnPropertyChanged(nameof(HourSlot7));

                ValidateHourSlots(true);
                UpdateNextScheduledAlarmText();
                PlayHourSlotFeedbackSound();
            }
        }

        // ==========================================
        // FLUXO DE CRIAÇÃO DE BACKUP (.cpb)
        // ==========================================
        public async Task CreateBackupAsync()
        {
            if (_backupService == null) return;

            ShowBackupSuccess = false;
            ShowBackupError = false;
            BackupStatusMessage = null;

            string? targetFile = (SaveFileDialogFunc != null) ? SaveFileDialogFunc() : DefaultSaveFileDialog();
            if (string.IsNullOrWhiteSpace(targetFile))
            {
                // Usuário cancelou o diálogo de salvamento
                return;
            }

            IsBusyBackup = true;
            try
            {
                var result = await _backupService.CreateBackupAsync(targetFile);
                if (result.Success)
                {
                    ShowBackupSuccess = true;
                    BackupStatusMessage = $"Backup gerado com sucesso! Arquivo: {targetFile} ({result.FileSize:N0} bytes).";
                    await LoadBackupInfoAsync();
                }
                else
                {
                    ShowBackupError = true;
                    BackupStatusMessage = result.Message;
                }
            }
            catch (Exception ex)
            {
                ShowBackupError = true;
                BackupStatusMessage = "Erro ao criar backup: " + ex.Message;
            }
            finally
            {
                IsBusyBackup = false;
            }
        }

        // ==========================================
        // FLUXO DE RESTAURAÇÃO DE BACKUP (.cpb)
        // ==========================================
        public async Task RestoreBackupAsync()
        {
            if (_backupService == null) return;

            ShowBackupSuccess = false;
            ShowBackupError = false;
            BackupStatusMessage = null;

            string? selectedFile = (OpenFileDialogFunc != null) ? OpenFileDialogFunc() : DefaultOpenFileDialog();
            if (string.IsNullOrWhiteSpace(selectedFile))
            {
                // Usuário cancelou o diálogo de seleção
                return;
            }

            // 1. Validação prévia da integridade do arquivo .cpb
            var validation = _backupService.ValidateBackupFile(selectedFile);
            if (!validation.IsValid)
            {
                ShowBackupError = true;
                BackupStatusMessage = "Arquivo inválido para restauração: " + validation.ErrorMessage;
                return;
            }

            // 2. Se for backup Android JSON: confirmação específica e importação sem sobrescrever banco PC
            if (validation.IsAndroid && (validation.IsAndroidJson || (_androidImportService != null && _androidImportService.IsAndroidJsonBackup(selectedFile))))
            {
                var summary = validation.AndroidSummary ?? _androidImportService?.GetSummary(selectedFile);
                int clients = summary?.ClientsCount ?? 0;
                int sales = summary?.SalesCount ?? 0;
                int installments = summary?.InstallmentsCount ?? 0;
                int payments = summary?.InstallmentPaymentsCount ?? 0;
                int monthlyCustomers = summary?.MonthlyCustomersCount ?? 0;
                int monthlyPayments = summary?.MonthlyPaymentsCount ?? 0;
                int expenses = summary?.ExpensesCount ?? 0;

                string confirmMsg =
                    $"Backup do CobraPix Android encontrado.\n\n" +
                    $"Clientes: {clients}\n" +
                    $"Vendas: {sales}\n" +
                    $"Parcelas: {installments}\n" +
                    $"Pagamentos de parcelas: {payments}\n" +
                    $"Mensalistas: {monthlyCustomers}\n" +
                    $"Pagamentos mensais: {monthlyPayments}\n" +
                    $"Despesas encontradas: {expenses}\n\n" +
                    $"Os clientes serão adicionados ao banco atual do PC.\n\n" +
                    $"Deseja continuar?";

                bool confirmed = (ConfirmDialogFunc != null)
                    ? ConfirmDialogFunc("Importar Backup do Android", confirmMsg)
                    : DefaultConfirmDialog("Importar Backup do Android", confirmMsg);

                if (!confirmed)
                {
                    BackupStatusMessage = "Importação cancelada pelo usuário. Os dados atuais foram preservados.";
                    return;
                }

                IsBusyBackup = true;
                try
                {
                    AndroidImportResult importResult;
                    if (_androidImportService != null)
                    {
                        importResult = await _androidImportService.ImportAsync(selectedFile, TargetDatabasePath);
                    }
                    else
                    {
                        var restRes = await _backupService.RestoreBackupAsync(selectedFile, TargetDatabasePath);
                        importResult = new AndroidImportResult
                        {
                            Success = restRes.Success,
                            Message = restRes.Message,
                            SafetyBackupPath = restRes.SafetyBackupPath
                        };
                    }

                    if (importResult.Success)
                    {
                        ShowBackupSuccess = true;
                        ShowBackupError = false;

                        string resultMsg =
                            $"Importação concluída.\n\n" +
                            $"Clientes importados: {importResult.ClientsImported}\n" +
                            $"Vendas importadas: {importResult.SalesImported}\n" +
                            $"Parcelas importadas: {importResult.InstallmentsImported}\n" +
                            $"Pagamentos de parcelas: {importResult.InstallmentPaymentsImported}\n" +
                            $"Mensalistas importados: {importResult.MonthlyCustomersImported}\n" +
                            $"Pagamentos mensais: {importResult.MonthlyPaymentsImported}\n" +
                            $"Despesas não importadas: {importResult.ExpensesSkipped}";

                        if (importResult.Warnings.Count > 0)
                        {
                            resultMsg += $"\n\nAvisos ({importResult.Warnings.Count}):\n" + string.Join("\n", importResult.Warnings.Take(5));
                            if (importResult.Warnings.Count > 5)
                                resultMsg += $"\n... e mais {importResult.Warnings.Count - 5} avisos.";
                        }

                        BackupStatusMessage = resultMsg;
                        await LoadSettingsAsync();
                        await LoadBackupInfoAsync();

                        try
                        {
                            OnDataImported?.Invoke();
                            DataRestoredOrImported?.Invoke();
                        }
                        catch { }
                    }
                    else
                    {
                        ShowBackupError = true;
                        ShowBackupSuccess = false;
                        BackupStatusMessage = importResult.Message;
                    }
                }
                catch (Exception ex)
                {
                    ShowBackupError = true;
                    BackupStatusMessage = "Falha durante a importação do backup Android: " + ex.Message;
                }
                finally
                {
                    IsBusyBackup = false;
                }
                return;
            }

            // 3. Confirmação explícita obrigatória do usuário para backup PC (ZIP) ou legado
            string pcConfirmMsg = $"Restaurar este backup substituirá os dados atuais do CobraPix PC.\n\n" +
                                $"Arquivo: {System.IO.Path.GetFileName(selectedFile)}\n" +
                                $"Data do Backup: {validation.Metadata?.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}\n" +
                                $"Tamanho original do banco: {validation.Metadata?.DatabaseSize:N0} bytes\n\n" +
                                $"Um backup de segurança do banco atual será criado automaticamente antes da substituição.\n\n" +
                                $"Deseja realmente continuar?";

            bool confirmedPc = (ConfirmDialogFunc != null)
                ? ConfirmDialogFunc("Confirmação de Restauração", pcConfirmMsg)
                : DefaultConfirmDialog("Confirmação de Restauração", pcConfirmMsg);

            if (!confirmedPc)
            {
                BackupStatusMessage = "Restauração cancelada pelo usuário. Os dados atuais foram preservados.";
                return;
            }

            IsBusyBackup = true;
            try
            {
                var restoreResult = await _backupService.RestoreBackupAsync(selectedFile, TargetDatabasePath);
                if (restoreResult.Success)
                {
                    ShowBackupSuccess = true;
                    BackupStatusMessage = $"Banco restaurado com sucesso!\n" +
                                          $"Backup de segurança preservado em: {restoreResult.SafetyBackupPath}\n" +
                                          $"Recomendamos reiniciar a aplicação para atualizar todas as telas.";
                    await LoadSettingsAsync();
                    await LoadBackupInfoAsync();

                    try
                    {
                        OnDataImported?.Invoke();
                        DataRestoredOrImported?.Invoke();
                    }
                    catch { }
                }
                else
                {
                    ShowBackupError = true;
                    BackupStatusMessage = restoreResult.Message;
                }
            }
            catch (Exception ex)
            {
                ShowBackupError = true;
                BackupStatusMessage = "Falha crítica durante restauração: " + ex.Message;
            }
            finally
            {
                IsBusyBackup = false;
            }
        }

        private static string? DefaultSaveFileDialog()
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Salvar Backup do CobraPix PC",
                    Filter = "CobraPix Backup (*.cpb)|*.cpb",
                    DefaultExt = ".cpb",
                    FileName = $"CobraPix_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.cpb"
                };
                return sfd.ShowDialog() == true ? sfd.FileName : null;
            }
            catch
            {
                return null;
            }
        }

        private static string? DefaultOpenFileDialog()
        {
            try
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Selecionar Backup do CobraPix PC",
                    Filter = "CobraPix Backup (*.cpb)|*.cpb",
                    DefaultExt = ".cpb"
                };
                return ofd.ShowDialog() == true ? ofd.FileName : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool DefaultConfirmDialog(string title, string message)
        {
            try
            {
                var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                return result == MessageBoxResult.Yes;
            }
            catch
            {
                return false;
            }
        }

        // ==========================================
        // FLUXO DE EXPORTAÇÃO PARA EXCEL (.xlsx)
        // ==========================================
        public async Task ExportToExcelAsync()
        {
            if (_excelExportService == null) return;

            ShowExcelSuccess = false;
            ShowExcelError = false;
            ExcelStatusMessage = null;

            string? targetFile = (SaveExcelFileDialogFunc != null) ? SaveExcelFileDialogFunc() : DefaultSaveExcelFileDialog();
            if (string.IsNullOrWhiteSpace(targetFile))
            {
                // Usuário cancelou o diálogo de salvamento
                return;
            }

            IsBusyExportingExcel = true;
            try
            {
                var result = await _excelExportService.ExportToExcelAsync(targetFile, TargetDatabasePath);
                if (result.Success)
                {
                    ShowExcelSuccess = true;
                    ExcelStatusMessage = $"Planilha Excel exportada com sucesso!\n" +
                                         $"Arquivo: {result.FilePath} ({result.FileSize:N0} bytes)\n" +
                                         $"Total exportado: {result.ClientsCount} clientes, {result.SalesCount} vendas, {result.InstallmentsCount} parcelas, {result.PaymentsCount} pagamentos, {result.MonthlyCustomersCount} mensalistas, {result.MonthlyPaymentsCount} pagamentos mensais e {result.FinancialHistoryCount} movimentações financeiras.";
                }
                else
                {
                    ShowExcelError = true;
                    ExcelStatusMessage = result.Message;
                }
            }
            catch (Exception ex)
            {
                ShowExcelError = true;
                ExcelStatusMessage = "Erro ao exportar dados para Excel: " + ex.Message;
            }
            finally
            {
                IsBusyExportingExcel = false;
            }
        }

        private static string? DefaultSaveExcelFileDialog()
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Exportar Dados para Planilha Excel",
                    Filter = "Planilha Excel (*.xlsx)|*.xlsx",
                    DefaultExt = ".xlsx",
                    FileName = $"CobraPix_Exportacao_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };
                return sfd.ShowDialog() == true ? sfd.FileName : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task LoadPlanInfoAsync()
        {
            if (_proService != null)
            {
                var status = await _proService.CheckClientLimitAsync();
                CurrentPlan = status.Plan;
                ActiveClientsCount = status.ActiveClientsCount;
            }
        }

        private async Task HandleSubscriptionClickAsync(bool isAnnual)
        {
            ProStatusMessage = null;

            if (_proService == null)
            {
                ProStatusMessage = "Link de assinatura ainda não configurado.";
                return;
            }

            string? url = isAnnual
                ? await _proService.GetAnnualSubscriptionUrlAsync()
                : await _proService.GetMonthlySubscriptionUrlAsync();

            if (string.IsNullOrWhiteSpace(url))
            {
                ProStatusMessage = "Link de assinatura ainda não configurado.";
                return;
            }

            try
            {
                (OpenBrowserAction ?? (u => Process.Start(new ProcessStartInfo { FileName = u, UseShellExecute = true })))(url);
            }
            catch (Exception ex)
            {
                ProStatusMessage = "Não foi possível abrir o navegador: " + ex.Message;
            }
        }

        private void SyncSelectedOption()
        {
            var match = AdvanceDayOptions.FirstOrDefault(o => o.Days == _advanceDays)
                     ?? AdvanceDayOptions.FirstOrDefault(o => o.Days == 1);
            SelectedAdvanceOption = match;
        }

        public async Task CheckForUpdatesAsync()
        {
            if (IsCheckingUpdate) return;

            IsCheckingUpdate = true;
            UpdateStatusMessage = null;
            ShowUpdateSuccess = false;
            ShowUpdateError = false;
            ShowUpdateInfo = false;
            IsUpdateAvailable = false;
            RemoteVersionText = null;
            RemoteSha256 = null;
            RemoteDownloadUrl = null;
            RemoteVersion = null;
            IsDownloadingUpdate = false;
            IsDownloadCompleted = false;
            IsDownloadValid = false;
            DownloadedFilePath = null;
            DownloadProgressPercentage = 0;
            DownloadProgressText = null;
            DownloadPercentageText = null;
            DownloadStatusText = null;
            OnPropertyChanged(nameof(CanStartDownload));
            (StartDownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
            ReleaseNotes.Clear();
            OnPropertyChanged(nameof(HasReleaseNotes));

            try
            {
                var result = await _updateService.CheckForUpdatesAsync();

                UpdateStatusMessage = result.Message;

                switch (result.Status)
                {
                    case UpdateStatus.UpdateAvailable:
                        IsUpdateAvailable = true;
                        RemoteVersion = result.RemoteVersion;
                        RemoteDownloadUrl = result.DownloadUrl;
                        RemoteVersionText = $"Nova versão: {result.RemoteVersion} (Atual: {result.CurrentVersion})";
                        RemoteSha256 = result.Sha256;
                        OnPropertyChanged(nameof(CanStartDownload));
                        (StartDownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
                        if (result.ReleaseNotes != null)
                        {
                            foreach (var note in result.ReleaseNotes)
                            {
                                ReleaseNotes.Add(note);
                            }
                        }
                        OnPropertyChanged(nameof(HasReleaseNotes));
                        ShowUpdateSuccess = true;
                        break;

                    case UpdateStatus.UpToDate:
                        ShowUpdateSuccess = true;
                        break;

                    case UpdateStatus.NotConfigured:
                        ShowUpdateInfo = true;
                        break;

                    case UpdateStatus.NetworkError:
                    case UpdateStatus.InvalidManifest:
                    case UpdateStatus.Error:
                    default:
                        ShowUpdateError = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                ShowUpdateError = true;
                UpdateStatusMessage = "Não foi possível verificar atualizações agora. O CobraPix continuará funcionando normalmente.";
                Debug.WriteLine($"[ConfiguracoesViewModel] Erro na verificação de atualização: {ex.Message}");
            }
            finally
            {
                IsCheckingUpdate = false;
            }
        }

        public async Task StartDownloadAsync(HttpClient? testHttpClient = null)
        {
            if (!CanStartDownload) return;

            if (string.IsNullOrWhiteSpace(RemoteDownloadUrl) || string.IsNullOrWhiteSpace(RemoteSha256))
            {
                ShowUpdateError = true;
                UpdateStatusMessage = "A URL de download ou o hash SHA-256 oficial não estão disponíveis no manifesto.";
                return;
            }

            IsDownloadingUpdate = true;
            IsDownloadCompleted = false;
            IsDownloadValid = false;
            DownloadProgressPercentage = 0;
            DownloadPercentageText = "0%";
            DownloadProgressText = "Preparando download...";
            DownloadStatusText = "Preparando download...";
            UpdateStatusMessage = null;
            ShowUpdateError = false;
            ShowUpdateSuccess = false;
            ShowUpdateInfo = false;

            _downloadCts = new CancellationTokenSource();

            var progress = new Progress<UpdateDownloadProgress>(p =>
            {
                DownloadProgressPercentage = p.Percentage;
                DownloadPercentageText = p.PercentageText;
                DownloadProgressText = p.ProgressText;
                DownloadStatusText = $"Baixando atualização... {p.PercentageText}";
            });

            try
            {
                var result = await _updateDownloadService.DownloadAndVerifyAsync(
                    RemoteDownloadUrl,
                    RemoteSha256,
                    RemoteVersion ?? "1.0.1",
                    progress,
                    _downloadCts.Token,
                    testHttpClient);

                if (result.IsCancelled)
                {
                    IsDownloadingUpdate = false;
                    DownloadStatusText = "Download cancelado.";
                    UpdateStatusMessage = "Download cancelado pelo usuário.";
                    ShowUpdateInfo = true;
                }
                else if (result.Success && result.IntegrityVerified)
                {
                    IsDownloadingUpdate = false;
                    IsDownloadCompleted = true;
                    IsDownloadValid = true;
                    DownloadedFilePath = result.LocalFilePath;
                    DownloadStatusText = "Download concluído e validado.";
                    UpdateStatusMessage = "Atualização pronta para instalar. Pacote de atualização baixado e validado com sucesso.";
                    ShowUpdateSuccess = true;
                }
                else
                {
                    IsDownloadingUpdate = false;
                    IsDownloadCompleted = false;
                    IsDownloadValid = false;
                    DownloadStatusText = "Falha na validação do download.";
                    UpdateStatusMessage = result.FriendlyMessage ?? "Não foi possível validar a atualização. O download foi descartado.";
                    ShowUpdateError = true;
                }
            }
            catch (OperationCanceledException)
            {
                IsDownloadingUpdate = false;
                DownloadStatusText = "Download cancelado.";
                UpdateStatusMessage = "Download cancelado pelo usuário.";
                ShowUpdateInfo = true;
            }
            catch (Exception ex)
            {
                IsDownloadingUpdate = false;
                IsDownloadCompleted = false;
                IsDownloadValid = false;
                DownloadStatusText = "Falha no download.";
                UpdateStatusMessage = "Ocorreu uma falha durante o download da atualização.";
                ShowUpdateError = true;
                Debug.WriteLine($"[ConfiguracoesViewModel] Erro no download: {ex.Message}");
            }
            finally
            {
                _downloadCts?.Dispose();
                _downloadCts = null;
                (StartDownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CancelDownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public void CancelDownload()
        {
            if (_downloadCts != null && !_downloadCts.IsCancellationRequested)
            {
                DownloadStatusText = "Cancelando download...";
                _downloadCts.Cancel();
            }
        }

        public void DismissDownload()
        {
            IsDownloadCompleted = false;
            IsDownloadValid = false;
            DownloadStatusText = null;
            DownloadProgressPercentage = 0;
            DownloadProgressText = null;
            DownloadPercentageText = null;
            DownloadedFilePath = null;
            OnPropertyChanged(nameof(CanStartDownload));
            OnPropertyChanged(nameof(CanApplyUpdate));
            (StartDownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        public async Task ApplyUpdateAsync()
        {
            if (!IsUpdateAvailable) return;

            // Se o download ainda não foi concluído ou validado, exibe aviso informativo e seguro (compatibilidade com fases anteriores)
            if (!IsDownloadCompleted || !IsDownloadValid || string.IsNullOrWhiteSpace(DownloadedFilePath))
            {
                const string msg = "Pacote de atualização baixado e validado. A instalação automática será implementada na próxima fase.\n\n" +
                                   "Lembre-se: antes de atualizar, certifique-se de ter criado um backup de segurança dos seus dados clicando em 'CRIAR BACKUP AGORA'.";

                UpdateStatusMessage = msg;
                ShowUpdateInfo = true;

                if (ConfirmDialogFunc != null)
                {
                    ConfirmDialogFunc("Atualização do CobraPix", msg);
                }
                else
                {
                    try
                    {
                        MessageBox.Show(msg, "Atualização do CobraPix", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch
                    {
                        // Ambiente sem UI interativa / automação de testes
                    }
                }

                await Task.CompletedTask;
                return;
            }

            // Fase 3: Confirmação e acionamento seguro do CobraPix.Updater.exe
            string confirmMsg = "Antes de atualizar, recomendamos fazer um backup dos seus dados.\n\n" +
                               "O CobraPix será fechado temporariamente para concluir a atualização.\n\n" +
                               "Deseja fechar o CobraPix e iniciar a atualização agora?";

            bool confirmed = (ConfirmDialogFunc != null)
                ? ConfirmDialogFunc("Atualização do CobraPix", confirmMsg)
                : DefaultConfirmDialog("Atualização do CobraPix", confirmMsg);

            if (!confirmed)
            {
                UpdateStatusMessage = "Atualização cancelada pelo usuário. Os arquivos atuais foram mantidos.";
                ShowUpdateInfo = true;
                return;
            }

            if (!File.Exists(DownloadedFilePath))
            {
                UpdateStatusMessage = "O pacote de atualização baixado não foi encontrado. Realize o download novamente.";
                ShowUpdateError = true;
                return;
            }

            string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string updaterExe = Path.Combine(baseDir, "CobraPix.Updater.exe");

            if (!File.Exists(updaterExe))
            {
                UpdateStatusMessage = $"O executável do atualizador ({Path.GetFileName(updaterExe)}) não foi encontrado em '{baseDir}'.";
                ShowUpdateError = true;
                return;
            }

            string mainExeName = "CobraPix PC.exe";
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var moduleName = currentProcess.MainModule?.ModuleName;
                if (!string.IsNullOrWhiteSpace(moduleName) && moduleName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    mainExeName = moduleName;
                }
            }
            catch
            {
                // Fallback padrão
            }

            int pid = Environment.ProcessId;
            string arguments = $"--pid {pid} --package \"{DownloadedFilePath}\" --install-dir \"{baseDir}\" --exe \"{mainExeName}\"";

            try
            {
                if (StartUpdaterProcessAction != null)
                {
                    StartUpdaterProcessAction(updaterExe, arguments);
                }
                else
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = updaterExe,
                        Arguments = arguments,
                        UseShellExecute = true,
                        WorkingDirectory = baseDir
                    };
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage = $"Falha ao iniciar o atualizador: {ex.Message}";
                ShowUpdateError = true;
                return;
            }

            if (ShutdownApplicationAction != null)
            {
                ShutdownApplicationAction();
            }
            else
            {
                if (Application.Current != null)
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    Environment.Exit(0);
                }
            }

            await Task.CompletedTask;
        }
    }
}
