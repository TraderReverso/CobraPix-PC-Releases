using System;

namespace CobraPix_PC.Data
{
    /// <summary>
    /// Responsável pela inicialização segura e criação automática do banco SQLite local.
    /// </summary>
    public static class DatabaseInitializer
    {
        /// <summary>
        /// Garante que o diretório %LOCALAPPDATA%\CobraPix exista e executa EnsureCreated()
        /// para criar o arquivo de banco cobrapix.db com todas as tabelas caso ainda não existam.
        /// </summary>
        /// <returns>True se o banco foi recém-criado; false se já existia.</returns>
        public static bool Initialize()
        {
            // Garante criação do diretório em %LOCALAPPDATA%\CobraPix
            DatabasePath.GetDatabaseDirectory();

            // Instancia o contexto e assegura que as tabelas sejam criadas
            using var context = new CobraPixDbContext();
            bool created = context.Database.EnsureCreated();

            // Garante de forma estritamente aditiva e não-destrutiva que a tabela Reminders exista em bases pré-existentes
            EnsureRemindersTableCreated(context);

            return created;
        }

        public static void EnsureRemindersTableCreated(CobraPixDbContext context)
        {
            try
            {
                var conn = Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetDbConnection(context.Database);
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
                // Tratamento seguro em caso de concorrência ou base em memória
            }
        }
    }
}
