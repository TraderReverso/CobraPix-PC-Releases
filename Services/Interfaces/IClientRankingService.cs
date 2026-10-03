using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.Models;
using CobraPix_PC.ViewModels.Modules;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato de serviço para cálculo de classificação objetiva de clientes (S, A, B, C, D, E)
    /// com base no comportamento de pagamento e pontualidade.
    /// </summary>
    public interface IClientRankingService
    {
        /// <summary>
        /// Calcula de maneira determinística a classificação de um cliente com base em seu histórico real.
        /// </summary>
        ClientClassification CalculateClassification(Client client);

        /// <summary>
        /// Retorna a lista de todos os clientes ranqueados por classificação (S até E) e nome em ordem alfabética.
        /// </summary>
        Task<List<RanqueamentoItemViewModel>> GetRankedClientsAsync();
    }
}
