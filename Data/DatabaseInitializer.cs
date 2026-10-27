using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace CobraPix_PC.Data
{
    /// <summary>
    /// Responsável pela inicialização segura e criação automática do banco SQLite local.
    /// </summary>
    public static class DatabaseInitializer
    {
        /// <summary>
        /// Garante que o diretório do banco configurado exista (após validação estrita de modo de execução)
        /// e executa EnsureCreated() para criar o arquivo de banco com todas as tabelas caso ainda não existam.
        /// </summary>
        /// <returns>True se o banco foi recém-criado; false se já existia.</returns>
        public static bool Initialize()
        {
            // Valida o caminho do banco antes de qualquer efeito colateral no filesystem
            string dbFilePath = DatabasePath.GetDatabaseFilePath();
            string? dbDir = Path.GetDirectoryName(dbFilePath);
            if (!string.IsNullOrWhiteSpace(dbDir) && !Directory.Exists(dbDir))
            {
                Directory.CreateDirectory(dbDir);
            }

            // Instancia o contexto e assegura que as tabelas sejam criadas
            using var context = new CobraPixDbContext();
            bool created = context.Database.EnsureCreated();

            // Garante de forma estritamente aditiva e não-destrutiva que a tabela Reminders exista em bases pré-existentes
            EnsureRemindersTableCreated(context);

            return created;
        }

        public static void EnsureParityColumnsCreated(CobraPixDbContext context)
        {
            try
            {
                var conn = context.Database.GetDbConnection();
                bool wasClosed = conn.State != System.Data.ConnectionState.Open;
                if (wasClosed) conn.Open();

                // 1. Verificar colunas na tabela Sales
                var salesColumns = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(Sales);";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        salesColumns.Add(reader.GetString(1));
                    }
                }

                if (salesColumns.Count > 0)
                {
                    if (!salesColumns.Contains("OriginalPrincipal"))
                    {
                        using var cmdAdd = conn.CreateCommand();
                        cmdAdd.CommandText = "ALTER TABLE Sales ADD COLUMN OriginalPrincipal DECIMAL(18, 2) NOT NULL DEFAULT 0;";
                        cmdAdd.ExecuteNonQuery();
                    }
                    if (!salesColumns.Contains("ContractInterestRate"))
                    {
                        using var cmdAdd = conn.CreateCommand();
                        cmdAdd.CommandText = "ALTER TABLE Sales ADD COLUMN ContractInterestRate REAL NOT NULL DEFAULT 0.0;";
                        cmdAdd.ExecuteNonQuery();
                    }
                    if (!salesColumns.Contains("ContractInterestAmount"))
                    {
                        using var cmdAdd = conn.CreateCommand();
                        cmdAdd.CommandText = "ALTER TABLE Sales ADD COLUMN ContractInterestAmount DECIMAL(18, 2) NOT NULL DEFAULT 0;";
                        cmdAdd.ExecuteNonQuery();
                    }
                    if (!salesColumns.Contains("UseFifthBusinessDay"))
                    {
                        using var cmdAdd = conn.CreateCommand();
                        cmdAdd.CommandText = "ALTER TABLE Sales ADD COLUMN UseFifthBusinessDay INTEGER NOT NULL DEFAULT 0;";
                        cmdAdd.ExecuteNonQuery();
                    }
                    if (!salesColumns.Contains("Periodicity"))
                    {
                        using var cmdAdd = conn.CreateCommand();
                        cmdAdd.CommandText = "ALTER TABLE Sales ADD COLUMN Periodicity INTEGER NOT NULL DEFAULT 0;";
                        cmdAdd.ExecuteNonQuery();
                    }
                }

                // 2. Verificar colunas na tabela MonthlyCustomers
                var monthlyColumns = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(MonthlyCustomers);";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        monthlyColumns.Add(reader.GetString(1));
                    }
                }

                if (monthlyColumns.Count > 0)
                {
                    if (!monthlyColumns.Contains("OriginalMonthlyAmount"))
                    {
                        using var cmdAdd = conn.CreateCommand();
                        cmdAdd.CommandText = "ALTER TABLE MonthlyCustomers ADD COLUMN OriginalMonthlyAmount DECIMAL(18, 2) NOT NULL DEFAULT 0;";
                        cmdAdd.ExecuteNonQuery();
                    }
                    if (!monthlyColumns.Contains("ContractInterestRate"))
                    {
                        using var cmdAdd = conn.CreateCommand();
                        cmdAdd.CommandText = "ALTER TABLE MonthlyCustomers ADD COLUMN ContractInterestRate REAL NOT NULL DEFAULT 0.0;";
                        cmdAdd.ExecuteNonQuery();
                    }
                    if (!monthlyColumns.Contains("ContractInterestAmount"))
                    {
                        using var cmdAdd = conn.CreateCommand();
                        cmdAdd.CommandText = "ALTER TABLE MonthlyCustomers ADD COLUMN ContractInterestAmount DECIMAL(18, 2) NOT NULL DEFAULT 0;";
                        cmdAdd.ExecuteNonQuery();
                    }
                }

                if (wasClosed) conn.Close();
            }
            catch
            {
                // Tratamento seguro em caso de concorrência ou base de teste em memória
            }
        }

        public static void EnsureRemindersTableCreated(CobraPixDbContext context)
        {
            try
            {
                var conn = context.Database.GetDbConnection();
                bool wasClosed = conn.State != System.Data.ConnectionState.Open;
                if (wasClosed) conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Reminders (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                        Title TEXT NOT NULL,
                        Description TEXT NULL,
                        DueDate TEXT NULL,
                        Priority INTEGER NOT NULL DEFAULT 1,
                        Category TEXT NOT NULL DEFAULT 'Geral',
                        Status INTEGER NOT NULL DEFAULT 0,
                        Recurrence INTEGER NOT NULL DEFAULT 0,
                        CustomIntervalDays INTEGER NULL,
                        SnoozeCount INTEGER NOT NULL DEFAULT 0,
                        RescheduledAt TEXT NULL,
                        CreatedAt TEXT NOT NULL,
                        CompletedAt TEXT NULL,
                        UpdatedAt TEXT NULL,
                        HistoryLog TEXT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_Reminders_DueDate ON Reminders (DueDate);
                    CREATE INDEX IF NOT EXISTS IX_Reminders_Status ON Reminders (Status);
                    CREATE INDEX IF NOT EXISTS IX_Reminders_Category ON Reminders (Category);
                    CREATE INDEX IF NOT EXISTS IX_Reminders_Priority ON Reminders (Priority);
                ";
                cmd.ExecuteNonQuery();

                if (wasClosed) conn.Close();
            }
            catch
            {
                // Tratamento seguro em caso de concorrência
            }
        }
    }
}
