using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CobraPix.LicenseGenerator
{
    public enum LicenseType : byte
    {
        Monthly = 1,
        Annual = 2
    }

    public class Program
    {
        private const string KeysDirectoryName = "keys";
        private const string PrivateKeyFileName = "license_private_key.pem";
        private const string PublicKeyFileName = "license_public_key.pem";

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("==========================================================");
            Console.WriteLine(" CobraPix PC — Gerador Oficial de Licenças PRO (Vendedor) ");
            Console.WriteLine("==========================================================");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Busca pasta keys no diretório do projeto ou no diretório base
            string keysDir = Path.Combine(baseDir, KeysDirectoryName);
            if (!Directory.Exists(keysDir))
            {
                // Se estiver rodando via dotnet run, pode estar em bin/Debug/...
                string projectKeysDir = Path.Combine(Directory.GetCurrentDirectory(), KeysDirectoryName);
                if (Directory.Exists(projectKeysDir) || !Directory.Exists(keysDir))
                {
                    keysDir = projectKeysDir;
                }
            }

            if (!Directory.Exists(keysDir))
            {
                Directory.CreateDirectory(keysDir);
            }

            string privateKeyPath = Path.Combine(keysDir, PrivateKeyFileName);
            string publicKeyPath = Path.Combine(keysDir, PublicKeyFileName);

            using var ecdsa = GetOrCreateKeyPair(privateKeyPath, publicKeyPath);

            if (args.Length == 0 || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            string customer = args.Length > 1 ? args[1] : "Cliente CobraPix";

            switch (command)
            {
                case "show-public-key":
                    Console.WriteLine("\n[CHAVE PÚBLICA (Para embutir no CobraPix PC)]:\n");
                    Console.WriteLine(File.ReadAllText(publicKeyPath));
                    return 0;

                case "monthly":
                case "mensal":
                    GenerateAndPrintLicense(ecdsa, LicenseType.Monthly, 30, customer);
                    return 0;

                case "annual":
                case "anual":
                    GenerateAndPrintLicense(ecdsa, LicenseType.Annual, 365, customer);
                    return 0;

                case "custom":
                    if (args.Length < 2 || !int.TryParse(args[1], out int days) || days <= 0)
                    {
                        Console.Error.WriteLine("Uso: custom <dias> [nome_cliente]");
                        return 1;
                    }
                    string cust = args.Length > 2 ? args[2] : "Cliente CobraPix";
                    var licType = days > 60 ? LicenseType.Annual : LicenseType.Monthly;
                    GenerateAndPrintLicense(ecdsa, licType, days, cust);
                    return 0;

                case "generate-test-keys":
                    Console.WriteLine("Chaves verificadas com sucesso.");
                    return 0;

                default:
                    Console.Error.WriteLine($"Comando desconhecido: '{command}'. Execute com --help para ver opções.");
                    return 1;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Comandos disponíveis:");
            Console.WriteLine("  monthly [cliente]         Gera licença PRO Mensal (30 dias)");
            Console.WriteLine("  annual [cliente]          Gera licença PRO Anual (365 dias)");
            Console.WriteLine("  custom <dias> [cliente]   Gera licença com validade personalizada");
            Console.WriteLine("  show-public-key           Exibe a chave pública PEM");
            Console.WriteLine();
            Console.WriteLine("Exemplos:");
            Console.WriteLine("  dotnet run -- monthly \"João Silva\"");
            Console.WriteLine("  dotnet run -- annual \"Maria Souza\"");
        }

        private static ECDsa GetOrCreateKeyPair(string privateKeyPath, string publicKeyPath)
        {
            var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            if (File.Exists(privateKeyPath))
            {
                string pem = File.ReadAllText(privateKeyPath);
                ecdsa.ImportFromPem(pem);
            }
            else
            {
                Console.WriteLine("[INFO] Gerando novo par de chaves assimétricas ECDSA P-256...");
                string privPem = ecdsa.ExportPkcs8PrivateKeyPem();
                string pubPem = ecdsa.ExportSubjectPublicKeyInfoPem();

                File.WriteAllText(privateKeyPath, privPem, Encoding.UTF8);
                File.WriteAllText(publicKeyPath, pubPem, Encoding.UTF8);

                Console.WriteLine($"[INFO] Chave privada salva com segurança em: {privateKeyPath}");
                Console.WriteLine($"[INFO] Chave pública salva em: {publicKeyPath}");
            }

            return ecdsa;
        }

        public static string GenerateLicenseKey(
            ECDsa ecdsa,
            LicenseType type,
            int validityDays,
            string? customerNotes,
            out string licenseId,
            out DateTime expiresAt,
            string product = "CobraPix PC",
            byte planByte = 1,
            DateTime? customIssuedAt = null)
        {
            var guid = Guid.NewGuid();
            string shortHex = guid.ToString("N")[..8].ToUpperInvariant();
            string prefix = type == LicenseType.Annual ? "CPX-A" : "CPX-M";
            var now = customIssuedAt ?? DateTime.UtcNow;
            licenseId = $"{prefix}-{now:yyyyMM}-{shortHex}";
            expiresAt = now.AddDays(validityDays);

            byte[] payload = BuildPayload(guid, type, now, expiresAt, customerNotes, product, planByte);
            byte[] signature = ecdsa.SignData(payload, HashAlgorithmName.SHA256);

            // Envelope: [2 bytes ushort payload length] + [payload] + [64 bytes signature]
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write((ushort)payload.Length);
            bw.Write(payload);
            bw.Write(signature);

            byte[] envelope = ms.ToArray();
            string base64Url = ToBase64Url(envelope);

            return $"CPX-{base64Url}";
        }

        private static void GenerateAndPrintLicense(ECDsa ecdsa, LicenseType type, int validityDays, string customer)
        {
            string key = GenerateLicenseKey(ecdsa, type, validityDays, customer, out string licenseId, out DateTime expiresAt);

            Console.WriteLine("\n==========================================================");
            Console.WriteLine($" LICENÇA GERADA COM SUCESSO ({type.ToString().ToUpperInvariant()})");
            Console.WriteLine("==========================================================");
            Console.WriteLine($"Identificador : {licenseId}");
            Console.WriteLine($"Tipo de Plano : {(type == LicenseType.Annual ? "PRO Anual" : "PRO Mensal")}");
            Console.WriteLine($"Cliente / Ref : {customer}");
            Console.WriteLine($"Emitido em    : {DateTime.UtcNow:dd/MM/yyyy HH:mm:ss} UTC");
            Console.WriteLine($"Expira em     : {expiresAt:dd/MM/yyyy HH:mm:ss} UTC ({validityDays} dias)");
            Console.WriteLine("----------------------------------------------------------");
            Console.WriteLine("CHAVE DE ATIVAÇÃO PARA ENTREGAR AO CLIENTE:");
            Console.WriteLine(key);
            Console.WriteLine("==========================================================\n");
        }

        private static byte[] BuildPayload(
            Guid guid,
            LicenseType type,
            DateTime issuedAt,
            DateTime expiresAt,
            string? notes,
            string product = "CobraPix PC",
            byte planByte = 1)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8);

            // Magic: 'C', 'P', 'X'
            bw.Write((byte)0x43);
            bw.Write((byte)0x50);
            bw.Write((byte)0x58);

            // Version: 1
            bw.Write((byte)1);

            // Product
            bw.Write(product);

            // Plan: 1 = Pro
            bw.Write(planByte);

            // Type: 1 = Monthly, 2 = Annual
            bw.Write((byte)type);

            // IssuedAt (Unix seconds)
            long issuedSec = new DateTimeOffset(issuedAt).ToUnixTimeSeconds();
            bw.Write(issuedSec);

            // ExpiresAt (Unix seconds)
            long expiresSec = new DateTimeOffset(expiresAt).ToUnixTimeSeconds();
            bw.Write(expiresSec);

            // Guid (16 bytes)
            bw.Write(guid.ToByteArray());

            // Customer notes (opcional)
            string safeNotes = notes ?? string.Empty;
            byte[] notesBytes = Encoding.UTF8.GetBytes(safeNotes);
            bw.Write((ushort)notesBytes.Length);
            if (notesBytes.Length > 0)
            {
                bw.Write(notesBytes);
            }

            return ms.ToArray();
        }

        private static string ToBase64Url(byte[] data)
        {
            return Convert.ToBase64String(data)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
    }
}
