using System;
using System.Threading.Tasks;
using CobraPix_PC.Data;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato de serviço para gestão comercial do plano (FREE / PRO) e controle centralizado do limite de clientes.
    /// Totalmente offline-first e seguro.
    /// </summary>
    public interface IProService
    {
        /// <summary>
        /// Obtém o plano comercial atualmente ativo (Free ou Pro).
        /// </summary>
        Task<PlanType> GetCurrentPlanAsync();

        /// <summary>
        /// Verifica se o sistema está em modo PRO.
        /// </summary>
        Task<bool> IsProAsync();

        /// <summary>
        /// Retorna a quantidade de clientes ativos (não arquivados e não quitados).
        /// </summary>
        Task<int> GetActiveClientsCountAsync();

        /// <summary>
        /// Retorna o relatório completo de status do limite de clientes.
        /// </summary>
        Task<ClientLimitStatus> CheckClientLimitAsync();

        /// <summary>
        /// Valida se um novo cliente pode ser cadastrado.
        /// </summary>
        Task<bool> CanCreateClientAsync();

        /// <summary>
        /// Valida se um cliente arquivado pode ser reativado sem violar o limite do plano.
        /// </summary>
        Task<bool> CanReactivateClientAsync(int clientId);

        /// <summary>
        /// Valida de forma centralizada se uma importação de clientes ativos
        /// pode ser admitida sem violar a quota comercial (activeCurrent + activeIncoming <= 10 no plano FREE).
        /// </summary>
        Task<ClientQuotaValidationResult> ValidateImportQuotaAsync(int incomingActiveCount, CobraPixDbContext? targetContext = null);

        /// <summary>
        /// Obtém a URL oficial configurada para assinatura do plano PRO Mensal.
        /// </summary>
        Task<string?> GetMonthlySubscriptionUrlAsync();

        /// <summary>
        /// Obtém a URL oficial configurada para assinatura do plano PRO Anual.
        /// </summary>
        Task<string?> GetAnnualSubscriptionUrlAsync();

        /// <summary>
        /// Configura centralizadamente as URLs de assinatura do Mercado Pago.
        /// </summary>
        Task SetSubscriptionUrlsAsync(string? monthlyUrl, string? annualUrl);

        /// <summary>
        /// Altera o plano de forma administrativa/testável (isolada, para testes e expansão futura).
        /// </summary>
        Task SetPlanAsync(PlanType plan, string source = "Manual");

        /// <summary>
        /// Obtém o código Pix Copia e Cola oficial fixo para o plano PRO Mensal (R$ 9,90).
        /// </summary>
        string GetMonthlyPixPayload();

        /// <summary>
        /// Obtém o código Pix Copia e Cola oficial fixo para o plano PRO Anual (R$ 89,90).
        /// </summary>
        string GetAnnualPixPayload();

        /// <summary>
        /// Valor oficial do plano PRO Mensal (R$ 9,90).
        /// </summary>
        decimal MonthlyPrice { get; }

        /// <summary>
        /// Valor oficial do plano PRO Anual (R$ 89,90).
        /// </summary>
        decimal AnnualPrice { get; }

        /// <summary>
        /// Obtém o status e detalhes da licença criptográfica PRO atualmente instalada.
        /// </summary>
        Task<LicenseValidationResult> GetCurrentLicenseStatusAsync();

        /// <summary>
        /// Ativa uma chave de licença PRO no aplicativo.
        /// </summary>
        Task<LicenseValidationResult> ActivateLicenseAsync(string rawKey);

        /// <summary>
        /// Número oficial do WhatsApp de suporte comercial para recebimento de comprovantes.
        /// </summary>
        string SupportPhoneNumber { get; }

        /// <summary>
        /// Mensagem padrão para abertura de suporte comercial no WhatsApp.
        /// </summary>
        string SupportWhatsAppMessage { get; }

        /// <summary>
        /// Serviço de licenças criptográficas.
        /// </summary>
        ILicenseService LicenseService { get; }

        /// <summary>
        /// Evento disparado quando o plano comercial é alterado.
        /// </summary>
        event EventHandler? PlanChanged;
    }
}
