using System;
using System.Runtime.InteropServices;

namespace CobraPix.Updater
{
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
                ShowMessageBox(
                    IntPtr.Zero,
                    $"Parâmetros de atualização inválidos:\n{errorMessage}",
                    "Atualizador do CobraPix PC",
                    MB_OK | MB_ICONERROR);
                return 1;
            }

            var result = UpdaterEngine.ExecuteUpdate(options);

            if (!result.Success)
            {
                string failureDetails = result.RollbackPerformed
                    ? $"A atualização encontrou um erro e foi revertida para a versão anterior com segurança.\n\nDetalhes: {result.Message}"
                    : $"Não foi possível aplicar a atualização.\n\nDetalhes: {result.Message}";

                ShowMessageBox(
                    IntPtr.Zero,
                    failureDetails,
                    "Atualizador do CobraPix PC",
                    MB_OK | MB_ICONWARNING);

                return 1;
            }

            return 0;
        }
    }
}
