using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Threading;

namespace CobraPix.Updater
{
    /// <summary>
    /// NOTA ARQUITETURAL: Mecanismo legado baseado em substituição direta de arquivos.
    /// O fluxo comercial oficial utiliza o instalador Inno Setup silencioso.
    /// Mantido para testes e retrocompatibilidade controlada.
    /// </summary>
    public class UpdaterEngine
    {
        public static bool WaitForProcessExit(int pid, TimeSpan timeout)
        {
            if (pid <= 0) return true;

            try
            {
                using var process = Process.GetProcessById(pid);
                return process.WaitForExit((int)timeout.TotalMilliseconds);
            }
            catch (ArgumentException)
            {
                // O processo já encerrou
                return true;
            }
            catch (InvalidOperationException)
            {
                // O processo já encerrou
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdaterEngine] Exceção ao aguardar processo PID {pid}: {ex.Message}");
                return false;
            }
        }

        public static void ValidateZipArchive(string packagePath, string stagingDir)
        {
            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException("Arquivo de pacote não encontrado.", packagePath);
            }

            var fi = new FileInfo(packagePath);
            if (fi.Length == 0)
            {
                throw new InvalidOperationException("O arquivo de pacote está vazio (0 bytes).");
            }

            string fullStagingPath = Path.GetFullPath(stagingDir);
            if (!fullStagingPath.EndsWith(Path.DirectorySeparatorChar))
            {
                fullStagingPath += Path.DirectorySeparatorChar;
            }

            using var archive = ZipFile.OpenRead(packagePath);
            if (archive.Entries.Count == 0)
            {
                throw new InvalidOperationException("O pacote ZIP não contém nenhum arquivo.");
            }

            foreach (var entry in archive.Entries)
            {
                string entryName = entry.FullName;

                // 1. Bloqueia tentativas de Path Traversal
                if (entryName.Contains("..") || Path.IsPathRooted(entryName))
                {
                    throw new SecurityException($"Tentativa de Path Traversal detectada no pacote ZIP: '{entryName}'.");
                }

                // 2. Garante que o arquivo extraído reside estritamente sob o diretório staging
                string destPath = Path.GetFullPath(Path.Combine(stagingDir, entryName));
                if (!destPath.StartsWith(fullStagingPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Entrada ZIP tenta escapar do diretório de extração: '{entryName}'.");
                }

                // 3. Bloqueia inclusão de extensões de banco/dados no pacote
                string ext = Path.GetExtension(entryName).ToLowerInvariant();
                if (ext is ".db" or ".db-wal" or ".db-shm" or ".cpb")
                {
                    throw new SecurityException($"Arquivo de dados proibido encontrado dentro do pacote: '{entryName}'.");
                }
            }
        }

        public static void ExtractToStaging(string packagePath, string stagingDir)
        {
            if (Directory.Exists(stagingDir))
            {
                Directory.Delete(stagingDir, true);
            }
            Directory.CreateDirectory(stagingDir);

            // Valida estrutura e segurança do zip antes de extrair qualquer arquivo
            ValidateZipArchive(packagePath, stagingDir);

            using var archive = ZipFile.OpenRead(packagePath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) && (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')))
                {
                    // Diretório
                    string dirPath = Path.Combine(stagingDir, entry.FullName);
                    Directory.CreateDirectory(dirPath);
                    continue;
                }

                string destinationPath = Path.Combine(stagingDir, entry.FullName);
                string? parentDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }

        public static void CreateApplicationBackup(string installDir, string backupDir)
        {
            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, true);
            }
            Directory.CreateDirectory(backupDir);

