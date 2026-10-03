using System.Collections.Generic;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa o manifesto de versão remota do CobraPix PC.
    /// Estrutura leve e segura para notificação de novas versões e notas de lançamento.
    /// </summary>
    public class UpdateManifest
    {
        /// <summary>
        /// Versão SemVer remota disponível (ex: 1.0.1).
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// URL oficial para download da atualização (obrigatoriamente HTTPS).
        /// </summary>
        public string? DownloadUrl { get; set; }

        /// <summary>
        /// Lista descritiva de novidades, melhorias e correções incluídas na versão.
        /// </summary>
        public List<string> ReleaseNotes { get; set; } = new();

        /// <summary>
        /// Checksum SHA-256 oficial do pacote de distribuição para validação criptográfica estrita.
        /// </summary>
        public string? Sha256 { get; set; }

        /// <summary>
        /// Indica se a atualização é crítica ou mandatória (por padrão false).
        /// </summary>
        public bool Mandatory { get; set; }
    }
}
