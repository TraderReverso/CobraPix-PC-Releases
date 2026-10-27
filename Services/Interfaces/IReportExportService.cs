using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.ViewModels;

namespace CobraPix_PC.Services.Interfaces
{
    public class ReportExportResult
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public long FileSize { get; set; }
        public int RecordsCount { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Contrato para exportação de relatórios financeiros do CobraPix PC em formato CSV e Excel (.xlsx).
    /// Opera de forma offline, estritamente em leitura, sem envio a redes externas e sem alterar dados de banco.
    /// </summary>
    public interface IReportExportService
    {
        Task<ReportExportResult> ExportToCsvAsync(
            string filePath,
            IEnumerable<FinancialTransactionItemViewModel> transactions,
            string reportPeriodDescription,
            decimal totalReceived,
            decimal installmentsTotal,
            decimal monthlyTotal);

        Task<ReportExportResult> ExportToExcelAsync(
            string filePath,
            IEnumerable<FinancialTransactionItemViewModel> transactions,
            string reportPeriodDescription,
            decimal totalReceived,
            decimal installmentsTotal,
            decimal monthlyTotal);
    }
}
