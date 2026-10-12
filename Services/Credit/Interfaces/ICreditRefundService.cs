using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.Models.Credit;
using CobraPix_PC.Models.Credit.Dtos;

namespace CobraPix_PC.Services.Credit.Interfaces
{
    public interface ICreditRefundService
    {
        /// <summary>
        /// Solicita e autoriza o estorno integral de um pagamento ativo.
        /// Reabre a dívida (revertendo exatamente as alocações originais de juros e principal),
        /// cria o estorno em estado Authorized e pré-autoriza a devolução de caixa sem movimentação física.
        /// </summary>
        Task<AuthorizePaymentRefundResponseDto> AuthorizePaymentRefundAsync(AuthorizeRefundRequestDto request);

        /// <summary>
        /// Autoriza uma devolução de caixa para um pagamento, validando o limite elegível.
        /// Estado: Authorized. Não movimenta o caixa nesta fase.
        /// </summary>
        Task<AuthorizeDevolutionResponseDto> AuthorizeDevolutionAsync(AuthorizeDevolutionRequestDto request);

        /// <summary>
        /// Liquida uma devolução de caixa autorizada.
        /// Transita para Settled e registra exatamente uma saída financeira em CreditCashOutflows atomicamente.
        /// </summary>
        Task<SettleDevolutionResponseDto> SettleDevolutionAsync(SettleDevolutionRequestDto request);

        /// <summary>
        /// Cancela uma devolução autorizada antes de sua liquidação.
        /// Transita para Cancelled e impede liquidações futuras.
        /// Se a devolução estiver vinculada a um estorno de pagamento, restaura exatamente a dívida e as alocações.
        /// </summary>
        Task<CancelDevolutionResponseDto> CancelDevolutionAsync(CancelDevolutionRequestDto request);

        /// <summary>
        /// Cancela a autorização de estorno antes de sua liquidação, restaurando integralmente a dívida e as alocações do pagamento.
        /// </summary>
        Task<CancelRefundAuthResponseDto> CancelRefundAuthAsync(CancelRefundAuthRequestDto request);

        Task<CreditPaymentRefund?> GetRefundByPaymentIdAsync(string paymentId);
        Task<CreditDevolution?> GetDevolutionByIdAsync(string devolutionId);
        Task<List<CreditDevolution>> GetDevolutionsByPaymentIdAsync(string paymentId);
        Task<List<CreditCashOutflow>> GetCashOutflowsByContractAsync(string contractId);
        Task<List<CreditPaymentRefund>> GetAllRefundsAsync();
        Task<List<CreditDevolution>> GetAllDevolutionsAsync();
    }
}
