using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.Models.Credit;
using CobraPix_PC.Models.Credit.Dtos;

namespace CobraPix_PC.Services.Credit.Interfaces
{
    /// <summary>
    /// Serviço de domínio para gestão de contratos de crédito e desembolsos.
    /// Exclusivo para usuários CobraPix PRO.
    /// </summary>
    public interface ICreditContractService
    {
        Task<CreateContractResponseDto> CreateFixedContractAsync(CreateContractRequestDto req);
        Task<DisburseContractResponseDto> DisburseContractAsync(DisburseContractRequestDto req);
        Task<CancelContractResponseDto> CancelContractAsync(CancelContractRequestDto req);
        Task<CreditContract?> GetContractByIdAsync(string contractId);
        Task<List<CreditContract>> GetContractsByClientIdAsync(int clientId);
        Task<List<CreditContract>> GetAllContractsAsync();
    }
}
