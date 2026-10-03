using System;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Metadados armazenados internamente no pacote de backup .cpb (metadata.json).
    /// </summary>
    public class BackupMetadata
    {
        public string Application { get; set; } = "CobraPix PC";
        public string Version { get; set; } = "1.0";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public long DatabaseSize { get; set; }
        public string Sha256Checksum { get; set; } = string.Empty;
        public string Notes { get; set; } = "Backup oficial CobraPix PC";
    }

    /// <summary>
    /// Resultado da criação de um backup.
    /// </summary>
    public class BackupResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? FilePath { get; set; }
        public long FileSize { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    /// <summary>
    /// Resultado da restauração de um backup.
    /// </summary>
    public class RestoreResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? SafetyBackupPath { get; set; }
        public BackupMetadata? Metadata { get; set; }
    }

    /// <summary>
    /// Resumo quantitativo prévio de um backup Android para exibição em confirmações de interface.
    /// </summary>
    public class AndroidBackupSummary
    {
        public int Version { get; set; }
        public int ClientsCount { get; set; }
        public int SalesCount { get; set; }
        public int InstallmentsCount { get; set; }
        public int InstallmentPaymentsCount { get; set; }
        public int MonthlyCustomersCount { get; set; }
        public int MonthlyPaymentsCount { get; set; }
        public int ExpensesCount { get; set; }
    }

    /// <summary>
    /// Resultado da validação prévia de um arquivo .cpb.
    /// </summary>
    public class BackupValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public BackupMetadata? Metadata { get; set; }
        public bool IsAndroid { get; set; }
        public bool IsAndroidJson { get; set; }
        public int AndroidVersion { get; set; }
        public AndroidBackupSummary? AndroidSummary { get; set; }
    }
}
