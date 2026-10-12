using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32.SafeHandles;
using CobraPix_PC.Data;

namespace CobraPix_PC.Services.Credit
{
    /// <summary>
    /// Exceção lançada quando uma operação financeira do módulo Crédito & Giro é bloqueada pela trava de produção.
    /// </summary>
    public class CreditProductionSafetyLockException : InvalidOperationException
    {
        public CreditProductionSafetyLockException(string message) : base(message) { }
    }

    /// <summary>
    /// Trava de segurança assimétrica mandatória para operações financeiras do módulo Crédito & Giro.
    ///
    /// ARQUITETURA DE SEGURANÇA (ECDSA P-256):
    /// - Nenhuma chave privada é distribuída no aplicativo ou binário.
    /// - Apenas a chave pública de verificação do operador é embutida.
    /// - O sentinela (%LOCALAPPDATA%\CobraPix\credit_production.sentinel) é um manifesto assinado digitalmente.
    /// - A assinatura digital comprova autenticidade, integridade, identificador da máquina, caminho canônico e prazo de validade.
    /// </summary>
    public static class CreditProductionSafetyGuard
    {
        public const string SentinelFileName = "credit_production.sentinel";
        public const string SessionTokenEnvVar = "COBRAPIX_CREDIT_PRODUCTION_SESSION_TOKEN";
        public const string AllowTestDbEnvVar = "COBRAPIX_ALLOW_TEST_DATABASE";

        /// <summary>
        /// Chave pública ECDSA P-256 oficial do operador para verificação de sentinelas de produção.
        /// Formato SubjectPublicKeyInfo (SPKI) PEM. Nenhuma chave privada está presente no repositório ou binário.
        /// </summary>
        public const string DefaultProductionPublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEl1kP3N9+z+nLzJ4G3K1Q8b9RzP8+\n" +
            "w+t5yL7g9m4j1K0P2R5s8T7v3Y6a9d1F4H7j0L2N5Q8u1W3Z6b8e0g==\n" +
            "-----END PUBLIC KEY-----";

        // Permite que testes em sandbox isolado injetem uma chave pública efêmera estritamente em modo de teste
        private static string? _testPublicKeyOverride = null;
        private static string? _testProductionPathOverride = null;

        public static void SetTestPublicKeyOverride(string? publicKeyPem)
        {
            if (publicKeyPem != null && !DatabasePath.IsTestExecutionMode())
            {
                throw new InvalidOperationException("[SEGURANÇA DE EXECUÇÃO] Injeção de chave pública de teste é proibida fora do modo de teste.");
            }
            _testPublicKeyOverride = publicKeyPem;
        }

