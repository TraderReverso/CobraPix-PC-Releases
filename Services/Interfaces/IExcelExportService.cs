using System;
using System.Threading.Tasks;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Resultado consolidado da operação de exportação de dados para Excel (.xlsx).
    /// </summary>
    public class ExcelExportResult
    {
        public bool Success { get; set; }

        public string FilePath { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public string Message { get; set; } = string.Empty;

        public int ClientsCount { get; set; }

        public int SalesCount { get; set; }

        public int InstallmentsCount { get; set; }

        public int PaymentsCount { get; set; }

        public int MonthlyCustomersCount { get; set; }

        public int MonthlyPaymentsCount { get; set; }

        public int FinancialHistoryCount { get; set; }
    }

    /// <summary>
    /// Contrato para o serviço de exportação local e offline de dados para planilha Excel (.xlsx).
    /// Realiza leitura estrita sem alterar registros, configurações ou quotas.
    /// </summary>
    public interface IExcelExportService
    {
        /// <summary>
        /// Exporta todos os dados cadastrais e financeiros (Clientes, Vendas, Parcelas, Pagamentos,
        /// Mensalistas, Pagamentos Mensais e Histórico Financeiro) para um único arquivo .xlsx multi-abas.
        /// </summary>
        /// <param name="targetFilePath">Caminho completo de destino do arquivo .xlsx a ser gerado.</param>
        /// <param name="sourceDatabasePath">Caminho opcional do banco SQLite de origem (útil para testes isolados).</param>
        /// <returns>Resultado contendo status, contagens e caminho do arquivo gerado.</returns>
        Task<ExcelExportResult> ExportToExcelAsync(string targetFilePath, string? sourceDatabasePath = null);
    }
}
