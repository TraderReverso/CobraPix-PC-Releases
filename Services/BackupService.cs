using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Serviço responsável pela criação e restauração segura de backups locais (.cpb) do CobraPix PC.
    /// Garante consistência de dados SQLite, verificação criptográfica SHA-256 e criação prévia de backup de segurança.
    /// </summary>
    public class BackupService : IBackupService
    {
        private readonly CobraPixDbContext? _context;
        private readonly IAndroidBackupReader _androidReader;
        private readonly IAndroidImportService _androidImportService;

        internal CobraPixDbContext? DbContext => _context;

        private const string SettingKeyLastBackupDate = "Backup.LastBackupDate";
        private const string SettingKeyLastBackupFile = "Backup.LastBackupFile";
        private const string SettingKeyLastRestoreDate = "Backup.LastRestoreDate";
        private const string SettingKeyLastRestoreFile = "Backup.LastRestoreFile";

        public const string SettingKeyAutoBackupEnabled = "Backup.AutoBackupEnabled";
        public const string SettingKeyLastAutoBackupDate = "Backup.LastAutoBackupDate";
        public const string SettingKeyLastAutoBackupFile = "Backup.LastAutoBackupFile";
        public const string SettingKeyLastAutoBackupError = "Backup.LastAutoBackupError";

        public const string BackupDirectoryEnvVar = "COBRAPIX_BACKUP_DIRECTORY";
        public const string SandboxRootEnvVar = "COBRAPIX_SANDBOX_ROOT";

        private static readonly System.Threading.SemaphoreSlim _autoBackupLock = new(1, 1);

        public BackupService(
            CobraPixDbContext? context = null,
            IAndroidBackupReader? androidReader = null,
            IAndroidImportService? androidImportService = null)
        {
            _context = context;
            _androidReader = androidReader ?? new AndroidBackupReader();
            _androidImportService = androidImportService ?? new AndroidImportService(context);
        }

        public string GetCurrentDatabasePath()
        {
            return DatabasePath.GetDatabaseFilePath();
        }

        public async Task<string?> GetLastBackupInfoAsync()
        {
            if (_context == null) return null;

            try
            {
                var setting = await _context.AppSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == SettingKeyLastBackupDate);

                return setting?.Value;
            }
            catch
            {
                return null;
            }
        }

        public async Task<BackupResult> CreateBackupAsync(string destinationFilePath, string? sourceDatabasePath = null)
        {
            if (string.IsNullOrWhiteSpace(destinationFilePath))
            {
                return new BackupResult { Success = false, Message = "O caminho de destino do arquivo não foi especificado." };
            }

            if (DatabasePath.IsTestExecutionMode())
            {
                DatabasePath.AssertSafeTestFilePath(destinationFilePath, "Criação de arquivo de backup");
                string? destDir = Path.GetDirectoryName(Path.GetFullPath(destinationFilePath.Trim()));
                if (!string.IsNullOrWhiteSpace(destDir))
                {
                    AssertSafeBackupDirectory(destDir);
                }
            }

            string sourceDb = DatabasePath.ValidateAndResolveTargetDatabasePath(sourceDatabasePath, "Leitura de banco de origem para backup");
            if (!File.Exists(sourceDb))
            {
                return new BackupResult { Success = false, Message = $"Banco de dados de origem não foi encontrado: {sourceDb}" };
            }

            // Garante extensão .cpb
            string targetPath = destinationFilePath;
            if (!targetPath.EndsWith(".cpb", StringComparison.OrdinalIgnoreCase))
            {
                targetPath += ".cpb";
            }

            // Garante diretório de destino
            string? targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string tempSnapshot = Path.Combine(Path.GetTempPath(), $"cpb_snap_{Guid.NewGuid():N}.db");

            try
            {
                SqliteConnection.ClearAllPools();

                // Cria um snapshot consistente do SQLite utilizando a API oficial de backup
                try
                {
                    using (var srcConn = new SqliteConnection($"Data Source={sourceDb};Mode=ReadOnly;Pooling=False"))
                    using (var dstConn = new SqliteConnection($"Data Source={tempSnapshot};Pooling=False"))
                    {
                        srcConn.Open();
                        dstConn.Open();
                        srcConn.BackupDatabase(dstConn);
                    }
                }
                catch
                {
                    // Fallback caso a API nativa falhe
                    File.Copy(sourceDb, tempSnapshot, true);
                }
                SqliteConnection.ClearAllPools();

                // Calcula hash SHA-256 e tamanho do banco capturado
                byte[] hashBytes;
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(tempSnapshot))
                {
                    hashBytes = sha.ComputeHash(fs);
                }
                string checksum = Convert.ToHexString(hashBytes).ToLowerInvariant();
                long dbSize = new FileInfo(tempSnapshot).Length;

                // Cria metadados estruturados
                var metadata = new BackupMetadata
                {
                    Application = "CobraPix PC",
                    Version = "1.0",
                    CreatedAt = DateTime.UtcNow,
                    DatabaseSize = dbSize,
                    Sha256Checksum = checksum,
                    Notes = "Backup oficial CobraPix PC"
                };
                string metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });

                // Empacota em arquivo .cpb (formato compactado ZIP)
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                using (var zipStream = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    // 1. Banco de Dados SQLite
                    var dbEntry = archive.CreateEntry("cobrapix.db", CompressionLevel.Optimal);
                    using (var entryStream = dbEntry.Open())
                    using (var snapStream = File.OpenRead(tempSnapshot))
                    {
                        snapStream.CopyTo(entryStream);
                    }

                    // 2. Metadados e Checksum
                    var metaEntry = archive.CreateEntry("metadata.json", CompressionLevel.Optimal);
                    using (var entryStream = metaEntry.Open())
                    using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
                    {
                        writer.Write(metadataJson);
                    }
                }

                // Registra data do último backup se o contexto estiver disponível
                await SaveLastBackupInfoAsync(targetPath);

                long finalZipSize = new FileInfo(targetPath).Length;
                return new BackupResult
                {
                    Success = true,
                    FilePath = targetPath,
                    FileSize = finalZipSize,
                    CreatedAt = metadata.CreatedAt,
                    Message = $"Backup criado com sucesso em: {targetPath}"
                };
            }
            catch (Exception ex)
            {
                return new BackupResult
                {
                    Success = false,
                    Message = $"Falha ao gerar o arquivo de backup: {ex.Message}"
                };
            }
            finally
            {
                // Limpeza do arquivo de snapshot temporário
                if (File.Exists(tempSnapshot))
                {
                    try { File.Delete(tempSnapshot); } catch { }
                }
            }
        }

        public BackupValidationResult ValidateBackupFile(string backupFilePath)
        {
            if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
            {
                return new BackupValidationResult { IsValid = false, ErrorMessage = "Arquivo de backup não encontrado." };
            }

            if (!backupFilePath.EndsWith(".cpb", StringComparison.OrdinalIgnoreCase))
            {
                return new BackupValidationResult { IsValid = false, ErrorMessage = "Extensão inválida. O arquivo de backup deve possuir a extensão .cpb." };
            }

            // 1. Verifica se é um backup Android no formato de container binário (CPBv1)
            if (_androidReader.IsAndroidBackup(backupFilePath))
            {
                var androidVal = _androidReader.ValidateHeader(backupFilePath);
                if (!androidVal.IsValid)
                {
                    return new BackupValidationResult
                    {
                        IsValid = false,
                        IsAndroid = true,
                        ErrorMessage = androidVal.ErrorMessage ?? "Backup Android inválido."
                    };
                }

                return new BackupValidationResult
                {
                    IsValid = true,
                    IsAndroid = true,
                    AndroidVersion = androidVal.Version,
                    Metadata = new BackupMetadata
                    {
                        Application = "CobraPix Android",
                        Version = $"v{androidVal.Version}",
                        CreatedAt = DateTime.UtcNow,
                        DatabaseSize = androidVal.PayloadLength,
                        Notes = "Backup oficial CobraPix Android (Container CPBv1)"
                    }
                };
            }

            // 2. Verifica se possui a assinatura do formato PC (ZIP - PK\x03\x04)
            bool isZip = false;
            try
            {
                using var fs = new FileStream(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length >= 4)
                {
                    byte[] sig = new byte[4];
                    fs.ReadExactly(sig, 0, 4);
                    isZip = sig[0] == 0x50 && sig[1] == 0x4B && sig[2] == 0x03 && sig[3] == 0x04;
                }
            }
            catch { }

            if (isZip)
            {
                // 2. Validação do arquivo ZIP do CobraPix PC
                try
                {
                    using var archive = ZipFile.OpenRead(backupFilePath);

                    var metaEntry = archive.GetEntry("metadata.json");
                    var dbEntry = archive.GetEntry("cobrapix.db");

                    if (metaEntry == null || dbEntry == null)
                    {
                        return new BackupValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = "O arquivo informado não possui a estrutura interna esperada do CobraPix PC (metadados ou banco ausentes)."
                        };
                    }

                    // Lê e deserializa metadados
                    BackupMetadata? metadata;
                    using (var metaStream = metaEntry.Open())
                    using (var reader = new StreamReader(metaStream, Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        metadata = JsonSerializer.Deserialize<BackupMetadata>(json);
                    }

                    if (metadata == null || metadata.Application != "CobraPix PC")
                    {
                        return new BackupValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = "O arquivo não foi reconhecido como um backup válido do CobraPix PC."
                        };
                    }

                    // Valida hash SHA-256 do cobrapix.db extraído
                    byte[] hash;
                    using (var sha = SHA256.Create())
                    using (var dbStream = dbEntry.Open())
                    {
                        hash = sha.ComputeHash(dbStream);
                    }

                    string computedChecksum = Convert.ToHexString(hash).ToLowerInvariant();
                    if (!string.Equals(computedChecksum, metadata.Sha256Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        return new BackupValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = "Falha de integridade: a verificação de hash SHA-256 do arquivo de banco de dados divergiu dos metadados."
                        };
                    }

                    return new BackupValidationResult
                    {
                        IsValid = true,
                        Metadata = metadata
                    };
                }
                catch (Exception ex)
                {
                    return new BackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Arquivo de backup inválido ou corrompido: {ex.Message}"
                    };
                }
            }

            // 3. Verifica se é um backup Android no formato JSON real (.cpb JSON)
            if (_androidImportService.IsAndroidJsonBackup(backupFilePath))
            {
                var summary = _androidImportService.GetSummary(backupFilePath);
                if (summary == null)
                {
                    return new BackupValidationResult
                    {
                        IsValid = false,
                        IsAndroid = true,
                        IsAndroidJson = true,
                        ErrorMessage = "Backup Android JSON inválido ou corrompido (campo 'app' ou 'backupVersion' inválidos)."
                    };
                }

                return new BackupValidationResult
                {
                    IsValid = true,
                    IsAndroid = true,
                    IsAndroidJson = true,
                    AndroidVersion = summary.Version,
                    AndroidSummary = summary,
                    Metadata = new BackupMetadata
                    {
                        Application = "CobraPix Android",
                        Version = $"v{summary.Version}",
                        CreatedAt = DateTime.UtcNow,
                        Notes = $"Backup oficial CobraPix Android JSON ({summary.ClientsCount} clientes, {summary.SalesCount} vendas, {summary.InstallmentsCount} parcelas, {summary.MonthlyCustomersCount} mensalistas)."
                    }
                };
            }

            // 4. Nenhum formato reconhecido
            return new BackupValidationResult
            {
                IsValid = false,
                ErrorMessage = "Não foi possível reconhecer este backup .cpb. Verifique se o arquivo foi gerado pelo CobraPix Android ou CobraPix PC."
            };
        }

        public async Task<RestoreResult> RestoreBackupAsync(string backupFilePath, string? targetDatabasePath = null)
        {
            // 0. Validação estrita do caminho de destino ANTES de abrir, ler, criar diretório ou modificar qualquer arquivo
            string targetDb = DatabasePath.ValidateAndResolveTargetDatabasePath(targetDatabasePath, "Restauração de backup");
            if (DatabasePath.IsTestExecutionMode())
            {
                string? targetParentDir = Path.GetDirectoryName(targetDb);
                if (!string.IsNullOrWhiteSpace(targetParentDir))
                {
                    AssertSafeBackupDirectory(targetParentDir);
                }
            }

            if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
            {
                return new RestoreResult { Success = false, Message = "Arquivo de backup não encontrado para restauração." };
            }

            // Detecta formato: Android JSON real, Android CPBv1 legado ou PC ZIP
            if (_androidImportService.IsAndroidJsonBackup(backupFilePath))
            {
                var importResult = await _androidImportService.ImportAsync(backupFilePath, targetDb);
                if (importResult.Success)
                {
                    await SaveLastRestoreInfoAsync(backupFilePath);
                }

                return new RestoreResult
                {
                    Success = importResult.Success,
                    Message = importResult.Message,
                    SafetyBackupPath = importResult.SafetyBackupPath
                };
            }

            if (_androidReader.IsAndroidBackup(backupFilePath))
            {
                var extraction = await _androidReader.ExtractSqlitePayloadAsync(backupFilePath);
                if (!extraction.Success)
                {
                    return new RestoreResult
                    {
                        Success = false,
                        Message = extraction.ErrorMessage ?? "Falha na validação ou extração do backup Android."
                    };
                }

                await SaveLastRestoreInfoAsync(backupFilePath);

                return new RestoreResult
                {
                    Success = true,
                    Message = $"Backup Android (CPBv1 v{extraction.Version}) validado com sucesso! SQLite temporário extraído ({extraction.TableCount} tabelas). Pronto para migração."
                };
            }

            // Fluxo original PC -> PC (ZIP)
            string? targetDir = Path.GetDirectoryName(targetDb);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string? safetyBackupPath = null;
            if (File.Exists(targetDb))
            {
                string safeDir = targetDir ?? Path.GetTempPath();
                string baseName = Path.GetFileNameWithoutExtension(targetDb);
                safetyBackupPath = Path.Combine(safeDir, $"{baseName}.safety_backup_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
                try
                {
                    SqliteConnection.ClearAllPools();
                    File.Copy(targetDb, safetyBackupPath, true);
                }
                catch (Exception ex)
                {
                    return new RestoreResult
                    {
                        Success = false,
                        Message = $"Não foi possível criar o backup de segurança do banco atual. Restauração cancelada: {ex.Message}"
                    };
                }
            }

            var validation = ValidateBackupFile(backupFilePath);
            if (!validation.IsValid)
            {
                return new RestoreResult { Success = false, Message = validation.ErrorMessage };
            }

            // Extrai ZIP para banco temporário e valida
            string tempRestoreDb = Path.Combine(Path.GetTempPath(), $"cpb_restore_{Guid.NewGuid():N}.db");
            try
            {
                using (var archive = ZipFile.OpenRead(backupFilePath))
                {
                    var dbEntry = archive.GetEntry("cobrapix.db")!;
                    dbEntry.ExtractToFile(tempRestoreDb, true);
                }

                // Valida tabelas essenciais
                using (var testConn = new SqliteConnection($"Data Source={tempRestoreDb};Pooling=False"))
                {
                    testConn.Open();
                    using var cmd = testConn.CreateCommand();
                    cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('Clients', 'Sales', 'Installments');";
                    var tableCount = Convert.ToInt32(cmd.ExecuteScalar());
                    if (tableCount < 3)
                        throw new InvalidOperationException("O banco restaurado não possui a estrutura essencial de tabelas do CobraPix.");
                }

                // Substitui o banco atual
                SqliteConnection.ClearAllPools();
                string walFile = targetDb + "-wal";
                string shmFile = targetDb + "-shm";
                if (File.Exists(walFile)) { try { File.Delete(walFile); } catch { } }
                if (File.Exists(shmFile)) { try { File.Delete(shmFile); } catch { } }
                File.Copy(tempRestoreDb, targetDb, true);
                SqliteConnection.ClearAllPools();

                await SaveLastRestoreInfoAsync(backupFilePath);

                return new RestoreResult
                {
                    Success = true,
                    SafetyBackupPath = safetyBackupPath,
                    Metadata = validation.Metadata,
                    Message = $"Banco de dados restaurado com sucesso! Backup de segurança salvo em: {safetyBackupPath}"
                };
            }
            catch (Exception ex)
            {
                // Em caso de falha, recupera o backup de segurança
                if (safetyBackupPath != null && File.Exists(safetyBackupPath))
                {
                    try
                    {
                        SqliteConnection.ClearAllPools();
                        File.Copy(safetyBackupPath, targetDb, true);
                        SqliteConnection.ClearAllPools();
                    }
                    catch { }
                }

                return new RestoreResult
                {
                    Success = false,
                    SafetyBackupPath = safetyBackupPath,
                    Message = $"Falha ao restaurar banco de dados (ZIP). O banco anterior foi preservado: {ex.Message}"
                };
            }
            finally
            {
                if (File.Exists(tempRestoreDb))
                {
                    try { File.Delete(tempRestoreDb); } catch { }
                }
            }
        }

        private async Task SaveLastBackupInfoAsync(string filePath)
        {
            if (_context == null) return;
            try
            {
                string nowStr = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                await UpsertSettingAsync(SettingKeyLastBackupDate, nowStr);
                await UpsertSettingAsync(SettingKeyLastBackupFile, filePath);
                await _context.SaveChangesAsync();
            }
            catch { }
        }

        private async Task SaveLastRestoreInfoAsync(string filePath)
        {
            if (_context == null) return;
            try
            {
                string nowStr = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                await UpsertSettingAsync(SettingKeyLastRestoreDate, nowStr);
                await UpsertSettingAsync(SettingKeyLastRestoreFile, filePath);
                await _context.SaveChangesAsync();
            }
            catch { }
        }

        private async Task UpsertSettingAsync(string key, string value)
        {
            if (_context == null) return;
            var existing = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (existing != null)
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.Now;
            }
            else
            {
                _context.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = value,
                    UpdatedAt = DateTime.Now
                });
            }
        }

        /// <summary>
        /// Valida se o diretório de destino de backup é estritamente seguro e isolado da produção em modo de teste.
        /// Rejeita colisões textuais e físicas (NTFS junctions, symlinks, aliases de volume) contra a base e pasta de produção,
        /// e assegura confinamento dentro do sandbox autorizado ou da árvore descartável (fail-closed).
        /// </summary>
        public static void AssertSafeBackupDirectory(string targetDir)
        {
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                throw new ArgumentException("Diretório de destino não pode ser nulo ou vazio.", nameof(targetDir));
            }

            if (!DatabasePath.IsTestExecutionMode())
            {
                return;
            }

            DatabasePath.AssertSafeTestDirectoryPath(targetDir, "Operação de backup");
        }

        /// <summary>
        /// Obtém o diretório para armazenamento dos backups automáticos.
        /// Em modo de teste, exige diretório configurado via COBRAPIX_BACKUP_DIRECTORY ou derivado
        /// da base de teste descartável, bloqueando qualquer fallback silencioso ou tentativa de gravação na produção (fail-closed).
        /// Em produção normal, utiliza a pasta %LOCALAPPDATA%\CobraPix\auto_backups.
        /// </summary>
        public string GetAutoBackupDirectory()
        {
            if (DatabasePath.IsTestExecutionMode())
            {
                string? envBackupDir = Environment.GetEnvironmentVariable(BackupDirectoryEnvVar);
                string targetDir;

                if (!string.IsNullOrWhiteSpace(envBackupDir))
                {
                    targetDir = envBackupDir.Trim();
                }
                else
                {
                    // Tenta derivar a partir do caminho da base descartável configurada
                    string? envDbPath = Environment.GetEnvironmentVariable(DatabasePath.DatabasePathEnvVar);
                    if (string.IsNullOrWhiteSpace(envDbPath))
                    {
                        throw new InvalidOperationException(
                            $"[SEGURANÇA DE EXECUÇÃO] O aplicativo está em modo de teste, mas nenhum diretório de backup foi configurado em {BackupDirectoryEnvVar} nem foi possível derivá-lo de {DatabasePath.DatabasePathEnvVar}.\n" +
                            "Fallback silencioso para a pasta de produção é estritamente proibido.");
                    }

                    string validatedDbPath = DatabasePath.AssertSafeTestFilePath(envDbPath, "Derivação de diretório de backup automático");
                    string dbDirectory = Path.GetDirectoryName(validatedDbPath)
                        ?? throw new InvalidOperationException("[SEGURANÇA DE EXECUÇÃO] Não foi possível determinar o diretório da base de dados descartável.");
                    targetDir = Path.Combine(dbDirectory, "auto_backups");
                }

                // Validação estrita de contenção: bloqueia qualquer colisão textual ou física contra a produção e vazamentos de sandbox
                AssertSafeBackupDirectory(targetDir);
                string canonicalTargetDir = Path.GetFullPath(targetDir);

                if (!Directory.Exists(canonicalTargetDir))
                {
                    Directory.CreateDirectory(canonicalTargetDir);
                }

                return canonicalTargetDir;
            }

            // Modo normal de produção
            string prodFolder = Path.Combine(DatabasePath.GetDatabaseDirectory(), "auto_backups");
            if (!Directory.Exists(prodFolder))
            {
                Directory.CreateDirectory(prodFolder);
            }
            return prodFolder;
        }

        public async Task<bool> IsAutoBackupEnabledAsync()
        {
            if (_context == null) return true; // Ativado por padrão
            try
            {
                var setting = await _context.AppSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == SettingKeyAutoBackupEnabled);

                if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
                {
                    return true; // Ativado por padrão
                }

                if (bool.TryParse(setting.Value, out bool enabled))
                {
                    return enabled;
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        public async Task SetAutoBackupEnabledAsync(bool enabled)
        {
            if (_context == null) return;
            try
            {
                await UpsertSettingAsync(SettingKeyAutoBackupEnabled, enabled.ToString());
                await _context.SaveChangesAsync();
            }
            catch { }
        }

        public async Task<string?> GetLastAutoBackupInfoAsync()
        {
            if (_context == null) return null;
            try
            {
                var dateSetting = await _context.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKeyLastAutoBackupDate);
                var fileSetting = await _context.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKeyLastAutoBackupFile);
                var errSetting = await _context.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKeyLastAutoBackupError);

                if (!string.IsNullOrWhiteSpace(errSetting?.Value))
                {
                    return $"Última tentativa com erro: {errSetting.Value}";
                }

                if (dateSetting != null && !string.IsNullOrWhiteSpace(dateSetting.Value))
                {
                    string fileName = !string.IsNullOrWhiteSpace(fileSetting?.Value)
                        ? Path.GetFileName(fileSetting.Value)
                        : "CobraPix_AutoBackup";
                    return $"Concluído em {dateSetting.Value} ({fileName})";
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<BackupResult?> CheckAndPerformDailyAutoBackupAsync(string? customSourceDb = null, string? customTargetDir = null, bool force = false)
        {
            // Proteção contra concorrência: tenta obter o semáforo imediatamente sem bloquear chamadas simultâneas
            if (!await _autoBackupLock.WaitAsync(0))
            {
                // Outra rotina de backup automático já está em execução no momento
                return null;
            }

            try
            {
                // 0. Se foi informado um banco de origem explícito, valida imediatamente antes de qualquer operação
                if (!string.IsNullOrWhiteSpace(customSourceDb))
                {
                    DatabasePath.ValidateAndResolveTargetDatabasePath(customSourceDb, "Leitura de banco para backup automático");
                }

                // 1. Verifica se a funcionalidade está habilitada
                if (!force)
                {
                    bool isEnabled = await IsAutoBackupEnabledAsync();
                    if (!isEnabled)
                    {
                        return null; // Recurso desativado pelo usuário
                    }
                }

                // 2. Data de hoje (padrão local yyyy-MM-dd)
                string todayStr = DateTime.Today.ToString("yyyy-MM-dd");

                // 3. Verifica se já existe um backup válido executado na data de hoje
                if (!force && _context != null)
                {
                    string? lastDate = null;
                    string? lastFile = null;
                    try
                    {
                        var lastDateSetting = await _context.AppSettings
                            .AsNoTracking()
                            .FirstOrDefaultAsync(s => s.Key == SettingKeyLastAutoBackupDate);
                        lastDate = lastDateSetting?.Value;

                        var lastFileSetting = await _context.AppSettings
                            .AsNoTracking()
                            .FirstOrDefaultAsync(s => s.Key == SettingKeyLastAutoBackupFile);
                        lastFile = lastFileSetting?.Value;
                    }
                    catch { }

                    if (string.Equals(lastDate, todayStr, StringComparison.OrdinalIgnoreCase))
                    {
                        // Se o registro diz que já executou hoje, verifica se o arquivo físico realmente existe e é íntegro
                        if (!string.IsNullOrWhiteSpace(lastFile) && File.Exists(lastFile))
                        {
                            var validation = ValidateBackupFile(lastFile);
                            if (validation.IsValid)
                            {
                                // Já existe um backup automático 100% íntegro e válido gerado hoje!
                                return null;
                            }
                        }
                    }
                }

                // 4. Determina diretório e nome do novo arquivo de backup
                // O bloco try/catch abrange a resolução e criação de diretório para tratar diretórios inacessíveis/inválidos
                // conforme o contrato do serviço, mas propaga sem ocultar qualquer falha de segurança ([SEGURANÇA DE EXECUÇÃO]).
                string targetDir;
                try
                {
                    if (!string.IsNullOrWhiteSpace(customTargetDir))
                    {
                        AssertSafeBackupDirectory(customTargetDir);
                        targetDir = Path.GetFullPath(customTargetDir.Trim());
                    }
                    else
                    {
                        targetDir = GetAutoBackupDirectory();
                    }

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                }
                catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
                {
                    throw;
                }
                catch (Exception dirEx)
                {
                    string dirErrMsg = $"Diretório de backup inacessível: {dirEx.Message}";
                    if (_context != null)
                    {
                        try
                        {
                            await UpsertSettingAsync(SettingKeyLastAutoBackupError, $"{DateTime.Now:dd/MM/yyyy HH:mm}: {dirErrMsg}");
                            await _context.SaveChangesAsync();
                        }
                        catch { }
                    }
                    return new BackupResult
                    {
                        Success = false,
                        Message = dirErrMsg
                    };
                }

                string fileName = $"CobraPix_AutoBackup_{DateTime.Now:yyyyMMdd_HHmmss}.cpb";
                string fullTargetPath = Path.Combine(targetDir, fileName);

                // 5. Executa a criação do backup usando o método seguro CreateBackupAsync
                var result = await CreateBackupAsync(fullTargetPath, customSourceDb);
                if (!result.Success)
                {
                    // Falha na criação: registra erro e preserva o estado anterior sem marcar a data de hoje
                    if (_context != null)
                    {
                        try
                        {
                            await UpsertSettingAsync(SettingKeyLastAutoBackupError, $"{DateTime.Now:dd/MM/yyyy HH:mm}: {result.Message}");
                            await _context.SaveChangesAsync();
                        }
                        catch { }
                    }
                    return result;
                }

                // 6. Validação rigorosa de integridade, formato e SHA-256 do arquivo gerado
                var validationResult = ValidateBackupFile(fullTargetPath);
                if (!validationResult.IsValid)
                {
                    // Se corrompido, exclui arquivo com defeito e registra falha
                    try { if (File.Exists(fullTargetPath)) File.Delete(fullTargetPath); } catch { }

                    string errMsg = $"Validação de integridade do backup automático falhou: {validationResult.ErrorMessage}";
                    if (_context != null)
                    {
                        try
                        {
                            await UpsertSettingAsync(SettingKeyLastAutoBackupError, $"{DateTime.Now:dd/MM/yyyy HH:mm}: {errMsg}");
                            await _context.SaveChangesAsync();
                        }
                        catch { }
                    }

                    return new BackupResult
                    {
                        Success = false,
                        Message = errMsg
                    };
                }

                // 7. Sucesso absoluto: grava o registro oficial do backup automático diário
                if (_context != null)
                {
                    try
                    {
                        await UpsertSettingAsync(SettingKeyLastAutoBackupDate, todayStr);
                        await UpsertSettingAsync(SettingKeyLastAutoBackupFile, fullTargetPath);
                        // Limpa qualquer erro anterior
                        var errSetting = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingKeyLastAutoBackupError);
                        if (errSetting != null)
                        {
                            _context.AppSettings.Remove(errSetting);
                        }
                        await _context.SaveChangesAsync();
                    }
                    catch { }
                }

                return result;
            }
            finally
            {
                _autoBackupLock.Release();
            }
        }
    }
}
