using System;
using System.IO;

namespace CobraPix.Updater
{
    /// <summary>
    /// Representa e valida os argumentos passados para o CobraPix.Updater.
    /// </summary>
    public class UpdaterOptions
    {
        public int Pid { get; set; }
        public string PackagePath { get; set; } = string.Empty;
        public string InstallDir { get; set; } = string.Empty;
        public string MainExeName { get; set; } = "CobraPix PC.exe";
        public bool SkipWait { get; set; }
        public bool NoRestart { get; set; }
        public bool Silent { get; set; }
        public int TimeoutSeconds { get; set; } = 25;

        public static bool TryParse(string[] args, out UpdaterOptions options, out string errorMessage)
        {
            options = new UpdaterOptions();
            errorMessage = string.Empty;

            if (args == null || args.Length == 0)
            {
                errorMessage = "Nenhum argumento foi fornecido ao atualizador.";
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].Trim();

                if (arg.Equals("--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out int pidVal) && pidVal > 0)
                    {
                        options.Pid = pidVal;
                    }
                    else
                    {
                        errorMessage = "O valor fornecido para --pid é inválido.";
                        return false;
                    }
                }
                else if (arg.Equals("--package", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.PackagePath = args[++i].Trim('"', ' ');
                }
                else if (arg.Equals("--install-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.InstallDir = args[++i].Trim('"', ' ');
                }
                else if (arg.Equals("--exe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.MainExeName = args[++i].Trim('"', ' ');
                }
                else if (arg.Equals("--skip-wait", StringComparison.OrdinalIgnoreCase))
                {
                    options.SkipWait = true;
                }
                else if (arg.Equals("--no-restart", StringComparison.OrdinalIgnoreCase))
                {
                    options.NoRestart = true;
                }
                else if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                {
                    options.Silent = true;
                }
                else if (arg.Equals("--timeout-seconds", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out int tVal) && tVal > 0)
                    {
                        options.TimeoutSeconds = tVal;
                    }
                }
            }

            return Validate(options, out errorMessage);
        }

        public static bool Validate(UpdaterOptions options, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (options == null)
            {
                errorMessage = "Opções de atualização nulas.";
                return false;
            }

            // 1. Validação do pacote
            if (string.IsNullOrWhiteSpace(options.PackagePath))
            {
                errorMessage = "O caminho do pacote de atualização (--package) é obrigatório.";
                return false;
            }

            if (!Path.IsPathRooted(options.PackagePath))
            {
                errorMessage = "O caminho do pacote deve ser absoluto.";
                return false;
            }

            if (!File.Exists(options.PackagePath))
            {
                errorMessage = $"O arquivo de pacote especificado não foi encontrado: '{options.PackagePath}'.";
                return false;
            }

            if (!options.PackagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "O pacote de atualização deve ser um arquivo com extensão .zip.";
                return false;
            }

            // 2. Validação do diretório de instalação
            if (string.IsNullOrWhiteSpace(options.InstallDir))
            {
                errorMessage = "O diretório de instalação (--install-dir) é obrigatório.";
                return false;
            }

            if (!Path.IsPathRooted(options.InstallDir))
            {
                errorMessage = "O diretório de instalação deve ser um caminho absoluto.";
                return false;
            }

            if (!Directory.Exists(options.InstallDir))
            {
                errorMessage = $"O diretório de instalação não existe: '{options.InstallDir}'.";
                return false;
            }

            // 3. REGRA CRÍTICA DE SEGURANÇA: Proibição estrita contra o diretório de dados do usuário
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string forbiddenUserDataDir = Path.Combine(localAppData, "CobraPix");

            string fullInstall = Path.GetFullPath(options.InstallDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullForbidden = Path.GetFullPath(forbiddenUserDataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (fullInstall.Equals(fullForbidden, StringComparison.OrdinalIgnoreCase) ||
                fullInstall.StartsWith(fullForbidden + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Violação de segurança: O diretório de instalação não pode ser a pasta de dados do usuário ('{forbiddenUserDataDir}').";
                return false;
            }

            // 4. Validação do executável principal
            if (string.IsNullOrWhiteSpace(options.MainExeName))
            {
                errorMessage = "O nome do executável principal (--exe) é obrigatório.";
                return false;
            }

            if (!options.MainExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "O executável principal deve possuir a extensão .exe.";
                return false;
            }

            return true;
        }
    }
}
