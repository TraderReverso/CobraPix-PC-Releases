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
    /// Exibe status do plano, benefícios e links para adesão.
    /// </summary>
    public class ProViewModel : ModuleViewModelBase
    {
        private readonly IProService? _proService;

        private PlanType _currentPlan = PlanType.Free;
        private int _activeClientsCount;
        private string? _statusMessage;
        private bool _isLoading;

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
                }
            }
        }

        public bool IsPro => CurrentPlan == PlanType.Pro;
        public bool IsFree => CurrentPlan == PlanType.Free;
        public string PlanBadgeText => IsPro ? "PRO ATIVO" : "PLANO FREE";

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

        // Propriedades do Modal Pix
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

        public Action<string>? OpenBrowserAction { get; set; }
        public Action<string>? CopyToClipboardAction { get; set; }

        public ICommand RefreshPlanCommand { get; }
        public ICommand OpenMonthlySubscriptionCommand { get; }
        public ICommand OpenAnnualSubscriptionCommand { get; }
        public ICommand OpenMonthlyPixCommand { get; }
        public ICommand OpenAnnualPixCommand { get; }
        public ICommand CopyPixPayloadCommand { get; }
        public ICommand ClosePixModalCommand { get; }

        public ProViewModel()
            : this(App.Services?.GetService<IProService>() ?? new ProService(new CobraPixDbContext()))
        {
        }

        public ProViewModel(IProService? proService)
        {
            _proService = proService;

            if (_proService != null)
            {
                _proService.PlanChanged += (s, e) => { _ = LoadPlanDataAsync(); };
            }

            RefreshPlanCommand = new RelayCommand(async () => await LoadPlanDataAsync());
            OpenMonthlySubscriptionCommand = new RelayCommand(async () => await HandleSubscriptionClickAsync(isAnnual: false));
            OpenAnnualSubscriptionCommand = new RelayCommand(async () => await HandleSubscriptionClickAsync(isAnnual: true));

            OpenMonthlyPixCommand = new RelayCommand(() => OpenPixModal(isAnnual: false));
            OpenAnnualPixCommand = new RelayCommand(() => OpenPixModal(isAnnual: true));
            CopyPixPayloadCommand = new RelayCommand(CopyPixPayload);
            ClosePixModalCommand = new RelayCommand(ClosePixModal);

            _ = LoadPlanDataAsync();
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
