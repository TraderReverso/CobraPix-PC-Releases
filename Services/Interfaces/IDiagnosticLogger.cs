using System;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato para registro seguro e rotativo de diagnósticos e erros do CobraPix PC.
    /// Garante sanitização de dados sensíveis (sem chaves Pix, senhas ou dados financeiros em texto aberto).
    /// </summary>
    public interface IDiagnosticLogger
    {
        /// <summary>
        /// Obtém o caminho do arquivo de log ativo.
        /// </summary>
        string LogFilePath { get; }

        /// <summary>
        /// Registra uma mensagem informativa de diagnóstico.
        /// </summary>
        void LogInformation(string context, string message);

        /// <summary>
        /// Registra um aviso no log.
        /// </summary>
        void LogWarning(string context, string message);

        /// <summary>
        /// Registra um erro com contexto técnico e mensagem.
        /// </summary>
        void LogError(string context, string message);

        /// <summary>
        /// Registra uma exceção com contexto técnico e detalhes para suporte.
        /// </summary>
        void LogError(string context, Exception ex, string? extraInfo = null);
    }
}
