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
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.ViewModels.Modules
{
    public class QuitadoItemViewModel : ViewModelBase
    {
        public int ClientId { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public int QuitadasSalesCount { get; set; }
        public decimal TotalQuitadoAmount { get; set; }
        public DateTime? LastSaleDate { get; set; }
        public string StatusText => "100% Quitado";

        public string FormattedTotalQuitadoAmount => TotalQuitadoAmount.ToString("C2");
        public string FormattedLastSaleDate => LastSaleDate.HasValue
            ? LastSaleDate.Value.ToString("dd/MM/yyyy")
            : "N/A";

        public string FormattedPhone => !string.IsNullOrWhiteSpace(Phone) ? Phone : "Não informado";
    }

    /// <summary>
    /// ViewModel do módulo de Clientes e Vendas Quitadas.
    /// Lista clientes que liquidaram integralmente todas as parcelas de suas vendas e não possuem mensalidades ativas.
    /// Não consome o limite do plano FREE.
    /// </summary>
    public class QuitadosViewModel : ModuleViewModelBase
    {
        private readonly CobraPixDbContext _context;
        private readonly IClientService _clientService;
        private readonly Action<int>? _openNewSaleForClient;
        private readonly List<QuitadoItemViewModel> _allQuitados = new();

        private string _searchText = string.Empty;
        private QuitadoItemViewModel? _selectedQuitado;
        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;

        // Controle do Diálogo de Confirmação de Arquivamento
        private bool _isConfirmOpen;
        private string _confirmTitle = string.Empty;
        private string _confirmMessage = string.Empty;
        private Func<Task>? _onConfirmAction;

        public override string Title => "Clientes Quitados";
        public override string Description => "Consulta a clientes com todas as compras totalmente liquidadas e sem dívidas ativas.";
        public override string Icon => "✅";
        public override string Category => "CONSULTAS & STATUS";

        public ObservableCollection<QuitadoItemViewModel> FilteredQuitados { get; } = new();

        public QuitadoItemViewModel? SelectedQuitado
        {
            get => _selectedQuitado;
            set
            {
                if (SetProperty(ref _selectedQuitado, value))
                {
                    OnPropertyChanged(nameof(HasSelectedQuitado));
                }
            }
        }

        public bool HasSelectedQuitado => SelectedQuitado != null;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilterAndSearch();
                    OnPropertyChanged(nameof(HasSearchText));
                }
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
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

        public int TotalQuitadosCount => _allQuitados.Count;
        public decimal TotalQuitadosAmount => _allQuitados.Sum(q => q.TotalQuitadoAmount);
        public string FormattedTotalQuitadosAmount => TotalQuitadosAmount.ToString("C2");

        public bool IsEmpty => !IsLoading && _allQuitados.Count == 0;
        public bool IsSearchEmpty => !IsLoading && _allQuitados.Count > 0 && FilteredQuitados.Count == 0;

        // Confirmação
        public bool IsConfirmOpen
        {
            get => _isConfirmOpen;
            set => SetProperty(ref _isConfirmOpen, value);
        }

        public string ConfirmTitle
        {
            get => _confirmTitle;
            set => SetProperty(ref _confirmTitle, value);
        }

        public string ConfirmMessage
        {
            get => _confirmMessage;
            set => SetProperty(ref _confirmMessage, value);
        }

        // Comandos
        public ICommand LoadQuitadosCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand StartNewSaleCommand { get; }
        public ICommand ArchiveSelectedCommand { get; }
        public ICommand ConfirmDialogCommand { get; }
        public ICommand CancelDialogCommand { get; }

        public QuitadosViewModel()
            : this(
                App.Services?.GetService<CobraPixDbContext>() ?? new CobraPixDbContext(),
                App.Services?.GetService<IClientService>() ?? new ClientService(new CobraPixDbContext()),
                null)
        {
        }

        public QuitadosViewModel(
            CobraPixDbContext context,
            IClientService clientService,
            Action<int>? openNewSaleForClient = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _openNewSaleForClient = openNewSaleForClient;

            LoadQuitadosCommand = new RelayCommand(async () => await LoadQuitadosAsync());
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            StartNewSaleCommand = new RelayCommand(param =>
            {
                var target = param as QuitadoItemViewModel ?? SelectedQuitado;
                if (target != null)
                {
                    _openNewSaleForClient?.Invoke(target.ClientId);
                }
            });

            ArchiveSelectedCommand = new RelayCommand(param =>
            {
                var target = param as QuitadoItemViewModel ?? SelectedQuitado;
                if (target != null)
                {
                    RequestArchive(target);
                }
            });

            ConfirmDialogCommand = new RelayCommand(async () =>
            {
                if (_onConfirmAction != null)
                {
                    await _onConfirmAction.Invoke();
                }
                IsConfirmOpen = false;
            });
            CancelDialogCommand = new RelayCommand(() => IsConfirmOpen = false);

            _ = LoadQuitadosAsync();
        }

        public async Task LoadQuitadosAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            try
            {
                var clients = await _context.Clients
                    .AsNoTracking()
                    .Include(c => c.Sales)
                        .ThenInclude(s => s.Installments)
                    .Include(c => c.MonthlyCustomer)
                    .Where(c => !c.IsArchived)
                    .ToListAsync();

                _allQuitados.Clear();

                foreach (var c in clients)
                {
                    // Regra canônica única através de ProService.IsClientQuitado:
                    if (ProService.IsClientQuitado(c))
                    {
                        var totalAmount = c.Sales!.Sum(s => s.TotalAmount);
                        var lastSale = c.Sales!.OrderByDescending(s => s.SaleDate).FirstOrDefault();

                        _allQuitados.Add(new QuitadoItemViewModel
                        {
                            ClientId = c.Id,
                            ClientName = c.Name,
                            Phone = c.Phone,
                            QuitadasSalesCount = c.Sales!.Count,
                            TotalQuitadoAmount = totalAmount,
                            LastSaleDate = lastSale?.SaleDate
                        });
                    }
                }

                _allQuitados.Sort((a, b) => string.Compare(a.ClientName, b.ClientName, StringComparison.CurrentCultureIgnoreCase));

                ApplyFilterAndSearch();
                OnPropertyChanged(nameof(TotalQuitadosCount));
                OnPropertyChanged(nameof(TotalQuitadosAmount));
                OnPropertyChanged(nameof(FormattedTotalQuitadosAmount));
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Erro ao carregar clientes quitados: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsSearchEmpty));
            }
        }

        public void ApplyFilterAndSearch()
        {
            IEnumerable<QuitadoItemViewModel> query = _allQuitados;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(q =>
                    (q.ClientName != null && q.ClientName.ToLowerInvariant().Contains(term)) ||
                    (q.Phone != null && q.Phone.Contains(term)));
            }

            FilteredQuitados.Clear();
            foreach (var item in query)
            {
                FilteredQuitados.Add(item);
            }

            if (SelectedQuitado != null && !FilteredQuitados.Contains(SelectedQuitado))
            {
                SelectedQuitado = FilteredQuitados.FirstOrDefault();
            }
            else if (SelectedQuitado == null)
            {
                SelectedQuitado = FilteredQuitados.FirstOrDefault();
            }

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        private void RequestArchive(QuitadoItemViewModel item)
        {
            ConfirmTitle = "Confirmar Arquivamento";
            ConfirmMessage = $"Deseja arquivar o cliente '{item.ClientName}'?\n\nO cliente ficará no módulo de Arquivados, e todo o seu histórico financeiro de vendas quitadas continuará 100% preservado.";
            _onConfirmAction = async () =>
            {
                try
                {
                    await _clientService.ArchiveClientAsync(item.ClientId);
                    await LoadQuitadosAsync();
                }
                catch (Exception ex)
                {
                    HasError = true;
                    ErrorMessage = "Erro ao arquivar cliente: " + ex.Message;
                }
            };
            IsConfirmOpen = true;
        }
    }
}
