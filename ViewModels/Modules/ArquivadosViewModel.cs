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
    public class ArquivadoItemViewModel : ViewModelBase
    {
        public int ClientId { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalSalesCount { get; set; }
        public decimal TotalSpentAmount { get; set; }

        public string StatusText => "Arquivado";
        public string FormattedCreatedAt => CreatedAt.ToString("dd/MM/yyyy");
        public string FormattedTotalSpentAmount => TotalSpentAmount.ToString("C2");
        public string FormattedPhone => !string.IsNullOrWhiteSpace(Phone) ? Phone : "Não informado";
    }

    /// <summary>
    /// ViewModel do módulo de Clientes Arquivados.
    /// Lista cadastros com IsArchived == true, preservando integralmente o histórico de transações.
    /// Gerencia reativação respeitando as regras e cotas do plano comercial FREE/PRO.
    /// </summary>
    public class ArquivadosViewModel : ModuleViewModelBase
    {
        private readonly CobraPixDbContext _context;
        private readonly IClientService _clientService;
        private readonly List<ArquivadoItemViewModel> _allArquivados = new();

        private string _searchText = string.Empty;
        private ArquivadoItemViewModel? _selectedArquivado;
        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;
        private string? _successMessage;

        public override string Title => "Clientes Arquivados";
        public override string Description => "Registros arquivados logicamente com preservação do histórico de transações.";
        public override string Icon => "📁";
        public override string Category => "CONSULTAS & STATUS";

        public ObservableCollection<ArquivadoItemViewModel> FilteredArquivados { get; } = new();

        public ArquivadoItemViewModel? SelectedArquivado
        {
            get => _selectedArquivado;
            set
            {
                if (SetProperty(ref _selectedArquivado, value))
                {
                    OnPropertyChanged(nameof(HasSelectedArquivado));
                }
            }
        }

        public bool HasSelectedArquivado => SelectedArquivado != null;

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

        public string? SuccessMessage
        {
            get => _successMessage;
            set
            {
                if (SetProperty(ref _successMessage, value))
                {
                    OnPropertyChanged(nameof(HasSuccessMessage));
                }
            }
        }

        public bool HasSuccessMessage => !string.IsNullOrEmpty(SuccessMessage);

        public int TotalArquivadosCount => _allArquivados.Count;
        public bool IsEmpty => !IsLoading && _allArquivados.Count == 0;
        public bool IsSearchEmpty => !IsLoading && _allArquivados.Count > 0 && FilteredArquivados.Count == 0;

        // Comandos
        public ICommand LoadArquivadosCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ReactivateSelectedCommand { get; }

        public ArquivadosViewModel()
            : this(
                App.Services?.GetService<CobraPixDbContext>() ?? new CobraPixDbContext(),
                App.Services?.GetService<IClientService>() ?? new ClientService(new CobraPixDbContext()))
        {
        }

        public ArquivadosViewModel(
            CobraPixDbContext context,
            IClientService clientService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));

            LoadArquivadosCommand = new RelayCommand(async () => await LoadArquivadosAsync());
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            ReactivateSelectedCommand = new RelayCommand(async param =>
            {
                var target = param as ArquivadoItemViewModel ?? SelectedArquivado;
                if (target != null)
                {
                    await ReactivateClientAsync(target.ClientId);
                }
            });

            _ = LoadArquivadosAsync();
        }

        public async Task LoadArquivadosAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;
            SuccessMessage = null;

            try
            {
                var archivedClients = await _context.Clients
                    .AsNoTracking()
                    .Include(c => c.Sales)
                    .Where(c => c.IsArchived)
                    .ToListAsync();

                _allArquivados.Clear();

                foreach (var c in archivedClients)
                {
                    int salesCount = c.Sales?.Count ?? 0;
                    decimal totalSpent = c.Sales?.Sum(s => s.TotalAmount) ?? 0m;

                    _allArquivados.Add(new ArquivadoItemViewModel
                    {
                        ClientId = c.Id,
                        ClientName = c.Name,
                        Phone = c.Phone,
                        Notes = c.Notes,
                        CreatedAt = c.CreatedAt,
                        TotalSalesCount = salesCount,
                        TotalSpentAmount = totalSpent
                    });
                }

                _allArquivados.Sort((a, b) => string.Compare(a.ClientName, b.ClientName, StringComparison.CurrentCultureIgnoreCase));

                ApplyFilterAndSearch();
                OnPropertyChanged(nameof(TotalArquivadosCount));
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Erro ao carregar clientes arquivados: " + ex.Message;
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
            IEnumerable<ArquivadoItemViewModel> query = _allArquivados;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(a =>
                    (a.ClientName != null && a.ClientName.ToLowerInvariant().Contains(term)) ||
                    (a.Phone != null && a.Phone.Contains(term)));
            }

            FilteredArquivados.Clear();
            foreach (var item in query)
            {
                FilteredArquivados.Add(item);
            }

            if (SelectedArquivado != null && !FilteredArquivados.Contains(SelectedArquivado))
            {
                SelectedArquivado = FilteredArquivados.FirstOrDefault();
            }
            else if (SelectedArquivado == null)
            {
                SelectedArquivado = FilteredArquivados.FirstOrDefault();
            }

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        public async Task ReactivateClientAsync(int clientId)
        {
            HasError = false;
            ErrorMessage = null;
            SuccessMessage = null;

            try
            {
                // Utiliza obrigatoriamente o serviço oficial IClientService (que valida cotas no IProService)
                await _clientService.ReactivateClientAsync(clientId);
                await LoadArquivadosAsync();
                SuccessMessage = "Cliente reativado com sucesso!";
            }
            catch (BusinessRuleException ex)
            {
                HasError = true;
                ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Erro ao reativar cliente: " + ex.Message;
            }
        }
    }
}
