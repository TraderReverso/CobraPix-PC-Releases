using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CobraPix.Updater
{
    /// <summary>
    /// NOTA ARQUITETURAL: Este executável auxiliar é legado e NÃO é mais utilizado como o
    /// mecanismo principal de atualização do CobraPix PC (substituído pelo instalador comercial Inno Setup silencioso).
    /// Mantido temporariamente no projeto para testes de regressão e compatibilidade.
    /// </summary>
    internal static class Program
    {
        [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
        private static extern int ShowMessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

        private const uint MB_OK = 0x00000000;
        private const uint MB_ICONWARNING = 0x00000030;
        private const uint MB_ICONERROR = 0x00000010;

        [STAThread]
        private static int Main(string[] args)
        {
            if (!UpdaterOptions.TryParse(args, out var options, out string errorMessage))
            {
                try
                {
                    string logDir = Path.Combine(Path.GetTempPath(), "CobraPix");
                    Directory.CreateDirectory(logDir);
                    File.WriteAllText(Path.Combine(logDir, "updater_last_error.log"), errorMessage);
                }
                catch { }

                bool isSilent = args != null && Array.Exists(args, a => string.Equals(a.Trim(), "--silent", StringComparison.OrdinalIgnoreCase));
                if (!isSilent)
                {
                    ShowMessageBox(
                        IntPtr.Zero,
                        $"Parâmetros de atualização inválidos:\n{errorMessage}",
                        "Atualizador do CobraPix PC",
                        MB_OK | MB_ICONERROR);
                }
                return 1;
            }

            // Verifica se o diretório de instalação exige privilégios de Administrador (UAC) e se o processo ainda não está elevado
            if (UpdaterEngine.RequiresElevation(options.InstallDir) && !UpdaterEngine.IsRunningAsAdministrator())
            {
                try
                {
                    string currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CobraPix.Updater.exe");
                    string elevatedArgs = args != null && args.Length > 0
                        ? string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))
                        : string.Empty;

                    var elevatedPsi = new ProcessStartInfo
                    {
                        FileName = currentExe,
                        Arguments = elevatedArgs,
                        UseShellExecute = true,
                        Verb = "runas",
                        WorkingDirectory = options.InstallDir
                    };

                    using var elevatedProc = Process.Start(elevatedPsi);
                    if (elevatedProc != null)
                    {
                        elevatedProc.WaitForExit();
                        return elevatedProc.ExitCode;
                    }
                }
                catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 1223)
                {
                    const string cancelMsg = "A atualização requer privilégios de administrador para ser instalada em diretórios protegidos. A permissão foi cancelada pelo usuário.";
                    try
                    {
                        string logDir = Path.Combine(Path.GetTempPath(), "CobraPix");
                        Directory.CreateDirectory(logDir);
                        File.WriteAllText(Path.Combine(logDir, "updater_last_error.log"), cancelMsg);
                    }
                    catch { }

                    if (!options.Silent)
                    {
                        ShowMessageBox(IntPtr.Zero, cancelMsg, "Atualizador do CobraPix PC", MB_OK | MB_ICONWARNING);
                    }
                    return 1223;
                }
                catch (Exception ex)
                {
                    string errElevMsg = $"Falha ao solicitar privilégios de administrador (UAC): {ex.Message}";
                    try
                    {
                        string logDir = Path.Combine(Path.GetTempPath(), "CobraPix");
                        Directory.CreateDirectory(logDir);
                        File.WriteAllText(Path.Combine(logDir, "updater_last_error.log"), errElevMsg);
                    }
                    catch { }

                    if (!options.Silent)
                    {
                        ShowMessageBox(IntPtr.Zero, errElevMsg, "Atualizador do CobraPix PC", MB_OK | MB_ICONERROR);
                    }
                    return 1;
                }
            }

            var result = UpdaterEngine.ExecuteUpdate(options);

            if (!result.Success)
            {
                try
                {
                    string logDir = Path.Combine(Path.GetTempPath(), "CobraPix");
                    Directory.CreateDirectory(logDir);
                    File.WriteAllText(Path.Combine(logDir, "updater_last_error.log"), result.Message);
                }
                catch { }

                if (!options.Silent)
                {
                    string failureDetails = result.RollbackPerformed
                        ? $"A atualização encontrou um erro e foi revertida para a versão anterior com segurança.\n\nDetalhes: {result.Message}"
                        : $"Não foi possível aplicar a atualização.\n\nDetalhes: {result.Message}";

                    ShowMessageBox(
                        IntPtr.Zero,
                        failureDetails,
                        "Atualizador do CobraPix PC",
                        MB_OK | MB_ICONWARNING);
                }

                return 1;
            }

            return 0;
        }
    }
}
