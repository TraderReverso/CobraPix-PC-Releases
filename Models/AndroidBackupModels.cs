using System;
using System.Collections.Generic;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Resultado da validação estrutural do cabeçalho do container Android (.cpb).
    /// </summary>
    public class AndroidBackupValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int Version { get; set; }
        public long PayloadLength { get; set; }
    }

    /// <summary>
    /// Resultado da extração e validação do payload SQLite a partir de um backup Android (.cpb).
    /// </summary>
    public class AndroidExtractionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ExtractedDbPath { get; set; }
        public int Version { get; set; }
        public long PayloadLength { get; set; }
        public int TableCount { get; set; }
    }

    /// <summary>
    /// Representação das informações de schema descobertas no banco SQLite Android.
    /// </summary>
    public class AndroidSchemaInfo
    {
        public List<AndroidTableInfo> Tables { get; set; } = new();
    }

    /// <summary>
    /// Metadados de uma tabela identificada no banco SQLite Android.
    /// </summary>
    public class AndroidTableInfo
    {
        public string TableName { get; set; } = string.Empty;
        public List<AndroidColumnInfo> Columns { get; set; } = new();
        public List<AndroidForeignKeyInfo> ForeignKeys { get; set; } = new();
    }

    /// <summary>
    /// Metadados de uma coluna de uma tabela SQLite.
    /// </summary>
    public class AndroidColumnInfo
    {
        public int Cid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool NotNull { get; set; }
        public bool IsPrimaryKey { get; set; }
        public string? DefaultValue { get; set; }
    }

    /// <summary>
    /// Metadados de uma Foreign Key configurada no SQLite.
    /// </summary>
    public class AndroidForeignKeyInfo
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public string Table { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string OnUpdate { get; set; } = string.Empty;
        public string OnDelete { get; set; } = string.Empty;
    }
}
