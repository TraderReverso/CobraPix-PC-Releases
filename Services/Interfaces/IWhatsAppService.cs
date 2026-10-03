using System;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato para serviços de integração com WhatsApp.
    /// Responsável por validação e normalização de números, montagem de link com mensagem codificada e abertura externa.
    /// </summary>
    public interface IWhatsAppService
    {
        /// <summary>
        /// Determina se o número informado é válido para contato via WhatsApp.
        /// </summary>
        bool IsValidPhoneNumber(string? phone);

        /// <summary>
        /// Normaliza o número removendo caracteres não numéricos e aplicando o DDI brasileiro (55)
        /// quando ausente, sem duplicar caso já esteja presente.
        /// Retorna null para números nulos, vazios ou inválidos.
        /// </summary>
        string? NormalizePhoneNumber(string? phone);

        /// <summary>
        /// Monta o link wa.me com número normalizado e mensagem codificada em formato URL.
        /// Retorna null caso o telefone seja inválido.
        /// </summary>
        string? BuildWhatsAppUrl(string? phone, string message);

        /// <summary>
        /// Abre o link no aplicativo ou navegador padrão do Windows.
        /// Retorna true em caso de sucesso ou false caso não seja possível abrir.
        /// Nunca lança exceção não tratada para a interface.
        /// </summary>
        bool OpenWhatsApp(string? phone, string message);

        /// <summary>
        /// Gera mensagem padronizada para parcela em atraso.
        /// </summary>
        string GenerateParcelaAtrasadaMessage(string clientName, string reference, DateTime dueDate, decimal remainingBalance);

        /// <summary>
        /// Gera mensagem padronizada para mensalidade em atraso.
        /// </summary>
        string GenerateMensalidadeAtrasadaMessage(string clientName, string competence, DateTime dueDate, decimal amount);

        /// <summary>
        /// Gera mensagem preventiva padronizada para vencimento próximo.
        /// </summary>
        string GenerateVencimentoProximoMessage(string clientName, string reference, DateTime dueDate, decimal amount);

        /// <summary>
        /// Gera mensagem amigável de contato simples para o cliente.
        /// </summary>
        string GenerateSimpleContactMessage(string clientName);
    }
}