            var dirInfo = new DirectoryInfo(installDir);
            CopyDirectoryRecursive(dirInfo, new DirectoryInfo(backupDir));
        }

        public static void ApplyUpdateFromStaging(string stagingDir, string installDir)
        {
            var stagingInfo = new DirectoryInfo(stagingDir);
            var installInfo = new DirectoryInfo(installDir);

            CopyDirectoryRecursive(stagingInfo, installInfo);
        }

        public static void Rollback(string backupDir, string installDir)
        {
            if (!Directory.Exists(backupDir)) return;

            try
            {
                var backupInfo = new DirectoryInfo(backupDir);
                var installInfo = new DirectoryInfo(installDir);

                CopyDirectoryRecursive(backupInfo, installInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdaterEngine] Erro crítico durante rollback: {ex.Message}");
            }
        }

        public static bool VerifyInstallation(string installDir, string mainExeName)
        {
            string mainExePath = Path.Combine(installDir, mainExeName);
            if (!File.Exists(mainExePath)) return false;

            var fi = new FileInfo(mainExePath);
            return fi.Length > 0;
        }

        public static UpdaterResult ExecuteUpdate(UpdaterOptions options)
        {
            if (!UpdaterOptions.Validate(options, out string validationError))
            {
                return UpdaterResult.Fail(validationError);
            }

            // 1. Aguarda encerramento do processo anterior
            if (!options.SkipWait && options.Pid > 0)
            {
                bool exited = WaitForProcessExit(options.Pid, TimeSpan.FromSeconds(options.TimeoutSeconds));
                if (!exited)
                {
                    return UpdaterResult.Fail($"Timeout esgotado ({options.TimeoutSeconds}s): O processo principal (PID {options.Pid}) não encerrou a tempo. Atualização abortada com segurança.");
                }
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string updatesBase = Path.Combine(Path.GetTempPath(), "CobraPix", "Updates");
            string stagingDir = Path.Combine(updatesBase, "staging", timestamp);
            string backupDir = Path.Combine(updatesBase, "backup", timestamp);

            bool backupCreated = false;

            try
            {
                // 2. Extração segura para Staging temporário
                ExtractToStaging(options.PackagePath, stagingDir);

                // 3. Backup dos arquivos atuais da aplicação
                CreateApplicationBackup(options.InstallDir, backupDir);
                backupCreated = true;

                // 4. Substituição controlada
                ApplyUpdateFromStaging(stagingDir, options.InstallDir);

                // 5. Verificação pós-instalação
                bool verified = VerifyInstallation(options.InstallDir, options.MainExeName);
                if (!verified)
                {
                    throw new InvalidOperationException("Falha na verificação pós-instalação: executável principal ausente ou corrompido.");
                }

                // 6. Sucesso: limpa staging e remove backup antigo
                TryDeleteDirectory(stagingDir);

                // 7. Reinício da aplicação
                string finalExePath = Path.Combine(options.InstallDir, options.MainExeName);
                if (!options.NoRestart && File.Exists(finalExePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = finalExePath,
                        WorkingDirectory = options.InstallDir,
                        UseShellExecute = true
                    });
                }

                return UpdaterResult.Ok("Atualização aplicada com sucesso!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdaterEngine] Falha durante atualização: {ex.Message}");

                if (backupCreated)
                {
                    Rollback(backupDir, options.InstallDir);

                    // Se solicitado e o executável anterior existir, reinicia a versão anterior após rollback
                    string originalExePath = Path.Combine(options.InstallDir, options.MainExeName);
                    if (!options.NoRestart && File.Exists(originalExePath))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = originalExePath,
                                WorkingDirectory = options.InstallDir,
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }

                    return UpdaterResult.Fail($"A atualização falhou ({ex.Message}), mas foi revertida com sucesso (rollback concluído).", rollbackPerformed: true, ex: ex);
                }

                return UpdaterResult.Fail($"A atualização falhou antes do backup: {ex.Message}", rollbackPerformed: false, ex: ex);
            }
            finally
            {
                TryDeleteDirectory(stagingDir);
            }
        }

        private static void CopyDirectoryRecursive(DirectoryInfo source, DirectoryInfo target)
        {
            if (!target.Exists)
            {
                target.Create();
            }

            foreach (var file in source.GetFiles())
            {
                string ext = file.Extension.ToLowerInvariant();
                // NUNCA copia arquivos de banco de dados ou backups de dados
                if (ext is ".db" or ".db-shm" or ".db-wal" or ".cpb")
                {
                    continue;
                }

                string targetFilePath = Path.Combine(target.FullName, file.Name);
                SafeCopyFile(file, targetFilePath);
            }

            foreach (var subDir in source.GetDirectories())
            {
                string subDirName = subDir.Name.ToLowerInvariant();
                // Ignora pastas temporárias ou de backup se estiverem aninhadas
                if (subDirName is "updates" or "backup" or "staging")
                {
                    continue;
                }

                var nextTargetSubDir = target.CreateSubdirectory(subDir.Name);
                CopyDirectoryRecursive(subDir, nextTargetSubDir);
            }
        }

        /// <summary>
        /// Verifica se o processo atual está executando com privilégios de Administrador.
        /// </summary>
        public static bool IsRunningAsAdministrator()
        {
            if (!OperatingSystem.IsWindows()) return true;
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Determina se o diretório alvo exige privilégios de Administrador (UAC) para substituição de arquivos.
        /// </summary>
        public static bool RequiresElevation(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;

            try
            {
                string normalized = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(programFiles))
                {
                    string normPF = Path.GetFullPath(programFiles).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (normalized.Equals(normPF, StringComparison.OrdinalIgnoreCase) ||
                        normalized.StartsWith(normPF + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrEmpty(programFilesX86))
                {
                    string normPFX86 = Path.GetFullPath(programFilesX86).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (normalized.Equals(normPFX86, StringComparison.OrdinalIgnoreCase) ||
                        normalized.StartsWith(normPFX86 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (!string.IsNullOrEmpty(systemDir))
                {
                    string normSys = Path.GetFullPath(systemDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (normalized.Equals(normSys, StringComparison.OrdinalIgnoreCase) ||
                        normalized.StartsWith(normSys + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                if (Directory.Exists(normalized))
                {
                    string probePath = Path.Combine(normalized, $".uac_probe_{Guid.NewGuid():N}.tmp");
                    try
                    {
                        using (var fs = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            fs.WriteByte(0);
                        }
                        File.Delete(probePath);
                        return false;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            catch
            {
                // Fallback seguro
            }

            return false;
        }

        private static void SafeCopyFile(FileInfo file, string targetFilePath)
        {
            try
            {
                file.CopyTo(targetFilePath, overwrite: true);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new UnauthorizedAccessException($"Acesso negado ao gravar '{Path.GetFileName(targetFilePath)}' em '{Path.GetDirectoryName(targetFilePath)}'. O diretório de instalação exige permissões de Administrador (UAC).", ex);
            }
            catch (IOException)
            {
                // Se o arquivo estiver em uso (ex: o próprio executável ou DLL do atualizador em execução no Windows),
                // tenta a estratégia padrão de renomear o arquivo em execução para .old e gravar o novo.
                string oldFilePath = targetFilePath + ".old";
                try
                {
                    if (File.Exists(oldFilePath))
                    {
                        File.Delete(oldFilePath);
                    }
                }
                catch { }

                try
                {
                    File.Move(targetFilePath, oldFilePath);
                    file.CopyTo(targetFilePath, overwrite: true);
                }
                catch
                {
                    // Se o arquivo for do próprio atualizador e já estiver em execução/presente no destino,
                    // ignora a substituição para permitir a atualização bem-sucedida de todos os demais arquivos da aplicação.
                    if (file.Name.StartsWith("CobraPix.Updater", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                    throw;
                }
            }
        }

        private static void TryDeleteDirectory(string dirPath)
        {
            try
            {
                if (Directory.Exists(dirPath))
                {
                    Directory.Delete(dirPath, true);
                }
            }
            catch
            {
                // Silencioso em caso de arquivo temporário
            }
        }
    }
}
