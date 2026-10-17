using System;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato para serviços de integração com WhatsApp.
    /// Responsável por validação e normalização de números, montagem de link com mensagem codificada e abertura externa.
    /// Suporta saudação dinâmica baseada no horário local e inserção automática de chave Pix configurável.
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
        /// Chave Pix configurada no sistema para inserção automática nas mensagens de cobrança.
        /// </summary>
        string? PixKey { get; set; }

        /// <summary>
        /// Retorna a saudação dinâmica conforme o horário do computador:
        /// 05:00 às 11:59: "Bom dia"
        /// 12:00 às 17:59: "Boa tarde"
        /// 18:00 às 04:59: "Boa noite"
        /// </summary>
        string GetGreeting(DateTime? dateTime = null);

        /// <summary>
        /// Gera mensagem padronizada para parcela em atraso, com saudação dinâmica, total pago, falta pagar e chave Pix opcional.
        /// </summary>
        string GenerateParcelaAtrasadaMessage(string clientName, string reference, DateTime dueDate, decimal amount, decimal? totalPaid = null, decimal? remainingBalance = null, string? pixKey = null, DateTime? referenceTime = null);

        /// <summary>
        /// Gera mensagem padronizada para mensalidade em atraso, com saudação dinâmica, total pago, falta pagar e chave Pix opcional.
        /// </summary>
        string GenerateMensalidadeAtrasadaMessage(string clientName, string competence, DateTime dueDate, decimal amount, decimal? totalPaid = null, decimal? remainingBalance = null, string? pixKey = null, DateTime? referenceTime = null);

        /// <summary>
        /// Gera mensagem preventiva padronizada para vencimento próximo, com saudação dinâmica, total pago, falta pagar e chave Pix opcional.
        /// </summary>
        string GenerateVencimentoProximoMessage(string clientName, string reference, DateTime dueDate, decimal amount, decimal? totalPaid = null, decimal? remainingBalance = null, string? pixKey = null, DateTime? referenceTime = null);

        /// <summary>
        /// Gera mensagem de confirmação de pagamento de parcela com valor e data/hora reais, total pago, saldo restante e forma de pagamento opcionais.
        /// </summary>
        string GeneratePagamentoConfirmadoMessage(string clientName, string reference, decimal amountPaid, DateTime paymentDate, decimal? totalPaid = null, decimal? remainingBalance = null, string? paymentMethod = null, DateTime? referenceTime = null);

        /// <summary>
        /// Gera mensagem de confirmação de pagamento de mensalidade com valor e data/hora reais, total pago, saldo restante e forma de pagamento opcionais.
        /// </summary>
        string GeneratePagamentoMensalidadeConfirmadoMessage(string clientName, string competence, decimal amountPaid, DateTime paymentDate, decimal? totalPaid = null, decimal? remainingBalance = null, string? paymentMethod = null, DateTime? referenceTime = null);

        /// <summary>
        /// Gera mensagem amigável de contato simples para o cliente, com saudação dinâmica.
        /// </summary>
        string GenerateSimpleContactMessage(string clientName, DateTime? referenceTime = null);
    }
}