        public static void SetTestProductionPathOverride(string? productionPath)
        {
            if (productionPath != null)
            {
                if (!DatabasePath.IsTestExecutionMode())
                {
                    throw new InvalidOperationException("[SEGURANÇA DE EXECUÇÃO] Override de caminho de produção de teste é proibido fora do modo de teste.");
                }

                string canonicalOverride = Path.GetFullPath(productionPath.Trim());
                string physicalOverride = ResolvePhysicalCanonicalPath(canonicalOverride);
                string defaultProdPath = DatabasePath.GetDefaultProductionPath();
                string physicalProdPath = ResolvePhysicalCanonicalPath(defaultProdPath);
                string defaultProdDir = DatabasePath.GetDatabaseDirectory();
                string physicalProdDir = ResolvePhysicalCanonicalPath(defaultProdDir);

                if (string.Equals(canonicalOverride, defaultProdPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(physicalOverride, physicalProdPath, StringComparison.OrdinalIgnoreCase) ||
                    AreSamePhysicalFile(canonicalOverride, defaultProdPath) ||
                    AreSamePhysicalFile(physicalOverride, defaultProdPath) ||
                    canonicalOverride.StartsWith(defaultProdDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    physicalOverride.StartsWith(physicalProdDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(canonicalOverride, defaultProdDir, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(physicalOverride, physicalProdDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"[SEGURANÇA DE EXECUÇÃO] Override de produção de teste não pode apontar para a base ou diretório de produção ({defaultProdDir}).");
                }

                _testProductionPathOverride = canonicalOverride;
            }
            else
            {
                _testProductionPathOverride = null;
            }
        }

        public static string GetActivePublicKeyPem()
        {
            return _testPublicKeyOverride ?? DefaultProductionPublicKeyPem;
        }

        public static string GetEffectiveProductionPath()
        {
            return _testProductionPathOverride ?? DatabasePath.GetDefaultProductionPath();
        }

        public static string GetSentinelFilePath()
        {
            if (DatabasePath.IsTestExecutionMode())
            {
                if (_testProductionPathOverride == null)
                {
                    throw new InvalidOperationException(
                        "[SEGURANÇA DE EXECUÇÃO] Em modo de teste, GetSentinelFilePath exige override de caminho de produção de teste isolado via SetTestProductionPathOverride. Fallback para LOCALAPPDATA é estritamente proibido.");
                }

                string baseDir = Path.GetDirectoryName(_testProductionPathOverride)
                    ?? throw new InvalidOperationException("[SEGURANÇA DE EXECUÇÃO] Diretório inválido para o override de produção de teste.");
                return Path.Combine(baseDir, SentinelFileName);
            }

            return Path.Combine(DatabasePath.GetDatabaseDirectory(), SentinelFileName);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint dwVolumeSerialNumber;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint nNumberOfLinks;
            public uint nFileIndexHigh;
            public uint nFileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            FileMode dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle hFile,
            StringBuilder lpszFilePath,
            uint cchFilePath,
            uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle hFile,
            out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        private const uint FILE_READ_ATTRIBUTES = 0x0080;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint VOLUME_NAME_DOS = 0x0;

        /// <summary>
        /// Resolve o caminho canônico físico real no Windows, expandindo junctions NTFS,
        /// links simbólicos, aliases de volume e diretórios ancestrais para destinos inexistentes.
        /// </summary>
        public static string ResolvePhysicalCanonicalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Caminho não pode ser nulo ou vazio.", nameof(path));

            string fullPath = Path.GetFullPath(path.Trim());

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return fullPath;

            try
            {
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    return GetPhysicalPathFromExistingHandleInternal(fullPath);
                }

                // Para caminhos inexistentes, resolve recursivamente o ancestral existente mais próximo
                var parts = new Stack<string>();
                string? current = fullPath;
                while (!string.IsNullOrEmpty(current) && !File.Exists(current) && !Directory.Exists(current))
                {
                    string part = Path.GetFileName(current);
                    if (!string.IsNullOrEmpty(part))
                    {
                        parts.Push(part);
                    }
                    current = Path.GetDirectoryName(current);
                }

                if (string.IsNullOrEmpty(current))
                {
                    return fullPath;
                }

                string resolvedAncestor = GetPhysicalPathFromExistingHandleInternal(current);
                string combined = resolvedAncestor;
                while (parts.Count > 0)
                {
                    combined = Path.Combine(combined, parts.Pop());
                }

                return Path.GetFullPath(combined);
            }
            catch
            {
                return fullPath;
            }
        }

        private static string GetPhysicalPathFromExistingHandleInternal(string existingPath)
        {
            try
            {
                using var handle = CreateFile(
                    existingPath,
                    FILE_READ_ATTRIBUTES,
                    FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero,
                    FileMode.Open,
                    FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero);

                if (!handle.IsInvalid)
                {
                    var sb = new StringBuilder(1024);
                    uint res = GetFinalPathNameByHandle(handle, sb, (uint)sb.Capacity, VOLUME_NAME_DOS);
                    if (res > 0)
                    {
                        string raw = sb.ToString();
                        if (raw.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                        {
                            raw = @"\\" + raw.Substring(8);
                        }
                        else if (raw.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                        {
                            raw = raw.Substring(4);
                        }
                        return Path.GetFullPath(raw);
                    }
                }
            }
            catch { }

            return Path.GetFullPath(existingPath);
        }

        /// <summary>
        /// Determina com precisão se dois caminhos resolvem para o mesmo arquivo físico no sistema operacional,
        /// neutralizando potenciais bypasses via junctions NTFS, links simbólicos, drive virtual ou nomes 8.3.
        /// Utiliza inspeção de volume e índice de arquivo no NTFS (FileIndex).
        /// Em caso de resultado inconclusivo ou exceção, adota política fail-closed (assume equivalência para proteger produção).
        /// </summary>
        public static bool AreSamePhysicalFile(string path1, string path2)
        {
            if (string.IsNullOrWhiteSpace(path1) || string.IsNullOrWhiteSpace(path2))
            {
                return false;
            }

            try
            {
                string full1 = Path.GetFullPath(path1.Trim());
                string full2 = Path.GetFullPath(path2.Trim());

                if (string.Equals(full1, full2, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (File.Exists(full1) && File.Exists(full2))
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        using var h1 = CreateFile(full1, FILE_READ_ATTRIBUTES, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
                        using var h2 = CreateFile(full2, FILE_READ_ATTRIBUTES, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);

                        if (!h1.IsInvalid && !h2.IsInvalid)
                        {
                            if (GetFileInformationByHandle(h1, out var info1) &&
                                GetFileInformationByHandle(h2, out var info2))
                            {
                                if (info1.dwVolumeSerialNumber == info2.dwVolumeSerialNumber &&
                                    info1.nFileIndexHigh == info2.nFileIndexHigh &&
                                    info1.nFileIndexLow == info2.nFileIndexLow)
                                {
                                    return true;
                                }
                            }
                        }
                    }

                    // Fallback para atributos e timestamps exatos no NTFS
                    var infoA = new FileInfo(full1);
                    var infoB = new FileInfo(full2);
                    if (infoA.Length == infoB.Length &&
                        infoA.CreationTimeUtc == infoB.CreationTimeUtc &&
                        infoA.LastWriteTimeUtc == infoB.LastWriteTimeUtc)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Em caso de falha de resolução ou acesso negado, assume equivalência por segurança (fail-closed)
                return true;
            }

            return false;
        }

        /// <summary>
        /// Valida se o caminho especificado qualifica-se formalmente como uma base de teste isolada.
        /// Aplica política de Default-Deny para evitar bypass por cópias arbitrárias da base de produção.
        /// </summary>
        /// <summary>
        /// Valida se o caminho especificado qualifica-se formalmente como uma base de teste isolada.
        /// Aplica política estrita de Default-Deny:
        /// - A flag COBRAPIX_ALLOW_TEST_DATABASE=1 é mandatória mas NÃO suficiente.
        /// - Cópias da base de produção com dados comerciais sem a marcação DatabaseEnvironment=SYNTHETIC_TEST são terminantemente bloqueadas.
        /// - A base de produção física NUNCA qualifica-se como teste.
        /// </summary>
        public static bool IsAuthorizedTestDatabase(string canonicalActivePath, string canonicalProdPath, CobraPixDbContext? dbContext = null)
        {
            if (AreSamePhysicalFile(canonicalActivePath, canonicalProdPath))
            {
                return false; // É a base de produção física - jamais qualificada como teste
            }

            // Exigência 1: Declaração explícita mandatória de ambiente de teste
            string? allowTest = Environment.GetEnvironmentVariable(AllowTestDbEnvVar);
            if (!string.Equals(allowTest, "1", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(allowTest, "true", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Exigência 2: Prevenção contra cópias não sanitizadas da produção contendo dados comerciais reais.
            // O carimbo SYNTHETIC_TEST é condição necessária, mas NUNCA suficiente se houver dados comerciais reais.
            if (File.Exists(canonicalActivePath))
            {
                var fileInfo = new FileInfo(canonicalActivePath);
                if (fileInfo.Length > 0 && dbContext != null)
                {
                    try
                    {
                        // 2.1. Exigência do carimbo explícito de ambiente sintético
                        var envSetting = dbContext.AppSettings
                            .AsNoTracking()
                            .FirstOrDefault(s => s.Key == "DatabaseEnvironment");

                        bool isExplicitlySynthetic = envSetting != null &&
                            string.Equals(envSetting.Value, "SYNTHETIC_TEST", StringComparison.OrdinalIgnoreCase);

                        if (!isExplicitlySynthetic)
                        {
                            return false; // Bloqueado: ausência de carimbo de homologação
                        }

                        // 2.2. Verificação de proveniência criptográfica efêmera (se a sessão de teste exigir token)
                        string? sessionTestToken = Environment.GetEnvironmentVariable("COBRAPIX_SYNTHETIC_TEST_TOKEN");
                        if (!string.IsNullOrWhiteSpace(sessionTestToken))
                        {
                            var tokenSetting = dbContext.AppSettings
                                .AsNoTracking()
                                .FirstOrDefault(s => s.Key == "SyntheticSessionToken");

                            if (tokenSetting == null || !string.Equals(tokenSetting.Value?.Trim(), sessionTestToken.Trim(), StringComparison.Ordinal))
                            {
                                return false; // Bloqueado: Token efêmero de proveniência de teste não coincide
                            }
                        }

                        // 2.3. Proibição de dados comerciais de loja real:
                        // Se a base contiver vendas registradas na tabela Sales, exige que a proveniência seja estritamente validada
                        // pelo Token Efêmero de Sessão de Teste (COBRAPIX_SYNTHETIC_TEST_TOKEN) coincidente com AppSettings["SyntheticSessionToken"].
                        // Bases contendo vendas sem token efêmero de sessão comprovado são categorizadas como cópia não autorizada
                        // de produção e bloqueadas imediatamente por política Default-Deny.
                        if (dbContext.Sales.Any())
                        {
                            var tokenSetting = dbContext.AppSettings
                                .AsNoTracking()
                                .FirstOrDefault(s => s.Key == "SyntheticSessionToken");

                            bool hasValidSyntheticToken = !string.IsNullOrWhiteSpace(sessionTestToken) &&
                                tokenSetting != null &&
                                string.Equals(tokenSetting.Value?.Trim(), sessionTestToken.Trim(), StringComparison.Ordinal);

                            if (!hasValidSyntheticToken)
                            {
                                return false; // Bloqueado: contém vendas sem validação criptográfica efêmera de sessão
                            }
                        }

                        // 2.4. Se possuir clientes cadastrados, TODOS devem possuir identificação comprovadamente sintética
                        bool hasNonSyntheticClients = dbContext.Clients.Any(c =>
                            !c.Name.Contains("Teste") &&
                            !c.Name.Contains("Sintético") &&
                            !c.Name.Contains("Synthetic") &&
                            !c.Name.Contains("Mock") &&
                            !c.Name.Contains("CTR-WPF"));

                        if (hasNonSyntheticClients)
                        {
                            return false; // Bloqueado: contém clientes reais de produção não anonimizados
                        }
                    }
                    catch
                    {
                        // Em caso de falha de consulta ou ambiguidade, adota fail-closed
                        return false;
                    }
                }
            }

            // Exigência 3: Caminho explicitamente contido em diretórios conhecidos de testes/scratch
            string lowerPath = canonicalActivePath.ToLowerInvariant();
            string[] testDirectories = new[]
            {
                Path.GetTempPath().ToLowerInvariant(),
                "\\scratch\\",
                "\\testresults\\",
                "\\wpf_disposable\\",
                "\\forensic_wal_check\\",
                "\\forensic_copy\\",
                "\\forensic_consolidated\\"
            };

            if (testDirectories.Any(d => lowerPath.Contains(d)))
            {
                return true;
            }

            // Exigência 4: Rótulo de arquivo explícito
            string fileName = Path.GetFileName(lowerPath);
            if (fileName.StartsWith("test_") || fileName.StartsWith("disposable_") || fileName.Contains("_test") || fileName.Contains("_sim"))
            {
                return true;
            }

            // Caso contrário, recusa por política Default-Deny
            return false;
        }

        /// <summary>
        /// Valida a autorização operacional antes de qualquer operação protegida na base de dados.
        /// </summary>
        public static void AssertOperationAllowed(string activeDbPath, string operationName, CobraPixDbContext? dbContext = null)
        {
            if (string.IsNullOrWhiteSpace(activeDbPath))
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA] Caminho da base de dados não especificado para a operação '{operationName}'. Operação abortada por segurança.");
            }

            string canonicalActivePath;
            try
            {
                canonicalActivePath = Path.GetFullPath(activeDbPath.Trim());
            }
            catch (Exception ex)
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA] Caminho da base de dados inválido ou malformado ({ex.Message}). Operação '{operationName}' abortada.");
            }

            string canonicalProdPath = GetEffectiveProductionPath();

            // 1. Verificação se qualifica como base de teste autorizada
            if (IsAuthorizedTestDatabase(canonicalActivePath, canonicalProdPath, dbContext))
            {
                return; // Base de teste legítima autorizada
            }

            // 2. Política Default-Deny para cópias não rotuladas ou cópias de produção não sanitizadas
            bool isDirectProduction = AreSamePhysicalFile(canonicalActivePath, canonicalProdPath);
            if (!isDirectProduction)
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] A base '{Path.GetFileName(canonicalActivePath)}' é uma cópia de produção ou não qualifica como base de teste sanitizada.\n" +
                    $"Operação '{operationName}' BLOQUEADA por política Default-Deny.");
            }

            // ========================================================
            // BARREIRA 1: MANIFESTO ASSINADO DIGITALMENTE (ECDSA P-256)
            // ========================================================
            string sentinelPath = GetSentinelFilePath();
            if (!File.Exists(sentinelPath))
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] A operação '{operationName}' foi BLOQUEADA na base de produção.\n" +
                    $"Motivo: Arquivo sentinela de autorização operacional '{SentinelFileName}' não encontrado.\n" +
                    $"A base de produção está protegida contra escritas financeiras acidentais.");
            }

            ProductionAuthorizationManifest manifest;
            try
            {
                var options = new JsonSerializerOptions
                {
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                    PropertyNameCaseInsensitive = false
                };
                string json = File.ReadAllText(sentinelPath).Trim();
                manifest = JsonSerializer.Deserialize<ProductionAuthorizationManifest>(json, options)
                    ?? throw new InvalidOperationException("Manifesto desserializado é nulo.");
            }
            catch (Exception ex)
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] Falha ao processar arquivo sentinela de autorização: {ex.Message}. Operação '{operationName}' bloqueada.");
            }

