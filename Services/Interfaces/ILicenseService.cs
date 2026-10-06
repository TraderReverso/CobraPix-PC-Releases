using System;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato do serviço de validação e ativação de licenças criptográficas do CobraPix PRO.
    /// Opera de forma 100% offline através de assinatura digital assimétrica ECDSA P-256.
    /// </summary>
    public interface ILicenseService
    {
        /// <summary>
        /// Valida uma chave de licença em formato string sem persistir.
        /// </summary>
        LicenseValidationResult ValidateKey(string? rawKey);

        /// <summary>
        /// Obtém o status da licença atualmente instalada no SQLite local.
        /// </summary>
        Task<LicenseValidationResult> GetCurrentLicenseStatusAsync();

        /// <summary>
        /// Ativa e persiste uma nova chave de licença no banco SQLite local.
        /// </summary>
        Task<LicenseValidationResult> ActivateLicenseAsync(string rawKey);

        /// <summary>
        /// Remove a licença instalada, revertendo o aplicativo ao plano FREE.
        /// </summary>
        Task DeactivateLicenseAsync();

        /// <summary>
        /// Obtém a chave pública em formato PEM utilizada pelo aplicativo para validação.
        /// </summary>
        string PublicKeyPem { get; }

        /// <summary>
        /// Evento disparado quando uma licença é ativada ou desativada.
        /// </summary>
        event EventHandler? LicenseStateChanged;
    }
}
