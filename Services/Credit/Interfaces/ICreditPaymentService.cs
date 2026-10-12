using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.Models.Credit;
using CobraPix_PC.Models.Credit.Dtos;

namespace CobraPix_PC.Services.Credit.Interfaces
{
    /// <summary>
    /// Contrato de serviço para registro de pagamentos e amortizações com saldo excedente.
    /// Aplica estritamente a Regra A: juros primeiro, principal depois.
    /// Exclusivo para usuários CobraPix PRO.
    /// </summary>
    public interface ICreditPaymentService
    {
        Task<PaymentResultDto> RegisterPaymentAsync(PaymentRequestDto req);
        Task<ConsumeSurplusResponseDto> ConsumeSurplusAsync(ConsumeSurplusRequestDto req);
        Task<ReverseSurplusConsumptionResponseDto> ReverseSurplusConsumptionAsync(ReverseSurplusConsumptionRequestDto req);
        Task<List<CreditSurplusBalanceDto>> GetSurplusBalancesByContractAsync(string contractId);
        Task<List<CreditPaymentDto>> GetPaymentsByContractAsync(string contractId);
        Task<PaymentPreviewDto> PreviewPaymentDistributionAsync(string contractId, long amountCents);
        Task<List<CreditPayment>> GetAllPaymentsWithDetailsAsync();
        Task<List<CreditSurplusBalance>> GetAllSurplusBalancesWithDetailsAsync();
    }
}