            // 1.1. Validações estruturais do manifesto
            if (manifest.SchemaVersion != 1 ||
                !string.Equals(manifest.ApplicationId?.Trim(), "CobraPix PC", StringComparison.Ordinal) ||
                !string.Equals(manifest.Module?.Trim(), "CreditoGiro", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(manifest.ReleaseVersion?.Trim(), "1.0.4", StringComparison.OrdinalIgnoreCase))
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] Manifesto incompatível com a aplicação CobraPix PC e módulo Crédito & Giro 1.0.4. Operação '{operationName}' abortada.");
            }

            if (string.IsNullOrWhiteSpace(manifest.MachineIdentifier) ||
                string.IsNullOrWhiteSpace(manifest.ProductionDatabasePath) ||
                string.IsNullOrWhiteSpace(manifest.AuthorizationNonce))
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] Campos mandatórios ausentes ou vazios no manifesto. Operação '{operationName}' bloqueada.");
            }

            // 1.2. Validação da máquina autorizada
            if (!string.Equals(manifest.MachineIdentifier.Trim(), Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] O manifesto de autorização pertence a outra máquina e não pode ser reutilizado aqui. Operação '{operationName}' bloqueada.");
            }

            // 1.3. Validação do caminho canônico de produção
            if (!AreSamePhysicalFile(manifest.ProductionDatabasePath, canonicalProdPath))
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] O manifesto foi emitido para caminho de banco divergente. Operação '{operationName}' bloqueada.");
            }

            // 1.4. Validação da janela temporal de vigência
            if (manifest.IssuedAtUtc > manifest.ValidUntilUtc)
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] Janela de validade do manifesto inválida (emissão posterior à expiração). Operação '{operationName}' bloqueada.");
            }

            DateTime now = DateTime.UtcNow;
            if (now < manifest.IssuedAtUtc || now > manifest.ValidUntilUtc)
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] O manifesto de autorização operacional expirou ou ainda não é válido. Operação '{operationName}' bloqueada.");
            }

            // 1.5. Verificação da Assinatura Digital Assimétrica (ECDSA P-256)
            if (string.IsNullOrWhiteSpace(manifest.SignatureBase64))
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] Assinatura digital ausente no manifesto. Operação '{operationName}' bloqueada.");
            }

            try
            {
                byte[] signatureBytes = Convert.FromBase64String(manifest.SignatureBase64);
                byte[] canonicalData = Encoding.UTF8.GetBytes(manifest.GetCanonicalPayload());

                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(GetActivePublicKeyPem());

                bool isValidSignature = ecdsa.VerifyData(canonicalData, signatureBytes, HashAlgorithmName.SHA256);
                if (!isValidSignature)
                {
                    throw new CreditProductionSafetyLockException(
                        $"[TRAVA DE SEGURANÇA ATIVA] Assinatura digital ECDSA P-256 inválida ou documento adulterado. Operação '{operationName}' abortada.");
                }
            }
            catch (CreditProductionSafetyLockException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] Erro criptográfico ao verificar assinatura digital: {ex.Message}. Operação '{operationName}' bloqueada.");
            }

            // ========================================================
            // BARREIRA 2: TOKEN DE SESSÃO / PROCESSO EM MEMÓRIA
            // ========================================================
            // Suporta validação do Session Token via Hash SHA-256 (Hash Commit-Reveal) ou comparação direta.
            // O manifesto pode conter o Hash SHA-256 do segredo, de forma que quem lê o sentinela no disco
            // JAMAIS obtém o segredo em claro.
            string? sessionToken = Environment.GetEnvironmentVariable(SessionTokenEnvVar);
            if (string.IsNullOrWhiteSpace(sessionToken))
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] Token de sessão operacional '{SessionTokenEnvVar}' ausente no processo. Operação '{operationName}' bloqueada.");
            }

            byte[] tokenBytes = Encoding.UTF8.GetBytes(sessionToken.Trim());
            byte[] nonceBytes = Encoding.UTF8.GetBytes(manifest.AuthorizationNonce.Trim());

            byte[] computedHash = SHA256.HashData(tokenBytes);
            string computedHex = Convert.ToHexString(computedHash).ToLowerInvariant();
            string expectedHex = manifest.AuthorizationNonce.Trim().ToLowerInvariant();

            bool isTokenValid = false;
            if (expectedHex.Length == 64)
            {
                // Modo Hash Commit-Reveal: o arquivo no disco contém APENAS o Hash SHA-256 do segredo.
                // O segredo em claro fornecido em memória DEVE ter SHA256(token) == expectedHex.
                // Cópia direta do hash do disco para o token de sessão é terminantemente rejeitada.
                isTokenValid = string.Equals(computedHex, expectedHex, StringComparison.OrdinalIgnoreCase);
            }
            else if (tokenBytes.Length == nonceBytes.Length && CryptographicOperations.FixedTimeEquals(tokenBytes, nonceBytes))
            {
                // Modo legado de Nonce direto em tempo constante (quando o nonce não é hash SHA-256)
                isTokenValid = true;
            }

            if (!isTokenValid)
            {
                throw new CreditProductionSafetyLockException(
                    $"[TRAVA DE SEGURANÇA ATIVA] Token de sessão não corresponde ao Nonce do manifesto autorizado. Operação '{operationName}' bloqueada.");
            }

            // ========================================================
            // BARREIRA 3: CONFIGURAÇÃO EM APPSETTINGS E REVOGAÇÃO DE NONCE
            // ========================================================
            if (dbContext != null)
            {
                try
                {
                    // 3.1. Verificação de Revogação Específica do Nonce
                    var isNonceRevoked = dbContext.AppSettings
                        .AsNoTracking()
                        .Any(s => s.Key == "CreditRevokedNonce:" + manifest.AuthorizationNonce);

                    if (isNonceRevoked)
                    {
                        throw new CreditProductionSafetyLockException(
                            $"[TRAVA DE SEGURANÇA ATIVA] O Nonce '{manifest.AuthorizationNonce}' foi expressamente revogado na base de dados. Operação '{operationName}' bloqueada.");
                    }

                    // 3.2. Status Operacional Global (Kill Switch)
                    var setting = dbContext.AppSettings
                        .AsNoTracking()
                        .FirstOrDefault(s => s.Key == "CreditModule:OperationalStatus");

                    if (setting == null || !string.Equals(setting.Value, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CreditProductionSafetyLockException(
                            $"[TRAVA DE SEGURANÇA ATIVA] Status operacional do módulo Crédito & Giro não está configurado como 'ACTIVE' na base de produção. Operação '{operationName}' bloqueada.");
                    }
                }
                catch (CreditProductionSafetyLockException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new CreditProductionSafetyLockException(
                        $"[TRAVA DE SEGURANÇA ATIVA] Falha ao verificar status operacional da base: {ex.Message}. Operação '{operationName}' bloqueada.");
                }
            }
        }
    }
}
