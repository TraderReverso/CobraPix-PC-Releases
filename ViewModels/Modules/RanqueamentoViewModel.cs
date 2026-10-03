using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    public enum RanqueamentoStatusFilter
    {
        Todos,
        Ativos,
        Quitados,
        Arquivados
    }

    public enum RanqueamentoFilterType
    {
        Todos,
        Ativos,
        Quitados,
        Arquivados
    }

    /// <summary>
    /// Item de exibição para a tabela de classificação e ranqueamento de clientes.
    /// Exibe a classificação em letras (S, A, B, C, D, E), histórico de pontualidade,
    /// quantitativo de pagamentos e atrasos, sem utilizar posições competitivas (1º/2º/3º).
    /// </summary>
    public class RanqueamentoItemViewModel : ViewModelBase
    {
        public int Position { get; set; }
        public string FormattedPosition => $"{Position}º";

        public int ClientId { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public string? Phone { get; set; }

        public int SalesCount { get; set; }
        public decimal TotalSoldAmount { get; set; }
        public int PaidInstallmentsCount { get; set; }
        public decimal TotalPaidAmount { get; set; }
        public int OpenInstallmentsCount { get; set; }
        public decimal TotalOpenAmount { get; set; }
        public int PaymentsCount { get; set; }
        public DateTime? LastPurchaseDate { get; set; }

        // Classificação Oficial (S, A, B, C, D, E)
        public ClientRank Rank { get; set; } = ClientRank.C;
        public string RankLetter => Rank.ToString();
        public string RankDescription { get; set; } = "Médio";
        public string RankColorHex { get; set; } = "#EAB308";
        public string FormattedBadge => $"[{RankLetter}] {RankDescription}";

        // Histórico de Pontualidade
        public decimal OnTimePercentage { get; set; }
        public string FormattedOnTimePercentage => HasSufficientHistory
            ? $"{OnTimePercentage:0.#}% no prazo"
            : "Sem histórico";
        public string PaymentHistoryDetail { get; set; } = string.Empty;
        public bool HasSufficientHistory { get; set; }

        // Atrasos e Situação
        public int OverdueCount { get; set; }
        public decimal OverdueAmount { get; set; }
        public bool HasOverdue => OverdueCount > 0;
        public string FormattedOverdueSummary => OverdueCount == 0
            ? "0 atrasos"
            : $"{OverdueCount} atraso(s) ({OverdueAmount:C2})";

        public string FormattedPaymentsSummary => $"{PaymentsCount} pagto(s) ({TotalPaidAmount:C2})";

        public bool IsActive { get; set; }
        public bool IsQuitado { get; set; }
        public bool IsArchived { get; set; }
        public string StatusDescription => IsArchived
            ? "Arquivado"
            : (IsQuitado ? "Quitado" : "Ativo");

        public string FormattedPhone => !string.IsNullOrWhiteSpace(Phone) ? Phone : "Não informado";
    }

    /// <summary>
    /// ViewModel oficial para o módulo de Ranqueamento de Clientes do CobraPix PC.
    /// Totalmente reformulado para o sistema não-competitivo por letras:
    /// S (Pagador Ouro), A (Top), B (Bom), C (Médio), D (Mais ou menos), E (Ruim).
    /// </summary>
    public class RanqueamentoViewModel : ModuleViewModelBase
    {
        private readonly IClientRankingService _rankingService;
        private readonly List<RanqueamentoItemViewModel> _allRanked = new();

        private ClientRank? _selectedRankFilter; // null = Todas
        private RanqueamentoStatusFilter _currentStatusFilter = RanqueamentoStatusFilter.Todos;
        private string _searchText = string.Empty;
        private RanqueamentoItemViewModel? _selectedItem;
        private bool _isLoading;
        private bool _hasError;
        private string? _errorMessage;

        public override string Title => "Ranqueamento de Clientes";
        public override string Description => "Classificação de clientes por comportamento financeiro, histórico de pontualidade e pagamentos.";
        public override string Icon => "🏅";
        public override string Category => "CONSULTAS & STATUS";

        public ObservableCollection<RanqueamentoItemViewModel> FilteredRanked { get; } = new();

        public RanqueamentoItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    OnPropertyChanged(nameof(HasSelectedItem));
                }
            }
        }

        public bool HasSelectedItem => SelectedItem != null;

        public ClientRank? SelectedRankFilter
        {
            get => _selectedRankFilter;
            set
            {
                if (SetProperty(ref _selectedRankFilter, value))
                {
                    OnPropertyChanged(nameof(IsFilterAllRanks));
                    OnPropertyChanged(nameof(IsFilterRankS));
                    OnPropertyChanged(nameof(IsFilterRankA));
                    OnPropertyChanged(nameof(IsFilterRankB));
                    OnPropertyChanged(nameof(IsFilterRankC));
                    OnPropertyChanged(nameof(IsFilterRankD));
                    OnPropertyChanged(nameof(IsFilterRankE));
                    ApplyFilters();
                }
            }
        }

        public bool IsFilterAllRanks => !SelectedRankFilter.HasValue;
        public bool IsFilterRankS => SelectedRankFilter == ClientRank.S;
        public bool IsFilterRankA => SelectedRankFilter == ClientRank.A;
        public bool IsFilterRankB => SelectedRankFilter == ClientRank.B;
        public bool IsFilterRankC => SelectedRankFilter == ClientRank.C;
        public bool IsFilterRankD => SelectedRankFilter == ClientRank.D;
        public bool IsFilterRankE => SelectedRankFilter == ClientRank.E;

        public RanqueamentoStatusFilter CurrentStatusFilter
        {
            get => _currentStatusFilter;
            set
            {
                if (SetProperty(ref _currentStatusFilter, value))
                {
                    OnPropertyChanged(nameof(IsStatusTodos));
                    OnPropertyChanged(nameof(IsStatusAtivos));
                    OnPropertyChanged(nameof(IsStatusQuitados));
                    OnPropertyChanged(nameof(IsStatusArquivados));
                    ApplyFilters();
                }
            }
        }

        public bool IsStatusTodos => CurrentStatusFilter == RanqueamentoStatusFilter.Todos;
        public bool IsStatusAtivos => CurrentStatusFilter == RanqueamentoStatusFilter.Ativos;
        public bool IsStatusQuitados => CurrentStatusFilter == RanqueamentoStatusFilter.Quitados;
        public bool IsStatusArquivados => CurrentStatusFilter == RanqueamentoStatusFilter.Arquivados;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    OnPropertyChanged(nameof(HasSearchText));
                    ApplyFilters();
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

        public int TotalRankedCount => _allRanked.Count;
        public int CountRankS => _allRanked.Count(x => x.Rank == ClientRank.S);
        public int CountRankA => _allRanked.Count(x => x.Rank == ClientRank.A);
        public int CountRankB => _allRanked.Count(x => x.Rank == ClientRank.B);
        public int CountRankC => _allRanked.Count(x => x.Rank == ClientRank.C);
        public int CountRankD => _allRanked.Count(x => x.Rank == ClientRank.D);
        public int CountRankE => _allRanked.Count(x => x.Rank == ClientRank.E);

        public bool IsEmpty => _allRanked.Count == 0 && !IsLoading;
        public bool IsSearchEmpty => FilteredRanked.Count == 0 && _allRanked.Count > 0 && !IsLoading;

        public ICommand LoadDataCommand { get; }
        public ICommand SetRankFilterCommand { get; }
        public ICommand SetStatusFilterCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand ClearSearchCommand { get; }

        public RanqueamentoViewModel()
            : this(App.Services?.GetService<IClientRankingService>() ?? new ClientRankingService(new CobraPixDbContext()))
        {
        }

        public RanqueamentoViewModel(CobraPixDbContext context)
            : this(new ClientRankingService(context))
        {
        }

        public RanqueamentoViewModel(IClientRankingService rankingService)
        {
            _rankingService = rankingService ?? throw new ArgumentNullException(nameof(rankingService));

            LoadDataCommand = new RelayCommand(async () => await LoadDataAsync());
            SetRankFilterCommand = new RelayCommand<string>(filterStr =>
            {
                if (string.IsNullOrWhiteSpace(filterStr) || filterStr.Equals("Todas", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedRankFilter = null;
                }
                else if (Enum.TryParse<ClientRank>(filterStr, true, out var rank))
                {
                    SelectedRankFilter = rank;
                }
            });

            SetStatusFilterCommand = new RelayCommand<RanqueamentoStatusFilter>(filter =>
            {
                CurrentStatusFilter = filter;
            });

            SetFilterCommand = new RelayCommand<object>(filterParam =>
            {
                if (filterParam is RanqueamentoFilterType ft)
                {
                    CurrentStatusFilter = (RanqueamentoStatusFilter)ft;
                }
                else if (filterParam is RanqueamentoStatusFilter sf)
                {
                    CurrentStatusFilter = sf;
                }
                else if (filterParam is string s && Enum.TryParse<RanqueamentoStatusFilter>(s, true, out var parsed))
                {
                    CurrentStatusFilter = parsed;
                }
            });

            ClearSearchCommand = new RelayCommand(() =>
            {
                SearchText = string.Empty;
                SelectedRankFilter = null;
                CurrentStatusFilter = RanqueamentoStatusFilter.Todos;
            });

            _ = LoadDataAsync();
        }

        public async Task LoadDataAsync()
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            try
            {
                var ranked = await _rankingService.GetRankedClientsAsync();

                _allRanked.Clear();
                _allRanked.AddRange(ranked);

                ApplyFilters();
                NotifyStatsChanged();
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Erro ao carregar ranqueamento de clientes: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

        private void ApplyFilters()
        {
            var query = _allRanked.AsEnumerable();

            // 1. Filtro por Classificação em Letras
            if (SelectedRankFilter.HasValue)
            {
                query = query.Where(item => item.Rank == SelectedRankFilter.Value);
            }

            // 2. Filtro por Situação Cadastral
            switch (CurrentStatusFilter)
            {
                case RanqueamentoStatusFilter.Ativos:
                    query = query.Where(item => item.IsActive);
                    break;
                case RanqueamentoStatusFilter.Quitados:
                    query = query.Where(item => item.IsQuitado);
                    break;
                case RanqueamentoStatusFilter.Arquivados:
                    query = query.Where(item => item.IsArchived);
                    break;
            }

            // 3. Filtro por Busca Textual
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(item =>
                    (item.ClientName != null && item.ClientName.ToLowerInvariant().Contains(term)) ||
                    (item.Phone != null && item.Phone.Contains(term)) ||
                    (item.RankDescription != null && item.RankDescription.ToLowerInvariant().Contains(term))
                );
            }

            // 4. Ordenação Padrão: Classificação (S -> E) e dentro da mesma alfabético por nome
            var list = query
                .OrderBy(item => item.Rank)
                .ThenBy(item => item.ClientName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            FilteredRanked.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Position = i + 1;
                FilteredRanked.Add(list[i]);
            }

            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        private void NotifyStatsChanged()
        {
            OnPropertyChanged(nameof(TotalRankedCount));
            OnPropertyChanged(nameof(CountRankS));
            OnPropertyChanged(nameof(CountRankA));
            OnPropertyChanged(nameof(CountRankB));
            OnPropertyChanged(nameof(CountRankC));
            OnPropertyChanged(nameof(CountRankD));
            OnPropertyChanged(nameof(CountRankE));
        }
    }
}
