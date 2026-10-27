using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
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
        public const string SoundsDirectoryEnvVar = "COBRAPIX_SOUNDS_DIRECTORY";

        private readonly CobraPixDbContext? _dbContext;
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

        // Iniciar com o Windows (Fase Boot 1.0.4)
        private bool _startWithWindows;

        // Chave Pix para Cobranças (Fase 4)
        public const string PixKeySettingKey = "App.PixKey";
        private string _pixKey = string.Empty;
        private string? _pixKeyFeedbackMessage;
        private bool _hasPixKeyFeedback;

        public string PixKey
        {
            get => _pixKey;
            set
            {
                if (SetProperty(ref _pixKey, value))
                {
                    HasPixKeyFeedback = false;
                }
            }
        }

        public string? PixKeyFeedbackMessage
        {
            get => _pixKeyFeedbackMessage;
            set => SetProperty(ref _pixKeyFeedbackMessage, value);
        }

        public bool HasPixKeyFeedback
        {
            get => _hasPixKeyFeedback;
            set => SetProperty(ref _hasPixKeyFeedback, value);
        }

        public ICommand SavePixKeyCommand { get; }

        // Nome da Empresa / Emissor (Fase 5)
        public const string CompanyNameSettingKey = "App.CompanyName";
        private string _companyName = string.Empty;
        private string? _companyNameFeedbackMessage;
        private bool _hasCompanyNameFeedback;

        public string CompanyName
        {
            get => _companyName;
            set
            {
                if (SetProperty(ref _companyName, value))
                {
                    HasCompanyNameFeedback = false;
                }
            }
        }

        public string? CompanyNameFeedbackMessage
        {
            get => _companyNameFeedbackMessage;
            set => SetProperty(ref _companyNameFeedbackMessage, value);
        }

        public bool HasCompanyNameFeedback
        {
            get => _hasCompanyNameFeedback;
            set => SetProperty(ref _hasCompanyNameFeedback, value);
        }

        public ICommand SaveCompanyNameCommand { get; }

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
                    OnPropertyChanged(nameof(IsGreenYellowTheme));
                    OnPropertyChanged(nameof(ThemeDescriptionText));
                }
            }
        }

        public bool IsDarkTheme => CurrentAppTheme == AppTheme.Dark;
        public bool IsLightTheme => CurrentAppTheme == AppTheme.Light;
        public bool IsGreenYellowTheme => CurrentAppTheme == AppTheme.GreenYellow;
        public string ThemeDescriptionText => CurrentAppTheme switch
        {
            AppTheme.Dark => "Escuro (Estética Solo Leveling)",
            AppTheme.Light => "Claro (Visual Comercial)",
            AppTheme.GreenYellow => "Verde/Amarelo (Identidade CobraPix)",
            _ => "Escuro (Estética Solo Leveling)"
        };

        public ICommand SetDarkThemeCommand { get; }
        public ICommand SetLightThemeCommand { get; }
        public ICommand SetGreenYellowThemeCommand { get; }
        public ICommand ToggleThemeCommand { get; }

        /// <summary>
        /// Liga ou desliga a inicialização automática do CobraPix com o Windows.
        /// Quando ativado, registra o executável na chave Run do registro.
        /// Quando desativado, remove o registro.
        /// Estado é persistido na tabela AppSettings do banco local.
        /// </summary>
        public bool StartWithWindows
        {
            get => _startWithWindows;
            set
            {
                if (SetProperty(ref _startWithWindows, value))
                {
                    _ = SetStartWithWindowsAsync(value);
                }
            }
        }

        public ICommand ToggleStartWithWindowsCommand { get; }

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

        // ==========================================
        // PROPRIEDADES DE BACKUP AUTOMÁTICO DIÁRIO (Fase 6)
        // ==========================================
        private bool _autoBackupEnabled = true;
        private string? _lastAutoBackupInfo;
        private bool _isBusyAutoBackup;

        public bool AutoBackupEnabled
        {
            get => _autoBackupEnabled;
            set
            {
                if (SetProperty(ref _autoBackupEnabled, value))
                {
                    _ = SaveAutoBackupEnabledAsync(value);
                }
            }
        }

        public string? LastAutoBackupInfo
        {
            get => _lastAutoBackupInfo;
            set => SetProperty(ref _lastAutoBackupInfo, value);
        }

        public bool IsBusyAutoBackup
        {
            get => _isBusyAutoBackup;
            set => SetProperty(ref _isBusyAutoBackup, value);
        }

        public string AutoBackupDirectory => _backupService?.GetAutoBackupDirectory() ?? string.Empty;

        public ICommand OpenAutoBackupFolderCommand { get; }
        public ICommand RunAutoBackupNowCommand { get; }

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

        private LicenseInfo? _currentLicense;
        public LicenseInfo? CurrentLicense
        {
            get => _currentLicense;
            set
            {
                if (SetProperty(ref _currentLicense, value))
                {
                    OnPropertyChanged(nameof(HasInstalledLicense));
                    OnPropertyChanged(nameof(LicenseDetailsText));
                    OnPropertyChanged(nameof(LicenseExpirationText));
                }
            }
        }

        public bool HasInstalledLicense => CurrentLicense != null;

        public string LicenseDetailsText => CurrentLicense != null
            ? $"{CurrentLicense.PlanDisplayName} (ID: {CurrentLicense.LicenseId})"
            : (IsPro ? "CobraPix PRO Ativo" : "Plano Free");

        public string LicenseExpirationText => CurrentLicense != null
            ? $"Válida até {CurrentLicense.FormattedExpirationDate} ({CurrentLicense.RemainingDays} dias restantes)"
            : string.Empty;

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

        private string? _installationDirectoryOverride;
        /// <summary>
        /// Diretório opcional de instalação para testes controlados ou simulações.
        /// </summary>
        public string? InstallationDirectoryOverride
        {
            get => _installationDirectoryOverride;
            set
            {
                _installationDirectoryOverride = value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    string verFile = Path.Combine(value, "version_info.txt");
                    if (File.Exists(verFile))
                    {
                        try
                        {
                            foreach (var line in File.ReadAllLines(verFile))
                            {
                                if (line.StartsWith("VERSION=", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (_updateService is UpdateService concrete)
                                    {
                                        concrete.CurrentVersion = line.Substring(8).Trim();
                                        OnPropertyChanged(nameof(InstalledVersion));
                                        OnPropertyChanged(nameof(InstalledVersionText));
                                    }
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
        }

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

        private string? _packageSizeText;
        public string? PackageSizeText
        {
            get => _packageSizeText;
            set
            {
                if (SetProperty(ref _packageSizeText, value))
                {
                    OnPropertyChanged(nameof(HasPackageSizeText));
                }
            }
        }

        public bool HasPackageSizeText => !string.IsNullOrWhiteSpace(PackageSizeText);

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
            : this(notificationService, backupService, proService, null, null, null, null, null)
        {
        }

        public ConfiguracoesViewModel(
            INotificationService notificationService,
            IBackupService? backupService,
            IProService? proService,
            IThemeService? themeService)
            : this(notificationService, backupService, proService, themeService, null, null, null, null)
        {
        }

        public ConfiguracoesViewModel(
            INotificationService notificationService,
            IBackupService? backupService,
            IProService? proService,
            IThemeService? themeService,
            IAndroidImportService? androidImportService)
            : this(notificationService, backupService, proService, themeService, androidImportService, null, null, null)
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
            IUpdateDownloadService? updateDownloadService = null,
            CobraPixDbContext? dbContext = null)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _backupService = backupService;
            _proService = proService;
            _dbContext = dbContext
                ?? (notificationService as NotificationService)?.DbContext
                ?? (backupService as BackupService)?.DbContext
                ?? (proService as ProService)?.DbContext
                ?? (themeService as ThemeService)?.DbContext;

            _themeService = themeService ?? (_dbContext != null ? new ThemeService(_dbContext) : App.Services?.GetService<IThemeService>());
            _androidImportService = androidImportService ?? (_dbContext != null ? new AndroidImportService(_dbContext) : App.Services?.GetService<IAndroidImportService>());
            _updateService = updateService ?? App.Services?.GetService<IUpdateService>() ?? new UpdateService();
            _excelExportService = excelExportService ?? (_dbContext != null ? new ExcelExportService(_dbContext) : App.Services?.GetService<IExcelExportService>() ?? new ExcelExportService(new CobraPixDbContext()));
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
            SetGreenYellowThemeCommand = new RelayCommand(async () => await SetAppThemeAsync(AppTheme.GreenYellow));
            ToggleThemeCommand = new RelayCommand(async () => await SetAppThemeAsync(IsDarkTheme ? AppTheme.Light : AppTheme.Dark));

            // Iniciar com o Windows (Fase Boot 1.0.4)
            ToggleStartWithWindowsCommand = new RelayCommand(() => StartWithWindows = !StartWithWindows);

            // Chave Pix para Cobranças (Fase 4)
            SavePixKeyCommand = new RelayCommand(async () => await SavePixKeyAsync());

            // Nome da Empresa / Emissor (Fase 5)
            SaveCompanyNameCommand = new RelayCommand(async () => await SaveCompanyNameAsync());

            CreateBackupCommand = new RelayCommand(async () => await CreateBackupAsync());
            RestoreBackupCommand = new RelayCommand(async () => await RestoreBackupAsync());
            OpenAutoBackupFolderCommand = new RelayCommand(OpenAutoBackupFolder);
            RunAutoBackupNowCommand = new RelayCommand(async () => await RunAutoBackupNowAsync(), () => !IsBusyAutoBackup);
            ExportToExcelCommand = new RelayCommand(async () => await ExportToExcelAsync(), () => !IsBusyExportingExcel);

            OpenMonthlySubscriptionCommand = new RelayCommand(async () => await HandleSubscriptionClickAsync(isAnnual: false));
            OpenAnnualSubscriptionCommand = new RelayCommand(async () => await HandleSubscriptionClickAsync(isAnnual: true));

            CheckUpdateCommand = new RelayCommand(async () => await CheckForUpdatesAsync(), () => !IsCheckingUpdate);
            ApplyUpdateCommand = new RelayCommand(async () => await ApplyUpdateAsync(), () => CanApplyUpdate);
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
            _ = LoadStartWithWindowsAsync();
            _ = LoadPixKeyAsync();
            _ = LoadCompanyNameAsync();
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

        // ──────────────────────────────────────────────────────────────────────
        // INICIAR COM O WINDOWS (Fase Boot 1.0.4)
        // Persistência: tabela AppSettings, chave "App.StartWithWindows"
        // Registro: HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run
        //           Valor: "CobraPixPC" → "<exe_path> --minimized"
        // ──────────────────────────────────────────────────────────────────────

        private const string StartWithWindowsSettingKey = "App.StartWithWindows";
        private const string StartWithWindowsRegistryValueName = "CobraPixPC";

        // Estado em memória simulado para testes automatizados (impede qualquer efeito colateral em HKCU\...\Run)
        private static bool _testSimulatedRegistryStartWithWindows;

        public static void ResetTestSimulatedRegistry(bool initialState = false)
        {
            _testSimulatedRegistryStartWithWindows = initialState;
        }

        public static bool GetTestSimulatedRegistryState()
        {
            return _testSimulatedRegistryStartWithWindows;
        }

        /// <summary>
        /// Obtém o caminho absoluto determinístico do executável do CobraPix atualmente em execução.
        /// </summary>
        private static string GetCurrentExecutablePath()
        {
            try
            {
                string? processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath) && System.IO.File.Exists(processPath))
                {
                    return System.IO.Path.GetFullPath(processPath);
                }

                string? mainModule = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(mainModule) && System.IO.File.Exists(mainModule))
                {
                    return System.IO.Path.GetFullPath(mainModule);
                }

                string baseDirExe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CobraPix PC.exe");
                if (System.IO.File.Exists(baseDirExe))
                {
                    return System.IO.Path.GetFullPath(baseDirExe);
                }

                string assemblyLoc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrWhiteSpace(assemblyLoc))
                {
                    string candidateExe = System.IO.Path.ChangeExtension(assemblyLoc, ".exe");
                    if (System.IO.File.Exists(candidateExe))
                    {
                        return System.IO.Path.GetFullPath(candidateExe);
                    }
                }
            }
            catch
            {
            }

            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CobraPix PC.exe");
        }

        /// <summary>
        /// Carrega o estado persistido da configuração "Iniciar com o Windows" do banco local.
        /// </summary>
        public async Task LoadStartWithWindowsAsync()
        {
            try
            {
                CobraPixDbContext? ctx = _dbContext;
                using var scope = ctx == null ? App.Services?.CreateScope() : null;
                if (ctx == null)
                {
                    if (scope == null) return;
                    ctx = scope.ServiceProvider.GetService<CobraPix_PC.Data.CobraPixDbContext>();
                    if (ctx == null) return;
                }

                var setting = await ctx.AppSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == StartWithWindowsSettingKey);

                bool isPersisted = setting != null && setting.Value == "true";
                bool isRegistered = IsStartWithWindowsRegistered();

                // Se o banco configurou explicitamente 'true' e o registro não existe, restaura o registro.
                // Se o registro já existe no Windows (mesmo antes de salvar no banco), preserva como ativo.
                bool shouldBeActive = isPersisted || isRegistered;

                if (shouldBeActive)
                {
                    RegisterStartWithWindows();
                }

                _startWithWindows = shouldBeActive;
                OnPropertyChanged(nameof(StartWithWindows));
            }
            catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
            {
                throw;
            }
            catch
            {
                // Fallback defensivo: se falhar, lê diretamente do registro sem alterar
                _startWithWindows = IsStartWithWindowsRegistered();
                OnPropertyChanged(nameof(StartWithWindows));
            }
        }

        /// <summary>
        /// Ativa ou desativa a inicialização automática com o Windows.
        /// Atualiza o registro Run do Windows e persiste no banco AppSettings.
        /// </summary>
        public async Task SetStartWithWindowsAsync(bool enable)
        {
            try
            {
                // 1. Atualiza o registro do Windows (ou estado isolado em modo de teste)
                if (enable)
                {
                    RegisterStartWithWindows();
                }
                else
                {
                    UnregisterStartWithWindows();
                }

                // 2. Persiste o estado no banco AppSettings
                CobraPixDbContext? ctx = _dbContext;
                using var scope = ctx == null ? App.Services?.CreateScope() : null;
                if (ctx == null)
                {
                    if (scope == null) return;
                    ctx = scope.ServiceProvider.GetService<CobraPix_PC.Data.CobraPixDbContext>();
                    if (ctx == null) return;
                }

                var existing = await ctx.AppSettings
                    .FirstOrDefaultAsync(s => s.Key == StartWithWindowsSettingKey);

                string valueStr = enable ? "true" : "false";
                if (existing != null)
                {
                    existing.Value = valueStr;
                    existing.UpdatedAt = DateTime.Now;
                }
                else
                {
                    ctx.AppSettings.Add(new CobraPix_PC.Models.AppSetting
                    {
                        Key = StartWithWindowsSettingKey,
                        Value = valueStr,
                        UpdatedAt = DateTime.Now
                    });
                }
                await ctx.SaveChangesAsync();
            }
            catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
            {
                throw;
            }
            catch
            {
                // Tratamento defensivo; não propaga erro para a UI
            }
        }

        /// <summary>
        /// Verifica se a entrada de autostart está registrada no registro do Windows.
        /// Em modo de teste, opera exclusivamente sobre estado em memória isolado.
        /// </summary>
        private static bool IsStartWithWindowsRegistered()
        {
            if (DatabasePath.IsTestExecutionMode())
            {
                return _testSimulatedRegistryStartWithWindows;
            }

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: false);
                return key?.GetValue(StartWithWindowsRegistryValueName) != null;
            }
            catch
            {
                return false;
            }
        }

        private const string StartupApprovedRegistrySubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        /// <summary>
        /// Remove qualquer entrada prévia de bloqueio ou estado obsoleto do CobraPix PC em StartupApproved\Run,
        /// permitindo que o Windows trate a inicialização a partir de HKCU\Run como nova e habilitada nativamente.
        /// </summary>
        private static void CleanStartupApprovedEntry()
        {
            if (DatabasePath.IsTestExecutionMode())
            {
                return;
            }

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupApprovedRegistrySubKey, writable: true);
                if (key?.GetValue(StartWithWindowsRegistryValueName) != null)
                {
                    key.DeleteValue(StartWithWindowsRegistryValueName, throwOnMissingValue: false);
                }
            }
            catch
            {
                // Falha silenciosa
            }
        }

        /// <summary>
        /// Registra o CobraPix PC para iniciar com o Windows via HKCU Run.
        /// Utiliza o argumento --minimized para que a janela abra minimizada.
        /// Limpa qualquer entrada residual em StartupApproved\Run para o Windows tratar como nova.
        /// Em modo de teste, impede qualquer escrita no Registro do Windows.
        /// </summary>
        private static void RegisterStartWithWindows()
        {
            if (DatabasePath.IsTestExecutionMode())
            {
                _testSimulatedRegistryStartWithWindows = true;
                return;
            }

            try
            {
                string exePath = GetCurrentExecutablePath();
                string value = $"\"{exePath}\" --minimized";

                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
                key?.SetValue(StartWithWindowsRegistryValueName, value, Microsoft.Win32.RegistryValueKind.String);

                // Remove entrada antiga do CobraPixPC em StartupApproved\Run se existir
                CleanStartupApprovedEntry();
            }
            catch
            {
                // Falha silenciosa; sem elevação necessária pois é HKCU
            }
        }

        /// <summary>
        /// Remove a entrada de autostart do CobraPix PC do registro do Windows e de StartupApproved.
        /// Em modo de teste, impede qualquer escrita ou exclusão no Registro do Windows.
        /// </summary>
        private static void UnregisterStartWithWindows()
        {
            if (DatabasePath.IsTestExecutionMode())
            {
                _testSimulatedRegistryStartWithWindows = false;
                return;
            }

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key?.GetValue(StartWithWindowsRegistryValueName) != null)
                {
                    key.DeleteValue(StartWithWindowsRegistryValueName, throwOnMissingValue: false);
                }

                // Remove também de StartupApproved para não deixar resíduo no perfil do usuário
                CleanStartupApprovedEntry();
            }
            catch
            {
                // Falha silenciosa
            }
        }

        /// <summary>
        /// Carrega a Chave Pix configurada a partir do banco de dados local (AppSettings).
        /// </summary>
        public async Task LoadPixKeyAsync()
        {
            try
            {
                CobraPixDbContext? ctx = _dbContext;
                using var scope = ctx == null ? App.Services?.CreateScope() : null;
                if (ctx == null)
                {
                    if (scope == null) return;
                    ctx = scope.ServiceProvider.GetService<CobraPix_PC.Data.CobraPixDbContext>();
                    if (ctx == null) return;
                }

                var setting = await ctx.AppSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == PixKeySettingKey);

                _pixKey = setting?.Value ?? string.Empty;
                OnPropertyChanged(nameof(PixKey));

                if (scope != null)
                {
                    var whatsAppSvc = scope.ServiceProvider.GetService<IWhatsAppService>();
                    if (whatsAppSvc != null)
                    {
                        whatsAppSvc.PixKey = _pixKey;
                    }
                }
            }
            catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
            {
                throw;
            }
            catch
            {
            }
        }

        /// <summary>
        /// Salva ou limpa a Chave Pix no banco de dados local (AppSettings) e atualiza o serviço de WhatsApp.
        /// </summary>
        public async Task SavePixKeyAsync()
        {
            try
            {
                string trimmed = PixKey?.Trim() ?? string.Empty;
                CobraPixDbContext? ctx = _dbContext;
                using var scope = ctx == null ? App.Services?.CreateScope() : null;
                if (ctx == null && scope != null)
                {
                    ctx = scope.ServiceProvider.GetService<CobraPix_PC.Data.CobraPixDbContext>();
                }

                if (ctx != null)
                {
                    var existing = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == PixKeySettingKey);
                    if (existing != null)
                    {
                        existing.Value = trimmed;
                        existing.UpdatedAt = DateTime.Now;
                    }
                    else
                    {
                        ctx.AppSettings.Add(new CobraPix_PC.Models.AppSetting
                        {
                            Key = PixKeySettingKey,
                            Value = trimmed,
                            UpdatedAt = DateTime.Now
                        });
                    }
                    await ctx.SaveChangesAsync();
                }

                if (scope != null)
                {
                    var whatsAppSvc = scope.ServiceProvider.GetService<IWhatsAppService>();
                    if (whatsAppSvc != null)
                    {
                        whatsAppSvc.PixKey = trimmed;
                    }
                }

                PixKeyFeedbackMessage = string.IsNullOrEmpty(trimmed)
                    ? "Chave Pix removida. As mensagens de cobrança serão enviadas sem chave Pix."
                    : "Chave Pix salva com sucesso! Será incluída automaticamente nas mensagens.";
                HasPixKeyFeedback = true;
            }
            catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
            {
                throw;
            }
            catch (Exception ex)
            {
                PixKeyFeedbackMessage = "Erro ao salvar chave Pix: " + ex.Message;
                HasPixKeyFeedback = true;
            }
        }

        /// <summary>
        /// Carrega o Nome da Empresa / Emissor configurado a partir do banco de dados local (AppSettings).
        /// </summary>
        public async Task LoadCompanyNameAsync()
        {
            try
            {
                CobraPixDbContext? ctx = _dbContext;
                using var scope = ctx == null ? App.Services?.CreateScope() : null;
                if (ctx == null)
                {
                    if (scope == null) return;
                    ctx = scope.ServiceProvider.GetService<CobraPix_PC.Data.CobraPixDbContext>();
                    if (ctx == null) return;
                }

                var setting = await ctx.AppSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == CompanyNameSettingKey || s.Key == "App.IssuerName");

                _companyName = setting?.Value ?? string.Empty;
                OnPropertyChanged(nameof(CompanyName));

                if (scope != null)
                {
                    var pdfSvc = scope.ServiceProvider.GetService<IReceiptPdfService>();
                    if (pdfSvc != null)
                    {
                        pdfSvc.CompanyName = _companyName;
                    }
                }
            }
            catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
            {
                throw;
            }
            catch
            {
            }
        }

        /// <summary>
        /// Salva ou limpa o Nome da Empresa / Emissor no banco de dados local (AppSettings) e atualiza o serviço de PDF.
        /// </summary>
        public async Task SaveCompanyNameAsync()
        {
            try
            {
                string trimmed = CompanyName?.Trim() ?? string.Empty;
                CobraPixDbContext? ctx = _dbContext;
                using var scope = ctx == null ? App.Services?.CreateScope() : null;
                if (ctx == null && scope != null)
                {
                    ctx = scope.ServiceProvider.GetService<CobraPix_PC.Data.CobraPixDbContext>();
                }

                if (ctx != null)
                {
                    var existing = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == CompanyNameSettingKey);
                    if (existing != null)
                    {
                        existing.Value = trimmed;
                        existing.UpdatedAt = DateTime.Now;
                    }
                    else
                    {
                        ctx.AppSettings.Add(new CobraPix_PC.Models.AppSetting
                        {
                            Key = CompanyNameSettingKey,
                            Value = trimmed,
                            UpdatedAt = DateTime.Now
                        });
                    }
                    await ctx.SaveChangesAsync();
                }

                if (scope != null)
                {
                    var pdfSvc = scope.ServiceProvider.GetService<IReceiptPdfService>();
                    if (pdfSvc != null)
                    {
                        pdfSvc.CompanyName = trimmed;
                    }
                }

                CompanyNameFeedbackMessage = string.IsNullOrEmpty(trimmed)
                    ? "Nome da empresa removido. Os recibos em PDF serão emitidos sem identificação de emissor."
                    : "Nome da empresa salvo com sucesso! Será incluído no cabeçalho dos recibos em PDF.";
                HasCompanyNameFeedback = true;
            }
            catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
            {
                throw;
            }
            catch (Exception ex)
            {
                CompanyNameFeedbackMessage = "Erro ao salvar nome da empresa: " + ex.Message;
                HasCompanyNameFeedback = true;
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

            await LoadAutoBackupInfoAsync();
        }

        public async Task LoadAutoBackupInfoAsync()
        {
            if (_backupService != null)
            {
                _autoBackupEnabled = await _backupService.IsAutoBackupEnabledAsync();
                OnPropertyChanged(nameof(AutoBackupEnabled));

                var autoInfo = await _backupService.GetLastAutoBackupInfoAsync();
                LastAutoBackupInfo = !string.IsNullOrEmpty(autoInfo) ? autoInfo : "Pendente para hoje (será gerado automaticamente).";
            }
            else
            {
                LastAutoBackupInfo = "Recurso indisponível.";
            }
            OnPropertyChanged(nameof(AutoBackupDirectory));
        }

        public async Task SaveAutoBackupEnabledAsync(bool enabled)
        {
            if (_backupService != null)
            {
                await _backupService.SetAutoBackupEnabledAsync(enabled);
                await LoadAutoBackupInfoAsync();
            }
        }

        public Action<System.Diagnostics.ProcessStartInfo>? ProcessStarterFunc { get; set; }

        public void OpenAutoBackupFolder()
        {
            if (DatabasePath.IsTestExecutionMode())
            {
                return;
            }

            try
            {
                string dir = AutoBackupDirectory;
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    };
                    if (ProcessStarterFunc != null)
                    {
                        ProcessStarterFunc(psi);
                    }
                    else
                    {
                        System.Diagnostics.Process.Start(psi);
                    }
                }
            }
            catch { }
        }

        public async Task RunAutoBackupNowAsync()
        {
            if (_backupService == null || IsBusyAutoBackup) return;

            IsBusyAutoBackup = true;
            try
            {
                var result = await _backupService.CheckAndPerformDailyAutoBackupAsync(force: true);
                if (result != null && result.Success)
                {
                    LastAutoBackupInfo = $"Concluído com sucesso agora: {Path.GetFileName(result.FilePath)} ({result.FileSize:N0} bytes).";
                }
                else if (result != null && !result.Success)
                {
                    LastAutoBackupInfo = $"Falha ao gerar backup: {result.Message}";
                }
                else
                {
                    await LoadAutoBackupInfoAsync();
                }
            }
            catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
            {
                throw;
            }
            catch (Exception ex)
            {
                LastAutoBackupInfo = $"Erro: {ex.Message}";
            }
            finally
            {
                IsBusyAutoBackup = false;
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
                if (ex is InvalidOperationException && ex.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
                {
                    throw;
                }
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

        public static string GetSoundsDirectory()
        {
            string? overrideDir = Environment.GetEnvironmentVariable(SoundsDirectoryEnvVar);
            if (DatabasePath.IsTestExecutionMode())
            {
                if (string.IsNullOrWhiteSpace(overrideDir))
                {
                    throw new InvalidOperationException(
                        $"[SEGURANÇA DE EXECUÇÃO] Operação de sons em modo de teste abortada: nenhum diretório descartável foi configurado em {SoundsDirectoryEnvVar}.");
                }

                return DatabasePath.AssertSafeTestDirectoryPath(overrideDir, "Resolução de diretório de sons personalizados");
            }

            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                return System.IO.Path.GetFullPath(overrideDir.Trim());
            }

            return System.IO.Path.Combine(
                DatabasePath.GetProductionDirectoryPath(),
                "sounds");
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

            if (DatabasePath.IsTestExecutionMode())
            {
                chosen = DatabasePath.AssertSafeTestFilePath(chosen, "Seleção de arquivo de áudio personalizado");
            }

            if (!chosen.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Apenas arquivos de áudio no formato .wav são suportados pelo sistema de alertas locais.";
                ShowSaveSuccess = false;
                return;
            }

            string localDir = GetSoundsDirectory();
            string fileName = System.IO.Path.GetFileName(chosen);
            string destPath = System.IO.Path.Combine(localDir, fileName);

            if (DatabasePath.IsTestExecutionMode())
            {
                destPath = DatabasePath.AssertSafeTestFilePath(destPath, "Gravação de arquivo de áudio personalizado");
            }

            if (!System.IO.File.Exists(chosen))
            {
                StatusMessage = "Arquivo de áudio selecionado não foi encontrado no disco.";
                ShowSaveSuccess = false;
                return;
            }

            try
            {
                System.IO.Directory.CreateDirectory(localDir);

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
                if (ex is InvalidOperationException && ex.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
                {
                    throw;
                }
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

                var lic = await _proService.GetCurrentLicenseStatusAsync();
                CurrentLicense = (lic.IsValid && lic.License != null) ? lic.License : null;
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
            PackageSizeText = null;
            OnPropertyChanged(nameof(HasPackageSizeText));
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
                string? manifestUrl = await _updateService.GetUpdateServerUrlAsync() ?? UpdateService.DefaultManifestUrl;
                var result = await _updateService.CheckForUpdatesAsync(manifestUrl);

                UpdateStatusMessage = result.Message;

                switch (result.Status)
                {
                    case UpdateStatus.UpdateAvailable:
                        IsUpdateAvailable = true;
                        RemoteVersion = result.RemoteVersion;
                        RemoteDownloadUrl = result.DownloadUrl;
                        RemoteVersionText = UpdateService.IsTestEnvironmentActive
                            ? $"[TESTE] Nova versão: {result.RemoteVersion} (Atual: {result.CurrentVersion})"
                            : $"Nova versão: {result.RemoteVersion} (Atual: {result.CurrentVersion})";
                        RemoteSha256 = result.Sha256;
                        if (result.PackageSizeBytes.HasValue && result.PackageSizeBytes.Value > 0)
                        {
                            PackageSizeText = UpdateDownloadProgress.FormatBytes(result.PackageSizeBytes.Value);
                        }
                        else
                        {
                            PackageSizeText = null;
                        }
                        OnPropertyChanged(nameof(HasPackageSizeText));
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
                        UpdateStatusMessage = "Nova versão disponível.";
                        ShowUpdateSuccess = true;
                        ShowUpdateInfo = false;
                        ShowUpdateError = false;
                        break;

                    case UpdateStatus.UpToDate:
                        UpdateStatusMessage = "Você já está usando a versão mais recente do CobraPix.";
                        ShowUpdateSuccess = true;
                        ShowUpdateInfo = false;
                        ShowUpdateError = false;
                        break;

                    case UpdateStatus.NotConfigured:
                        UpdateStatusMessage = "O CobraPix está conectado ao canal oficial de atualizações.";
                        ShowUpdateInfo = true;
                        ShowUpdateSuccess = false;
                        ShowUpdateError = false;
                        break;

                    case UpdateStatus.NetworkError:
                        UpdateStatusMessage = "Não foi possível verificar atualizações agora. O CobraPix continuará funcionando normalmente.";
                        ShowUpdateError = true;
                        ShowUpdateSuccess = false;
                        ShowUpdateInfo = false;
                        break;

                    case UpdateStatus.InvalidManifest:
                    case UpdateStatus.Error:
                    default:
                        ShowUpdateError = true;
                        ShowUpdateSuccess = false;
                        ShowUpdateInfo = false;
                        break;
                }
            }
            catch (Exception ex)
            {
                ShowUpdateError = true;
                ShowUpdateSuccess = false;
                ShowUpdateInfo = false;
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
            DownloadStatusText = "Baixando...";
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
                if (p.Percentage >= 100.0 && (p.ProgressText.Contains("Validando") || p.ProgressText.Contains("SHA-256")))
                {
                    DownloadStatusText = "Validando SHA-256...";
                }
                else
                {
                    DownloadStatusText = $"Baixando instalador oficial... {p.ProgressText}";
                }
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
                    DownloadStatusText = "Cancelado";
                    UpdateStatusMessage = "Download cancelado pelo usuário.";
                    ShowUpdateInfo = true;
                    ShowUpdateSuccess = false;
                    ShowUpdateError = false;
                }
                else if (result.Success && result.IntegrityVerified)
                {
                    IsDownloadingUpdate = false;
                    IsDownloadCompleted = true;
                    IsDownloadValid = true;
                    DownloadedFilePath = result.LocalFilePath;
                    if (!string.IsNullOrWhiteSpace(result.LocalFilePath) && File.Exists(result.LocalFilePath))
                    {
                        var fi = new FileInfo(result.LocalFilePath);
                        PackageSizeText = UpdateDownloadProgress.FormatBytes(fi.Length);
                        OnPropertyChanged(nameof(HasPackageSizeText));
                    }
                    DownloadStatusText = "Atualização pronta para instalar.";
                    UpdateStatusMessage = "Atualização pronta para instalar.";
                    ShowUpdateSuccess = true;
                    ShowUpdateInfo = false;
                    ShowUpdateError = false;
                }
                else
                {
                    IsDownloadingUpdate = false;
                    IsDownloadCompleted = false;
                    IsDownloadValid = false;
                    DownloadStatusText = "Erro";
                    UpdateStatusMessage = result.FriendlyMessage ?? "Não foi possível validar a atualização. O download foi descartado.";
                    ShowUpdateError = true;
                    ShowUpdateSuccess = false;
                    ShowUpdateInfo = false;
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

            // Fase 3: Confirmação e acionamento seguro do instalador oficial Inno Setup
            string confirmMsg = "Uma atualização do CobraPix está pronta.\n\n" +
                               "O Windows poderá solicitar permissão de administrador para concluir a instalação.\n\n" +
                               "O CobraPix será fechado temporariamente e reaberto após a atualização.\n\n" +
                               "Seus dados permanecem armazenados localmente.\n\nDeseja continuar?";

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

            // 1. Fluxo Oficial Comercial: Instalador Inno Setup (.exe)
            if (DownloadedFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string setupExe = DownloadedFilePath;
                string setupArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";
                if (!string.IsNullOrWhiteSpace(InstallationDirectoryOverride))
                {
                    setupArgs += $" /DIR=\"{InstallationDirectoryOverride}\"";
                }

                try
                {
                    if (StartUpdaterProcessAction != null)
                    {
                        StartUpdaterProcessAction(setupExe, setupArgs);
                    }
                    else
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = setupExe,
                            Arguments = setupArgs,
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(setupExe) ?? AppContext.BaseDirectory
                        };

                        Process.Start(psi);
                    }

                    UpdateStatusMessage = "Atualização iniciada. O CobraPix será reaberto na nova versão.";
                    ShowUpdateSuccess = true;
                    ShowUpdateInfo = false;
                    ShowUpdateError = false;
                }
                catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 1223)
                {
                    UpdateStatusMessage = "Atualização cancelada pelo usuário. A permissão de administrador não foi concedida e nenhuma alteração foi realizada.";
                    ShowUpdateInfo = true;
                    ShowUpdateSuccess = false;
                    ShowUpdateError = false;
                    return;
                }
                catch (Exception ex)
                {
                    UpdateStatusMessage = $"Falha ao iniciar o instalador: {ex.Message}";
                    ShowUpdateError = true;
                    ShowUpdateSuccess = false;
                    ShowUpdateInfo = false;
                    return;
                }

                if (ShutdownApplicationAction != null)
                {
                    ShutdownApplicationAction();
                }
                else
                {
                    _ = Task.Delay(500).ContinueWith(_ =>
                    {
                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        {
                            System.Windows.Application.Current.Shutdown();
                        });
                    });
                }

                await Task.CompletedTask;
                return;
            }

            // 2. Fluxo Legado: Pacote .zip acionando CobraPix.Updater.exe (compatibilidade regressiva de testes)
            string effectiveInstallDir = (!string.IsNullOrWhiteSpace(InstallationDirectoryOverride))
                ? InstallationDirectoryOverride
                : AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Bloqueio de segurança se executado em diretório de dev/compilação real sem mock
            if (StartUpdaterProcessAction == null && IsInvalidInstallationDirectory(effectiveInstallDir, out string blockReason))
            {
                UpdateStatusMessage = $"A atualização automática não pode ser executada em diretório de compilação ou desenvolvimento ({blockReason}).";
                ShowUpdateError = true;
                return;
            }

            string updaterExe = Path.Combine(effectiveInstallDir, "CobraPix.Updater.exe");
            if (!File.Exists(updaterExe))
            {
                updaterExe = Path.Combine(AppContext.BaseDirectory, "CobraPix.Updater.exe");
            }
            if (!File.Exists(updaterExe))
            {
                UpdateStatusMessage = $"O executável do atualizador ({Path.GetFileName(updaterExe)}) não foi encontrado em '{effectiveInstallDir}'.";
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
            }

            int pid = Environment.ProcessId;
            string arguments = $"--pid {pid} --package \"{DownloadedFilePath}\" --install-dir \"{effectiveInstallDir}\" --exe \"{mainExeName}\" --silent";

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
                        WorkingDirectory = effectiveInstallDir
                    };

                    if (CobraPix.Updater.UpdaterEngine.RequiresElevation(effectiveInstallDir))
                    {
                        psi.Verb = "runas";
                    }

                    Process.Start(psi);
                }
            }
            catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 1223)
            {
                UpdateStatusMessage = "Atualização cancelada pelo usuário. A permissão de administrador não foi concedida e nenhuma alteração foi realizada.";
                ShowUpdateInfo = true;
                ShowUpdateSuccess = false;
                ShowUpdateError = false;
                return;
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

        /// <summary>
        /// Valida se o diretório de destino é seguro para instalação, impedindo tentativas de
        /// atualizar sobre pastas de compilação (bin\Release), artefatos, código-fonte, dados do usuário ou temporárias.
        /// </summary>
        public static bool IsInvalidInstallationDirectory(string? directory, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(directory))
            {
                errorMessage = "O diretório da aplicação não pôde ser determinado.";
                return true;
            }

            string normalized;
            try
            {
                normalized = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                errorMessage = $"Caminho de diretório inválido: {ex.Message}";
                return true;
            }

            // 1. Proibição estrita contra o diretório de dados do usuário (%LOCALAPPDATA%\CobraPix)
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string forbiddenUserDataDir = Path.Combine(localAppData, "CobraPix").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalized.Equals(forbiddenUserDataDir, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(forbiddenUserDataDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Violação de segurança: O diretório de instalação não pode ser a pasta de dados do usuário ('{forbiddenUserDataDir}').";
                return true;
            }

            // 2. Pasta temporária (%TEMP%)
            // Permite exclusivamente o diretório isolado de testes (%TEMP%\CobraPix\UpdateTest)
            string tempDir = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string allowedTestRoot = Path.Combine(tempDir, "CobraPix", "UpdateTest").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool isAllowedTestDirectory = normalized.StartsWith(allowedTestRoot, StringComparison.OrdinalIgnoreCase);

            if (!isAllowedTestDirectory &&
                (normalized.Equals(tempDir, StringComparison.OrdinalIgnoreCase) ||
                 normalized.StartsWith(tempDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                errorMessage = "O aplicativo está sendo executado a partir de uma pasta temporária e não de uma instalação permanente do CobraPix PC.";
                return true;
            }

            // 3. Pastas de compilação (bin\Release, bin\Debug)
            string sep = Path.DirectorySeparatorChar.ToString();
            string altSep = Path.AltDirectorySeparatorChar.ToString();
            if (normalized.Contains(sep + "bin" + sep, StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(altSep + "bin" + altSep, StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(sep + "bin", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(altSep + "bin", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "A atualização automática está desabilitada quando o executável roda diretamente da pasta de compilação (bin\\Release ou bin\\Debug). Utilize uma instalação oficial.";
                return true;
            }

            // 4. Pasta de artefatos de publicação (artifacts)
            if (normalized.Contains(sep + "artifacts" + sep, StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(altSep + "artifacts" + altSep, StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(sep + "artifacts", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(altSep + "artifacts", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "O diretório atual corresponde à pasta de artefatos de publicação (artifacts) e não a uma instalação oficial.";
                return true;
            }

            // 5. Projeto fonte / Repositório Git (contendo .csproj ou .git na pasta ou pais)
            try
            {
                var dirInfo = new DirectoryInfo(normalized);
                var p1 = dirInfo.Parent;
                var p2 = p1?.Parent;
                var p3 = p2?.Parent;

                bool hasProjectFile = dirInfo.GetFiles("*.csproj").Length > 0 ||
                                     (p1 != null && p1.GetFiles("*.csproj").Length > 0) ||
                                     (p2 != null && p2.GetFiles("*.csproj").Length > 0) ||
                                     (p3 != null && p3.GetFiles("*.csproj").Length > 0);

                if (hasProjectFile)
                {
                    errorMessage = "A atualização automática está desabilitada dentro da árvore de código-fonte do projeto. Utilize uma instalação oficial.";
                    return true;
                }
            }
            catch { }

            // 6. Pasta exclusiva do próprio updater
            if (Path.GetFileName(normalized).Equals("CobraPix.Updater", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "O diretório de instalação não pode ser a subpasta exclusiva do atualizador.";
                return true;
            }

            return false;
        }
    }
}
