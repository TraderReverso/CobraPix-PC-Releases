using System;
using System.IO;

namespace CobraPix_PC.Data
{
    /// <summary>
    /// Gerencia a localização do diretório e a string de conexão do SQLite local.
    /// Implementa isolamento explícito entre modos de execução e prevenção estrita contra fallback silencioso.
    /// </summary>
    public static class DatabasePath
    {
        private const string FolderName = "CobraPix";
        private const string DatabaseFileName = "cobrapix.db";

        public const string ExecutionModeEnvVar = "COBRAPIX_EXECUTION_MODE";
        public const string DatabasePathEnvVar = "COBRAPIX_DATABASE_PATH";
        public const string AllowTestDbEnvVar = "COBRAPIX_ALLOW_TEST_DATABASE";
        public const string SandboxRootEnvVar = "COBRAPIX_SANDBOX_ROOT";

        /// <summary>
        /// Obtém o caminho canônico do diretório de produção (%LOCALAPPDATA%\CobraPix) sem criar diretórios no disco.
        /// </summary>
        internal static string GetProductionDirectoryPath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.GetFullPath(Path.Combine(localAppData, FolderName));
        }

        /// <summary>
        /// Obtém o caminho do diretório em %LOCALAPPDATA%\CobraPix,
        /// criando a pasta caso ela não exista (apenas fora do modo de teste).
        /// </summary>
        public static string GetDatabaseDirectory()
        {
            string appFolder = GetProductionDirectoryPath();

            if (!IsTestExecutionMode() && !Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }

            return appFolder;
        }

        /// <summary>
        /// Obtém o caminho padrão canônico do banco de dados de produção.
        /// </summary>
        public static string GetDefaultProductionPath()
        {
            return Path.GetFullPath(Path.Combine(GetProductionDirectoryPath(), DatabaseFileName));
        }

