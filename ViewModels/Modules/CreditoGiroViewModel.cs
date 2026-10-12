using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CobraPix_PC.Helpers;
using CobraPix_PC.Models;
using CobraPix_PC.Models.Credit;
using CobraPix_PC.Models.Credit.Dtos;
using CobraPix_PC.Services.Credit.Interfaces;
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.ViewModels.Modules
{
    /// <summary>
    /// ViewModel do módulo Crédito & Giro (Exclusivo CobraPix PRO).
    /// Controla as telas de Contratos, Desembolsos, Pagamentos, Excedentes e Estornos/Devoluções.
    /// Impõe validação estrita da licença PRO e utiliza valores monetários em centavos inteiros via serviços de domínio.
    /// </summary>
    public class CreditoGiroViewModel : ModuleViewModelBase
    {
        private readonly ICreditContractService _contractService;
        private readonly ICreditPaymentService _paymentService;
        private readonly ICreditRefundService _refundService;
        private readonly IProService _proService;
        private readonly IClientService _clientService;

        public override string Title => "Crédito & Giro";
        public override string Description => "Gestão de capital de giro, contratos, amortizações sob Regra A e devoluções";
        public override string Icon => "💰";
        public override string Category => "FINANCEIRO";

        private bool _isPro;
        private bool _isBusy;
        private string? _statusMessage;
        private string? _errorMessage;
        private string? _successMessage;
        private int _selectedTabIndex;

        public bool IsPro
        {
            get => _isPro;
            set
            {
                if (SetProperty(ref _isPro, value))
                {
                    OnPropertyChanged(nameof(IsFree));
                    OnPropertyChanged(nameof(PlanBadgeText));
                }
            }
        }

        public bool IsFree => !IsPro;
        public string PlanBadgeText => IsPro ? "👑 PRO ATIVO" : "BLOQUEADO (RECURSO PRO)";

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

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (SetProperty(ref _selectedTabIndex, value))
                {
                    ClearMessages();
                    _ = RefreshCurrentTabAsync();
                }
            }
        }

        // ==========================================
        // 1. ABA CONTRATOS & DESEMBOLSOS
        // ==========================================
        public ObservableCollection<CreditContract> Contracts { get; } = new();
        public ObservableCollection<Client> Clients { get; } = new();

        private CreditContract? _selectedContract;
        public CreditContract? SelectedContract
        {
            get => _selectedContract;
            set
            {
                if (SetProperty(ref _selectedContract, value))
                {
                    OnPropertyChanged(nameof(HasSelectedContract));
                    OnPropertyChanged(nameof(CanDisburseSelectedContract));
                    OnPropertyChanged(nameof(CanCancelSelectedContract));
                    if (value != null)
                    {
                        PaymentContractId = value.Id;
                    }
                }
            }
        }

        public bool HasSelectedContract => SelectedContract != null;
        public bool CanDisburseSelectedContract => SelectedContract != null && SelectedContract.Status == CreditContractStatus.Draft;
        public bool CanCancelSelectedContract => SelectedContract != null && SelectedContract.Status != CreditContractStatus.Cancelled && SelectedContract.Status != CreditContractStatus.Completed;

        private Client? _selectedClient;
        public Client? SelectedClient
        {
            get => _selectedClient;
            set => SetProperty(ref _selectedClient, value);
        }

        private string _newContractNumber = string.Empty;
        public string NewContractNumber
        {
            get => _newContractNumber;
            set => SetProperty(ref _newContractNumber, value);
        }

        private decimal _newPrincipalAmount = 1000m;
        public decimal NewPrincipalAmount
        {
            get => _newPrincipalAmount;
            set => SetProperty(ref _newPrincipalAmount, value);
        }

        private decimal _newTotalInterest = 200m;
        public decimal NewTotalInterest
        {
            get => _newTotalInterest;
            set => SetProperty(ref _newTotalInterest, value);
        }

        private int _newInstallmentsCount = 1;
        public int NewInstallmentsCount
        {
            get => _newInstallmentsCount;
            set => SetProperty(ref _newInstallmentsCount, value);
        }

        private DateTime _newFirstDueDate = DateTime.Today.AddDays(30);
        public DateTime NewFirstDueDate
        {
            get => _newFirstDueDate;
            set => SetProperty(ref _newFirstDueDate, value);
        }

        private DateTime _newContractDate = DateTime.Today;
        public DateTime NewContractDate
        {
            get => _newContractDate;
            set => SetProperty(ref _newContractDate, value);
        }

        private string _newNotes = string.Empty;
        public string NewNotes
        {
            get => _newNotes;
            set => SetProperty(ref _newNotes, value);
        }

        private DateTime _disbursementDate = DateTime.Today;
        public DateTime DisbursementDate
        {
            get => _disbursementDate;
            set => SetProperty(ref _disbursementDate, value);
        }

        private PaymentMethod _disbursementMethod = PaymentMethod.Pix;
        public PaymentMethod DisbursementMethod
        {
            get => _disbursementMethod;
            set => SetProperty(ref _disbursementMethod, value);
        }

        private string _cancellationReason = string.Empty;
        public string CancellationReason
        {
            get => _cancellationReason;
            set => SetProperty(ref _cancellationReason, value);
        }

        // ==========================================
        // 2. ABA PAGAMENTOS & ALOCAÇÕES
        // ==========================================
        public ObservableCollection<CreditPayment> Payments { get; } = new();

        private CreditPayment? _selectedPayment;
        public CreditPayment? SelectedPayment
        {
            get => _selectedPayment;
            set
            {
                if (SetProperty(ref _selectedPayment, value))
                {
                    OnPropertyChanged(nameof(HasSelectedPayment));
                    OnPropertyChanged(nameof(CanRefundSelectedPayment));
                    if (value != null)
                    {
                        RefundPaymentId = value.Id;
                    }
                }
            }
        }

        public bool HasSelectedPayment => SelectedPayment != null;
        public bool CanRefundSelectedPayment => SelectedPayment != null && SelectedPayment.Status == CreditPaymentStatus.Active;

        private string _paymentContractId = string.Empty;
        public string PaymentContractId
        {
            get => _paymentContractId;
            set => SetProperty(ref _paymentContractId, value);
        }

        private decimal _paymentAmount = 100m;
        public decimal PaymentAmount
        {
            get => _paymentAmount;
            set => SetProperty(ref _paymentAmount, value);
        }

        private DateTime _paymentDate = DateTime.Today;
        public DateTime PaymentDate
        {
            get => _paymentDate;
            set => SetProperty(ref _paymentDate, value);
        }

        private PaymentMethod _paymentMethod = PaymentMethod.Pix;
        public PaymentMethod PaymentMethod
        {
            get => _paymentMethod;
            set => SetProperty(ref _paymentMethod, value);
        }

        private string _paymentNotes = string.Empty;
        public string PaymentNotes
        {
            get => _paymentNotes;
            set => SetProperty(ref _paymentNotes, value);
        }

        private PaymentPreviewDto? _paymentPreview;
        public PaymentPreviewDto? PaymentPreview
        {
            get => _paymentPreview;
            set => SetProperty(ref _paymentPreview, value);
        }

        // ==========================================
        // 3. ABA SALDOS EXCEDENTES
        // ==========================================
        public ObservableCollection<CreditSurplusBalance> SurplusBalances { get; } = new();

        private CreditSurplusBalance? _selectedSurplus;
        public CreditSurplusBalance? SelectedSurplus
        {
            get => _selectedSurplus;
            set
            {
                if (SetProperty(ref _selectedSurplus, value))
                {
                    OnPropertyChanged(nameof(HasSelectedSurplus));
                }
            }
        }

        public bool HasSelectedSurplus => SelectedSurplus != null && SelectedSurplus.RemainingSurplusCents > 0;

        private string _consumeTargetInstallmentId = string.Empty;
        public string ConsumeTargetInstallmentId
        {
            get => _consumeTargetInstallmentId;
            set => SetProperty(ref _consumeTargetInstallmentId, value);
        }

        private decimal _consumeAmount = 50m;
        public decimal ConsumeAmount
        {
            get => _consumeAmount;
            set => SetProperty(ref _consumeAmount, value);
        }

        private CreditSurplusConsumption? _selectedConsumption;
        public CreditSurplusConsumption? SelectedConsumption
        {
            get => _selectedConsumption;
            set
            {
                if (SetProperty(ref _selectedConsumption, value))
                {
                    OnPropertyChanged(nameof(CanReverseConsumption));
                }
            }
        }

        public bool CanReverseConsumption => SelectedConsumption != null && !SelectedConsumption.IsReversed;

        // ==========================================
        // 4. ABA ESTORNOS & DEVOLUÇÕES
        // ==========================================
        public ObservableCollection<CreditPaymentRefund> Refunds { get; } = new();
        public ObservableCollection<CreditDevolution> Devolutions { get; } = new();

        private CreditDevolution? _selectedDevolution;
        public CreditDevolution? SelectedDevolution
        {
            get => _selectedDevolution;
            set
            {
                if (SetProperty(ref _selectedDevolution, value))
                {
                    OnPropertyChanged(nameof(CanSettleSelectedDevolution));
                    OnPropertyChanged(nameof(CanCancelSelectedDevolution));
                }
            }
        }

        public bool CanSettleSelectedDevolution => SelectedDevolution != null && SelectedDevolution.Status == CreditDevolutionStatus.Authorized;
        public bool CanCancelSelectedDevolution => SelectedDevolution != null && SelectedDevolution.Status == CreditDevolutionStatus.Authorized;

        private string _refundPaymentId = string.Empty;
        public string RefundPaymentId
        {
            get => _refundPaymentId;
            set => SetProperty(ref _refundPaymentId, value);
        }

        private string _refundReason = string.Empty;
        public string RefundReason
        {
            get => _refundReason;
            set => SetProperty(ref _refundReason, value);
        }

        private PaymentMethod _settlementPaymentMethod = PaymentMethod.Pix;
        public PaymentMethod SettlementPaymentMethod
        {
            get => _settlementPaymentMethod;
            set => SetProperty(ref _settlementPaymentMethod, value);
        }

        private string _cancelDevolutionReason = string.Empty;
        public string CancelDevolutionReason
        {
            get => _cancelDevolutionReason;
            set => SetProperty(ref _cancelDevolutionReason, value);
        }

        // ==========================================
        // COMANDOS
        // ==========================================
        public ICommand LoadDataCommand { get; }
        public ICommand CreateContractCommand { get; }
        public ICommand DisburseContractCommand { get; }
        public ICommand CancelContractCommand { get; }

        public ICommand PreviewPaymentCommand { get; }
        public ICommand RegisterPaymentCommand { get; }

        public ICommand ConsumeSurplusCommand { get; }
        public ICommand ReverseSurplusCommand { get; }

        public ICommand AuthorizeRefundCommand { get; }
        public ICommand SettleDevolutionCommand { get; }
        public ICommand CancelDevolutionCommand { get; }

        public CreditoGiroViewModel(
            ICreditContractService contractService,
            ICreditPaymentService paymentService,
            ICreditRefundService refundService,
            IProService proService,
            IClientService clientService)
        {
            _contractService = contractService ?? throw new ArgumentNullException(nameof(contractService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _refundService = refundService ?? throw new ArgumentNullException(nameof(refundService));
            _proService = proService ?? throw new ArgumentNullException(nameof(proService));
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));

            LoadDataCommand = new RelayCommand(async () => await InitializeAsync());
            CreateContractCommand = new RelayCommand(async () => await CreateContractAsync());
            DisburseContractCommand = new RelayCommand(async () => await DisburseContractAsync());
            CancelContractCommand = new RelayCommand(async () => await CancelContractAsync());

            PreviewPaymentCommand = new RelayCommand(async () => await PreviewPaymentAsync());
            RegisterPaymentCommand = new RelayCommand(async () => await RegisterPaymentAsync());

            ConsumeSurplusCommand = new RelayCommand(async () => await ConsumeSurplusAsync());
            ReverseSurplusCommand = new RelayCommand(async () => await ReverseSurplusAsync());

            AuthorizeRefundCommand = new RelayCommand(async () => await AuthorizeRefundAsync());
            SettleDevolutionCommand = new RelayCommand(async () => await SettleDevolutionAsync());
            CancelDevolutionCommand = new RelayCommand(async () => await CancelDevolutionAsync());

            _ = InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            ClearMessages();
            IsBusy = true;
            try
            {
                IsPro = await _proService.IsProAsync();
                if (!IsPro)
                {
                    ErrorMessage = "O módulo Crédito & Giro é exclusivo para assinantes CobraPix PRO. Faça upgrade para desbloquear.";
                    return;
                }

                await LoadClientsAsync();
                await RefreshCurrentTabAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Falha ao inicializar módulo de crédito: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshCurrentTabAsync()
        {
            if (!IsPro) return;

            switch (SelectedTabIndex)
            {
                case 0:
                    await LoadContractsAsync();
                    break;
                case 1:
                    await LoadPaymentsAsync();
                    break;
                case 2:
                    await LoadSurplusBalancesAsync();
                    break;
                case 3:
                    await LoadRefundsAndDevolutionsAsync();
                    break;
            }
        }

        public async Task LoadClientsAsync()
        {
            try
            {
                var list = await _clientService.GetAllClientsAsync();
                Clients.Clear();
                foreach (var c in list)
                {
                    Clients.Add(c);
                }
                if (Clients.Count > 0 && SelectedClient == null)
                {
                    SelectedClient = Clients[0];
                }
            }
            catch { }
        }

        public async Task LoadContractsAsync()
        {
            try
            {
                var prevId = SelectedContract?.Id;
                var list = await _contractService.GetAllContractsAsync();
                Contracts.Clear();
                foreach (var c in list)
                {
                    Contracts.Add(c);
                }
                if (prevId != null)
                {
                    SelectedContract = Contracts.FirstOrDefault(c => c.Id == prevId) ?? (Contracts.Count > 0 ? Contracts[0] : null);
                }
                else if (Contracts.Count > 0)
                {
                    SelectedContract = Contracts[0];
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erro ao carregar contratos: {ex.Message}";
            }
        }

        public async Task LoadPaymentsAsync()
        {
            try
            {
                var prevId = SelectedPayment?.Id;
                var list = await _paymentService.GetAllPaymentsWithDetailsAsync();
                Payments.Clear();
                foreach (var p in list)
                {
                    Payments.Add(p);
                }
                if (prevId != null)
                {
                    SelectedPayment = Payments.FirstOrDefault(p => p.Id == prevId) ?? (Payments.Count > 0 ? Payments[0] : null);
                }
                else if (Payments.Count > 0)
                {
                    SelectedPayment = Payments[0];
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erro ao carregar pagamentos: {ex.Message}";
            }
        }

        public async Task LoadSurplusBalancesAsync()
        {
            try
            {
                var prevId = SelectedSurplus?.Id;
                var list = await _paymentService.GetAllSurplusBalancesWithDetailsAsync();
                SurplusBalances.Clear();
                foreach (var s in list)
                {
                    SurplusBalances.Add(s);
                }
                if (prevId != null)
                {
                    SelectedSurplus = SurplusBalances.FirstOrDefault(s => s.Id == prevId) ?? (SurplusBalances.Count > 0 ? SurplusBalances[0] : null);
                }
                else if (SurplusBalances.Count > 0)
                {
                    SelectedSurplus = SurplusBalances[0];
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erro ao carregar saldos de excedente: {ex.Message}";
            }
        }

        public async Task LoadRefundsAndDevolutionsAsync()
        {
            try
            {
                var prevDevId = SelectedDevolution?.Id;
                var refundsList = await _refundService.GetAllRefundsAsync();
                Refunds.Clear();
                foreach (var r in refundsList)
                {
                    Refunds.Add(r);
                }

                var devsList = await _refundService.GetAllDevolutionsAsync();
                Devolutions.Clear();
                foreach (var d in devsList)
                {
                    Devolutions.Add(d);
                }
                if (prevDevId != null)
                {
                    SelectedDevolution = Devolutions.FirstOrDefault(d => d.Id == prevDevId) ?? (Devolutions.Count > 0 ? Devolutions[0] : null);
                }
                else if (Devolutions.Count > 0)
                {
                    SelectedDevolution = Devolutions[0];
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erro ao carregar estornos e devoluções: {ex.Message}";
            }
        }

        public async Task LoadAllDataAsync()
        {
            await InitializeAsync();
            if (IsPro)
            {
                await LoadContractsAsync();
                await LoadPaymentsAsync();
                await LoadSurplusBalancesAsync();
                await LoadRefundsAndDevolutionsAsync();
            }
        }

        // ==========================================
        // AÇÕES DA ABA CONTRATOS
        // ==========================================
        public async Task CreateContractAsync()
        {
            if (IsBusy) return;
            ClearMessages();

            if (!IsPro)
            {
                ErrorMessage = "Operação bloqueada: requer licença CobraPix PRO ativa.";
                return;
            }

            if (SelectedClient == null)
            {
                ErrorMessage = "Selecione um cliente para criar o contrato.";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewContractNumber))
            {
                ErrorMessage = "Informe o número/código do contrato.";
                return;
            }

            if (NewPrincipalAmount <= 0)
            {
                ErrorMessage = "O valor principal deve ser maior que zero.";
                return;
            }

            if (NewInstallmentsCount < 1)
            {
                ErrorMessage = "A quantidade de parcelas deve ser de pelo menos 1.";
                return;
            }

            IsBusy = true;
            try
            {
                long principalCents = (long)Math.Round(NewPrincipalAmount * 100m, MidpointRounding.AwayFromZero);
                long interestCents = (long)Math.Round(NewTotalInterest * 100m, MidpointRounding.AwayFromZero);

                var req = new CreateContractRequestDto
                {
                    ClientId = SelectedClient.Id,
                    ContractNumber = NewContractNumber.Trim(),
                    ContractType = CreditContractType.FixedInstallments,
                    PrincipalAmountCents = principalCents,
                    TotalInterestCents = interestCents,
                    InstallmentsCount = NewInstallmentsCount,
                    FirstDueDate = NewFirstDueDate.Date,
                    ContractDate = NewContractDate.Date,
                    Notes = NewNotes,
                    IdempotencyKey = Guid.NewGuid().ToString()
                };

                var res = await _contractService.CreateFixedContractAsync(req);
                SuccessMessage = $"Contrato '{res.ContractNumber}' criado com sucesso! Total: R$ {res.TotalAmountCents / 100.0:N2} ({res.InstallmentsGenerated} parcelas).";
                NewContractNumber = string.Empty;
                await LoadContractsAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = HandleDomainException(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task DisburseContractAsync()
        {
            if (IsBusy || SelectedContract == null) return;
            ClearMessages();

            if (!IsPro)
            {
                ErrorMessage = "Operação bloqueada: requer licença CobraPix PRO ativa.";
                return;
            }

            IsBusy = true;
            try
            {
                var req = new DisburseContractRequestDto
                {
                    ContractId = SelectedContract.Id,
                    DisbursementDate = DisbursementDate.Date,
                    PaymentMethod = DisbursementMethod,
                    Notes = "Desembolso registrado via painel de Crédito & Giro",
                    IdempotencyKey = Guid.NewGuid().ToString()
                };

                var res = await _contractService.DisburseContractAsync(req);
                SuccessMessage = $"Desembolso de R$ {res.DisbursedAmountCents / 100.0:N2} realizado com sucesso! Contrato ativo.";
                await LoadContractsAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = HandleDomainException(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task CancelContractAsync()
        {
            if (IsBusy || SelectedContract == null) return;
            ClearMessages();

            if (!IsPro)
            {
                ErrorMessage = "Operação bloqueada: requer licença CobraPix PRO ativa.";
                return;
            }

            if (string.IsNullOrWhiteSpace(CancellationReason))
            {
                ErrorMessage = "Informe o motivo do cancelamento.";
                return;
            }

            IsBusy = true;
            try
            {
                var req = new CancelContractRequestDto
                {
                    ContractId = SelectedContract.Id,
                    Reason = CancellationReason.Trim(),
                    IdempotencyKey = Guid.NewGuid().ToString()
                };

                var res = await _contractService.CancelContractAsync(req);
                SuccessMessage = $"Contrato cancelado com sucesso: {res.Message}";
                CancellationReason = string.Empty;
                await LoadContractsAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = HandleDomainException(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ==========================================
        // AÇÕES DA ABA PAGAMENTOS
        // ==========================================
        public async Task PreviewPaymentAsync()
        {
            if (IsBusy) return;
            ClearMessages();

            if (string.IsNullOrWhiteSpace(PaymentContractId))
            {
                ErrorMessage = "Selecione ou informe o ID do contrato para a prévia.";
                return;
            }

            if (PaymentAmount <= 0)
            {
                ErrorMessage = "O valor do pagamento deve ser maior que zero.";
                return;
            }

            try
            {
                long cents = (long)Math.Round(PaymentAmount * 100m, MidpointRounding.AwayFromZero);
                PaymentPreview = await _paymentService.PreviewPaymentDistributionAsync(PaymentContractId.Trim(), cents);
                StatusMessage = $"Prévia calculada: Juros R$ {PaymentPreview.TotalInterestAllocatedCents / 100.0:N2}, Principal R$ {PaymentPreview.TotalPrincipalAllocatedCents / 100.0:N2}, Excedente R$ {PaymentPreview.SurplusGeneratedCents / 100.0:N2}.";
            }
            catch (Exception ex)
            {
                ErrorMessage = HandleDomainException(ex);
            }
        }

        public async Task RegisterPaymentAsync()
        {
            if (IsBusy) return;
            ClearMessages();

            if (!IsPro)
            {
                ErrorMessage = "Operação bloqueada: requer licença CobraPix PRO ativa.";
                return;
            }

            if (string.IsNullOrWhiteSpace(PaymentContractId))
            {
                ErrorMessage = "Informe o ID do contrato.";
                return;
            }

            if (PaymentAmount <= 0)
            {
                ErrorMessage = "O valor do pagamento deve ser maior que zero.";
                return;
            }

            IsBusy = true;
            try
            {
                long cents = (long)Math.Round(PaymentAmount * 100m, MidpointRounding.AwayFromZero);
                var req = new PaymentRequestDto
                {
                    ContractId = PaymentContractId.Trim(),
                    AmountCents = cents,
                    PaymentDate = PaymentDate.Date,
                    PaymentMethod = PaymentMethod,
                    Notes = PaymentNotes,
                    IdempotencyKey = Guid.NewGuid().ToString()
                };

                var res = await _paymentService.RegisterPaymentAsync(req);
                SuccessMessage = $"Pagamento de R$ {res.AmountPaidCents / 100.0:N2} registrado com sucesso! Juros amortizados: R$ {res.TotalInterestAllocatedCents / 100.0:N2}, Principal: R$ {res.TotalPrincipalAllocatedCents / 100.0:N2}.";
                if (res.SurplusGeneratedCents > 0)
                {
                    SuccessMessage += $" Excedente gerado: R$ {res.SurplusGeneratedCents / 100.0:N2}.";
                }

                PaymentNotes = string.Empty;
                PaymentPreview = null;
                await LoadPaymentsAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = HandleDomainException(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ==========================================
        // AÇÕES DA ABA EXCEDENTES
        // ==========================================
        public async Task ConsumeSurplusAsync()
        {
            if (IsBusy || SelectedSurplus == null) return;
            ClearMessages();

            if (!IsPro)
            {
                ErrorMessage = "Operação bloqueada: requer licença CobraPix PRO ativa.";
                return;
            }

            if (string.IsNullOrWhiteSpace(ConsumeTargetInstallmentId))
            {
                ErrorMessage = "Informe a parcela de destino do mesmo contrato.";
                return;
            }

            if (ConsumeAmount <= 0)
            {
                ErrorMessage = "O valor a consumir deve ser maior que zero.";
                return;
            }

            IsBusy = true;
            try
            {
                long cents = (long)Math.Round(ConsumeAmount * 100m, MidpointRounding.AwayFromZero);
                var req = new ConsumeSurplusRequestDto
                {
                    SurplusBalanceId = SelectedSurplus.Id,
                    TargetInstallmentId = ConsumeTargetInstallmentId.Trim(),
                    AmountCents = cents,
                    IdempotencyKey = Guid.NewGuid().ToString()
                };

                var res = await _paymentService.ConsumeSurplusAsync(req);
                SuccessMessage = $"Excedente consumido com sucesso! Juros: R$ {res.InterestConsumedCents / 100.0:N2}, Principal: R$ {res.PrincipalConsumedCents / 100.0:N2}. Saldo restante: R$ {res.RemainingSurplusCents / 100.0:N2}.";
                await LoadSurplusBalancesAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = HandleDomainException(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ReverseSurplusAsync()
        {
            if (IsBusy || SelectedConsumption == null) return;
            ClearMessages();

            if (!IsPro)
            {
                ErrorMessage = "Operação bloqueada: requer licença CobraPix PRO ativa.";
                return;
            }

            IsBusy = true;
            try
            {
                var req = new ReverseSurplusConsumptionRequestDto
                {
                    ConsumptionId = SelectedConsumption.Id,
                    Reason = "Reversão solicitada pelo operador",
                    IdempotencyKey = Guid.NewGuid().ToString()
                };

                var res = await _paymentService.ReverseSurplusConsumptionAsync(req);
                SuccessMessage = $"Consumo revertido! Saldo de excedente restaurado em R$ {res.ReversedAmountCents / 100.0:N2}.";
                await LoadSurplusBalancesAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = HandleDomainException(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ==========================================
        // AÇÕES DA ABA ESTORNOS E DEVOLUÇÕES
        // ==========================================
        public async Task AuthorizeRefundAsync()
        {
            if (IsBusy) return;
            ClearMessages();

            if (!IsPro)
            {
                ErrorMessage = "Operação bloqueada: requer licença CobraPix PRO ativa.";
                return;
            }

            if (string.IsNullOrWhiteSpace(RefundPaymentId))
            {
                ErrorMessage = "Informe o ID do pagamento para estorno.";
                return;
            }

            if (string.IsNullOrWhiteSpace(RefundReason))
            {
                ErrorMessage = "Informe o motivo do estorno.";
                return;
            }

            IsBusy = true;
            try
            {
                var req = new AuthorizeRefundRequestDto
                {
                    PaymentId = RefundPaymentId.Trim(),
                    Reason = RefundReason.Trim(),
                    IdempotencyKey = Guid.NewGuid().ToString()
                };

                var res = await _refundService.AuthorizePaymentRefundAsync(req);
                SuccessMessage = $"Estorno autorizado! Dívida reaberta com sucesso ({res.ReopenedInstallmentsCount} parcelas). Valor: R$ {res.AuthorizedAmountCents / 100.0:N2}. (Atenção: o caixa não é movimentado até a liquidação da devolução).";
                RefundReason = string.Empty;
                await LoadRefundsAndDevolutionsAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = HandleDomainException(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task SettleDevolutionAsync()
        {
            if (IsBusy || SelectedDevolution == null) return;
            ClearMessages();

            if (!IsPro)
            {
                ErrorMessage = "Operação bloqueada: requer licença CobraPix PRO ativa.";
                return;
            }

            IsBusy = true;
            try
            {
                var req = new SettleDevolutionRequestDto
                {
                    DevolutionId = SelectedDevolution.Id,
                    SettlementDate = DateTime.Today,
                    PaymentMethod = SettlementPaymentMethod,
                    IdempotencyKey = Guid.NewGuid().ToString()
                };

                var res = await _refundService.SettleDevolutionAsync(req);
                SuccessMessage = $"Devolução liquidada com sucesso! Saída financeira registrada no caixa: R$ {res.AmountCents / 100.0:N2}.";
                await LoadRefundsAndDevolutionsAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = HandleDomainException(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task CancelDevolutionAsync()
        {
            if (IsBusy || SelectedDevolution == null) return;
            ClearMessages();

            if (!IsPro)
            {
                ErrorMessage = "Operação bloqueada: requer licença CobraPix PRO ativa.";
                return;
            }

            if (string.IsNullOrWhiteSpace(CancelDevolutionReason))
            {
                ErrorMessage = "Informe o motivo do cancelamento da devolução.";
                return;
            }

            IsBusy = true;
            try
            {
                var req = new CancelDevolutionRequestDto
                {
                    DevolutionId = SelectedDevolution.Id,
                    Reason = CancelDevolutionReason.Trim(),
                    IdempotencyKey = Guid.NewGuid().ToString()
                };

                var res = await _refundService.CancelDevolutionAsync(req);
                SuccessMessage = $"Devolução cancelada com sucesso! A dívida e a alocação original do pagamento foram restauradas integralmente.";
                CancelDevolutionReason = string.Empty;
                await LoadRefundsAndDevolutionsAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = HandleDomainException(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private string HandleDomainException(Exception ex)
        {
            if (ex is ProLicenseRequiredException)
                return "Operação exclusiva do CobraPix PRO. Licença PRO requerida.";

            if (ex is DependentSurplusConsumptionsActiveException)
                return "Bloqueio de domínio: Este pagamento gerou excedente que possui consumos ativos. Reverta os consumos dependentes primeiro.";

            if (ex is PaymentAlreadyRefundedException)
                return "Bloqueio de domínio: Este pagamento já possui estorno registrado ou não está ativo.";

            if (ex is ExceededEligibleDevolutionAmountException)
                return "Bloqueio de domínio: O valor solicitado excede o saldo elegível do pagamento.";

            if (ex is DevolutionInvalidStateException)
                return $"Bloqueio de domínio: Estado inválido para a operação: {ex.Message}";

            if (ex is IdempotencyConflictException)
                return "Conflito de idempotência: Esta operação já foi executada com parâmetros diferentes.";

            if (ex is FinancialInvariantViolationException)
                return $"Violação de invariante financeira: {ex.Message}";

            if (ex is CreditOperationPreconditionException)
                return $"Pré-condição não atendida: {ex.Message}";

            return $"Erro: {ex.Message}";
        }

        private void ClearMessages()
        {
            ErrorMessage = null;
            SuccessMessage = null;
            StatusMessage = null;
        }
    }
}
