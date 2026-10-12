using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Data.Credit;

namespace CobraPix_PC.Services.Credit
{
    /// <summary>
    /// Executor oficial da migração de Crédito & Giro para produção ou cópia descartável controlada.
    /// Executa verificação prévia, snapshot consistente via SQLite Backup API,
    /// validação do backup, migração DDL aditiva, conferência de integridade e teste de idempotência.
    /// Não abre interface gráfica e encerra com código de saída adequado.
    /// </summary>
    public static class ProductionCreditMigrationRunner
    {
        public static async Task<List<string>> ExecuteMigrationAsync(string? targetDbPath = null)
        {
            var logs = new List<string>();

            string dbPath = !string.IsNullOrWhiteSpace(targetDbPath)
                ? targetDbPath
                : DatabasePath.GetDatabaseFilePath();

            logs.Add("==================================================================");
            logs.Add("COBRAPIX PC — EXECUTOR DE MIGRAÇÃO CONTROLADA DO CRÉDITO & GIRO");
            logs.Add("==================================================================");
            logs.Add($"Data/Hora: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            logs.Add($"Banco Alvo: {dbPath}");

            if (!File.Exists(dbPath))
            {
                throw new FileNotFoundException($"Banco de dados alvo não encontrado: {dbPath}");
            }

            // 0. Verificação de Processos Ativos do CobraPix
            int currentPid = Environment.ProcessId;
            var activeProcs = System.Diagnostics.Process.GetProcesses()
                .Where(p => p.Id != currentPid && (p.ProcessName.Contains("CobraPix", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (activeProcs.Count > 0)
            {
                string procList = string.Join(", ", activeProcs.Select(p => $"{p.ProcessName} (PID {p.Id})"));
                throw new InvalidOperationException($"ABORTADO: Processos CobraPix ativos detectados: {procList}. A aplicação deve estar fechada.");
            }
            logs.Add("Nenhum processo concorrente do CobraPix em execução: OK");

            // 1. Verificação de Espaço em Disco
            string driveRoot = Path.GetPathRoot(Path.GetFullPath(dbPath)) ?? "C:\\";
            var driveInfo = new DriveInfo(driveRoot);
            long freeBytes = driveInfo.AvailableFreeSpace;
            long requiredBytes = 100 * 1024 * 1024; // 100 MB mínimo
            logs.Add($"Espaço livre em {driveRoot}: {freeBytes / (1024 * 1024)} MB (Mínimo requerido: 100 MB)");

            if (freeBytes < requiredBytes)
            {
                throw new InvalidOperationException($"Espaço em disco insuficiente em {driveRoot}. Requerido: 100 MB, Disponível: {freeBytes / (1024 * 1024)} MB");
            }

            SqliteConnection.ClearAllPools();

            // 2. Pré-Verificações na Base Alvo
            logs.Add("------------------------------------------------------------------");
            logs.Add("1. PRÉ-VERIFICAÇÕES NO BANCO ALVO");
            logs.Add("------------------------------------------------------------------");

            long targetSizeBefore = new FileInfo(dbPath).Length;
            string targetShaBefore = ComputeFileSha256(dbPath);
            logs.Add($"Tamanho inicial: {targetSizeBefore} bytes");
            logs.Add($"SHA-256 inicial: {targetShaBefore}");

            var preCounts = new Dictionary<string, int>();

            using (var preConn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await preConn.OpenAsync();

                // PRAGMA integrity_check
                using (var cmd = preConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA integrity_check;";
                    var res = (string?)await cmd.ExecuteScalarAsync();
                    if (res != "ok")
                        throw new InvalidOperationException($"PRAGMA integrity_check falhou na base alvo antes da migração: {res}");
                    logs.Add($"PRAGMA integrity_check: {res}");
                }

                // PRAGMA foreign_key_check
                using (var cmd = preConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_key_check;";
                    using var rdr = await cmd.ExecuteReaderAsync();
                    int fkIssues = 0;
                    while (await rdr.ReadAsync()) fkIssues++;
                    if (fkIssues > 0)
                        throw new InvalidOperationException($"PRAGMA foreign_key_check encontrou {fkIssues} inconsistências prévias.");
                    logs.Add($"PRAGMA foreign_key_check: ok (0 violações)");
                }

                // Contagens pré-migração de tabelas legadas
                string[] legacyTables = new[] { "AppSettings", "Clients", "InstallmentPayments", "Installments", "MonthlyCustomers", "MonthlyPayments", "Sales" };
                foreach (var tbl in legacyTables)
                {
                    using (var cmdExists = preConn.CreateCommand())
                    {
                        cmdExists.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tbl}';";
                        int exists = Convert.ToInt32(await cmdExists.ExecuteScalarAsync());
                        if (exists > 0)
                        {
                            using var cmdCount = preConn.CreateCommand();
                            cmdCount.CommandText = $"SELECT COUNT(*) FROM [{tbl}];";
                            int c = Convert.ToInt32(await cmdCount.ExecuteScalarAsync());
                            preCounts[tbl] = c;
                            logs.Add($"Contagem prévia [{tbl}]: {c} registros");
                        }
                    }
                }

                // Verificação de versões de esquema prévias e consistência
                using (var cmdVerCheck = preConn.CreateCommand())
                {
                    cmdVerCheck.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CreditSchemaVersions';";
                    int hasVerTable = Convert.ToInt32(await cmdVerCheck.ExecuteScalarAsync());
                    if (hasVerTable > 0)
                    {
                        cmdVerCheck.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM CreditSchemaVersions;";
                        int existingVer = Convert.ToInt32(await cmdVerCheck.ExecuteScalarAsync());
                        logs.Add($"Tabela CreditSchemaVersions encontrada. Versão existente: {existingVer}");
                        if (existingVer > 2)
                        {
                            throw new InvalidOperationException($"ABORTADO: A base possui versão de esquema ({existingVer}) superior à suportada por este software (2).");
                        }
                    }
                    else
                    {
                        logs.Add("Tabela CreditSchemaVersions não presente (Base legada v0): OK");
                    }
                }
            }

            // 3. Backup Consistente via SQLite Online Backup API
            logs.Add("------------------------------------------------------------------");
            logs.Add("2. GERAÇÃO E VALIDAÇÃO DO BACKUP CONSISTENTE (SQLITE BACKUP API)");
            logs.Add("------------------------------------------------------------------");

            string backupDir = Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "Backups");
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            string backupFileName = $"pre_credit_migration_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db";
            string backupPath = Path.Combine(backupDir, backupFileName);

            using (var srcConn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False"))
            using (var dstConn = new SqliteConnection($"Data Source={backupPath};Pooling=False"))
            {
                await srcConn.OpenAsync();
                await dstConn.OpenAsync();
                srcConn.BackupDatabase(dstConn);
            }

            SqliteConnection.ClearAllPools();

            if (!File.Exists(backupPath))
            {
                throw new InvalidOperationException("FALHA CRÍTICA: O arquivo de backup não foi gerado.");
            }

            long backupSize = new FileInfo(backupPath).Length;
            string backupSha = ComputeFileSha256(backupPath);
            logs.Add($"Backup gerado com sucesso: {backupPath}");
            logs.Add($"Tamanho do backup: {backupSize} bytes");
            logs.Add($"SHA-256 do backup: {backupSha}");

            // Validação de Integridade e Conteúdo no Backup
            using (var bkpConn = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly;Pooling=False"))
            {
                await bkpConn.OpenAsync();

                using (var cmd = bkpConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA integrity_check;";
                    var res = (string?)await cmd.ExecuteScalarAsync();
                    if (res != "ok")
                        throw new InvalidOperationException($"PRAGMA integrity_check falhou na validação do arquivo de backup: {res}");
                    logs.Add($"Validação do backup - PRAGMA integrity_check: {res}");
                }

                using (var cmd = bkpConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_key_check;";
                    using var rdr = await cmd.ExecuteReaderAsync();
                    int fkIssues = 0;
                    while (await rdr.ReadAsync()) fkIssues++;
                    if (fkIssues > 0)
                        throw new InvalidOperationException("PRAGMA foreign_key_check falhou na validação do backup.");
                    logs.Add("Validação do backup - PRAGMA foreign_key_check: ok (0 violações)");
                }

                foreach (var kvp in preCounts)
                {
                    using var cmd = bkpConn.CreateCommand();
                    cmd.CommandText = $"SELECT COUNT(*) FROM [{kvp.Key}];";
                    int bkpCnt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    if (bkpCnt != kvp.Value)
                        throw new InvalidOperationException($"Divergência de contagem no backup para a tabela {kvp.Key} ({bkpCnt} != {kvp.Value}).");
                }
                logs.Add("Validação do backup - Contagem das tabelas legadas: 100% CONFERIDA");
            }

            // 4. Execução da Migração DDL Aditiva
            logs.Add("------------------------------------------------------------------");
            logs.Add("3. EXECUÇÃO DA MIGRAÇÃO ADITIVA (CreditDatabaseMigrator)");
            logs.Add("------------------------------------------------------------------");

            var options = new DbContextOptionsBuilder<CobraPixDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using (var migCtx = new CobraPixDbContext(options))
            {
                await CreditDatabaseMigrator.ApplyCreditMigrationsAsync(migCtx);
                logs.Add("CreditDatabaseMigrator.ApplyCreditMigrationsAsync executado com sucesso.");
            }

            // 5. Verificações Pós-Migração
            logs.Add("------------------------------------------------------------------");
            logs.Add("4. VERIFICAÇÕES PÓS-MIGRAÇÃO");
            logs.Add("------------------------------------------------------------------");

            SqliteConnection.ClearAllPools();

            using (var postConn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await postConn.OpenAsync();

                // PRAGMA integrity_check pós-migração
                using (var cmd = postConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA integrity_check;";
                    var res = (string?)await cmd.ExecuteScalarAsync();
                    if (res != "ok")
                        throw new InvalidOperationException($"PRAGMA integrity_check falhou após a migração: {res}");
                    logs.Add($"PRAGMA integrity_check pós-migração: {res}");
                }

                // PRAGMA foreign_key_check pós-migração
                using (var cmd = postConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_key_check;";
                    using var rdr = await cmd.ExecuteReaderAsync();
                    int fkIssues = 0;
                    while (await rdr.ReadAsync()) fkIssues++;
                    if (fkIssues > 0)
                        throw new InvalidOperationException($"PRAGMA foreign_key_check acusou {fkIssues} violações pós-migração.");
                    logs.Add("PRAGMA foreign_key_check pós-migração: ok (0 violações)");
                }

                // Versão gravada em CreditSchemaVersions
                int finalVersion = 0;
                using (var cmd = postConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM CreditSchemaVersions;";
                    finalVersion = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    logs.Add($"Versão de esquema em CreditSchemaVersions: {finalVersion} (Esperado >= 2)");
                    if (finalVersion < 2)
                        throw new InvalidOperationException($"Versão em CreditSchemaVersions inválida pós-migração: {finalVersion}");
                }

                // Preservação de contagens legadas
                foreach (var kvp in preCounts)
                {
                    using var cmd = postConn.CreateCommand();
                    cmd.CommandText = $"SELECT COUNT(*) FROM [{kvp.Key}];";
                    int postCnt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    if (postCnt != kvp.Value)
                        throw new InvalidOperationException($"VIOLAÇÃO DE DADOS LEGADOS: Tabela {kvp.Key} alterada de {kvp.Value} para {postCnt}.");
                    logs.Add($"Preservação da tabela [{kvp.Key}]: {kvp.Value} -> {postCnt} (INTACTA)");
                }

                // Confirmação das 15 tabelas de crédito
                string[] expectedCreditTables = new[]
                {
                    "CreditContracts", "CreditDisbursements", "CreditInstallments", "CreditCycles",
                    "CreditPayments", "CreditPaymentInstallmentAllocs", "CreditSurplusBalances",
                    "CreditSurplusConsumptions", "CreditSurplusConsumptionReversals",
                    "CreditPaymentRefunds", "CreditDevolutions", "CreditCashOutflows",
                    "CreditIdempotencyRecords", "CreditPaymentIntents", "CreditTransactionAudits"
                };

                foreach (var ct in expectedCreditTables)
                {
                    using var cmd = postConn.CreateCommand();
                    cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{ct}';";
                    int cnt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    if (cnt == 0)
                        throw new InvalidOperationException($"Tabela de crédito obrigatória não encontrada pós-migração: {ct}");
                }
                logs.Add($"Confirmação de presença das 15 tabelas de crédito: 100% CRIADAS");
            }

            // 6. Teste de Idempotência
            logs.Add("------------------------------------------------------------------");
            logs.Add("5. TESTE DE IDEMPOTÊNCIA DA MIGRAÇÃO");
            logs.Add("------------------------------------------------------------------");

            using (var idempCtx = new CobraPixDbContext(options))
            {
                // Re-executa migração na mesma base
                await CreditDatabaseMigrator.ApplyCreditMigrationsAsync(idempCtx);
                logs.Add("Segunda execução de CreditDatabaseMigrator.ApplyCreditMigrationsAsync concluída sem erros.");
            }

            using (var idempConn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await idempConn.OpenAsync();
                using var cmd = idempConn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM CreditSchemaVersions;";
                int versionRows = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                logs.Add($"Total de registros em CreditSchemaVersions após re-execução: {versionRows} (Não duplicado)");
                if (versionRows > 2)
                    throw new InvalidOperationException("Falha de idempotência: versões foram duplicadas.");
            }

            long targetSizeAfter = new FileInfo(dbPath).Length;
            string targetShaAfter = ComputeFileSha256(dbPath);
            logs.Add("------------------------------------------------------------------");
            logs.Add("RESULTADO FINAL DA MIGRAÇÃO");
            logs.Add("------------------------------------------------------------------");
            logs.Add($"Tamanho final do banco: {targetSizeAfter} bytes (Inicial: {targetSizeBefore} bytes)");
            logs.Add($"SHA-256 final: {targetShaAfter}");
            logs.Add("MIGRAÇÃO DE CRÉDITO & GIRO CONCLUÍDA COM ÊXITO.");
            logs.Add("==================================================================");

            return logs;
        }

        private static string ComputeFileSha256(string filePath)
        {
            SqliteConnection.ClearAllPools();
            using var sha = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        }
    }
}
