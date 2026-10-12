using System;
using System.Text.Json.Serialization;

namespace CobraPix_PC.Services.Credit
{
    /// <summary>
    /// Representa o manifesto de autorização operacional contido no arquivo sentinela.
    /// É assinado digitalmente por chave privada assimétrica (ECDSA P-256) mantida exclusivamente pelo operador.
    /// O aplicativo distribui unicamente a chave pública de verificação.
    /// </summary>
    public class ProductionAuthorizationManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("applicationId")]
        public string ApplicationId { get; set; } = "CobraPix PC";

        [JsonPropertyName("module")]
        public string Module { get; set; } = "CreditoGiro";

        [JsonPropertyName("releaseVersion")]
        public string ReleaseVersion { get; set; } = "1.0.4";

        [JsonPropertyName("machineIdentifier")]
        public string MachineIdentifier { get; set; } = string.Empty;

        [JsonPropertyName("productionDatabasePath")]
        public string ProductionDatabasePath { get; set; } = string.Empty;

        [JsonPropertyName("issuedAtUtc")]
        public DateTime IssuedAtUtc { get; set; }

        [JsonPropertyName("validUntilUtc")]
        public DateTime ValidUntilUtc { get; set; }

        [JsonPropertyName("authorizationNonce")]
        public string AuthorizationNonce { get; set; } = string.Empty;

        [JsonPropertyName("signatureBase64")]
        public string SignatureBase64 { get; set; } = string.Empty;

        /// <summary>
        /// Gera a string canônica determinística para validação ou geração da assinatura digital.
        /// </summary>
        public string GetCanonicalPayload()
        {
            return $"SCHEMA:{SchemaVersion}|APP:{ApplicationId}|MOD:{Module}|VER:{ReleaseVersion}|" +
                   $"MACHINE:{MachineIdentifier.ToUpperInvariant()}|PATH:{ProductionDatabasePath.ToUpperInvariant()}|" +
                   $"ISSUED:{IssuedAtUtc:yyyy-MM-ddTHH:mm:ssZ}|VALID:{ValidUntilUtc:yyyy-MM-ddTHH:mm:ssZ}|" +
                   $"NONCE:{AuthorizationNonce}";
        }
    }
}
