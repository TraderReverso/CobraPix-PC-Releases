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
    /// Implementação oficial do serviço de pagamentos e apropriação de excedentes de crédito.
    /// Executa estritamente sob a Regra A: juros remanescentes primeiro, principal depois.
    /// Exclusivo para usuários CobraPix PRO.
    /// </summary>
    public class CreditPaymentService : ICreditPaymentService
    {
        private readonly CobraPixDbContext _dbContext;
        private readonly IProService _proService;
        private readonly ICreditTransactionExecutor _transactionExecutor;

        public CreditPaymentService(
            CobraPixDbContext dbContext,
            IProService proService,
            ICreditTransactionExecutor? transactionExecutor = null)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _proService = proService ?? throw new ArgumentNullException(nameof(proService));
            _transactionExecutor = transactionExecutor ?? new CreditTransactionExecutor(dbContext);
        }

        public async Task<PaymentResultDto> RegisterPaymentAsync(PaymentRequestDto req)
        {
            // 1. Validação prévia de licença PRO (Fail-fast antes de qualquer lock)
            if (!await _proService.IsProAsync())
            {
                throw new ProLicenseRequiredException("O módulo Crédito & Giro é exclusivo do CobraPix PRO.");
            }

            // 2. Validações preliminares de entrada
            if (req.AmountCents <= 0)
                throw new ArgumentException("O valor do pagamento deve ser estritamente maior que zero.", nameof(req.AmountCents));

            if (string.IsNullOrWhiteSpace(req.ContractId))
                throw new ArgumentException("O ID do contrato é obrigatório.", nameof(req.ContractId));

            if (string.IsNullOrWhiteSpace(req.IdempotencyKey))
                throw new ArgumentException("A chave de idempotência é obrigatória.", nameof(req.IdempotencyKey));

            string canonicalPayload = req.Canonicalize();

            return await _transactionExecutor.ExecuteTransactionalAsync(
                "RegisterPayment",
                req.IdempotencyKey,
                canonicalPayload,
                async ctx =>
                {
                    // 3. Carregar contrato e parcelas abertas sob lock exclusivo (BEGIN IMMEDIATE)
                    var contract = await ctx.CreditContracts
                        .Include(c => c.Installments)
                        .FirstOrDefaultAsync(c => c.Id == req.ContractId);

                    if (contract == null)
                        throw new InvalidOperationException($"Contrato '{req.ContractId}' não encontrado.");

                    if (contract.Status != CreditContractStatus.Active)
                        throw new InvalidOperationException($"Pagamentos só são admitidos para contratos em status Active. Status atual: {contract.Status}.");

                    // 4. Criação da entidade de pagamento em dinheiro
                    var payment = new CreditPayment
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = contract.Id,
                        PaymentDate = req.PaymentDate,
                        AmountCents = req.AmountCents,
                        Status = CreditPaymentStatus.Active,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    ctx.CreditPayments.Add(payment);

                    long remainingPaymentAmount = req.AmountCents;
                    long totalInterestAllocated = 0;
                    long totalPrincipalAllocated = 0;

                    // 5. Ordenar obrigações abertas por ordem de vencimento (número de parcela)
                    var openInstallments = contract.Installments
                        .Where(i => i.Status == CreditInstallmentStatus.Pending || i.Status == CreditInstallmentStatus.PartiallyPaid)
                        .OrderBy(i => i.InstallmentNumber)
                        .ToList();

                    foreach (var inst in openInstallments)
                    {
                        if (remainingPaymentAmount <= 0)
                            break;

                        // Apuração dos componentes já pagos ativos na parcela
                        long paidInterest = (await ctx.CreditPaymentInstallmentAllocs
                            .Where(a => a.InstallmentId == inst.Id && a.Payment != null && a.Payment.Status == CreditPaymentStatus.Active)
                            .SumAsync(a => (long?)a.InterestAllocated) ?? 0L)
                            + (await ctx.CreditSurplusConsumptions
                            .Where(sc => sc.TargetInstallmentId == inst.Id && !sc.IsReversed)
                            .SumAsync(sc => (long?)sc.InterestConsumed) ?? 0L);

                        long paidPrincipal = (await ctx.CreditPaymentInstallmentAllocs
                            .Where(a => a.InstallmentId == inst.Id && a.Payment != null && a.Payment.Status == CreditPaymentStatus.Active)
                            .SumAsync(a => (long?)a.PrincipalAllocated) ?? 0L)
                            + (await ctx.CreditSurplusConsumptions
                            .Where(sc => sc.TargetInstallmentId == inst.Id && !sc.IsReversed)
                            .SumAsync(sc => (long?)sc.PrincipalConsumed) ?? 0L);

                        if (paidInterest > inst.NominalInterestCents || paidPrincipal > inst.NominalPrincipalCents)
                            throw new FinancialInvariantViolationException("Invariante violada: pagamentos ativos superam o valor nominal da parcela.");

                        long remainingInterest = inst.NominalInterestCents - paidInterest;
                        long remainingPrincipal = inst.NominalPrincipalCents - paidPrincipal;
                        long installmentBalance = remainingInterest + remainingPrincipal;

                        if (installmentBalance <= 0)
                            continue;

                        long amountToAllocate = Math.Min(remainingPaymentAmount, installmentBalance);

                        // APLICAÇÃO MANDATÓRIA DA REGRA A: Juros primeiro, principal depois
                        long interestAlloc = Math.Min(amountToAllocate, remainingInterest);
                        long principalAlloc = amountToAllocate - interestAlloc;

                        // Invariante relacional
                        if (interestAlloc + principalAlloc != amountToAllocate)
                            throw new FinancialInvariantViolationException("A soma de juros e principal alocados diverge do montante aplicado.");

                        if (principalAlloc > remainingPrincipal)
                            throw new FinancialInvariantViolationException("Amortização de principal calculada supera o principal em aberto da parcela.");

                        var alloc = new CreditPaymentInstallmentAlloc
                        {
                            Id = Guid.NewGuid().ToString(),
                            PaymentId = payment.Id,
                            InstallmentId = inst.Id,
                            AllocatedAmount = amountToAllocate,
                            InterestAllocated = interestAlloc,
                            PrincipalAllocated = principalAlloc,
                            CreatedAtUtc = DateTime.UtcNow
                        };
                        ctx.CreditPaymentInstallmentAllocs.Add(alloc);

                        // Recomposição exata do estado da parcela
                        long newTotalPaid = paidInterest + paidPrincipal + amountToAllocate;
                        inst.PaidAmountCents = newTotalPaid;
                        inst.Status = (newTotalPaid == inst.TotalAmountCents)
                            ? CreditInstallmentStatus.Paid
                            : CreditInstallmentStatus.PartiallyPaid;

                        remainingPaymentAmount -= amountToAllocate;
                        totalInterestAllocated += interestAlloc;
                        totalPrincipalAllocated += principalAlloc;
                    }

                    // 6. Direcionamento do excedente para bolsa do mesmo contrato
                    long surplusGenerated = 0;
                    if (remainingPaymentAmount > 0)
                    {
                        surplusGenerated = remainingPaymentAmount;
                        var surplus = new CreditSurplusBalance
                        {
                            Id = Guid.NewGuid().ToString(),
                            ContractId = contract.Id,
                            SourcePaymentId = payment.Id,
                            OriginalSurplusCents = surplusGenerated,
                            RemainingSurplusCents = surplusGenerated,
                            Status = CreditSurplusStatus.Available,
                            CreatedAtUtc = DateTime.UtcNow
                        };
                        ctx.CreditSurplusBalances.Add(surplus);
                    }

                    // 7. Validação da Invariante Global de Amortização de Principal <= Desembolsado
                    long totalHistoricalPrincipalAmortized = (await ctx.CreditPaymentInstallmentAllocs
                        .Where(a => a.Installment != null && a.Installment.ContractId == contract.Id && a.Payment != null && a.Payment.Status == CreditPaymentStatus.Active)
                        .SumAsync(a => (long?)a.PrincipalAllocated) ?? 0L)
                        + (await ctx.CreditSurplusConsumptions
                        .Where(sc => sc.TargetInstallment != null && sc.TargetInstallment.ContractId == contract.Id && !sc.IsReversed)
                        .SumAsync(sc => (long?)sc.PrincipalConsumed) ?? 0L)
                        + totalPrincipalAllocated;

                    if (totalHistoricalPrincipalAmortized > contract.DisbursedPrincipalCents)
                    {
                        throw new FinancialInvariantViolationException(
                            $"Invariante global violada: total de principal amortizado ({totalHistoricalPrincipalAmortized}) supera o capital desembolsado ({contract.DisbursedPrincipalCents}).");
                    }

                    // 8. Se todas as parcelas foram pagas e não há excedente sobrando, liquida o contrato
                    bool allPaid = contract.Installments.All(i => i.Status == CreditInstallmentStatus.Paid);
                    if (allPaid && surplusGenerated == 0)
                    {
                        contract.Status = CreditContractStatus.Completed;
                    }

                    // 9. Registro de auditoria transacional
                    ctx.CreditTransactionAudits.Add(new CreditTransactionAudit
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = contract.Id,
                        OperationType = "PaymentRegistration",
                        DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            payment.Id,
                            req.AmountCents,
                            totalInterestAllocated,
                            totalPrincipalAllocated,
                            surplusGenerated
                        }),
                        CreatedAtUtc = DateTime.UtcNow
                    });

                    return new PaymentResultDto
                    {
                        PaymentId = payment.Id,
                        ContractId = contract.Id,
                        AmountPaidCents = req.AmountCents,
                        TotalInterestAllocatedCents = totalInterestAllocated,
                        TotalPrincipalAllocatedCents = totalPrincipalAllocated,
                        SurplusGeneratedCents = surplusGenerated,
                        Status = "Success"
                    };
                });
        }

        public async Task<ConsumeSurplusResponseDto> ConsumeSurplusAsync(ConsumeSurplusRequestDto req)
        {
            if (!await _proService.IsProAsync())
            {
                throw new ProLicenseRequiredException("O módulo Crédito & Giro é exclusivo do CobraPix PRO.");
            }

            if (req.AmountCents <= 0)
                throw new ArgumentException("O valor a consumir deve ser maior que zero.", nameof(req.AmountCents));

            if (string.IsNullOrWhiteSpace(req.SurplusBalanceId))
                throw new ArgumentException("O ID da bolsa de excedente é obrigatório.", nameof(req.SurplusBalanceId));

            if (string.IsNullOrWhiteSpace(req.TargetInstallmentId))
                throw new ArgumentException("O ID da parcela destino é obrigatório.", nameof(req.TargetInstallmentId));

            string canonicalPayload = req.Canonicalize();

            return await _transactionExecutor.ExecuteTransactionalAsync(
                "ConsumeSurplus",
                req.IdempotencyKey,
                canonicalPayload,
                async ctx =>
                {
                    // 1. Carregar bolsa de excedente sob lock
                    var surplus = await ctx.CreditSurplusBalances
                        .Include(s => s.Contract)
                        .FirstOrDefaultAsync(s => s.Id == req.SurplusBalanceId);

                    if (surplus == null)
                        throw new InvalidOperationException($"Bolsa de excedente '{req.SurplusBalanceId}' não encontrada.");

                    if (surplus.RemainingSurplusCents < req.AmountCents)
                        throw new InvalidOperationException($"Saldo de excedente insuficiente ({surplus.RemainingSurplusCents} < {req.AmountCents}).");

                    // 2. Carregar parcela destino
                    var inst = await ctx.CreditInstallments
                        .Include(i => i.Contract)
                        .FirstOrDefaultAsync(i => i.Id == req.TargetInstallmentId);

                    if (inst == null)
                        throw new InvalidOperationException($"Parcela destino '{req.TargetInstallmentId}' não encontrada.");

                    // Regra comercial 2 & 7: Escopo restrito ao mesmo contrato de origem na V1
                    if (inst.ContractId != surplus.ContractId)
                    {
                        throw new InvalidOperationException(
                            "O saldo excedente pertence exclusivamente ao contrato de origem e não pode ser consumido em obrigações de outro contrato (V1).");
                    }

                    if (inst.Status == CreditInstallmentStatus.Paid)
                        throw new InvalidOperationException("A parcela destino já se encontra integralmente quitada.");

                    if (inst.Status == CreditInstallmentStatus.Cancelled)
                        throw new InvalidOperationException("A parcela destino encontra-se cancelada.");

                    // 3. Componentes já pagos ativos na parcela destino
                    long paidInterest = (await ctx.CreditPaymentInstallmentAllocs
                        .Where(a => a.InstallmentId == inst.Id && a.Payment != null && a.Payment.Status == CreditPaymentStatus.Active)
                        .SumAsync(a => (long?)a.InterestAllocated) ?? 0L)
                        + (await ctx.CreditSurplusConsumptions
                        .Where(sc => sc.TargetInstallmentId == inst.Id && !sc.IsReversed)
                        .SumAsync(sc => (long?)sc.InterestConsumed) ?? 0L);

                    long paidPrincipal = (await ctx.CreditPaymentInstallmentAllocs
                        .Where(a => a.InstallmentId == inst.Id && a.Payment != null && a.Payment.Status == CreditPaymentStatus.Active)
                        .SumAsync(a => (long?)a.PrincipalAllocated) ?? 0L)
                        + (await ctx.CreditSurplusConsumptions
                        .Where(sc => sc.TargetInstallmentId == inst.Id && !sc.IsReversed)
                        .SumAsync(sc => (long?)sc.PrincipalConsumed) ?? 0L);

                    long remainingInterest = inst.NominalInterestCents - paidInterest;
                    long remainingPrincipal = inst.NominalPrincipalCents - paidPrincipal;
                    long totalRemaining = remainingInterest + remainingPrincipal;

                    // Bloqueio de sobrealocação: não permite consumir mais que o saldo exigível
                    if (req.AmountCents > totalRemaining)
                    {
                        throw new InvalidOperationException(
                            $"Sobrealocação rejeitada: valor a consumir ({req.AmountCents}) supera o saldo remanescente da parcela ({totalRemaining}).");
                    }

                    // 4. APLICAÇÃO MANDATÓRIA DA REGRA A AO CONSUMO DE EXCEDENTE:
                    long interestConsumed = Math.Min(req.AmountCents, remainingInterest);
                    long principalConsumed = req.AmountCents - interestConsumed;

                    if (interestConsumed + principalConsumed != req.AmountCents)
                        throw new FinancialInvariantViolationException("A soma de juros e principal consumidos diverge do valor solicitado.");

                    // 5. Persistência dos componentes históricos exatos do consumo
                    var consumption = new CreditSurplusConsumption
                    {
                        Id = Guid.NewGuid().ToString(),
                        SurplusBalanceId = surplus.Id,
                        TargetInstallmentId = inst.Id,
                        ConsumedAmount = req.AmountCents,
                        InterestConsumed = interestConsumed,
                        PrincipalConsumed = principalConsumed,
                        IsReversed = false,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    ctx.CreditSurplusConsumptions.Add(consumption);

                    // 6. Atualização da bolsa de excedente
                    surplus.RemainingSurplusCents -= req.AmountCents;
                    surplus.Status = surplus.RemainingSurplusCents == 0
                        ? CreditSurplusStatus.FullyConsumed
                        : CreditSurplusStatus.PartiallyConsumed;

                    // 7. Recomposição exata do estado da parcela
                    long newTotalPaid = paidInterest + paidPrincipal + req.AmountCents;
                    inst.PaidAmountCents = newTotalPaid;
                    inst.Status = (newTotalPaid == inst.TotalAmountCents)
                        ? CreditInstallmentStatus.Paid
                        : CreditInstallmentStatus.PartiallyPaid;

                    // 8. Se todas as parcelas do contrato estão pagas, completa o contrato
                    var allContractInstallments = await ctx.CreditInstallments
                        .Where(i => i.ContractId == inst.ContractId)
                        .ToListAsync();

                    if (allContractInstallments.All(i => i.Status == CreditInstallmentStatus.Paid) && inst.Contract != null)
                    {
                        inst.Contract.Status = CreditContractStatus.Completed;
                    }

                    ctx.CreditTransactionAudits.Add(new CreditTransactionAudit
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = inst.ContractId,
                        OperationType = "SurplusConsumption",
                        DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            consumption.Id,
                            req.AmountCents,
                            interestConsumed,
                            principalConsumed,
                            remainingSurplus = surplus.RemainingSurplusCents
                        }),
                        CreatedAtUtc = DateTime.UtcNow
                    });

                    return new ConsumeSurplusResponseDto
                    {
                        ConsumptionId = consumption.Id,
                        SurplusBalanceId = surplus.Id,
                        TargetInstallmentId = inst.Id,
                        ConsumedAmountCents = req.AmountCents,
                        InterestConsumedCents = interestConsumed,
                        PrincipalConsumedCents = principalConsumed,
                        RemainingSurplusCents = surplus.RemainingSurplusCents,
                        TargetInstallmentStatus = inst.Status
                    };
                });
        }

        public async Task<ReverseSurplusConsumptionResponseDto> ReverseSurplusConsumptionAsync(ReverseSurplusConsumptionRequestDto req)
        {
            if (!await _proService.IsProAsync())
            {
                throw new ProLicenseRequiredException("O módulo Crédito & Giro é exclusivo do CobraPix PRO.");
            }

            if (string.IsNullOrWhiteSpace(req.ConsumptionId))
                throw new ArgumentException("O ID do consumo é obrigatório.", nameof(req.ConsumptionId));

            string canonicalPayload = req.Canonicalize();

            return await _transactionExecutor.ExecuteTransactionalAsync(
                "ReverseSurplusConsumption",
                req.IdempotencyKey,
                canonicalPayload,
                async ctx =>
                {
                    var consumption = await ctx.CreditSurplusConsumptions
                        .Include(sc => sc.SurplusBalance)
                        .Include(sc => sc.TargetInstallment)
                            .ThenInclude(i => i!.Contract)
                        .FirstOrDefaultAsync(sc => sc.Id == req.ConsumptionId);

                    if (consumption == null)
                        throw new InvalidOperationException($"Consumo de excedente '{req.ConsumptionId}' não encontrado.");

                    if (consumption.IsReversed)
                        throw new InvalidOperationException("Este consumo de excedente já foi revertido anteriormente.");

                    // 1. Marcação do consumo como revertido e gravação da reversão 1:1
                    consumption.IsReversed = true;

                    var reversal = new CreditSurplusConsumptionReversal
                    {
                        Id = Guid.NewGuid().ToString(),
                        CreditSurplusConsumptionId = consumption.Id,
                        ReversedAmountCents = consumption.ConsumedAmount,
                        ReversedInterestCents = consumption.InterestConsumed,
                        ReversedPrincipalCents = consumption.PrincipalConsumed,
                        ReversalDateUtc = DateTime.UtcNow
                    };
                    ctx.CreditSurplusConsumptionReversals.Add(reversal);

                    // 2. Restauração do saldo na bolsa de excedente
                    if (consumption.SurplusBalance != null)
                    {
                        consumption.SurplusBalance.RemainingSurplusCents += consumption.ConsumedAmount;
                        consumption.SurplusBalance.Status = consumption.SurplusBalance.RemainingSurplusCents == consumption.SurplusBalance.OriginalSurplusCents
                            ? CreditSurplusStatus.Available
                            : CreditSurplusStatus.PartiallyConsumed;
                    }

                    // 3. Recomposição algébrica da obrigação destino (preservando pagamentos independentes)
                    var inst = consumption.TargetInstallment;
                    if (inst != null)
                    {
                        long totalActivePaid = (await ctx.CreditPaymentInstallmentAllocs
                            .Where(a => a.InstallmentId == inst.Id && a.Payment != null && a.Payment.Status == CreditPaymentStatus.Active)
                            .SumAsync(a => (long?)a.AllocatedAmount) ?? 0L)
                            + (await ctx.CreditSurplusConsumptions
                            .Where(sc => sc.TargetInstallmentId == inst.Id && !sc.IsReversed && sc.Id != consumption.Id)
                            .SumAsync(sc => (long?)sc.ConsumedAmount) ?? 0L);

                        inst.PaidAmountCents = totalActivePaid;
                        long remaining = inst.TotalAmountCents - totalActivePaid;

                        if (remaining == 0)
                            inst.Status = CreditInstallmentStatus.Paid;
                        else if (totalActivePaid > 0)
                            inst.Status = CreditInstallmentStatus.PartiallyPaid; // Retorno correto preservando pagamentos parciais
                        else
                            inst.Status = CreditInstallmentStatus.Pending;

                        // Se o contrato estava Completed, reabre para Active
                        if (inst.Contract != null && inst.Contract.Status == CreditContractStatus.Completed)
                        {
                            inst.Contract.Status = CreditContractStatus.Active;
                        }
                    }

                    ctx.CreditTransactionAudits.Add(new CreditTransactionAudit
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = inst?.ContractId,
                        OperationType = "SurplusConsumptionReversal",
                        DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            ReversalId = reversal.Id,
                            ConsumptionId = consumption.Id,
                            consumption.ConsumedAmount,
                            req.Reason
                        }),
                        CreatedAtUtc = DateTime.UtcNow
                    });

                    return new ReverseSurplusConsumptionResponseDto
                    {
                        ReversalId = reversal.Id,
                        ConsumptionId = consumption.Id,
                        ReversedAmountCents = consumption.ConsumedAmount,
                        RestoredSurplusBalanceCents = consumption.SurplusBalance?.RemainingSurplusCents ?? 0L,
                        TargetInstallmentNewStatus = inst?.Status ?? CreditInstallmentStatus.Pending,
                        Message = "Consumo de excedente revertido com sucesso."
                    };
                });
        }

        public async Task<List<CreditSurplusBalanceDto>> GetSurplusBalancesByContractAsync(string contractId)
        {
            return await _dbContext.CreditSurplusBalances
                .AsNoTracking()
                .Where(s => s.ContractId == contractId)
                .Select(s => new CreditSurplusBalanceDto
                {
                    Id = s.Id,
                    ContractId = s.ContractId,
                    OriginalSurplusCents = s.OriginalSurplusCents,
                    RemainingSurplusCents = s.RemainingSurplusCents,
                    Status = s.Status
                })
                .ToListAsync();
        }

        public async Task<List<CreditPaymentDto>> GetPaymentsByContractAsync(string contractId)
        {
            return await _dbContext.CreditPayments
                .AsNoTracking()
                .Where(p => p.ContractId == contractId)
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => new CreditPaymentDto
                {
                    Id = p.Id,
                    ContractId = p.ContractId,
                    PaymentDate = p.PaymentDate,
                    AmountCents = p.AmountCents,
                    Status = p.Status
                })
                .ToListAsync();
        }

        public async Task<PaymentPreviewDto> PreviewPaymentDistributionAsync(string contractId, long amountCents)
        {
            var result = new PaymentPreviewDto
            {
                TotalAmountCents = amountCents
            };

            if (amountCents <= 0) return result;

            var installments = await _dbContext.CreditInstallments
                .AsNoTracking()
                .Where(i => i.ContractId == contractId && i.Status != CreditInstallmentStatus.Cancelled && i.Status != CreditInstallmentStatus.Paid)
                .OrderBy(i => i.DueDate)
                .ToListAsync();

            long remainingToDistribute = amountCents;

            foreach (var inst in installments)
            {
                if (remainingToDistribute <= 0) break;

                long outstanding = inst.TotalAmountCents - inst.PaidAmountCents;
                if (outstanding <= 0) continue;

                long nominalInterest = inst.NominalInterestCents;
                long activeInterestAllocated = await _dbContext.CreditPaymentInstallmentAllocs
                    .Where(a => a.InstallmentId == inst.Id && a.Payment != null && a.Payment.Status == CreditPaymentStatus.Active)
                    .SumAsync(a => (long?)a.InterestAllocated) ?? 0L;
                long activeInterestConsumed = await _dbContext.CreditSurplusConsumptions
                    .Where(sc => sc.TargetInstallmentId == inst.Id && !sc.IsReversed)
                    .SumAsync(sc => (long?)sc.InterestConsumed) ?? 0L;

                long remainingInterest = Math.Max(0L, nominalInterest - (activeInterestAllocated + activeInterestConsumed));

                long allocAmount = Math.Min(remainingToDistribute, outstanding);
                long interestAlloc = Math.Min(allocAmount, remainingInterest);
                long principalAlloc = allocAmount - interestAlloc;

                result.TotalInterestAllocatedCents += interestAlloc;
                result.TotalPrincipalAllocatedCents += principalAlloc;
                remainingToDistribute -= allocAmount;

                result.Installments.Add(new PaymentPreviewInstallmentDto
                {
                    InstallmentNumber = inst.InstallmentNumber,
                    DueDate = inst.DueDate,
                    TotalAmountCents = inst.TotalAmountCents,
                    OutstandingBeforeCents = outstanding,
                    InterestAllocatedCents = interestAlloc,
                    PrincipalAllocatedCents = principalAlloc,
                    AllocatedAmountCents = allocAmount,
                    RemainingAfterCents = outstanding - allocAmount
                });
            }

            result.SurplusGeneratedCents = remainingToDistribute;
            return result;
        }

        public async Task<List<CreditPayment>> GetAllPaymentsWithDetailsAsync()
        {
            return await _dbContext.CreditPayments
                .AsNoTracking()
                .Include(p => p.Contract)
                    .ThenInclude(c => c!.Client)
                .Include(p => p.Allocations)
                    .ThenInclude(a => a.Installment)
                .Include(p => p.Refund)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();
        }

        public async Task<List<CreditSurplusBalance>> GetAllSurplusBalancesWithDetailsAsync()
        {
            return await _dbContext.CreditSurplusBalances
                .AsNoTracking()
                .Include(s => s.Contract)
                    .ThenInclude(c => c!.Client)
                .Include(s => s.Consumptions)
                    .ThenInclude(c => c.Reversal)
                .OrderByDescending(s => s.CreatedAtUtc)
                .ToListAsync();
        }
    }
}
