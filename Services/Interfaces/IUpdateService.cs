using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato para o serviço de atualização do CobraPix PC.
    /// Responsável por gerenciar a versão atual, consultar manifestos remotos de forma segura (HTTPS),
    /// comparar versões SemVer e fornecer informações de atualização.
    /// Totalmente offline-first e seguro.
    /// </summary>
    public interface IUpdateService
    {
        /// <summary>
        /// Obtém a versão oficial atualmente instalada do aplicativo (ex: 1.0.0).
        /// </summary>
        string CurrentVersion { get; }

        /// <summary>
        /// Obtém a URL configurada do servidor/manifesto remoto de atualizações.
        /// Retorna null caso nenhum servidor tenha sido configurado ainda.
        /// </summary>
        Task<string?> GetUpdateServerUrlAsync();

        /// <summary>
        /// Configura de forma administrativa ou persistente a URL do manifesto remoto.
        /// </summary>
        Task SetUpdateServerUrlAsync(string? url);

        /// <summary>
        /// Executa a verificação de atualizações.
        /// Se nenhum servidor estiver configurado, retorna status NotConfigured amigavelmente.
        /// Se não houver internet ou houver timeout, retorna NetworkError sem interromper o app.
        /// Se houver versão nova, retorna UpdateAvailable com detalhes e release notes.
        /// </summary>
        Task<UpdateCheckResult> CheckForUpdatesAsync(string? overrideUrl = null, HttpClient? httpClient = null);

        /// <summary>
        /// Avalia diretamente o conteúdo de um manifesto JSON em memória (útil para testes e simulações).
        /// </summary>
        UpdateCheckResult EvaluateManifestJson(string manifestJson, string localVersion = AppVersionInfo.CurrentVersion);

        /// <summary>
        /// Valida se uma string é um hash SHA-256 válido (64 caracteres hexadecimais).
        /// </summary>
        bool IsValidSha256(string? hash);

        /// <summary>
        /// Calcula o hash SHA-256 em formato hexadecimal minúsculo para um array de bytes.
        /// </summary>
        string ComputeSha256(byte[] data);

        /// <summary>
        /// Calcula o hash SHA-256 em formato hexadecimal minúsculo para uma stream de dados.
        /// </summary>
        string ComputeSha256(Stream stream);

        /// <summary>
        /// Calcula o hash SHA-256 em formato hexadecimal minúsculo para um arquivo no disco.
        /// </summary>
        string ComputeFileSha256(string filePath);

        /// <summary>
        /// Valida se os bytes informados correspondem ao hash SHA-256 esperado (comparação segura e case-insensitive).
        /// </summary>
        bool VerifySha256(byte[] data, string? expectedSha256);

        /// <summary>
        /// Valida se o arquivo informado no disco corresponde ao hash SHA-256 esperado (comparação segura e case-insensitive).
        /// </summary>
        bool VerifyFileSha256(string filePath, string? expectedSha256);
    }
}
