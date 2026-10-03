using System.Collections.Generic;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Resultado consolidado da importação de um backup Android (.cpb JSON) para o banco PC.
    /// </summary>
    public class AndroidImportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? SafetyBackupPath { get; set; }

        // Contagens
        public int ClientsFound { get; set; }
        public int ClientsImported { get; set; }
        public int SalesFound { get; set; }
        public int SalesImported { get; set; }
        public int InstallmentsFound { get; set; }
        public int InstallmentsImported { get; set; }
        public int InstallmentPaymentsFound { get; set; }
        public int InstallmentPaymentsImported { get; set; }
        public int MonthlyCustomersFound { get; set; }
        public int MonthlyCustomersImported { get; set; }
        public int MonthlyPaymentsFound { get; set; }
        public int MonthlyPaymentsImported { get; set; }
        public int ExpensesFound { get; set; }
        public int ExpensesSkipped { get; set; }

        public string? BackupVersion { get; set; }

        public List<string> Warnings { get; set; } = new();
    }
}
