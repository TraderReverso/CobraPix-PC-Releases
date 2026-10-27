using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models.Credit;
using CobraPix_PC.Models.Credit.Dtos;
using CobraPix_PC.Services.Credit.Interfaces;
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services.Credit
{
    /// <summary>
    /// Implementação oficial do serviço de contratos e desembolsos de crédito.
    /// Exclusivo para usuários PRO. Garante cálculo de parcelas em centavos exatos,
    /// vencimentos em dias civis sem deslocamento e proibição de cancelamento com pagamentos históricos.
    /// </summary>
    public class CreditContractService : ICreditContractService
    {
        private readonly CobraPixDbContext _dbContext;
        private readonly IProService _proService;
        private readonly ICreditTransactionExecutor _transactionExecutor;

        public CreditContractService(
            CobraPixDbContext dbContext,
            IProService proService,
            ICreditTransactionExecutor? transactionExecutor = null)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _proService = proService ?? throw new ArgumentNullException(nameof(proService));
            _transactionExecutor = transactionExecutor ?? new CreditTransactionExecutor(dbContext);
        }

        public async Task<CreateContractResponseDto> CreateFixedContractAsync(CreateContractRequestDto req)
        {
            // 1. Validação prévia de licença PRO (Fail-fast antes de qualquer lock)
            if (!await _proService.IsProAsync())
            {
                throw new ProLicenseRequiredException("O módulo Crédito & Giro é exclusivo do CobraPix PRO.");
            }

            // 2. Validações preliminares do payload
            if (req.PrincipalAmountCents <= 0)
                throw new ArgumentException("O valor principal deve ser maior que zero.", nameof(req.PrincipalAmountCents));

            if (req.TotalInterestCents < 0)
                throw new ArgumentException("Os juros totais não podem ser negativos.", nameof(req.TotalInterestCents));

            if (req.InstallmentsCount <= 0 || req.InstallmentsCount > 360)
                throw new ArgumentException("A quantidade de parcelas deve estar entre 1 e 360.", nameof(req.InstallmentsCount));

            if (!Enum.IsDefined(typeof(CreditPeriodicity), req.Periodicity))
                throw new ArgumentException("Periodicidade de contrato inválida.", nameof(req.Periodicity));

            if (req.Periodicity == CreditPeriodicity.CustomDays)
            {
                if (!req.CustomIntervalDays.HasValue || req.CustomIntervalDays.Value < 1 || req.CustomIntervalDays.Value > 365)
                    throw new ArgumentException("Para periodicidade personalizada, o intervalo de dias deve ser entre 1 e 365.", nameof(req.CustomIntervalDays));
            }
            else
            {
                req.CustomIntervalDays = null;
            }

            if (string.IsNullOrWhiteSpace(req.ContractNumber))
                throw new ArgumentException("O número do contrato é obrigatório.", nameof(req.ContractNumber));

            string canonicalPayload = req.Canonicalize();

            return await _transactionExecutor.ExecuteTransactionalAsync(
                "CreateContract",
                req.IdempotencyKey,
                canonicalPayload,
                async ctx =>
                {
                    // Validação de existência do cliente sob lock
                    var client = await ctx.Clients.FindAsync(req.ClientId);
                    if (client == null)
                        throw new InvalidOperationException($"Cliente #{req.ClientId} não encontrado.");

                    if (client.IsArchived)
                        throw new InvalidOperationException($"Cliente #{req.ClientId} está arquivado e não pode receber contratos de crédito.");

                    // Unicidade do número do contrato
                    bool existsNumber = await ctx.CreditContracts.AnyAsync(c => c.ContractNumber == req.ContractNumber);
                    if (existsNumber)
                        throw new InvalidOperationException($"Já existe um contrato cadastrado com o número '{req.ContractNumber}'.");

                    long totalAmountCents = req.PrincipalAmountCents + req.TotalInterestCents;

                    var contract = new CreditContract
                    {
                        Id = Guid.NewGuid().ToString(),
                        ClientId = req.ClientId,
                        ContractNumber = req.ContractNumber.Trim(),
                        ContractType = CreditContractType.FixedInstallments,
                        Periodicity = req.Periodicity,
                        CustomIntervalDays = req.Periodicity == CreditPeriodicity.CustomDays ? req.CustomIntervalDays : null,
                        Status = CreditContractStatus.Draft,
                        DisbursedPrincipalCents = req.PrincipalAmountCents,
                        TotalNominalInterestCents = req.TotalInterestCents,
                        TotalAmountCents = totalAmountCents,
                        ContractDate = req.ContractDate,
                        Notes = req.Notes?.Trim(),
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    ctx.CreditContracts.Add(contract);

                    // Decomposição exata das parcelas em centavos inteiros com resíduos determinísticos
                    var partitions = DistributeInstallments(req.PrincipalAmountCents, req.TotalInterestCents, req.InstallmentsCount);

                    for (int i = 1; i <= req.InstallmentsCount; i++)
                    {
                        var (instPrincipal, instInterest, instTotal) = partitions[i - 1];
                        DateTime dueDate = CalculateDueDate(req.FirstDueDate, req.Periodicity, i, req.CustomIntervalDays);

                        var installment = new CreditInstallment
                        {
                            Id = Guid.NewGuid().ToString(),
                            ContractId = contract.Id,
                            InstallmentNumber = i,
                            DueDate = dueDate,
                            NominalPrincipalCents = instPrincipal,
                            NominalInterestCents = instInterest,
                            TotalAmountCents = instTotal,
                            PaidAmountCents = 0,
                            Status = CreditInstallmentStatus.Pending,
                            CreatedAtUtc = DateTime.UtcNow
                        };
                        ctx.CreditInstallments.Add(installment);
                    }

                    // Auditoria da criação
                    ctx.CreditTransactionAudits.Add(new CreditTransactionAudit
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = contract.Id,
                        OperationType = "ContractCreation",
                        DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            contract.ContractNumber,
                            req.PrincipalAmountCents,
                            req.TotalInterestCents,
                            req.InstallmentsCount,
                            Periodicity = contract.Periodicity.ToString(),
                            contract.CustomIntervalDays
                        }),
                        CreatedAtUtc = DateTime.UtcNow
                    });

                    return new CreateContractResponseDto
                    {
                        ContractId = contract.Id,
                        ContractNumber = contract.ContractNumber,
                        Status = contract.Status,
                        Periodicity = contract.Periodicity,
                        CustomIntervalDays = contract.CustomIntervalDays,
                        TotalAmountCents = contract.TotalAmountCents,
                        InstallmentsGenerated = req.InstallmentsCount
                    };
                });
        }

        /// <summary>
        /// Calcula determinística e independentemente a data de vencimento de uma parcela.
        /// Preserva dias civis de calendário, evita drift cumulativo e valida limites.
        /// </summary>
        public static DateTime CalculateDueDate(
            DateTime firstDueDate,
            CreditPeriodicity periodicity,
            int installmentNumber,
            int? customIntervalDays = null)
        {
            if (installmentNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(installmentNumber), "O número da parcela deve ser maior ou igual a 1.");

            int offset = installmentNumber - 1;
            if (offset == 0) return firstDueDate.Date;

            switch (periodicity)
            {
                case CreditPeriodicity.Daily:
                    return firstDueDate.Date.AddDays(offset);

                case CreditPeriodicity.Weekly:
                    return firstDueDate.Date.AddDays(offset * 7);

                case CreditPeriodicity.Biweekly:
                    // Intervalo estritamente fixo de 15 dias civis corridos
                    return firstDueDate.Date.AddDays(offset * 15);

                case CreditPeriodicity.Monthly:
                    // Baseado na data inicial original (D1) com AddMonths determinístico (sem drift de fim de mês)
                    return firstDueDate.Date.AddMonths(offset);

                case CreditPeriodicity.CustomDays:
                    if (!customIntervalDays.HasValue || customIntervalDays.Value < 1 || customIntervalDays.Value > 365)
                    {
                        throw new ArgumentException("Intervalo personalizado deve estar entre 1 e 365 dias.", nameof(customIntervalDays));
                    }
                    return firstDueDate.Date.AddDays(offset * customIntervalDays.Value);

                default:
                    throw new ArgumentOutOfRangeException(nameof(periodicity), $"Periodicidade '{periodicity}' não suportada.");
            }
        }

        /// <summary>
        /// Distribui determinística e equitativamente o principal e os juros totais entre as parcelas em centavos inteiros.
        /// Garante:
        /// 1. Soma exata do principal: sum(P_i) == principalAmountCents
        /// 2. Soma exata dos juros: sum(J_i) == totalInterestCents
        /// 3. Soma exata dos totais: sum(T_i) == principalAmountCents + totalInterestCents
        /// 4. Variação máxima entre parcelas de no máximo 1 centavo: max(T_i) - min(T_i) <= 1
        /// </summary>
        public static (long principal, long interest, long total)[] DistributeInstallments(
            long principalAmountCents,
            long totalInterestCents,
            int installmentsCount)
        {
            if (installmentsCount <= 0)
                throw new ArgumentException("A quantidade de parcelas deve ser pelo menos 1.", nameof(installmentsCount));

            long baseP = principalAmountCents / installmentsCount;
            long remP = principalAmountCents % installmentsCount;

            long baseJ = totalInterestCents / installmentsCount;
            long remJ = totalInterestCents % installmentsCount;

            var result = new (long principal, long interest, long total)[installmentsCount];

            if (remP + remJ < installmentsCount)
            {
                // Caso A: resíduos de P e J cabem sem colisão de 2 centavos na mesma parcela
                for (int i = 0; i < installmentsCount; i++)
                {
                    long addP = (i < remP) ? 1 : 0;
                    long addJ = (i >= remP && i < remP + remJ) ? 1 : 0;
                    long p = baseP + addP;
                    long j = baseJ + addJ;
                    result[i] = (p, j, p + j);
                }
            }
            else
            {
                // Caso B: resíduos somados excedem N. Exatamente k parcelas recebem bônus duplo (+1 P e +1 J)
                // de modo que cada parcela no total recebe baseT + 1 (quando tem bônus duplo) ou baseT (quando tem bônus simples)
                long k = remP + remJ - installmentsCount;
                for (int i = 0; i < installmentsCount; i++)
                {
                    long addP = (i < remP) ? 1 : 0;
                    long addJ = (i < k || (i >= remP && i < installmentsCount)) ? 1 : 0;
                    long p = baseP + addP;
                    long j = baseJ + addJ;
                    result[i] = (p, j, p + j);
                }
            }

            return result;
        }

        public async Task<DisburseContractResponseDto> DisburseContractAsync(DisburseContractRequestDto req)
        {
            if (!await _proService.IsProAsync())
            {
                throw new ProLicenseRequiredException("O módulo Crédito & Giro é exclusivo do CobraPix PRO.");
            }

            if (string.IsNullOrWhiteSpace(req.ContractId))
                throw new ArgumentException("O ID do contrato é obrigatório.", nameof(req.ContractId));

            string canonicalPayload = req.Canonicalize();

            return await _transactionExecutor.ExecuteTransactionalAsync(
                "DisburseContract",
                req.IdempotencyKey,
                canonicalPayload,
                async ctx =>
                {
                    var contract = await ctx.CreditContracts
                        .Include(c => c.Disbursement)
                        .FirstOrDefaultAsync(c => c.Id == req.ContractId);

                    if (contract == null)
                        throw new InvalidOperationException($"Contrato '{req.ContractId}' não encontrado.");

                    if (contract.Status != CreditContractStatus.Draft)
                        throw new InvalidOperationException($"Apenas contratos em rascunho (Draft) podem ser desembolsados. Status atual: {contract.Status}.");

                    if (contract.Disbursement != null)
                        throw new InvalidOperationException("Este contrato já possui um desembolso registrado.");

                    var disbursement = new CreditDisbursement
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = contract.Id,
                        DisbursedAmountCents = contract.DisbursedPrincipalCents,
                        DisbursementDate = req.DisbursementDate,
                        PaymentMethod = req.PaymentMethod,
                        Notes = req.Notes?.Trim(),
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    ctx.CreditDisbursements.Add(disbursement);

                    // Contrato transiciona para Ativo
                    contract.Status = CreditContractStatus.Active;

                    ctx.CreditTransactionAudits.Add(new CreditTransactionAudit
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = contract.Id,
                        OperationType = "ContractDisbursement",
                        DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            disbursement.Id,
                            disbursement.DisbursedAmountCents,
                            disbursement.DisbursementDate,
                            disbursement.PaymentMethod
                        }),
                        CreatedAtUtc = DateTime.UtcNow
                    });

                    return new DisburseContractResponseDto
                    {
                        DisbursementId = disbursement.Id,
                        ContractId = contract.Id,
                        DisbursedAmountCents = disbursement.DisbursedAmountCents,
                        ContractStatus = contract.Status
                    };
                });
        }

        public async Task<CancelContractResponseDto> CancelContractAsync(CancelContractRequestDto req)
        {
            if (!await _proService.IsProAsync())
            {
                throw new ProLicenseRequiredException("O módulo Crédito & Giro é exclusivo do CobraPix PRO.");
            }

            if (string.IsNullOrWhiteSpace(req.ContractId))
                throw new ArgumentException("O ID do contrato é obrigatório.", nameof(req.ContractId));

            string canonicalPayload = req.Canonicalize();

            return await _transactionExecutor.ExecuteTransactionalAsync(
                "CancelContract",
                req.IdempotencyKey,
                canonicalPayload,
                async ctx =>
                {
                    var contract = await ctx.CreditContracts
                        .Include(c => c.Installments)
                        .FirstOrDefaultAsync(c => c.Id == req.ContractId);

                    if (contract == null)
                        throw new InvalidOperationException($"Contrato '{req.ContractId}' não encontrado.");

                    if (contract.Status == CreditContractStatus.Cancelled)
                    {
                        return new CancelContractResponseDto
                        {
                            ContractId = contract.Id,
                            Status = contract.Status,
                            Message = "Contrato já se encontra cancelado."
                        };
                    }

                    // Regra comercial aprovada:
                    // Cancelamento de desembolso permitido somente quando não houver pagamentos vinculados
                    bool hasPayments = await ctx.CreditPayments.AnyAsync(p => p.ContractId == contract.Id);
                    if (hasPayments)
                    {
                        throw new CreditOperationPreconditionException(
                            "Cancelamento de contrato proibido: já existem pagamentos vinculados a este contrato no histórico.");
                    }

                    contract.Status = CreditContractStatus.Cancelled;
                    contract.CancelledAtUtc = DateTime.UtcNow;
                    contract.CancellationReason = req.Reason.Trim();

                    // Cancela todas as parcelas pendentes
                    foreach (var inst in contract.Installments)
                    {
                        inst.Status = CreditInstallmentStatus.Cancelled;
                    }

                    ctx.CreditTransactionAudits.Add(new CreditTransactionAudit
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = contract.Id,
                        OperationType = "ContractCancellation",
                        DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            contract.Id,
                            Reason = req.Reason
                        }),
                        CreatedAtUtc = DateTime.UtcNow
                    });

                    return new CancelContractResponseDto
                    {
                        ContractId = contract.Id,
                        Status = contract.Status,
                        Message = "Contrato cancelado com sucesso."
                    };
                });
        }

        public async Task<CreditContract?> GetContractByIdAsync(string contractId)
        {
            return await _dbContext.CreditContracts
                .AsNoTracking()
                .Include(c => c.Client)
                .Include(c => c.Disbursement)
                .Include(c => c.Installments)
                .Include(c => c.Payments)
                .Include(c => c.SurplusBalances)
                .FirstOrDefaultAsync(c => c.Id == contractId);
        }

        public async Task<List<CreditContract>> GetContractsByClientIdAsync(int clientId)
        {
            return await _dbContext.CreditContracts
                .AsNoTracking()
                .Where(c => c.ClientId == clientId)
                .Include(c => c.Client)
                .Include(c => c.Installments)
                .Include(c => c.Disbursement)
                .OrderByDescending(c => c.ContractDate)
                .ToListAsync();
        }

        public async Task<List<CreditContract>> GetAllContractsAsync()
        {
            return await _dbContext.CreditContracts
                .AsNoTracking()
                .Include(c => c.Client)
                .Include(c => c.Installments)
                .Include(c => c.Disbursement)
                .Include(c => c.Payments)
                .Include(c => c.SurplusBalances)
                .OrderByDescending(c => c.ContractDate)
                .ToListAsync();
        }
    }
}
