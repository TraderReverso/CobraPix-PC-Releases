using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Testing
{
    /// <summary>
    /// Gerenciador exclusivo do ambiente de teste controlado para atualizações.
    /// Opera estritamente dentro de %TEMP%\CobraPix\UpdateTest, gerando manifestos e pacotes de teste 1.0.2
    /// completamente isolados, sem tocar no banco de dados de produção ou na instalação real.
    /// </summary>
    public static class TestUpdateEnvironmentManager
    {
        public static string TestEnvironmentRoot => Path.Combine(Path.GetTempPath(), "CobraPix", "UpdateTest");
        public static string TestPackagesDirectory => Path.Combine(TestEnvironmentRoot, "Packages");
        public static string TestManifestPath => Path.Combine(TestEnvironmentRoot, "manifest_test_1.0.2.json");
        public static string TestPackagePath => Path.Combine(TestPackagesDirectory, "CobraPix-PC-1.0.2-TEST-win-x64.zip");
        public static string TestSimulatedAppDirectory => Path.Combine(TestEnvironmentRoot, "SimulatedApp_1.0.1");

        public record TestPackageInfo(
            string ManifestPath,
            string PackagePath,
            string Version,
            string Sha256,
            long PackageSizeBytes,
            string DownloadUrl,
            string ManifestJson);

        /// <summary>
        /// Prepara e gera o pacote de teste 1.0.2 e o respectivo manifesto em %TEMP%\CobraPix\UpdateTest.
        /// </summary>
        public static async Task<TestPackageInfo> PrepareTestEnvironmentAsync(string? sourceBinariesDir = null)
        {
            // 1. Garantir existência dos diretórios isolados
            Directory.CreateDirectory(TestEnvironmentRoot);
            Directory.CreateDirectory(TestPackagesDirectory);
            Directory.CreateDirectory(TestSimulatedAppDirectory);

            // 2. Localização dos binários de origem
            string baseDir = sourceBinariesDir ?? AppContext.BaseDirectory;
            if (!File.Exists(Path.Combine(baseDir, "CobraPix PC.dll")))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
                string candidateRelease = Path.Combine(projectRoot, "bin", "Release", "net10.0-windows");
                if (Directory.Exists(candidateRelease) && File.Exists(Path.Combine(candidateRelease, "CobraPix PC.dll")))
                {
                    baseDir = candidateRelease;
                }
            }

            // 3. Staging para o pacote 1.0.2
            string stagingDir = Path.Combine(TestEnvironmentRoot, $"Staging_1.0.2_{Guid.NewGuid():N}");
            if (Directory.Exists(stagingDir))
            {
                Directory.Delete(stagingDir, true);
            }
            Directory.CreateDirectory(stagingDir);

            try
            {
                // Copia executáveis e dependências essenciais
                string[] essentialFiles = new[]
                {
                    "CobraPix PC.exe",
                    "CobraPix PC.dll",
                    "CobraPix PC.runtimeconfig.json",
                    "CobraPix PC.deps.json",
                    "CobraPix.Updater.exe",
                    "CobraPix.Updater.dll",
                    "CobraPix.Updater.runtimeconfig.json",
                    "CobraPix.Updater.deps.json"
                };

                foreach (var fileName in essentialFiles)
                {
                    string srcFile = Path.Combine(baseDir, fileName);
                    string destFile = Path.Combine(stagingDir, fileName);
                    if (File.Exists(srcFile))
                    {
                        File.Copy(srcFile, destFile, true);
                    }
                    else if (fileName.EndsWith(".exe") || fileName.EndsWith(".dll"))
                    {
                        // Fallback de simulação caso arquivo específico não esteja presente
                        await File.WriteAllTextAsync(destFile, $"SIMULATED_{fileName.ToUpperInvariant()}_V1.0.2");
                    }
                }

                // Copia DLLs de dependências para garantir que o pacote seja auto-suficiente
                foreach (var dllFile in Directory.GetFiles(baseDir, "*.dll"))
                {
                    string dest = Path.Combine(stagingDir, Path.GetFileName(dllFile));
                    if (!File.Exists(dest))
                    {
                        File.Copy(dllFile, dest, true);
                    }
                }

                // Cria marcador explícito de versão de teste dentro do pacote
                var marker = new
                {
                    packageType = "CONTROLLED_TEST_PACKAGE",
                    simulatedVersion = "1.0.2",
                    baseVersion = "1.0.1",
                    isTestPackage = true,
                    generatedAtUtc = DateTime.UtcNow.ToString("o"),
                    purpose = "Ambiente controlado para validação isolada do CobraPix.Updater",
                    officialRelease = false
                };

                string markerJson = JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(stagingDir, "test_version_marker.json"), markerJson);

                // AUDITORIA DE SEGURANÇA: Garantir ausência total de bancos (.db), backups (.cpb) ou dados reais
                var filesInStaging = Directory.GetFiles(stagingDir, "*.*", SearchOption.AllDirectories);
                foreach (var f in filesInStaging)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is ".db" or ".db-wal" or ".db-shm" or ".cpb" or ".sqlite" or ".sqlite3")
                    {
                        throw new InvalidOperationException($"Violação de segurança: Arquivo de dados proibido encontrado no staging de teste: {f}");
                    }
                }

                // 4. Compactação para o ZIP oficial de teste
                if (File.Exists(TestPackagePath))
                {
                    File.Delete(TestPackagePath);
                }

                ZipFile.CreateFromDirectory(stagingDir, TestPackagePath, CompressionLevel.Optimal, false);

                // 5. Cálculo do SHA-256 real do pacote gerado
                byte[] pkgBytes = await File.ReadAllBytesAsync(TestPackagePath);
                using var sha = SHA256.Create();
                byte[] hashBytes = sha.ComputeHash(pkgBytes);
                string computedSha256 = Convert.ToHexString(hashBytes).ToUpperInvariant();

                // 6. Geração do manifesto de teste 1.0.2
                string fileUri = "file:///" + TestPackagePath.Replace('\\', '/');
                var manifestObj = new
                {
                    version = "1.0.2",
                    downloadUrl = fileUri,
                    releaseNotes = new[]
                    {
                        "Versão 1.0.2 de TESTE CONTROLADO",
                        "Ambiente isolado para validação do fluxo do atualizador",
                        "Validação de substituição segura de binários e reinício",
                        "Pacote não publicado no canal de produção"
                    },
                    sha256 = computedSha256,
                    mandatory = false
                };

                string manifestJson = JsonSerializer.Serialize(manifestObj, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(TestManifestPath, manifestJson);

                var fi = new FileInfo(TestPackagePath);

                return new TestPackageInfo(
                    ManifestPath: TestManifestPath,
                    PackagePath: TestPackagePath,
                    Version: "1.0.2",
                    Sha256: computedSha256,
                    PackageSizeBytes: fi.Length,
                    DownloadUrl: fileUri,
                    ManifestJson: manifestJson);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingDir))
                    {
                        Directory.Delete(stagingDir, true);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Ativa explicitamente o ambiente de teste apontando para o manifesto de teste em memória.
        /// </summary>
        public static void ActivateTestEnvironment(string? customManifestPath = null)
        {
            string path = customManifestPath ?? TestManifestPath;
            string url = path.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? path
                : "file:///" + Path.GetFullPath(path).Replace('\\', '/');

            UpdateService.SetTestManifestUrl(url);
        }

        /// <summary>
        /// Desativa o ambiente de teste e restaura o comportamento padrão de produção.
        /// </summary>
        public static void DeactivateTestEnvironment()
        {
            UpdateService.ClearTestManifestUrl();
        }
    }
}
