using System;
using System.Diagnostics;
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
    /// <summary>
    /// ViewModel do módulo informativo e de gestão do CobraPix PRO.
    /// Exibe status do plano, benefícios, checkout via Mercado Pago / Pix,
    /// canal de suporte via WhatsApp e ativação de licença assinada.
    /// </summary>
    public class ProViewModel : ModuleViewModelBase
    {
        private readonly IProService? _proService;
        private readonly IWhatsAppService? _whatsAppService;

        private PlanType _currentPlan = PlanType.Free;
        private int _activeClientsCount;
        private string? _statusMessage;
        private bool _isLoading;

        // Propriedades de Ativação de Licença
        private string _licenseKeyInput = string.Empty;
        private bool _isActivating;
        private string? _activationErrorMessage;
        private string? _activationSuccessMessage;
        private LicenseInfo? _currentLicense;

        public override string Title => "CobraPix PRO";
        public override string Description => "Recursos ilimitados, clientes ativos sem limite e ferramentas premium.";
        public override string Icon => "👑";
        public override string Category => "UPGRADE";

        public PlanType CurrentPlan
        {
            get => _currentPlan;
            set
            {
                if (SetProperty(ref _currentPlan, value))
                {
                    OnPropertyChanged(nameof(IsPro));
                    OnPropertyChanged(nameof(IsFree));
                    OnPropertyChanged(nameof(PlanBadgeText));
                    OnPropertyChanged(nameof(ActiveClientsSummary));
                    OnPropertyChanged(nameof(LicenseStatusBadgeText));
                }
            }
        }

        public bool IsPro => CurrentPlan == PlanType.Pro;
        public bool IsFree => CurrentPlan == PlanType.Free;
        public string PlanBadgeText => IsPro ? "COBRAPIX PRO ATIVO" : "COBRAPIX FREE";
        public string LicenseStatusBadgeText => IsPro ? "👑 COBRAPIX PRO ATIVO" : "COBRAPIX FREE";

        public int ActiveClientsCount
        {
            get => _activeClientsCount;
            set
            {
                if (SetProperty(ref _activeClientsCount, value))
                {
                    OnPropertyChanged(nameof(ActiveClientsSummary));
                }
            }
        }

        public string ActiveClientsSummary => IsPro
            ? $"Você possui {ActiveClientsCount} clientes ativos (Sem limite de clientes no PRO)"
            : $"Você possui {ActiveClientsCount} de {ProService.FreeMaxActiveClients} clientes ativos no plano FREE";

        public string MonthlyPriceText => "R$ 9,90 / mês";
        public string AnnualPriceText => "R$ 89,90 / ano";

        public string? StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (SetProperty(ref _statusMessage, value))
                {
                    OnPropertyChanged(nameof(HasStatusMessage));
                }
            }
        }

        public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        // ==========================================
        // Propriedades de Licença e Ativação
        // ==========================================
        public string LicenseKeyInput
        {
            get => _licenseKeyInput;
            set
            {
                if (SetProperty(ref _licenseKeyInput, value))
                {
                    if (HasActivationError)
                    {
                        ActivationErrorMessage = null;
                    }
                }
            }
        }

        public bool IsActivating
        {
            get => _isActivating;
            set
            {
                if (SetProperty(ref _isActivating, value))
                {
                    OnPropertyChanged(nameof(CanActivate));
                    (ActivateLicenseCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanActivate => !IsActivating;

        public string? ActivationErrorMessage
        {
            get => _activationErrorMessage;
            set
            {
                if (SetProperty(ref _activationErrorMessage, value))
                {
                    OnPropertyChanged(nameof(HasActivationError));
                }
            }
        }

        public bool HasActivationError => !string.IsNullOrEmpty(ActivationErrorMessage);

        public string? ActivationSuccessMessage
        {
            get => _activationSuccessMessage;
            set
            {
                if (SetProperty(ref _activationSuccessMessage, value))
                {
                    OnPropertyChanged(nameof(HasActivationSuccess));
                }
            }
        }

        public bool HasActivationSuccess => !string.IsNullOrEmpty(ActivationSuccessMessage);

        public LicenseInfo? CurrentLicense
        {
            get => _currentLicense;
            set
            {
                if (SetProperty(ref _currentLicense, value))
                {
                    OnPropertyChanged(nameof(HasInstalledLicense));
                    OnPropertyChanged(nameof(LicenseIdText));
                    OnPropertyChanged(nameof(LicensePlanTypeText));
                    OnPropertyChanged(nameof(LicenseExpirationText));
                }
            }
        }

        public bool HasInstalledLicense => CurrentLicense != null;

        public string LicenseIdText => CurrentLicense != null
            ? $"Identificador: {CurrentLicense.LicenseId}"
            : string.Empty;

        public string LicensePlanTypeText => CurrentLicense != null
            ? CurrentLicense.PlanDisplayName
            : (IsPro ? "CobraPix PRO" : "Plano Free");

        public string LicenseExpirationText => CurrentLicense != null
            ? $"Válida até {CurrentLicense.FormattedExpirationDate} ({CurrentLicense.RemainingDays} dias restantes)"
            : string.Empty;

        public string SupportPhoneFormatted => "(21) 99747-0463";

        // ==========================================
        // Propriedades do Modal Pix
        // ==========================================
        private bool _isPixModalOpen;
        private string _pixModalTitle = "Pagamento via Pix";
        private string _pixModalPlanName = "CobraPix PRO";
        private decimal _pixModalAmount;
        private string _pixModalPayload = string.Empty;
        private System.Windows.Media.Imaging.BitmapImage? _pixModalQrCode;
        private bool _isPixCopied;
        private string _pixCopyFeedback = string.Empty;

        public bool IsPixModalOpen
        {
            get => _isPixModalOpen;
            set => SetProperty(ref _isPixModalOpen, value);
        }

        public string PixModalTitle
        {
            get => _pixModalTitle;
            set => SetProperty(ref _pixModalTitle, value);
        }

        public string PixModalPlanName
        {
            get => _pixModalPlanName;
            set => SetProperty(ref _pixModalPlanName, value);
        }

        public decimal PixModalAmount
        {
            get => _pixModalAmount;
            set
            {
                if (SetProperty(ref _pixModalAmount, value))
                {
                    OnPropertyChanged(nameof(PixModalAmountFormatted));
                }
            }
        }

        public string PixModalAmountFormatted => PixModalAmount.ToString("C2");

        public string PixModalPayload
        {
            get => _pixModalPayload;
            set => SetProperty(ref _pixModalPayload, value);
        }

        public System.Windows.Media.Imaging.BitmapImage? PixModalQrCode
        {
            get => _pixModalQrCode;
            set => SetProperty(ref _pixModalQrCode, value);
        }

        public bool IsPixCopied
        {
            get => _isPixCopied;
            set
            {
                if (SetProperty(ref _isPixCopied, value))
                {
                    OnPropertyChanged(nameof(HasPixCopyFeedback));
                }
            }
        }

        public string PixCopyFeedback
        {
            get => _pixCopyFeedback;
            set
            {
                if (SetProperty(ref _pixCopyFeedback, value))
                {
                    OnPropertyChanged(nameof(HasPixCopyFeedback));
                }
            }
        }

        public bool HasPixCopyFeedback => IsPixCopied && !string.IsNullOrEmpty(PixCopyFeedback);

        // Ações e Callbacks
        public Action<string>? OpenBrowserAction { get; set; }
        public Action<string>? CopyToClipboardAction { get; set; }
        public Func<string?>? GetClipboardTextAction { get; set; }
        public Action<string, string>? ContactSupportAction { get; set; }

        // Comandos de Licença e Suporte
        public ICommand ActivateLicenseCommand { get; }
        public ICommand PasteLicenseKeyCommand { get; }
        public ICommand ContactSupportCommand { get; }

        // Comandos existentes
        public ICommand RefreshPlanCommand { get; }
        public ICommand OpenMonthlySubscriptionCommand { get; }
        public ICommand OpenAnnualSubscriptionCommand { get; }
        public ICommand OpenMonthlyPixCommand { get; }
        public ICommand OpenAnnualPixCommand { get; }
        public ICommand CopyPixPayloadCommand { get; }
        public ICommand ClosePixModalCommand { get; }

        public ProViewModel()
            : this(App.Services?.GetService<IProService>() ?? new ProService(new CobraPixDbContext()),
                   App.Services?.GetService<IWhatsAppService>() ?? new WhatsAppService())
        {
        }

        public ProViewModel(IProService? proService, IWhatsAppService? whatsAppService = null)
        {
            _proService = proService;
            _whatsAppService = whatsAppService ?? new WhatsAppService();

            if (_proService != null)
            {
                _proService.PlanChanged += (s, e) => { _ = LoadPlanDataAsync(); };
            }

            ActivateLicenseCommand = new RelayCommand(async () => await ActivateLicenseAsync(), () => !IsActivating);
            PasteLicenseKeyCommand = new RelayCommand(PasteLicenseKeyFromClipboard);
            ContactSupportCommand = new RelayCommand(ContactSupportViaWhatsApp);

            RefreshPlanCommand = new RelayCommand(async () => await LoadPlanDataAsync());
            OpenMonthlySubscriptionCommand = new RelayCommand(async () => await HandleSubscriptionClickAsync(isAnnual: false));
            OpenAnnualSubscriptionCommand = new RelayCommand(async () => await HandleSubscriptionClickAsync(isAnnual: true));

            OpenMonthlyPixCommand = new RelayCommand(() => OpenPixModal(isAnnual: false));
            OpenAnnualPixCommand = new RelayCommand(() => OpenPixModal(isAnnual: true));
            CopyPixPayloadCommand = new RelayCommand(CopyPixPayload);
            ClosePixModalCommand = new RelayCommand(ClosePixModal);

            _ = LoadPlanDataAsync();
        }

        public async Task ActivateLicenseAsync()
        {
            ActivationErrorMessage = null;
            ActivationSuccessMessage = null;

            if (string.IsNullOrWhiteSpace(LicenseKeyInput))
            {
                ActivationErrorMessage = "Por favor, informe ou cole sua chave de ativação.";
                return;
            }

            if (_proService == null)
            {
                ActivationErrorMessage = "Serviço de licenças indisponível no momento.";
                return;
            }

            IsActivating = true;
            try
            {
                var result = await _proService.ActivateLicenseAsync(LicenseKeyInput);
                if (result.IsValid && result.License != null)
                {
                    CurrentLicense = result.License;
                    CurrentPlan = result.License.PlanType;
                    LicenseKeyInput = string.Empty;
                    ActivationSuccessMessage = $"Parabéns! Sua licença {result.License.PlanDisplayName} foi ativada com sucesso! Válida até {result.License.FormattedExpirationDate}.";
                    await LoadPlanDataAsync();
                }
                else
                {
                    ActivationErrorMessage = result.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                ActivationErrorMessage = "Erro ao ativar licença: " + ex.Message;
            }
            finally
            {
                IsActivating = false;
            }
        }

        public void PasteLicenseKeyFromClipboard()
        {
            try
            {
                string? text = null;
                if (GetClipboardTextAction != null)
                {
                    text = GetClipboardTextAction.Invoke();
                }
                else
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        text = System.Windows.Clipboard.GetText();
                    }
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    LicenseKeyInput = text.Trim();
                    ActivationErrorMessage = null;
                    ActivationSuccessMessage = null;
                }
            }
            catch
            {
                // Silencioso se clipboard estiver bloqueado por outro processo do Windows
            }
        }

        public void ContactSupportViaWhatsApp()
        {
            string phone = _proService?.SupportPhoneNumber ?? ProService.DefaultSupportPhone;
            string message = _proService?.SupportWhatsAppMessage ?? ProService.DefaultSupportMessage;

            if (ContactSupportAction != null)
            {
                ContactSupportAction.Invoke(phone, message);
                return;
            }

            if (_whatsAppService != null)
            {
                _whatsAppService.OpenWhatsApp(phone, message);
            }
            else
            {
                var ws = new WhatsAppService();
                ws.OpenWhatsApp(phone, message);
            }
        }

        public void OpenPixModal(bool isAnnual)
        {
            PixModalPlanName = isAnnual ? "CobraPix PRO Anual" : "CobraPix PRO Mensal";
            PixModalTitle = isAnnual ? "Pagamento via Pix — PRO Anual" : "Pagamento via Pix — PRO Mensal";
            PixModalAmount = isAnnual ? ProService.AnnualPrice : ProService.MonthlyPrice;
            PixModalPayload = isAnnual
                ? (_proService?.GetAnnualPixPayload() ?? ProService.AnnualPixPayload)
                : (_proService?.GetMonthlyPixPayload() ?? ProService.MonthlyPixPayload);

            try
            {
                PixModalQrCode = QrCodeHelper.GenerateQrCodeBitmap(PixModalPayload);
            }
            catch
            {
                PixModalQrCode = null;
            }

            IsPixCopied = false;
            PixCopyFeedback = string.Empty;
            IsPixModalOpen = true;
        }

        public void CopyPixPayload()
        {
            if (string.IsNullOrWhiteSpace(PixModalPayload))
                return;

            if (CopyToClipboardAction != null)
            {
                CopyToClipboardAction.Invoke(PixModalPayload);
            }
            else
            {
                try
                {
                    System.Windows.Clipboard.SetDataObject(new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, PixModalPayload), true);
                }
                catch
                {
                    try
                    {
                        System.Windows.Clipboard.SetText(PixModalPayload);
                    }
                    catch { }
                }
            }

            IsPixCopied = true;
            PixCopyFeedback = "Pix copiado.";
        }

        public void ClosePixModal()
        {
            IsPixModalOpen = false;
            IsPixCopied = false;
            PixCopyFeedback = string.Empty;
        }

        public async Task LoadPlanDataAsync()
        {
            if (_proService == null) return;

            IsLoading = true;
            try
            {
                var status = await _proService.CheckClientLimitAsync();
                CurrentPlan = status.Plan;
                ActiveClientsCount = status.ActiveClientsCount;

                var licResult = await _proService.GetCurrentLicenseStatusAsync();
                if (licResult.IsValid && licResult.License != null)
                {
                    CurrentLicense = licResult.License;
                }
                else
                {
                    CurrentLicense = null;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Erro ao carregar dados do plano: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task HandleSubscriptionClickAsync(bool isAnnual)
        {
            StatusMessage = null;

            if (_proService == null)
            {
                StatusMessage = "Link de assinatura ainda não configurado.";
                return;
            }

            string? url = isAnnual
                ? await _proService.GetAnnualSubscriptionUrlAsync()
                : await _proService.GetMonthlySubscriptionUrlAsync();

            if (string.IsNullOrWhiteSpace(url))
            {
                StatusMessage = "Link de assinatura ainda não configurado.";
                return;
            }

            try
            {
                (OpenBrowserAction ?? (u => Process.Start(new ProcessStartInfo { FileName = u, UseShellExecute = true })))(url);
            }
            catch (Exception ex)
            {
                StatusMessage = "Não foi possível abrir o navegador: " + ex.Message;
            }
        }
    }
}
