using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato para operações locais e seguras de Backup e Restauração de dados (.cpb) do CobraPix PC.
    /// </summary>
    public interface IBackupService
    {
        /// <summary>
        /// Obtém o caminho absoluto do banco de dados local atual (%LOCALAPPDATA%\CobraPix\cobrapix.db).
        /// </summary>
        string GetCurrentDatabasePath();

        /// <summary>
        /// Obtém a informação do último backup registrado no sistema (data e caminho).
        /// </summary>
        Task<string?> GetLastBackupInfoAsync();

        /// <summary>
        /// Gera uma cópia consistente do banco SQLite e empacota com metadados em um arquivo com extensão .cpb.
        /// </summary>
        /// <param name="destinationFilePath">Caminho de destino do arquivo .cpb escolhido pelo usuário.</param>
        /// <param name="sourceDatabasePath">Caminho opcional do banco de origem (útil para testes isolados).</param>
        Task<BackupResult> CreateBackupAsync(string destinationFilePath, string? sourceDatabasePath = null);

        /// <summary>
        /// Valida a integridade, o formato, a assinatura e o hash SHA-256 de um arquivo de backup (.cpb).
        /// </summary>
        BackupValidationResult ValidateBackupFile(string backupFilePath);

        /// <summary>
        /// Restaura um backup a partir de um arquivo .cpb.
        /// Cria automaticamente um backup de segurança do banco atual antes de aplicar a substituição.
        /// </summary>
        /// <param name="backupFilePath">Caminho do arquivo .cpb a ser restaurado.</param>
        /// <param name="targetDatabasePath">Caminho opcional do banco de destino (útil para testes isolados).</param>
        Task<RestoreResult> RestoreBackupAsync(string backupFilePath, string? targetDatabasePath = null);

        /// <summary>
        /// Retorna o caminho do diretório exclusivo para backups automáticos (%LOCALAPPDATA%\CobraPix\auto_backups).
        /// </summary>
        string GetAutoBackupDirectory();

        /// <summary>
        /// Indica se a rotina de backup automático diário está ativada nas configurações.
        /// </summary>
        Task<bool> IsAutoBackupEnabledAsync();

        /// <summary>
        /// Salva a preferência de ativação ou desativação do backup automático diário.
        /// </summary>
        Task SetAutoBackupEnabledAsync(bool enabled);

        /// <summary>
        /// Retorna a data (yyyy-MM-dd) ou informações resumidas do último backup automático diário realizado.
        /// </summary>
        Task<string?> GetLastAutoBackupInfoAsync();

        /// <summary>
        /// Verifica se já existe um backup automático válido realizado na data atual.
        /// Se não existir e o recurso estiver ativado, realiza o backup de forma segura e não bloqueante.
        /// Retorna o resultado da operação, ou null se a execução foi dispensada por já existir backup do dia ou estar desativado.
        /// </summary>
        Task<BackupResult?> CheckAndPerformDailyAutoBackupAsync(string? customSourceDb = null, string? customTargetDir = null, bool force = false);
    }
}