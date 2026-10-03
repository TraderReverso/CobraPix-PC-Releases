using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato responsável pela leitura, validação estrutural do cabeçalho
    /// e extração segura do payload SQLite de backups gerados no formato Android (.cpb).
    /// </summary>
    public interface IAndroidBackupReader
    {
        /// <summary>
        /// Verifica de forma rápida e segura se o arquivo possui a assinatura do container Android ("CPBv1").
        /// </summary>
        bool IsAndroidBackup(string filePath);

        /// <summary>
        /// Valida integralmente o cabeçalho de 17 bytes (Magic, Version, PayloadLength) e a integridade de tamanho do arquivo.
        /// </summary>
        AndroidBackupValidationResult ValidateHeader(string filePath);

        /// <summary>
        /// Extrai o payload SQLite bruto para um arquivo temporário seguro (fora do banco de produção)
        /// e valida se a integridade do banco SQLite é válida.
        /// </summary>
        Task<AndroidExtractionResult> ExtractSqlitePayloadAsync(string filePath, string? targetTempPath = null);

        /// <summary>
        /// Inspeciona o schema do banco SQLite Android extraído (tabelas, colunas, tipos, PKs e FKs).
        /// </summary>
        AndroidSchemaInfo InspectSchema(string sqliteDbPath);

        /// <summary>
        /// Valida se o arquivo apontado é um banco SQLite íntegro e passível de consulta via SELECT em sqlite_master.
        /// </summary>
        bool ValidateSqliteDatabase(string sqliteDbPath, out string? errorMessage);
    }
}
