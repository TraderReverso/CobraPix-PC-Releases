using System;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CobraPix_PC.Helpers;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// ViewModel de pré-visualização e edição de mensagem de WhatsApp antes do envio.
    /// Garante que o usuário revise o texto, edite se necessário, ou cancele a ação sem afetar o banco.
    /// Suporta estrutura de 2 mensagens: Mensagem principal e código Pix Copia e Cola isolado com QR Code.
    /// </summary>
    public class WhatsAppPreviewViewModel : ViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly IWhatsAppService _whatsAppService;
        private bool _isOpen;
        private string _clientName = string.Empty;
        private string _clientPhone = string.Empty;
        private string _tipo = string.Empty;
        private string _amount = string.Empty;
        private string _message = string.Empty;
        private string _isolatedPixCode = string.Empty;
        private BitmapImage? _qrCodeImage;
        private string? _errorMessage;
        private string? _successFeedback;

        public bool IsOpen
        {
            get => _isOpen;
            set => SetProperty(ref _isOpen, value);
        }

        public string ClientName
        {
            get => _clientName;
            set => SetProperty(ref _clientName, value);
        }

        public string ClientPhone
        {
            get => _clientPhone;
            set => SetProperty(ref _clientPhone, value);
        }

        public string Tipo
        {
            get => _tipo;
            set => SetProperty(ref _tipo, value);
        }

        public string Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public string IsolatedPixCode
        {
            get => _isolatedPixCode;
            set
            {
                if (SetProperty(ref _isolatedPixCode, value))
                {
                    OnPropertyChanged(nameof(HasIsolatedPixCode));
                    UpdateQrCode();
                }
            }
        }

        public bool HasIsolatedPixCode => !string.IsNullOrWhiteSpace(_isolatedPixCode);

        public BitmapImage? QrCodeImage
        {
            get => _qrCodeImage;
            private set => SetProperty(ref _qrCodeImage, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(HasError));
                    OnPropertyChanged(nameof(CanOpenWhatsApp));
                }
            }
        }

        public string? SuccessFeedback
        {
            get => _successFeedback;
            set => SetProperty(ref _successFeedback, value);
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
        public bool CanOpenWhatsApp => !HasError || ErrorMessage != "Este cliente não possui um telefone válido para WhatsApp.";

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CopyPixCodeCommand { get; }
        public ICommand CopyPrimaryMessageCommand { get; }

        public WhatsAppPreviewViewModel(IWhatsAppService whatsAppService)
        {
            _whatsAppService = whatsAppService ?? throw new ArgumentNullException(nameof(whatsAppService));

            ConfirmCommand = new RelayCommand(ExecuteConfirm);
            CancelCommand = new RelayCommand(ExecuteCancel);
            CopyPixCodeCommand = new RelayCommand(ExecuteCopyPixCode);
            CopyPrimaryMessageCommand = new RelayCommand(ExecuteCopyPrimaryMessage);
        }

        public void Open(string clientName, string? clientPhone, string tipo, decimal amount, string defaultMessage, string? isolatedPixCode = null)
        {
            string amountStr = amount > 0 ? amount.ToString("C2", PtBr) : string.Empty;
            Open(clientName, clientPhone, tipo, amountStr, defaultMessage, isolatedPixCode);
        }

        public void Open(string clientName, string? clientPhone, string tipo, string amount, string defaultMessage, string? isolatedPixCode = null)
        {
            ClientName = clientName;
            ClientPhone = clientPhone ?? string.Empty;
            Tipo = tipo;
            Amount = amount;
            Message = defaultMessage;
            IsolatedPixCode = isolatedPixCode ?? string.Empty;
            ErrorMessage = null;
            SuccessFeedback = null;

            if (!_whatsAppService.IsValidPhoneNumber(clientPhone))
            {
                ErrorMessage = "Este cliente não possui um telefone válido para WhatsApp.";
            }

            IsOpen = true;
        }

        private void UpdateQrCode()
        {
            if (string.IsNullOrWhiteSpace(_isolatedPixCode))
            {
                QrCodeImage = null;
                return;
            }

            try
            {
                QrCodeImage = QrCodeHelper.GenerateQrCodeBitmap(_isolatedPixCode, 8);
            }
            catch
            {
                QrCodeImage = null;
            }
        }

        public void ExecuteCopyPixCode()
        {
            if (string.IsNullOrWhiteSpace(IsolatedPixCode)) return;

            Services.WhatsAppService.CopyToClipboardSafe(IsolatedPixCode.Trim());
            SuccessFeedback = "Código Pix copiado! Cole na conversa com o cliente.";
        }

        public void ExecuteCopyPrimaryMessage()
        {
            if (string.IsNullOrWhiteSpace(Message)) return;

            Services.WhatsAppService.CopyToClipboardSafe(Message);
            SuccessFeedback = "Mensagem principal copiada para a área de transferência.";
        }

        public void ExecuteConfirm()
        {
            if (!_whatsAppService.IsValidPhoneNumber(ClientPhone))
            {
                ErrorMessage = "Este cliente não possui um telefone válido para WhatsApp.";
                return;
            }

            // Abre o WhatsApp com a mensagem 1 e copia o código Pix isolado (mensagem 2) para o clipboard
            bool opened = _whatsAppService.OpenWhatsAppWithPixCopy(ClientPhone, Message, IsolatedPixCode);
            if (!opened)
            {
                ErrorMessage = "Não foi possível abrir o WhatsApp neste computador.";
                return;
            }

            IsOpen = false;
            ErrorMessage = null;
            SuccessFeedback = null;
        }

        public void ExecuteCancel()
        {
            IsOpen = false;
            ErrorMessage = null;
            SuccessFeedback = null;
        }
    }
}
