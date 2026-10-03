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
            return context.Database.EnsureCreated();
        }
    }
}
