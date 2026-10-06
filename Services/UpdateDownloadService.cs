using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Implementação oficial do serviço de download seguro de atualizações do CobraPix PC.
    /// Operação estritamente via HTTPS, com salvamento exclusivo em diretório temporário,
    /// suporte a cancelamento gracioso, notificação de progresso e validação de hash SHA-256.
    /// NUNCA executa nenhum binário baixado e não substitui arquivos em produção.
    /// </summary>
    public class UpdateDownloadService : IUpdateDownloadService
    {
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        private readonly IUpdateService _updateService;

        public string UpdatesTempDirectory => Path.Combine(Path.GetTempPath(), "CobraPix", "Updates");

        public UpdateDownloadService(IUpdateService? updateService = null)
        {
            _updateService = updateService ?? new UpdateService();
        }

        public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(
            string downloadUrl,
            string expectedSha256,
            string version,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            HttpClient? httpClient = null)
        {
            // 1. Validação estrita da URL (não vazia e HTTPS obrigatório)
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return UpdateDownloadResult.CreateFailure(
                    "URL de download vazia ou não informada.",
                    "A URL para download da atualização não foi informada.");
            }

            string cleanUrl = downloadUrl.Trim();
            bool isLocalTestUrl = UpdateService.IsTestEnvironmentActive &&
                (cleanUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                 (Path.IsPathRooted(cleanUrl) && File.Exists(cleanUrl)));

            if (!cleanUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !isLocalTestUrl)
            {
                return UpdateDownloadResult.CreateFailure(
                    "Conexão HTTP insegura rejeitada.",
                    "Por motivos de segurança, o download de atualizações exige conexão HTTPS criptografada.");
            }

            Uri? validatedUri = null;
            if (!isLocalTestUrl)
            {
                if (!Uri.TryCreate(cleanUrl, UriKind.Absolute, out validatedUri) ||
                    (validatedUri.Scheme != Uri.UriSchemeHttps))
                {
                    return UpdateDownloadResult.CreateFailure(
                        "URL de download em formato inválido.",
                        "A URL informada para o download não é válida.");
                }
            }

            // 2. Validação obrigatória do hash SHA-256 no manifesto
            if (string.IsNullOrWhiteSpace(expectedSha256) || !UpdateService.ValidateSha256Format(expectedSha256))
            {
                return UpdateDownloadResult.CreateFailure(
                    "Manifesto sem hash SHA-256 válido.",
                    "O manifesto de atualização não possui um hash SHA-256 válido. O download foi abortado por motivos de segurança.");
            }

            string cleanExpectedSha = expectedSha256.Trim().ToLowerInvariant();

            // 3. Validação do parâmetro de versão
            string cleanVersion = string.IsNullOrWhiteSpace(version) ? "unknown" : version.Trim();

            // 4. Preparação do diretório e nomes de arquivo temporários
            // O novo formato comercial oficial é o instalador executável (.exe)
            string extractedFileName = string.Empty;
            if (Uri.TryCreate(cleanUrl, UriKind.Absolute, out var parsedUri))
            {
                extractedFileName = Path.GetFileName(parsedUri.LocalPath);
            }
            else if (!string.IsNullOrWhiteSpace(cleanUrl))
            {
                extractedFileName = Path.GetFileName(cleanUrl);
            }

            string targetFileName;
            if (!string.IsNullOrWhiteSpace(extractedFileName) && (extractedFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || extractedFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                targetFileName = Uri.UnescapeDataString(extractedFileName);
            }
            else
            {
                targetFileName = $"CobraPix-PC-Setup-{cleanVersion}.exe";
            }

            string targetFilePath;
            string tempPartFilePath;

            try
            {
                if (!Directory.Exists(UpdatesTempDirectory))
                {
                    Directory.CreateDirectory(UpdatesTempDirectory);
                }

                targetFilePath = Path.Combine(UpdatesTempDirectory, targetFileName);
                tempPartFilePath = Path.Combine(UpdatesTempDirectory, targetFileName + ".part");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateDownloadService] Falha ao criar diretório temporário: {ex.Message}");
                return UpdateDownloadResult.CreateFailure(
                    ex.Message,
                    "Não foi possível criar a pasta temporária de atualização no disco.");
            }

            // Se o cancelamento já foi solicitado antes de iniciar
            if (cancellationToken.IsCancellationRequested)
            {
                return UpdateDownloadResult.CreateCancelled();
            }

            // Remove resquício parcial de download anterior
            TryDeleteFile(tempPartFilePath);

            // Ramo específico para ambiente de teste local (opera 100% offline)
            if (isLocalTestUrl)
            {
                string sourcePath = cleanUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(cleanUrl).LocalPath
                    : cleanUrl;

                if (!File.Exists(sourcePath))
                {
                    return UpdateDownloadResult.CreateFailure(
                        "Arquivo de pacote de teste não encontrado.",
                        $"O arquivo de teste em '{sourcePath}' não foi encontrado.");
                }

                try
                {
                    using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var fileStream = new FileStream(tempPartFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        byte[] buffer = new byte[81920];
                        long totalBytes = sourceStream.Length;
                        long totalBytesRead = 0;
                        int bytesRead;

                        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            totalBytesRead += bytesRead;
                            if (totalBytes > 0)
                            {
                                double percentage = Math.Round((double)totalBytesRead / totalBytes * 100.0, 1);
                                progress?.Report(new UpdateDownloadProgress(totalBytesRead, totalBytes, percentage));
                            }
                        }

                        // Notifica conclusão da leitura e início da validação de integridade criptográfica
                        progress?.Report(new UpdateDownloadProgress(totalBytes, totalBytes, 100.0)
                        {
                            ProgressText = "Validando integridade (SHA-256)..."
                        });
                    }

                    // Validação Criptográfica de Integridade via SHA-256
                    string testHash = _updateService.ComputeFileSha256(tempPartFilePath);
                    if (!string.Equals(testHash, cleanExpectedSha, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDeleteFile(tempPartFilePath);
                        return UpdateDownloadResult.CreateFailure(
                            $"SHA-256 divergente. Calculado={testHash}, Esperado={cleanExpectedSha}",
                            "Não foi possível validar a atualização de teste. O hash SHA-256 diverge do esperado.",
                            testHash,
                            cleanExpectedSha);
                    }

                    TryDeleteFile(targetFilePath);
                    File.Move(tempPartFilePath, targetFilePath);
                    return UpdateDownloadResult.CreateSuccess(targetFilePath, testHash, cleanExpectedSha);
                }
                catch (OperationCanceledException)
                {
                    TryDeleteFile(tempPartFilePath);
                    return UpdateDownloadResult.CreateCancelled();
                }
                catch (Exception ex)
                {
                    TryDeleteFile(tempPartFilePath);
                    return UpdateDownloadResult.CreateFailure(ex.Message, "Falha ao processar pacote de teste local.");
                }
            }

            var client = httpClient ?? SharedHttpClient;

            try
            {
                // 5. Início da requisição HTTP (lendo apenas os headers primeiro para obter o tamanho)
                using var response = await client.GetAsync(
                    validatedUri!,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return UpdateDownloadResult.CreateFailure(
                        $"Servidor retornou status HTTP {(int)response.StatusCode}",
                        $"Não foi possível baixar a atualização. O servidor retornou status HTTP {(int)response.StatusCode}.");
                }

                long? totalBytes = response.Content.Headers.ContentLength;
                long totalBytesRead = 0;

                // 6. Download streaming com cálculo de progresso
                using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var fileStream = new FileStream(tempPartFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] buffer = new byte[81920];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        totalBytesRead += bytesRead;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            double percentage = Math.Round((double)totalBytesRead / totalBytes.Value * 100.0, 1);
                            progress?.Report(new UpdateDownloadProgress(totalBytesRead, totalBytes.Value, percentage));
                        }
                        else
                        {
                            progress?.Report(new UpdateDownloadProgress(totalBytesRead, null, 0));
                        }
                    }

                    // Se não recebeu nenhum byte
                    if (totalBytesRead == 0)
                    {
                        fileStream.Dispose();
                        TryDeleteFile(tempPartFilePath);
                        return UpdateDownloadResult.CreateFailure(
                            "Arquivo recebido com tamanho zero.",
                            "O arquivo de atualização recebido está vazio. O download foi descartado.");
                    }
                }

                // Notifica conclusão da leitura e início da validação de integridade criptográfica
                progress?.Report(new UpdateDownloadProgress(totalBytesRead, totalBytes, 100.0)
                {
                    ProgressText = "Validando integridade (SHA-256)..."
                });

                // 7. Validação Criptográfica de Integridade via SHA-256
                string computedHash = _updateService.ComputeFileSha256(tempPartFilePath);

                if (!string.Equals(computedHash, cleanExpectedSha, StringComparison.OrdinalIgnoreCase))
                {
                    // Falha de integridade: descarta arquivo parcial imediatamente
                    TryDeleteFile(tempPartFilePath);
                    Debug.WriteLine($"[UpdateDownloadService] Checksum SHA-256 divergente: Calculado={computedHash}, Esperado={cleanExpectedSha}");

                    return UpdateDownloadResult.CreateFailure(
                        $"SHA-256 divergente. Calculado={computedHash}, Esperado={cleanExpectedSha}",
                        "Não foi possível validar a atualização. O hash SHA-256 do arquivo baixado diverge do oficial e o download foi descartado.",
                        computedHash,
                        cleanExpectedSha);
                }

                // 8. Integridade verificada com sucesso: promove .part para .zip final
                TryDeleteFile(targetFilePath);
                File.Move(tempPartFilePath, targetFilePath);

                return UpdateDownloadResult.CreateSuccess(targetFilePath, computedHash, cleanExpectedSha);
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tempPartFilePath);
                return UpdateDownloadResult.CreateCancelled();
            }
            catch (HttpRequestException ex)
            {
                TryDeleteFile(tempPartFilePath);
                Debug.WriteLine($"[UpdateDownloadService] Erro de rede HTTP: {ex.Message}");
                return UpdateDownloadResult.CreateFailure(
                    ex.Message,
                    "Não foi possível conectar ao servidor de atualizações. Verifique sua conexão com a internet.");
            }
            catch (IOException ex)
            {
                TryDeleteFile(tempPartFilePath);
                Debug.WriteLine($"[UpdateDownloadService] Erro de E/S de disco: {ex.Message}");
                return UpdateDownloadResult.CreateFailure(
                    ex.Message,
                    "Erro ao gravar o arquivo de atualização no disco. Verifique o espaço livre e permissões.");
            }
            catch (Exception ex)
            {
                TryDeleteFile(tempPartFilePath);
                Debug.WriteLine($"[UpdateDownloadService] Erro inesperado no download: {ex.Message}");
                return UpdateDownloadResult.CreateFailure(
                    ex.Message,
                    "Ocorreu uma falha inesperada durante o download da atualização.");
            }
        }

        public void CleanUpTempUpdates()
        {
            try
            {
                if (Directory.Exists(UpdatesTempDirectory))
                {
                    var files = Directory.GetFiles(UpdatesTempDirectory);
                    foreach (var file in files)
                    {
                        TryDeleteFile(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateDownloadService] Erro ao limpar arquivos temporários: {ex.Message}");
            }
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // Silencioso em caso de arquivo temporário bloqueado momentaneamente
            }
        }
    }
}
