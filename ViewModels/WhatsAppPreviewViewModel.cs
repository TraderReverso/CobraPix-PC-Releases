using System;
using System.Globalization;
using System.Windows.Input;
using CobraPix_PC.Helpers;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// ViewModel de pré-visualização e edição de mensagem de WhatsApp antes do envio.
    /// Garante que o usuário revise o texto, edite se necessário, ou cancele a ação sem afetar o banco.
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
        private string? _errorMessage;

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

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
        public bool CanOpenWhatsApp => !HasError || ErrorMessage != "Este cliente não possui um telefone válido para WhatsApp.";

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public WhatsAppPreviewViewModel(IWhatsAppService whatsAppService)
        {
            _whatsAppService = whatsAppService ?? throw new ArgumentNullException(nameof(whatsAppService));

            ConfirmCommand = new RelayCommand(ExecuteConfirm);
            CancelCommand = new RelayCommand(ExecuteCancel);
        }

        public void Open(string clientName, string? clientPhone, string tipo, decimal amount, string defaultMessage)
        {
            string amountStr = amount > 0 ? amount.ToString("C2", PtBr) : string.Empty;
            Open(clientName, clientPhone, tipo, amountStr, defaultMessage);
        }

        public void Open(string clientName, string? clientPhone, string tipo, string amount, string defaultMessage)
        {
            ClientName = clientName;
            ClientPhone = clientPhone ?? string.Empty;
            Tipo = tipo;
            Amount = amount;
            Message = defaultMessage;
            ErrorMessage = null;

            if (!_whatsAppService.IsValidPhoneNumber(clientPhone))
            {
                ErrorMessage = "Este cliente não possui um telefone válido para WhatsApp.";
            }

            IsOpen = true;
        }

        public void ExecuteConfirm()
        {
            if (!_whatsAppService.IsValidPhoneNumber(ClientPhone))
            {
                ErrorMessage = "Este cliente não possui um telefone válido para WhatsApp.";
                return;
            }

            bool opened = _whatsAppService.OpenWhatsApp(ClientPhone, Message);
            if (!opened)
            {
                ErrorMessage = "Não foi possível abrir o WhatsApp neste computador.";
                return;
            }

            IsOpen = false;
            ErrorMessage = null;
        }

        public void ExecuteCancel()
        {
            IsOpen = false;
            ErrorMessage = null;
        }
    }
}
