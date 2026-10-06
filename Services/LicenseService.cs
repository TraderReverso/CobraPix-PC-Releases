using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Implementação oficial do serviço de licenças do CobraPix PRO.
    /// Valida e ativa assinaturas criptográficas utilizando ECDSA P-256 e SHA-256.
    /// Opera 100% offline, contendo estritamente a CHAVE PÚBLICA do emissor.
    /// A chave privada nunca reside na aplicação ou no instalador.
    /// </summary>
    public class LicenseService : ILicenseService
    {
        public const string KeyLicenseKey = "Pro.LicenseKey";
        public const string KeyLicenseId = "Pro.LicenseId";
        public const string KeyLicenseType = "Pro.LicenseType";
        public const string KeyLicenseIssuedAt = "Pro.LicenseIssuedAt";
        public const string KeyLicenseExpiresAt = "Pro.LicenseExpiresAt";

        /// <summary>
        /// Chave pública oficial de validação das licenças do CobraPix PRO.
        /// </summary>
        public const string OfficialPublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEaTAg2lH8Fm0nXGsccrcgFbd7g6UK\n" +
            "lXD12XqgZeodliIFFKAWK4jSaV6TEoZrxx3PnREwHC1oYgirHo4+TKEgQg==\n" +
            "-----END PUBLIC KEY-----";

        private readonly CobraPixDbContext _context;
        private readonly string _publicKeyPem;

        public string PublicKeyPem => _publicKeyPem;

        public event EventHandler? LicenseStateChanged;

        public LicenseService(CobraPixDbContext context, string? publicKeyPem = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _publicKeyPem = !string.IsNullOrWhiteSpace(publicKeyPem) ? publicKeyPem.Trim() : OfficialPublicKeyPem;
        }

        public LicenseValidationResult ValidateKey(string? rawKey)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                return LicenseValidationResult.Failure(LicenseStatus.InvalidFormat, "A chave de ativação não foi informada.");
            }

            // Normalização: remove espaços, quebras de linha e caracteres não imprimíveis
            string clean = rawKey.Trim()
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace(" ", "");

            // Trata prefixo CPX-
            if (clean.StartsWith("CPX-", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(4).Trim();
            }

            if (string.IsNullOrWhiteSpace(clean))
            {
                return LicenseValidationResult.Failure(LicenseStatus.InvalidFormat, "Formato de chave inválido.");
            }

            byte[] envelopeBytes;
            try
            {
                envelopeBytes = FromBase64Url(clean);
            }
            catch
            {
                return LicenseValidationResult.Failure(LicenseStatus.InvalidFormat, "A chave contém caracteres inválidos ou corrompidos.");
            }

            // Envelope binário mínimo: 2 bytes payload length + 31 bytes payload mínimo + 64 bytes assinatura = 97 bytes
            if (envelopeBytes.Length < 97)
            {
                return LicenseValidationResult.Failure(LicenseStatus.InvalidFormat, "Tamanho da chave inválido ou truncado.");
            }

            ushort payloadLength;
            byte[] payloadBytes;
            byte[] signatureBytes;

            try
            {
                using var ms = new MemoryStream(envelopeBytes);
                using var br = new BinaryReader(ms);

                payloadLength = br.ReadUInt16();
                if (payloadLength <= 0 || payloadLength > (envelopeBytes.Length - 2))
                {
                    return LicenseValidationResult.Failure(LicenseStatus.InvalidFormat, "Estrutura interna da chave corrompida.");
                }

                payloadBytes = br.ReadBytes(payloadLength);
                int remaining = (int)(ms.Length - ms.Position);
                if (remaining <= 0)
                {
                    return LicenseValidationResult.Failure(LicenseStatus.InvalidFormat, "Assinatura ausente na chave.");
                }

                signatureBytes = br.ReadBytes(remaining);
            }
            catch
            {
                return LicenseValidationResult.Failure(LicenseStatus.InvalidFormat, "Falha ao decodificar a chave de ativação.");
            }

            // 1. Validação Criptográfica de Assinatura Digital Assimétrica (ECDSA P-256 + SHA-256)
            bool signatureValid = false;
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(_publicKeyPem);
                signatureValid = ecdsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256);
            }
            catch (Exception ex)
            {
                return LicenseValidationResult.Failure(LicenseStatus.InvalidSignature, $"Falha criptográfica: {ex.Message}");
            }

            if (!signatureValid)
            {
                return LicenseValidationResult.Failure(LicenseStatus.InvalidSignature, "Chave de ativação inválida ou com assinatura adulterada.");
            }

            // 2. Deserialização e Validação das Regras de Negócio do Payload
            LicenseInfo license;
            try
            {
                license = ParsePayload(payloadBytes, rawKey);
            }
            catch (Exception ex)
            {
                return LicenseValidationResult.Failure(LicenseStatus.InvalidFormat, $"Falha ao ler dados da licença: {ex.Message}");
            }

            // Validação de Produto
            if (!string.Equals(license.Product, "CobraPix PC", StringComparison.OrdinalIgnoreCase))
            {
                return LicenseValidationResult.Failure(LicenseStatus.InvalidProduct, $"Esta licença destina-se ao produto '{license.Product}' e não ao CobraPix PC.", license);
            }

            // Validação de Plano
            if (license.PlanType != PlanType.Pro)
            {
                return LicenseValidationResult.Failure(LicenseStatus.InvalidPlan, "A licença informada não concede acesso ao plano PRO.", license);
            }

            // Validação de Data de Emissão (tolerância de 15 minutos para relógios com pequeno desvio)
            if (license.IssuedAt > DateTime.UtcNow.AddMinutes(15))
            {
                return LicenseValidationResult.Failure(LicenseStatus.NotYetValid, $"A data de início desta licença ({license.IssuedAt.ToLocalTime():dd/MM/yyyy HH:mm}) ainda não foi atingida.", license);
            }

            // Validação de Expiração
            if (DateTime.UtcNow > license.ExpiresAt)
            {
                return LicenseValidationResult.Failure(LicenseStatus.Expired, $"Esta licença do CobraPix PRO expirou em {license.ExpiresAt.ToLocalTime():dd/MM/yyyy às HH:mm}.", license);
            }

            return LicenseValidationResult.Success(license);
        }

        public async Task<LicenseValidationResult> GetCurrentLicenseStatusAsync()
        {
            var keySetting = await _context.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == KeyLicenseKey);

            if (keySetting == null || string.IsNullOrWhiteSpace(keySetting.Value))
            {
                return LicenseValidationResult.Failure(LicenseStatus.NotConfigured, "Nenhuma licença PRO configurada no sistema.");
            }

            return ValidateKey(keySetting.Value);
        }

        public async Task<LicenseValidationResult> ActivateLicenseAsync(string rawKey)
        {
            var result = ValidateKey(rawKey);
            if (!result.IsValid || result.License == null)
            {
                return result;
            }

            var lic = result.License;

            // Persiste no banco de dados local
            await UpsertSettingAsync(KeyLicenseKey, rawKey.Trim());
            await UpsertSettingAsync(KeyLicenseId, lic.LicenseId);
            await UpsertSettingAsync(KeyLicenseType, lic.LicenseType.ToString());
            await UpsertSettingAsync(KeyLicenseIssuedAt, lic.IssuedAt.ToString("O"));
            await UpsertSettingAsync(KeyLicenseExpiresAt, lic.ExpiresAt.ToString("O"));

            await UpsertSettingAsync(ProService.KeyPlanType, PlanType.Pro.ToString());
            await UpsertSettingAsync(ProService.KeyActivatedAt, DateTime.UtcNow.ToString("O"));
            await UpsertSettingAsync(ProService.KeyActivationSource, "CryptographicLicense");

            await _context.SaveChangesAsync();

            LicenseStateChanged?.Invoke(this, EventArgs.Empty);
            return result;
        }

        public async Task DeactivateLicenseAsync()
        {
            await UpsertSettingAsync(KeyLicenseKey, string.Empty);
            await UpsertSettingAsync(ProService.KeyPlanType, PlanType.Free.ToString());
            await UpsertSettingAsync(ProService.KeyActivationSource, "ManualDeactivation");
            await _context.SaveChangesAsync();

            LicenseStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private static LicenseInfo ParsePayload(byte[] payload, string rawKey)
        {
            using var ms = new MemoryStream(payload);
            using var br = new BinaryReader(ms);

            byte m1 = br.ReadByte();
            byte m2 = br.ReadByte();
            byte m3 = br.ReadByte();

            if (m1 != 0x43 || m2 != 0x50 || m3 != 0x58) // 'C', 'P', 'X'
            {
                throw new InvalidDataException("Identificador de cabeçalho da licença inválido.");
            }

            byte version = br.ReadByte();
            if (version != 1)
            {
                throw new InvalidDataException($"Versão do formato de licença '{version}' não suportada.");
            }

            string product = br.ReadString();

            byte planByte = br.ReadByte();
            PlanType plan = planByte == 1 ? PlanType.Pro : PlanType.Free;

            byte typeByte = br.ReadByte();
            LicenseType type = typeByte == 2 ? LicenseType.Annual : LicenseType.Monthly;

            long issuedSec = br.ReadInt64();
            long expiresSec = br.ReadInt64();

            DateTime issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedSec).UtcDateTime;
            DateTime expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresSec).UtcDateTime;

            byte[] guidBytes = br.ReadBytes(16);
            var guid = new Guid(guidBytes);
            string shortHex = guid.ToString("N")[..8].ToUpperInvariant();
            string prefix = type == LicenseType.Annual ? "CPX-A" : "CPX-M";
            string licenseId = $"{prefix}-{issuedAt:yyyyMM}-{shortHex}";

            string? notes = null;
            if (ms.Position < ms.Length)
            {
                ushort notesLen = br.ReadUInt16();
                if (notesLen > 0 && ms.Position + notesLen <= ms.Length)
                {
                    byte[] notesBytes = br.ReadBytes(notesLen);
                    notes = Encoding.UTF8.GetString(notesBytes);
                }
            }

            return new LicenseInfo
            {
                LicenseId = licenseId,
                Product = product,
                PlanType = plan,
                LicenseType = type,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt,
                CustomerNotes = notes,
                RawKey = rawKey
            };
        }

        private async Task UpsertSettingAsync(string key, string value)
        {
            var existing = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (existing != null)
            {
                existing.Value = value;
            }
            else
            {
                _context.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = value
                });
            }
        }

        private static byte[] FromBase64Url(string base64Url)
        {
            string padded = base64Url.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
