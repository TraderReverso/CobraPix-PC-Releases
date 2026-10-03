using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato para download seguro do pacote de atualização do CobraPix PC.
    /// Opera estritamente via HTTPS, salva em diretório temporário isolado,
    /// reporta progresso em tempo real, permite cancelamento gracioso
    /// e valida rigorosamente o checksum SHA-256 antes de qualquer liberação.
    /// Não executa nenhum código baixado e não substitui arquivos da aplicação.
    /// </summary>
    public interface IUpdateDownloadService
    {
        /// <summary>
        /// Diretório temporário exclusivo do CobraPix para armazenamento de pacotes (%TEMP%\CobraPix\Updates\).
        /// </summary>
        string UpdatesTempDirectory { get; }

        /// <summary>
        /// Realiza o download seguro do pacote de atualização da nova versão,
        /// reportando progresso, tratando cancelamento e validando a integridade via SHA-256.
        /// Se o hash divergir, ou houver erro/cancelamento, o arquivo parcial é excluído.
        /// </summary>
        Task<UpdateDownloadResult> DownloadAndVerifyAsync(
            string downloadUrl,
            string expectedSha256,
            string version,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            HttpClient? httpClient = null);

        /// <summary>
        /// Remove arquivos temporários de atualizações antigas ou parciais no diretório temporário.
        /// </summary>
        void CleanUpTempUpdates();
    }
}
