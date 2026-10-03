using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Implementação segura para leitura, validação e extração de backups no formato Android CPBv1.
    /// Estrutura do container binário:
    /// - 5 bytes: Magic ("CPBv1" ASCII)
    /// - 4 bytes: Version (Int32 little-endian)
    /// - 8 bytes: PayloadLength (Int64 little-endian)
    /// - Payload: Banco SQLite bruto
    /// </summary>
    public class AndroidBackupReader : IAndroidBackupReader
    {
        public const int HeaderSize = 17;
        public const string MagicHeader = "CPBv1";

        /// <summary>
        /// Verifica de forma rápida e segura se o arquivo possui a assinatura do container Android ("CPBv1").
        /// </summary>
        public bool IsAndroidBackup(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Length < 5)
                    return false;

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] magicBytes = new byte[5];
                fs.ReadExactly(magicBytes, 0, 5);
                string magic = Encoding.ASCII.GetString(magicBytes);
                return string.Equals(magic, MagicHeader, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Valida integralmente o cabeçalho de 17 bytes (Magic, Version, PayloadLength) e a integridade de tamanho do arquivo.
        /// </summary>
        public AndroidBackupValidationResult ValidateHeader(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new AndroidBackupValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Arquivo de backup não encontrado."
                };
            }

            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Length < HeaderSize)
                {
                    return new AndroidBackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Backup Android inválido ou incompleto: tamanho do arquivo ({fi.Length} bytes) é menor que o cabeçalho obrigatório ({HeaderSize} bytes)."
                    };
                }

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] headerBytes = new byte[HeaderSize];
                fs.ReadExactly(headerBytes, 0, HeaderSize);

                // 1. Magic (5 bytes)
                string magic = Encoding.ASCII.GetString(headerBytes, 0, 5);
                if (!string.Equals(magic, MagicHeader, StringComparison.Ordinal))
                {
                    return new AndroidBackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Backup Android inválido: identificador mágico 'CPBv1' não encontrado (encontrado: '{magic}')."
                    };
                }

                // 2. Version (4 bytes Int32)
                int version = BitConverter.ToInt32(headerBytes, 5);
                if (version <= 0)
                {
                    return new AndroidBackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Backup Android inválido: versão inválida ({version}). A versão deve ser maior que zero."
                    };
                }

                // 3. PayloadLength (8 bytes Int64)
                long payloadLength = BitConverter.ToInt64(headerBytes, 9);
                if (payloadLength < 0)
                {
                    return new AndroidBackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Backup Android inválido: tamanho do payload negativo ({payloadLength})."
                    };
                }

                long remainingBytes = fi.Length - HeaderSize;
                if (payloadLength > remainingBytes)
                {
                    return new AndroidBackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Backup Android inválido ou incompleto: o arquivo está truncado. Esperado {payloadLength} bytes de payload, mas restam apenas {remainingBytes} bytes no arquivo."
                    };
                }

                if (remainingBytes != payloadLength)
                {
                    return new AndroidBackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Backup Android inválido: tamanho do arquivo inconsistente com o payload declarado (restante: {remainingBytes} bytes, declarado: {payloadLength} bytes)."
                    };
                }

                return new AndroidBackupValidationResult
                {
                    IsValid = true,
                    Version = version,
                    PayloadLength = payloadLength
                };
            }
            catch (Exception ex)
            {
                return new AndroidBackupValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Falha ao ler cabeçalho do backup Android: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Extrai o payload SQLite bruto para um arquivo temporário seguro (fora do banco de produção)
        /// e valida se a integridade do banco SQLite é válida.
        /// </summary>
        public async Task<AndroidExtractionResult> ExtractSqlitePayloadAsync(string filePath, string? targetTempPath = null)
        {
            var headerVal = ValidateHeader(filePath);
            if (!headerVal.IsValid)
            {
                return new AndroidExtractionResult
                {
                    Success = false,
                    ErrorMessage = headerVal.ErrorMessage
                };
            }

            string tempDbPath = targetTempPath ?? Path.Combine(Path.GetTempPath(), $"cobrapix_android_{Guid.NewGuid():N}.db");
            string? dir = Path.GetDirectoryName(tempDbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            try
            {
                // Extração segura do payload para arquivo temporário
                using (var inFs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var outFs = new FileStream(tempDbPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    inFs.Seek(HeaderSize, SeekOrigin.Begin);

                    byte[] buffer = new byte[81920];
                    long bytesRemaining = headerVal.PayloadLength;

                    while (bytesRemaining > 0)
                    {
                        int toRead = (int)Math.Min(buffer.Length, bytesRemaining);
                        int read = await inFs.ReadAsync(buffer.AsMemory(0, toRead));
                        if (read == 0)
                        {
                            throw new EndOfStreamException("Fim prematuro do arquivo durante a extração do payload SQLite.");
                        }

                        await outFs.WriteAsync(buffer.AsMemory(0, read));
                        bytesRemaining -= read;
                    }

                    await outFs.FlushAsync();
                }

                // Validação do banco SQLite extraído
                if (!ValidateSqliteDatabase(tempDbPath, out string? sqliteError))
                {
                    try { File.Delete(tempDbPath); } catch { }
                    return new AndroidExtractionResult
                    {
                        Success = false,
                        ErrorMessage = $"Backup Android inválido: o payload extraído não é um banco SQLite válido. Detalhe: {sqliteError}"
                    };
                }

                // Contagem de tabelas de usuário
                int tableCount = 0;
                using (var conn = new SqliteConnection($"Data Source={tempDbPath};Mode=ReadOnly;Pooling=False"))
                {
                    await conn.OpenAsync();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
                    var val = await cmd.ExecuteScalarAsync();
                    tableCount = Convert.ToInt32(val);
                }

                return new AndroidExtractionResult
                {
                    Success = true,
                    ExtractedDbPath = tempDbPath,
                    Version = headerVal.Version,
                    PayloadLength = headerVal.PayloadLength,
                    TableCount = tableCount
                };
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tempDbPath)) File.Delete(tempDbPath); } catch { }
                return new AndroidExtractionResult
                {
                    Success = false,
                    ErrorMessage = $"Falha durante a extração do payload Android: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Valida se o arquivo apontado é um banco SQLite íntegro e passível de consulta via SELECT em sqlite_master.
        /// </summary>
        public bool ValidateSqliteDatabase(string sqliteDbPath, out string? errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(sqliteDbPath) || !File.Exists(sqliteDbPath))
            {
                errorMessage = "Arquivo do banco SQLite não encontrado.";
                return false;
            }

            try
            {
                using var conn = new SqliteConnection($"Data Source={sqliteDbPath};Mode=ReadOnly;Pooling=False");
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
                cmd.ExecuteScalar();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Inspeciona o schema do banco SQLite Android extraído (tabelas, colunas, tipos, PKs e FKs).
        /// Não altera os dados.
        /// </summary>
        public AndroidSchemaInfo InspectSchema(string sqliteDbPath)
        {
            var schema = new AndroidSchemaInfo();
            if (string.IsNullOrWhiteSpace(sqliteDbPath) || !File.Exists(sqliteDbPath))
                return schema;

            using var conn = new SqliteConnection($"Data Source={sqliteDbPath};Mode=ReadOnly;Pooling=False");
            conn.Open();

            // 1. Lista tabelas de usuário
            var tableNames = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'android_metadata' ORDER BY name;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    tableNames.Add(reader.GetString(0));
                }
            }

            // 2. Para cada tabela, consulta colunas e foreign keys
            foreach (var tableName in tableNames)
            {
                var tableInfo = new AndroidTableInfo { TableName = tableName };

                // Colunas via PRAGMA table_info
                using (var cmdCols = conn.CreateCommand())
                {
                    cmdCols.CommandText = $"PRAGMA table_info(\"{tableName}\");";
                    using var colReader = cmdCols.ExecuteReader();
                    while (colReader.Read())
                    {
                        tableInfo.Columns.Add(new AndroidColumnInfo
                        {
                            Cid = colReader.GetInt32(0),
                            Name = colReader.GetString(1),
                            Type = colReader.IsDBNull(2) ? string.Empty : colReader.GetString(2),
                            NotNull = colReader.GetInt32(3) == 1,
                            DefaultValue = colReader.IsDBNull(4) ? null : colReader.GetString(4),
                            IsPrimaryKey = colReader.GetInt32(5) > 0
                        });
                    }
                }

                // Foreign Keys via PRAGMA foreign_key_list
                using (var cmdFk = conn.CreateCommand())
                {
                    cmdFk.CommandText = $"PRAGMA foreign_key_list(\"{tableName}\");";
                    using var fkReader = cmdFk.ExecuteReader();
                    while (fkReader.Read())
                    {
                        tableInfo.ForeignKeys.Add(new AndroidForeignKeyInfo
                        {
                            Id = fkReader.GetInt32(0),
                            Seq = fkReader.GetInt32(1),
                            Table = fkReader.GetString(2),
                            From = fkReader.GetString(3),
                            To = fkReader.GetString(4),
                            OnUpdate = fkReader.IsDBNull(5) ? string.Empty : fkReader.GetString(5),
                            OnDelete = fkReader.IsDBNull(6) ? string.Empty : fkReader.GetString(6)
                        });
                    }
                }

                schema.Tables.Add(tableInfo);
            }

            return schema;
        }

        /// <summary>
        /// Método utilitário para gerar arquivos de container no formato CPBv1 para testes controlados.
        /// </summary>
        public static void CreateContainerFile(string destinationPath, int version, byte[] sqliteBytes)
        {
            string? dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            byte[] magic = Encoding.ASCII.GetBytes(MagicHeader);
            fs.Write(magic, 0, 5);

            byte[] verBytes = BitConverter.GetBytes(version);
            fs.Write(verBytes, 0, 4);

            byte[] lenBytes = BitConverter.GetBytes((long)sqliteBytes.Length);
            fs.Write(lenBytes, 0, 8);

            fs.Write(sqliteBytes, 0, sqliteBytes.Length);
            fs.Flush();
        }
    }
}
