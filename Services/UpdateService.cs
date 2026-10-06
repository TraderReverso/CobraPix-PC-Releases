using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Implementação oficial do serviço de atualizações do CobraPix PC.
    /// Operação segura, orientada a SemVer (MAJOR.MINOR.PATCH), com HTTPS estrito,
    /// tratamento de ausência de internet e compatibilidade 100% offline-first.
    /// </summary>
    public class UpdateService : IUpdateService
    {
        public const string KeyUpdateServerUrl = "Update.ManifestUrl";
        public const string DefaultManifestUrl = IUpdateService.DefaultManifestUrl;
        private const int DefaultTimeoutSeconds = 5;

        private readonly CobraPixDbContext? _context;
        private readonly IBackupService? _backupService;
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds)
        };

        private static string? _testManifestUrlOverride;
        private static bool _isTestEnvironmentActive;

        /// <summary>
        /// Indica se o ambiente controlado de testes de atualização está explicitamente ativo.
        /// </summary>
        public static bool IsTestEnvironmentActive => _isTestEnvironmentActive && !string.IsNullOrWhiteSpace(_testManifestUrlOverride);

        /// <summary>
        /// Retorna a URL de manifesto de teste ativa em memória, ou null quando em produção oficial.
        /// </summary>
        public static string? ActiveTestManifestUrl => IsTestEnvironmentActive ? _testManifestUrlOverride : null;

        /// <summary>
        /// Ativa explicitamente o ambiente de teste apontando para um manifesto alternativo em memória.
        /// NÃO grava no banco de dados e NÃO altera permanentemente as configurações do aplicativo.
        /// </summary>
        public static void SetTestManifestUrl(string? testUrl)
        {
            if (string.IsNullOrWhiteSpace(testUrl))
            {
                ClearTestManifestUrl();
                return;
            }

            _testManifestUrlOverride = testUrl.Trim();
            _isTestEnvironmentActive = true;
            Console.WriteLine($"[AVISO DE TESTE] ATENÇÃO: Ambiente controlado de atualização ATIVADO.");
            Console.WriteLine($"[AVISO DE TESTE] Manifesto de teste: {_testManifestUrlOverride}");
        }

        /// <summary>
        /// Desativa o ambiente de teste em memória, restaurando imediatamente o endpoint oficial padrão.
        /// </summary>
        public static void ClearTestManifestUrl()
        {
            _testManifestUrlOverride = null;
            _isTestEnvironmentActive = false;
        }

        private string? _currentVersionOverride;
        public string CurrentVersion
        {
            get => _currentVersionOverride ?? AppVersionInfo.CurrentVersion;
            set => _currentVersionOverride = value;
        }

        public UpdateService(CobraPixDbContext? context = null, IBackupService? backupService = null)
        {
            _context = context;
            _backupService = backupService;
        }

        public async Task<string?> GetUpdateServerUrlAsync()
        {
            // Se o ambiente de teste controlado foi explicitamente ativado em memória, utiliza a URL de teste
            if (IsTestEnvironmentActive)
            {
                return ActiveTestManifestUrl;
            }

            if (_context == null) return null;

            try
            {
                var setting = await _context.AppSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == KeyUpdateServerUrl);

                if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
                {
                    return setting.Value.Trim();
                }
            }
            catch
            {
                // Fallback gracioso em caso de indisponibilidade momentânea do banco
            }

            return null;
        }

        public async Task SetUpdateServerUrlAsync(string? url)
        {
            if (_context == null) return;

            string clean = url?.Trim() ?? string.Empty;
            var existing = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == KeyUpdateServerUrl);
            if (existing != null)
            {
                existing.Value = clean;
            }
            else
            {
                _context.AppSettings.Add(new AppSetting
                {
                    Key = KeyUpdateServerUrl,
                    Value = clean
                });
            }

            await _context.SaveChangesAsync();
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(string? overrideUrl = null, HttpClient? httpClient = null)
        {
            string? url = overrideUrl ?? await GetUpdateServerUrlAsync();

            // 1. Tratamento amigável de servidor não configurado
            if (string.IsNullOrWhiteSpace(url))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.NotConfigured,
                    CurrentVersion = CurrentVersion,
                    Message = "Atualização automática ainda não está disponível. O sistema de atualização está preparado para as próximas versões."
                };
            }

            // 2. Validação estrita de segurança: HTTPS obrigatório (bloqueia HTTP inseguro)
            // No ambiente de teste controlado, URLs locais (file:// ou arquivos existentes em %TEMP%) são suportadas offline.
            bool isLocalTestUrl = IsTestEnvironmentActive &&
                (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                 (Path.IsPathRooted(url) && File.Exists(url)));

            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !isLocalTestUrl)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.Error,
                    CurrentVersion = CurrentVersion,
                    Message = "Por motivos de segurança, a verificação de atualizações exige conexão HTTPS criptografada."
                };
            }

            string jsonContent;
            if (isLocalTestUrl)
            {
                string localPath = url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(url).LocalPath
                    : url;

                if (!File.Exists(localPath))
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateStatus.NetworkError,
                        CurrentVersion = CurrentVersion,
                        Message = $"Arquivo de manifesto de teste não encontrado: {localPath}"
                    };
                }

                jsonContent = await File.ReadAllTextAsync(localPath);
            }
            else
            {
                // 3. Consulta HTTP remota com tratamento robusto para modo offline / falhas de rede
                var client = httpClient ?? SharedHttpClient;
                try
                {
                    using var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        return new UpdateCheckResult
                        {
                            Status = UpdateStatus.NetworkError,
                            CurrentVersion = CurrentVersion,
                            Message = "Não foi possível verificar atualizações agora. O CobraPix continuará funcionando normalmente."
                        };
                    }

                    jsonContent = await response.Content.ReadAsStringAsync();
                }
                catch (TaskCanceledException)
                {
                    // Timeout controlado
                    return new UpdateCheckResult
                    {
                        Status = UpdateStatus.NetworkError,
                        CurrentVersion = CurrentVersion,
                        Message = "Não foi possível verificar atualizações agora. O CobraPix continuará funcionando normalmente."
                    };
                }
                catch (HttpRequestException)
                {
                    // Sem conexão com a internet ou servidor inacessível
                    return new UpdateCheckResult
                    {
                        Status = UpdateStatus.NetworkError,
                        CurrentVersion = CurrentVersion,
                        Message = "Não foi possível verificar atualizações agora. O CobraPix continuará funcionando normalmente."
                    };
                }
                catch (Exception)
                {
                    // Falha genérica de rede
                    return new UpdateCheckResult
                    {
                        Status = UpdateStatus.NetworkError,
                        CurrentVersion = CurrentVersion,
                        Message = "Não foi possível verificar atualizações agora. O CobraPix continuará funcionando normalmente."
                    };
                }
            }

            // 4. Avaliação e comparação SemVer do manifesto
            var result = EvaluateManifestJson(jsonContent, CurrentVersion);
            if (IsTestEnvironmentActive && result.Status == UpdateStatus.UpdateAvailable)
            {
                result.Message = $"[AMBIENTE DE TESTE] {result.Message}";
            }
            return result;
        }

        public UpdateCheckResult EvaluateManifestJson(string manifestJson, string localVersion = AppVersionInfo.CurrentVersion)
        {
            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.InvalidManifest,
                    CurrentVersion = localVersion,
                    Message = "Manifesto remoto vazio ou inexistente."
                };
            }

            try
            {
                using var doc = JsonDocument.Parse(manifestJson);
                var root = doc.RootElement;

                // Valida campo obrigatório 'version'
                if (!root.TryGetProperty("version", out var versionProp) ||
                    versionProp.ValueKind != JsonValueKind.String)
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateStatus.InvalidManifest,
                        CurrentVersion = localVersion,
                        Message = "Manifesto remoto incompleto: o campo 'version' é obrigatório."
                    };
                }

                string remoteVersion = versionProp.GetString()?.Trim() ?? string.Empty;
                if (!AppVersionInfo.IsValidSemVer(remoteVersion))
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateStatus.InvalidManifest,
                        CurrentVersion = localVersion,
                        RemoteVersion = remoteVersion,
                        Message = $"Versão remota inválida ('{remoteVersion}'). O formato deve seguir estritamente SemVer (MAJOR.MINOR.PATCH)."
                    };
                }

                // downloadUrl obrigatório
                string? downloadUrl = null;
                if (!root.TryGetProperty("downloadUrl", out var dlProp) ||
                    dlProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(dlProp.GetString()))
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateStatus.InvalidManifest,
                        CurrentVersion = localVersion,
                        RemoteVersion = remoteVersion,
                        Message = "Manifesto remoto incompleto: o campo 'downloadUrl' é obrigatório."
                    };
                }

                downloadUrl = dlProp.GetString()!.Trim();
                bool isLocalTestDl = IsTestEnvironmentActive &&
                    (downloadUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                     (Path.IsPathRooted(downloadUrl) && File.Exists(downloadUrl)));

                if (!downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !isLocalTestDl)
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateStatus.InvalidManifest,
                        CurrentVersion = localVersion,
                        RemoteVersion = remoteVersion,
                        Message = "A URL de download do manifesto deve utilizar conexão segura HTTPS."
                    };
                }

                // releaseNotes
                var releaseNotes = new List<string>();
                if (root.TryGetProperty("releaseNotes", out var notesProp) &&
                    notesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in notesProp.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            string? note = item.GetString();
                            if (!string.IsNullOrWhiteSpace(note))
                            {
                                releaseNotes.Add(note.Trim());
                            }
                        }
                    }
                }

                // mandatory
                bool mandatory = false;
                if (root.TryGetProperty("mandatory", out var mandProp))
                {
                    if (mandProp.ValueKind == JsonValueKind.True || mandProp.ValueKind == JsonValueKind.False)
                    {
                        mandatory = mandProp.GetBoolean();
                    }
                }

                // sha256 (validação criptográfica do hash hexadecimal de 64 caracteres quando informado)
                string? sha256 = null;
                if (root.TryGetProperty("sha256", out var shaProp) &&
                    shaProp.ValueKind == JsonValueKind.String)
                {
                    string rawSha = shaProp.GetString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(rawSha))
                    {
                        if (!IsValidSha256(rawSha))
                        {
                            return new UpdateCheckResult
                            {
                                Status = UpdateStatus.InvalidManifest,
                                CurrentVersion = localVersion,
                                RemoteVersion = remoteVersion,
                                Message = "O checksum SHA-256 do manifesto remoto é inválido. Esperado formato hexadecimal de 64 caracteres."
                            };
                        }
                        sha256 = rawSha.ToLowerInvariant();
                    }
                }

                // packageSizeBytes / size (quando disponível)
                long? packageSizeBytes = null;
                if (root.TryGetProperty("packageSizeBytes", out var sizeProp) && sizeProp.TryGetInt64(out long sVal))
                {
                    packageSizeBytes = sVal;
                }
                else if (root.TryGetProperty("size", out var sizeProp2) && sizeProp2.TryGetInt64(out long sVal2))
                {
                    packageSizeBytes = sVal2;
                }

                // Comparação SemVer canônica
                int cmp = AppVersionInfo.CompareVersions(remoteVersion, localVersion);
                if (cmp > 0)
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateStatus.UpdateAvailable,
                        CurrentVersion = localVersion,
                        RemoteVersion = remoteVersion,
                        DownloadUrl = downloadUrl,
                        Sha256 = sha256,
                        PackageSizeBytes = packageSizeBytes,
                        ReleaseNotes = releaseNotes,
                        Mandatory = mandatory,
                        Message = "Nova versão disponível"
                    };
                }

                return new UpdateCheckResult
                {
                    Status = UpdateStatus.UpToDate,
                    CurrentVersion = localVersion,
                    RemoteVersion = remoteVersion,
                    DownloadUrl = downloadUrl,
                    Sha256 = sha256,
                    PackageSizeBytes = packageSizeBytes,
                    ReleaseNotes = releaseNotes,
                    Mandatory = mandatory,
                    Message = "Você já está usando a versão mais recente do CobraPix."
                };
            }
            catch (JsonException ex)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.InvalidManifest,
                    CurrentVersion = localVersion,
                    Message = $"Manifesto remoto inválido: conteúdo não é um JSON válido ({ex.Message})."
                };
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.Error,
                    CurrentVersion = localVersion,
                    Message = $"Erro ao processar manifesto de atualização: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Valida se uma string é um hash SHA-256 válido (exatamente 64 caracteres hexadecimais).
        /// </summary>
        public bool IsValidSha256(string? hash) => ValidateSha256Format(hash);

        /// <summary>
        /// Validação estática de formato SHA-256 hexadecimal.
        /// </summary>
        public static bool ValidateSha256Format(string? hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return false;
            string clean = hash.Trim();
            if (clean.Length != 64) return false;
            foreach (char c in clean)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Calcula o hash SHA-256 em formato hexadecimal minúsculo para um array de bytes.
        /// </summary>
        public string ComputeSha256(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        }

        /// <summary>
        /// Calcula o hash SHA-256 em formato hexadecimal minúsculo para uma stream de dados.
        /// </summary>
        public string ComputeSha256(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        /// <summary>
        /// Calcula o hash SHA-256 em formato hexadecimal minúsculo para um arquivo no disco.
        /// </summary>
        public string ComputeFileSha256(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Caminho do arquivo não informado.", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Arquivo não encontrado para cálculo de SHA-256.", filePath);
            using var stream = File.OpenRead(filePath);
            return ComputeSha256(stream);
        }

        /// <summary>
        /// Valida se os bytes informados correspondem ao hash SHA-256 esperado (comparação segura e case-insensitive).
        /// </summary>
        public bool VerifySha256(byte[] data, string? expectedSha256)
        {
            if (data == null || !IsValidSha256(expectedSha256)) return false;
            string computed = ComputeSha256(data);
            return string.Equals(computed, expectedSha256?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Valida se o arquivo informado no disco corresponde ao hash SHA-256 esperado (comparação segura e case-insensitive).
        /// </summary>
        public bool VerifyFileSha256(string filePath, string? expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || !IsValidSha256(expectedSha256))
                return false;

            string computed = ComputeFileSha256(filePath);
            return string.Equals(computed, expectedSha256?.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
