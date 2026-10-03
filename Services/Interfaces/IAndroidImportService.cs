using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato para importação de backups Android (.cpb JSON) para o banco do CobraPix PC.
    /// </summary>
    public interface IAndroidImportService
    {
        /// <summary>
        /// Determina se o arquivo .cpb informado é um backup Android no formato JSON.
        /// </summary>
        bool IsAndroidJsonBackup(string filePath);

        /// <summary>
        /// Inspeciona o arquivo de backup Android JSON sem modificar o banco, retornando o resumo quantitativo.
        /// Retorna null se o arquivo não for um backup Android JSON válido.
        /// </summary>
        AndroidBackupSummary? GetSummary(string filePath);

        /// <summary>
        /// Importa todos os dados do backup Android JSON para o banco PC de forma transacional.
        /// Cria safety backup antes de modificar o banco de destino.
        /// Realiza ROLLBACK automático em caso de falha.
        /// NÃO importa expenses (módulo ausente no PC).
        /// </summary>
        Task<AndroidImportResult> ImportAsync(string backupFilePath, string? targetDatabasePath = null);
    }
}
