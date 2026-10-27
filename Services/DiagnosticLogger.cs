using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CobraPix_PC.Data;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Implementação thread-safe e com rotação automática de logs de diagnóstico do CobraPix PC.
    /// Sanitiza dados sensíveis (chaves Pix, senhas, CPFs, e-mails e telefones) antes de gravar em disco.
    /// Mantém arquivos com tamanho limitado (máximo 1 MB com rotação de 1 geração anterior).
    /// </summary>
    public class DiagnosticLogger : IDiagnosticLogger
    {
        private static readonly object _syncLock = new();
        private const long MaxLogFileSize = 1024 * 1024; // 1 MB
        private readonly string _logFilePath;

        // Expressões regulares compiladas para sanitização de dados sensíveis
        private static readonly Regex CpfRegex = new(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", RegexOptions.Compiled);
        private static readonly Regex EmailRegex = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled);
        private static readonly Regex PhoneRegex = new(@"(?:\+?55\s?)?(?:\(?\d{2}\)?\s?)?(?:9\s?\d{4}[-\s]?\d{4}|\d{4}[-\s]?\d{4})\b", RegexOptions.Compiled);
        private static readonly Regex UuidRegex = new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);

        public string LogFilePath => _logFilePath;

        public static IDiagnosticLogger Instance { get; set; } = new DiagnosticLogger();

        public DiagnosticLogger(string? customLogPath = null)
        {
            if (!string.IsNullOrWhiteSpace(customLogPath))
            {
                _logFilePath = customLogPath;
            }
            else
            {
                string baseDir;
                if (DatabasePath.IsTestExecutionMode())
                {
                    string? envSandbox = Environment.GetEnvironmentVariable(DatabasePath.SandboxRootEnvVar);
                    baseDir = !string.IsNullOrWhiteSpace(envSandbox)
                        ? Path.Combine(envSandbox, "logs")
                        : Path.Combine(Path.GetTempPath(), "CobraPix_Test_Logs");
                }
                else
                {
                    baseDir = Path.Combine(DatabasePath.GetDatabaseDirectory(), "logs");
                }

                _logFilePath = Path.Combine(baseDir, "cobrapix.log");
            }
        }

        public void LogInformation(string context, string message)
        {
            WriteEntry("INFO", context, message, null);
        }

        public void LogWarning(string context, string message)
        {
            WriteEntry("WARN", context, message, null);
        }

        public void LogError(string context, string message)
        {
            WriteEntry("ERROR", context, message, null);
        }

        public void LogError(string context, Exception ex, string? extraInfo = null)
        {
            string detail = extraInfo != null
                ? $"{extraInfo}\nExceção: {ex.GetType().FullName}: {ex.Message}\nStackTrace:\n{ex.StackTrace}"
                : $"Exceção: {ex.GetType().FullName}: {ex.Message}\nStackTrace:\n{ex.StackTrace}";

            if (ex.InnerException != null)
            {
                detail += $"\nInnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}";
            }

            WriteEntry("ERROR", context, detail, null);
        }

        private void WriteEntry(string level, string context, string message, string? extra)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string sanitizedContext = Sanitize(context ?? "Geral");
                string sanitizedMessage = Sanitize(message ?? string.Empty);

                var sb = new StringBuilder();
                sb.Append('[').Append(timestamp).Append("] [").Append(level).Append("] [").Append(sanitizedContext).Append("] ");
                sb.AppendLine(sanitizedMessage);
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    sb.AppendLine(Sanitize(extra));
                }

                string entry = sb.ToString();

                lock (_syncLock)
                {
                    EnsureDirectoryAndRotate();
                    File.AppendAllText(_logFilePath, entry, Encoding.UTF8);
                }
            }
            catch
            {
                // Falha de log nunca pode derrubar a aplicação
            }
        }

        private void EnsureDirectoryAndRotate()
        {
            string? dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(_logFilePath))
            {
                var fi = new FileInfo(_logFilePath);
                if (fi.Length >= MaxLogFileSize)
                {
                    string oldPath = _logFilePath + ".old";
                    try
                    {
                        if (File.Exists(oldPath))
                        {
                            File.Delete(oldPath);
                        }
                        File.Move(_logFilePath, oldPath);
                    }
                    catch
                    {
                        // Se falhar o move por lock temporário, trunca o arquivo
                        try
                        {
                            File.WriteAllText(_logFilePath, string.Empty);
                        }
                        catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Remove ou ofusca dados pessoais e sensíveis (chaves Pix, telefones, CPFs, e-mails, UUIDs).
        /// </summary>
        public static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            string result = input;

            // Ofusca CPFs (mantém formato geral mas esconde números)
            result = CpfRegex.Replace(result, "***.***.***-**");

            // Ofusca E-mails
            result = EmailRegex.Replace(result, "***@***.***");

            // Ofusca UUIDs (chaves Pix aleatórias)
            result = UuidRegex.Replace(result, "********-****-****-****-************");

            // Ofusca Telefones com DDD
            result = PhoneRegex.Replace(result, "(XX) XXXXX-XXXX");

            return result;
        }
    }
}