        /// <summary>
        /// Determina se o processo atual está executando em modo explícito de teste/desenvolvimento/homologação.
        /// </summary>
        public static bool IsTestExecutionMode()
        {
            string? mode = Environment.GetEnvironmentVariable(ExecutionModeEnvVar);
            if (string.Equals(mode, "TEST", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "DEV", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "HOMOLOG", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string? allowTest = Environment.GetEnvironmentVariable(AllowTestDbEnvVar);
            if (string.Equals(allowTest, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(allowTest, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool ContainsRelativeParentTraversal(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return false;

            string[] segments = rawPath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string segment in segments)
            {
                if (string.Equals(segment.Trim(), "..", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Valida de forma estrita (fail-closed) um caminho de diretório antes de qualquer efeito colateral em modo de teste.
        /// Rejeita o diretório/base de produção, aliases físicos, junctions/symlinks maliciosos e fugas da sandbox.
        /// </summary>
        public static string AssertSafeTestDirectoryPath(string? targetDir, string operationDescription = "Operação em diretório")
        {
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                throw new InvalidOperationException(
                    $"[SEGURANÇA DE EXECUÇÃO] {operationDescription} abortada: o diretório alvo não pode ser nulo ou vazio.");
            }

            string rawTrimmed = targetDir.Trim();
            if (IsTestExecutionMode() && ContainsRelativeParentTraversal(rawTrimmed))
            {
                throw new InvalidOperationException(
                    $"[SEGURANÇA DE EXECUÇÃO] {operationDescription} em modo de teste abortada: travessia relativa ('..') não é permitida ('{rawTrimmed}').");
            }

            string fullTarget = Path.GetFullPath(rawTrimmed);
            if (!IsTestExecutionMode())
            {
                return fullTarget;
            }

            string physicalTarget = ResolvePhysicalCanonicalPath(fullTarget);
            string prodDir = GetProductionDirectoryPath();
            string physicalProdDir = ResolvePhysicalCanonicalPath(prodDir);
            string prodDbPath = GetDefaultProductionPath();
            string physicalProdDbPath = ResolvePhysicalCanonicalPath(prodDbPath);

            if (string.Equals(fullTarget, prodDir, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(physicalTarget, physicalProdDir, StringComparison.OrdinalIgnoreCase) ||
                AreSamePhysicalFile(fullTarget, prodDir) ||
                AreSamePhysicalFile(physicalTarget, prodDir) ||
                fullTarget.StartsWith(prodDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                physicalTarget.StartsWith(physicalProdDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullTarget, prodDbPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(physicalTarget, physicalProdDbPath, StringComparison.OrdinalIgnoreCase) ||
                AreSamePhysicalFile(fullTarget, prodDbPath) ||
                AreSamePhysicalFile(physicalTarget, prodDbPath))
            {
                throw new InvalidOperationException(
                    $"[SEGURANÇA DE EXECUÇÃO] {operationDescription} em modo de teste abortada: o diretório configurado ('{fullTarget}', físico: '{physicalTarget}') coincide com o diretório ou base de produção ({prodDir}) ou reside dentro dele.");
            }

            string? sandboxRoot = Environment.GetEnvironmentVariable(SandboxRootEnvVar);
            if (!string.IsNullOrWhiteSpace(sandboxRoot))
            {
                string canonicalSandbox = Path.GetFullPath(sandboxRoot.Trim());
                string physicalSandbox = ResolvePhysicalCanonicalPath(canonicalSandbox);

                if (string.Equals(canonicalSandbox, prodDir, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(physicalSandbox, physicalProdDir, StringComparison.OrdinalIgnoreCase) ||
                    canonicalSandbox.StartsWith(prodDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    physicalSandbox.StartsWith(physicalProdDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"[SEGURANÇA DE EXECUÇÃO] Raiz de sandbox inválida: {SandboxRootEnvVar} aponta para o diretório de produção ({prodDir}).");
                }

                bool containedTextually = fullTarget.StartsWith(canonicalSandbox + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(fullTarget, canonicalSandbox, StringComparison.OrdinalIgnoreCase);

                bool containedPhysically = physicalTarget.StartsWith(physicalSandbox + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(physicalTarget, physicalSandbox, StringComparison.OrdinalIgnoreCase);

                if (!containedTextually || !containedPhysically)
                {
                    throw new InvalidOperationException(
                        $"[SEGURANÇA DE EXECUÇÃO] {operationDescription} em modo de teste abortada: o diretório alvo '{fullTarget}' (físico: '{physicalTarget}') escapa da raiz da sandbox autorizada '{canonicalSandbox}'.");
                }
            }

            return fullTarget;
        }

        /// <summary>
        /// Valida de forma estrita (fail-closed) um caminho de arquivo antes de qualquer leitura, criação, truncamento ou substituição em modo de teste.
        /// Rejeita o banco/diretório de produção, aliases físicos, caminhos relativos que escapem da sandbox, junctions e symlinks externos.
        /// </summary>
        public static string AssertSafeTestFilePath(string? targetFilePath, string operationDescription = "Operação em arquivo")
        {
            if (string.IsNullOrWhiteSpace(targetFilePath))
            {
                throw new InvalidOperationException(
                    $"[SEGURANÇA DE EXECUÇÃO] {operationDescription} abortada: o caminho de arquivo alvo não pode ser nulo ou vazio.");
            }

            string rawTrimmed = targetFilePath.Trim();
            if (IsTestExecutionMode() && ContainsRelativeParentTraversal(rawTrimmed))
            {
                throw new InvalidOperationException(
                    $"[SEGURANÇA DE EXECUÇÃO] {operationDescription} em modo de teste abortada: travessia relativa ('..') não é permitida ('{rawTrimmed}').");
            }

            string fullTarget = Path.GetFullPath(rawTrimmed);
            if (!IsTestExecutionMode())
            {
                return fullTarget;
            }

            string physicalTarget = ResolvePhysicalCanonicalPath(fullTarget);
            string prodPath = GetDefaultProductionPath();
            string physicalProdPath = ResolvePhysicalCanonicalPath(prodPath);
            string prodDir = GetProductionDirectoryPath();
            string physicalProdDir = ResolvePhysicalCanonicalPath(prodDir);

            if (string.Equals(fullTarget, prodPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(physicalTarget, physicalProdPath, StringComparison.OrdinalIgnoreCase) ||
                AreSamePhysicalFile(fullTarget, prodPath) ||
                AreSamePhysicalFile(physicalTarget, prodPath) ||
                fullTarget.StartsWith(prodDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                physicalTarget.StartsWith(physicalProdDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullTarget, prodDir, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(physicalTarget, physicalProdDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"[SEGURANÇA DE EXECUÇÃO] {operationDescription} em modo de teste abortada: o arquivo alvo ('{fullTarget}', físico: '{physicalTarget}') coincide com a produção ou reside em seu diretório ({prodDir}).");
            }

            string? parentDir = Path.GetDirectoryName(fullTarget);
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                AssertSafeTestDirectoryPath(parentDir, operationDescription);
            }

            string? sandboxRoot = Environment.GetEnvironmentVariable(SandboxRootEnvVar);
            if (!string.IsNullOrWhiteSpace(sandboxRoot))
            {
                string canonicalSandbox = Path.GetFullPath(sandboxRoot.Trim());
                string physicalSandbox = ResolvePhysicalCanonicalPath(canonicalSandbox);

                bool containedTextually = fullTarget.StartsWith(canonicalSandbox + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                bool containedPhysically = physicalTarget.StartsWith(physicalSandbox + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

                if (!containedTextually || !containedPhysically)
                {
                    throw new InvalidOperationException(
                        $"[SEGURANÇA DE EXECUÇÃO] {operationDescription} em modo de teste abortada: o arquivo alvo '{fullTarget}' (físico: '{physicalTarget}') escapa da raiz da sandbox autorizada '{canonicalSandbox}'.");
                }
            }

            return fullTarget;
        }

        /// <summary>
        /// Resolve e valida um caminho explícito ou padrão de banco de dados antes de qualquer efeito colateral.
        /// </summary>
        public static string ValidateAndResolveTargetDatabasePath(string? explicitDatabasePath, string operationDescription = "Acesso ao banco de dados")
        {
            if (explicitDatabasePath != null && string.IsNullOrWhiteSpace(explicitDatabasePath))
            {
                throw new InvalidOperationException(
                    $"[SEGURANÇA DE EXECUÇÃO] {operationDescription} abortada: o caminho explícito do banco de dados não pode ser vazio.");
            }

            if (!string.IsNullOrWhiteSpace(explicitDatabasePath))
            {
                if (IsTestExecutionMode())
                {
                    return AssertSafeTestFilePath(explicitDatabasePath, operationDescription);
                }

                return Path.GetFullPath(explicitDatabasePath.Trim());
            }

            return GetDatabaseFilePath();
        }

        /// <summary>
        /// Obtém o caminho completo do arquivo de banco de dados cobrapix.db com resolução estrita de modo de execução.
        /// Lança exceção caso esteja em modo de teste sem base descartável configurada (anti-fallback silencioso).
        /// </summary>
        public static string GetDatabaseFilePath()
        {
            string? overridePath = Environment.GetEnvironmentVariable(DatabasePathEnvVar);

            // Modo de Teste / Desenvolvimento / Homologação:
            if (IsTestExecutionMode())
            {
                if (string.IsNullOrWhiteSpace(overridePath))
                {
                    throw new InvalidOperationException(
                        $"[SEGURANÇA DE EXECUÇÃO] O aplicativo está em modo de teste ({ExecutionModeEnvVar}=TEST), mas nenhuma base descartável foi configurada em {DatabasePathEnvVar}.\n" +
                        $"Fallback silencioso para a base de produção é estritamente proibido.");
                }

                return AssertSafeTestFilePath(overridePath, "Resolução de base de dados de teste");
            }

            // Modo Produção Regular:
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return Path.GetFullPath(overridePath.Trim());
            }

            return GetDefaultProductionPath();
        }

        /// <summary>
        /// Obtém a string de conexão formatada para o SQLite com integridade referencial mandatória.
        /// </summary>
        public static string GetConnectionString()
        {
            return $"Data Source={GetDatabaseFilePath()};Foreign Keys=True;";
        }

        private static string ResolvePhysicalCanonicalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return Path.GetFullPath(path.Trim());
        }

        private static bool AreSamePhysicalFile(string path1, string path2)
        {
            if (string.IsNullOrWhiteSpace(path1) || string.IsNullOrWhiteSpace(path2)) return false;
            return string.Equals(Path.GetFullPath(path1.Trim()), Path.GetFullPath(path2.Trim()), StringComparison.OrdinalIgnoreCase);
        }
    }
}
