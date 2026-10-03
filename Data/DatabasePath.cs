using System;
using System.IO;

namespace CobraPix_PC.Data
{
    /// <summary>
    /// Gerencia a localização do diretório e a string de conexão do SQLite local.
    /// Os dados são persistidos isoladamente em %LOCALAPPDATA%\CobraPix\cobrapix.db.
    /// </summary>
    public static class DatabasePath
    {
        private const string FolderName = "CobraPix";
        private const string DatabaseFileName = "cobrapix.db";

        /// <summary>
        /// Obtém o caminho do diretório em %LOCALAPPDATA%\CobraPix,
        /// criando a pasta caso ela não exista.
        /// </summary>
        public static string GetDatabaseDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(localAppData, FolderName);

            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }

            return appFolder;
        }

        /// <summary>
        /// Obtém o caminho completo do arquivo de banco de dados cobrapix.db.
        /// </summary>
        public static string GetDatabaseFilePath()
        {
            return Path.Combine(GetDatabaseDirectory(), DatabaseFileName);
        }

        /// <summary>
        /// Obtém a string de conexão formatada para o SQLite.
        /// </summary>
        public static string GetConnectionString()
        {
            return $"Data Source={GetDatabaseFilePath()}";
        }
    }
}
