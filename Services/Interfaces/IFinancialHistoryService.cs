using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato de serviço para consulta e consolidação do histórico financeiro de entradas (parcelas e mensalidades).
    /// </summary>
    public interface IFinancialHistoryService
    {
        Task<List<FinancialTransaction>> GetAllTransactionsAsync();

        Task<List<FinancialTransaction>> GetTransactionsByClientAsync(int clientId);

        Task<List<FinancialTransaction>> GetTransactionsByDateRangeAsync(DateTime startDate, DateTime endDate);

        Task<List<FinancialTransaction>> GetTransactionsByMonthAsync(int month, int year);

        Task<FinancialSummary> GetFinancialSummaryAsync(DateTime? startDate = null, DateTime? endDate = null);
    }
}
