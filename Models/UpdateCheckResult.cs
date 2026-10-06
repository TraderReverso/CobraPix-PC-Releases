using System.Collections.Generic;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Status resultante da verificação de atualizações do aplicativo.
    /// </summary>
    public enum UpdateStatus
    {
        /// <summary>
        /// Nenhum servidor remoto oficial está configurado ainda.
        /// </summary>
        NotConfigured,

        /// <summary>
        /// O aplicativo já está na versão mais recente disponível.
        /// </summary>
        UpToDate,

        /// <summary>
        /// Uma nova versão válida está disponível para atualização.
        /// </summary>
        UpdateAvailable,

        /// <summary>
        /// Não foi possível conectar à internet ou houve falha de rede/timeout.
        /// </summary>
        NetworkError,

        /// <summary>
        /// O manifesto remoto recebido é inválido, incompleto ou não está em HTTPS.
        /// </summary>
        InvalidManifest,

        /// <summary>
        /// Erro genérico controlado durante a verificação.
        /// </summary>
        Error
    }

    /// <summary>
    /// Objeto de resultado consolidado da verificação de atualizações.
    /// </summary>
    public class UpdateCheckResult
    {
        public UpdateStatus Status { get; set; } = UpdateStatus.NotConfigured;

        /// <summary>
        /// Versão local atualmente em execução.
        /// </summary>
        public string CurrentVersion { get; set; } = AppVersionInfo.CurrentVersion;

        /// <summary>
        /// Versão remota reportada pelo servidor (quando disponível).
        /// </summary>
        public string? RemoteVersion { get; set; }

        /// <summary>
        /// Indica se há uma versão mais nova disponível para instalação.
        /// </summary>
        public bool IsUpdateAvailable => Status == UpdateStatus.UpdateAvailable;

        /// <summary>
        /// Notas da versão reportadas pelo manifesto.
        /// </summary>
        public List<string> ReleaseNotes { get; set; } = new();

        /// <summary>
        /// URL oficial para download do pacote (obrigatoriamente HTTPS).
        /// </summary>
        public string? DownloadUrl { get; set; }

        /// <summary>
        /// Hash SHA-256 criptográfico esperado do pacote para validação de integridade.
        /// </summary>
        public string? Sha256 { get; set; }

        /// <summary>
        /// Flag indicando se a atualização é mandatória.
        /// </summary>
        public bool Mandatory { get; set; }

        /// <summary>
        /// Tamanho estimado do pacote em bytes reportado pelo manifesto (quando disponível).
        /// </summary>
        public long? PackageSizeBytes { get; set; }

        /// <summary>
        /// Mensagem explicativa amigável para exibição ao usuário.
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
