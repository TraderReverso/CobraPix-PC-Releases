using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    /// Serviço de domínio para solicitação e processamento de estornos e devoluções de caixa.
    /// Garante que o estorno reabre a dívida imediatamente sem movimentar o caixa ficticiamente.
    /// A saída de caixa só é registrada exatamente uma vez no momento da liquidação efetiva (Settled).
    /// </summary>
    public class CreditRefundService : ICreditRefundService
    {
        private readonly CobraPixDbContext _context;
        private readonly IProService _proService;
        private readonly CreditTransactionExecutor _executor;

        public CreditRefundService(CobraPixDbContext context, IProService proService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _proService = proService ?? throw new ArgumentNullException(nameof(proService));
            _executor = new CreditTransactionExecutor(_context);
        }

        public async Task<AuthorizePaymentRefundResponseDto> AuthorizePaymentRefundAsync(AuthorizeRefundRequestDto request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (!await _proService.IsProAsync())
                throw new ProLicenseRequiredException();

            if (string.IsNullOrWhiteSpace(request.PaymentId))
                throw new ArgumentException("PaymentId é obrigatório.");

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                throw new ArgumentException("IdempotencyKey é obrigatória.");

            string canonical = request.Canonicalize();

            return await _executor.ExecuteTransactionalAsync(
                "CreditRefund.Authorize",
                request.IdempotencyKey,
                canonical,
                async localCtx =>
                {
                    var payment = await localCtx.CreditPayments
                        .Include(p => p.Allocations)
                        .Include(p => p.SurplusBalances)
                        .FirstOrDefaultAsync(p => p.Id == request.PaymentId);

                    if (payment == null)
                        throw new InvalidOperationException($"Pagamento com ID '{request.PaymentId}' não foi encontrado.");

                    if (payment.Status != CreditPaymentStatus.Active)
                        throw new PaymentAlreadyRefundedException($"Pagamento não está no estado Ativo (estado atual: {payment.Status}).");

                    var existingRefund = await localCtx.CreditPaymentRefunds
                        .FirstOrDefaultAsync(r => r.PaymentId == payment.Id);

                    if (existingRefund != null && existingRefund.Status != CreditRefundStatus.Cancelled)
                        throw new PaymentAlreadyRefundedException("Já existe estorno registrado para este pagamento.");

                    // Verificar se já existem devoluções ativas ou liquidadas para o pagamento
                    bool hasActiveOrSettledDevolutions = await localCtx.CreditDevolutions
                        .AnyAsync(d => d.PaymentId == payment.Id && (d.Status == CreditDevolutionStatus.Authorized || d.Status == CreditDevolutionStatus.Settled));

                    if (hasActiveOrSettledDevolutions)
                        throw new PaymentAlreadyRefundedException("Já existem devoluções ativas ou liquidadas vinculadas a este pagamento.");

                    // Verificar se o pagamento originou excedente com consumos ativos
                    var surplus = await localCtx.CreditSurplusBalances
                        .Include(sb => sb.Consumptions)
                        .FirstOrDefaultAsync(sb => sb.SourcePaymentId == payment.Id);

                    if (surplus != null)
                    {
                        bool hasActiveConsumptions = surplus.Consumptions.Any(c => !c.IsReversed) ||
                                                     surplus.RemainingSurplusCents < surplus.OriginalSurplusCents;

                        if (hasActiveConsumptions)
                            throw new DependentSurplusConsumptionsActiveException(
                                "Não é permitido estornar pagamento com excedente consumido ativamente. Reverter os consumos dependentes primeiro.");

                        // Se não tem consumos ativos, o saldo restante do excedente é anulado
                        surplus.RemainingSurplusCents = 0;
                        surplus.Status = CreditSurplusStatus.FullyConsumed;
                    }

                    // Reabertura exata das parcelas afetadas
                    int reopenedCount = 0;
                    foreach (var alloc in payment.Allocations)
                    {
                        var inst = await localCtx.CreditInstallments
                            .FirstOrDefaultAsync(i => i.Id == alloc.InstallmentId);

                        if (inst != null)
                        {
                            inst.PaidAmountCents -= alloc.AllocatedAmount;
                            if (inst.PaidAmountCents < 0)
                                throw new FinancialInvariantViolationException($"Saldo pago da parcela #{inst.InstallmentNumber} ficaria negativo após estorno.");

                            if (inst.PaidAmountCents == 0)
                                inst.Status = CreditInstallmentStatus.Pending;
                            else if (inst.PaidAmountCents < inst.TotalAmountCents)
                                inst.Status = CreditInstallmentStatus.PartiallyPaid;
                            else
                                inst.Status = CreditInstallmentStatus.Paid;

                            reopenedCount++;
                        }
                    }

                    payment.Status = CreditPaymentStatus.ReversalAuthorized;

                    var refund = new CreditPaymentRefund
                    {
                        Id = Guid.NewGuid().ToString(),
                        PaymentId = payment.Id,
                        AuthorizedAmountCents = payment.AmountCents,
                        Status = CreditRefundStatus.Authorized,
                        Reason = request.Reason,
                        AuthorizedAtUtc = DateTime.UtcNow
                    };
                    localCtx.CreditPaymentRefunds.Add(refund);

                    // Cria devolução correspondente para o fluxo de caixa
                    var devolution = new CreditDevolution
                    {
                        Id = Guid.NewGuid().ToString(),
                        RefundId = refund.Id,
                        PaymentId = payment.Id,
                        ContractId = payment.ContractId,
                        AuthorizedAmountCents = payment.AmountCents,
                        Status = CreditDevolutionStatus.Authorized,
                        Reason = request.Reason,
                        AuthorizedAtUtc = DateTime.UtcNow
                    };
                    localCtx.CreditDevolutions.Add(devolution);

                    localCtx.CreditTransactionAudits.Add(new CreditTransactionAudit
                    {
                        ContractId = payment.ContractId,
                        OperationType = "AuthorizePaymentRefund",
                        DetailsJson = JsonSerializer.Serialize(new
                        {
                            RefundId = refund.Id,
                            DevolutionId = devolution.Id,
                            PaymentId = payment.Id,
                            AuthorizedAmountCents = payment.AmountCents,
                            ReopenedInstallmentsCount = reopenedCount,
                            Reason = request.Reason
                        })
                    });

                    await localCtx.SaveChangesAsync();

                    return new AuthorizePaymentRefundResponseDto
                    {
                        RefundId = refund.Id,
                        PaymentId = payment.Id,
                        DevolutionId = devolution.Id,
                        AuthorizedAmountCents = refund.AuthorizedAmountCents,
                        Status = refund.Status,
                        ReopenedInstallmentsCount = reopenedCount,
                        AuthorizedAtUtc = refund.AuthorizedAtUtc
                    };
                });
        }

        public async Task<AuthorizeDevolutionResponseDto> AuthorizeDevolutionAsync(AuthorizeDevolutionRequestDto request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (!await _proService.IsProAsync())
                throw new ProLicenseRequiredException();

            if (string.IsNullOrWhiteSpace(request.PaymentId))
                throw new ArgumentException("PaymentId é obrigatório.");

            if (request.AmountCents <= 0)
                throw new ArgumentException("AmountCents deve ser maior que zero.");

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                throw new ArgumentException("IdempotencyKey é obrigatória.");

            string canonical = request.Canonicalize();

            return await _executor.ExecuteTransactionalAsync(
                "CreditDevolution.Authorize",
                request.IdempotencyKey,
                canonical,
                async localCtx =>
                {
                    var payment = await localCtx.CreditPayments
                        .FirstOrDefaultAsync(p => p.Id == request.PaymentId);

                    if (payment == null)
                        throw new InvalidOperationException($"Pagamento com ID '{request.PaymentId}' não encontrado.");

                    if (payment.Status != CreditPaymentStatus.Active)
                        throw new PaymentAlreadyRefundedException($"Pagamento não está no estado Ativo (estado atual: {payment.Status}).");

                    // Limite elegível para devolução
                    long alreadyDevolvedOrAuthorized = await localCtx.CreditDevolutions
                        .Where(d => d.PaymentId == payment.Id && (d.Status == CreditDevolutionStatus.Authorized || d.Status == CreditDevolutionStatus.Settled))
                        .SumAsync(d => d.AuthorizedAmountCents);

                    long eligibleAmount = payment.AmountCents - alreadyDevolvedOrAuthorized;

                    if (request.AmountCents > eligibleAmount)
                        throw new ExceededEligibleDevolutionAmountException(
                            $"Valor solicitado ({request.AmountCents}) excede o saldo elegível para devolução ({eligibleAmount}).");

                    var devolution = new CreditDevolution
                    {
                        Id = Guid.NewGuid().ToString(),
                        PaymentId = payment.Id,
                        ContractId = payment.ContractId,
                        AuthorizedAmountCents = request.AmountCents,
                        Status = CreditDevolutionStatus.Authorized,
                        Reason = request.Reason,
                        AuthorizedAtUtc = DateTime.UtcNow
                    };
                    localCtx.CreditDevolutions.Add(devolution);

                    localCtx.CreditTransactionAudits.Add(new CreditTransactionAudit
                    {
                        ContractId = payment.ContractId,
                        OperationType = "AuthorizeDevolution",
                        DetailsJson = JsonSerializer.Serialize(new
                        {
                            DevolutionId = devolution.Id,
                            PaymentId = payment.Id,
                            AuthorizedAmountCents = request.AmountCents,
                            Reason = request.Reason
                        })
                    });

                    await localCtx.SaveChangesAsync();

                    return new AuthorizeDevolutionResponseDto
                    {
                        DevolutionId = devolution.Id,
                        PaymentId = payment.Id,
                        ContractId = payment.ContractId,
                        AuthorizedAmountCents = devolution.AuthorizedAmountCents,
                        Status = devolution.Status,
                        AuthorizedAtUtc = devolution.AuthorizedAtUtc
                    };
                });
        }

        public async Task<SettleDevolutionResponseDto> SettleDevolutionAsync(SettleDevolutionRequestDto request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (!await _proService.IsProAsync())
                throw new ProLicenseRequiredException();

            if (string.IsNullOrWhiteSpace(request.DevolutionId))
                throw new ArgumentException("DevolutionId é obrigatório.");

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                throw new ArgumentException("IdempotencyKey é obrigatória.");

            string canonical = request.Canonicalize();

            return await _executor.ExecuteTransactionalAsync(
                "CreditDevolution.Settle",
                request.IdempotencyKey,
                canonical,
                async localCtx =>
                {
                    var devolution = await localCtx.CreditDevolutions
                        .FirstOrDefaultAsync(d => d.Id == request.DevolutionId);

                    if (devolution == null)
                        throw new InvalidOperationException($"Devolução '{request.DevolutionId}' não encontrada.");

                    if (devolution.Status == CreditDevolutionStatus.Settled)
                    {
                        var existingOutflow = await localCtx.CreditCashOutflows
                            .FirstOrDefaultAsync(o => o.DevolutionId == devolution.Id);

                        return new SettleDevolutionResponseDto
                        {
                            DevolutionId = devolution.Id,
                            CashOutflowId = existingOutflow?.Id ?? string.Empty,
                            AmountCents = devolution.AuthorizedAmountCents,
                            Status = devolution.Status,
                            SettledAtUtc = devolution.SettledAtUtc ?? DateTime.UtcNow,
                            OutflowDate = existingOutflow?.OutflowDate ?? DateTime.Today
                        };
                    }

                    if (devolution.Status == CreditDevolutionStatus.Cancelled)
                        throw new DevolutionInvalidStateException("Não é permitido liquidar uma devolução cancelada.");

                    if (devolution.Status != CreditDevolutionStatus.Authorized)
                        throw new DevolutionInvalidStateException($"Devolução em estado inválido para liquidação: {devolution.Status}.");

                    // Garantir que não existe saída prévia para esta devolução
                    bool alreadyHasOutflow = await localCtx.CreditCashOutflows
                        .AnyAsync(o => o.DevolutionId == devolution.Id);

                    if (alreadyHasOutflow)
                        throw new FinancialInvariantViolationException("Já existe saída financeira registrada para esta devolução.");

                    DateTime outflowDate = request.SettlementDate ?? DateTime.Today;

                    var outflow = new CreditCashOutflow
                    {
                        Id = Guid.NewGuid().ToString(),
                        DevolutionId = devolution.Id,
                        PaymentId = devolution.PaymentId,
                        ContractId = devolution.ContractId,
                        AmountCents = devolution.AuthorizedAmountCents,
                        OutflowDate = outflowDate,
                        PaymentMethod = request.PaymentMethod,
                        Description = $"Liquidação de Devolução #{devolution.Id}: {devolution.Reason}",
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    localCtx.CreditCashOutflows.Add(outflow);

                    devolution.Status = CreditDevolutionStatus.Settled;
                    devolution.SettledAtUtc = DateTime.UtcNow;

                    if (!string.IsNullOrEmpty(devolution.RefundId))
                    {
                        var refund = await localCtx.CreditPaymentRefunds
                            .FirstOrDefaultAsync(r => r.Id == devolution.RefundId);

                        if (refund != null)
                        {
                            refund.Status = CreditRefundStatus.Settled;
                            refund.SettledAtUtc = DateTime.UtcNow;
                        }

                        var payment = await localCtx.CreditPayments
                            .FirstOrDefaultAsync(p => p.Id == devolution.PaymentId);

                        if (payment != null)
                        {
                            payment.Status = CreditPaymentStatus.ReversalSettled;
                        }
                    }

                    localCtx.CreditTransactionAudits.Add(new CreditTransactionAudit
                    {
                        ContractId = devolution.ContractId,
                        OperationType = "SettleDevolution",
                        DetailsJson = JsonSerializer.Serialize(new
                        {
                            DevolutionId = devolution.Id,
                            CashOutflowId = outflow.Id,
                            AmountCents = outflow.AmountCents,
                            PaymentMethod = (int)outflow.PaymentMethod
                        })
                    });

                    await localCtx.SaveChangesAsync();

                    return new SettleDevolutionResponseDto
                    {
                        DevolutionId = devolution.Id,
                        CashOutflowId = outflow.Id,
                        AmountCents = outflow.AmountCents,
                        Status = devolution.Status,
                        SettledAtUtc = devolution.SettledAtUtc.Value,
                        OutflowDate = outflow.OutflowDate
                    };
                });
        }

        public async Task<CancelDevolutionResponseDto> CancelDevolutionAsync(CancelDevolutionRequestDto request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (!await _proService.IsProAsync())
                throw new ProLicenseRequiredException();

            if (string.IsNullOrWhiteSpace(request.DevolutionId))
                throw new ArgumentException("DevolutionId é obrigatório.");

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                throw new ArgumentException("IdempotencyKey é obrigatória.");

            string canonical = request.Canonicalize();

            return await _executor.ExecuteTransactionalAsync(
                "CreditDevolution.Cancel",
                request.IdempotencyKey,
                canonical,
                async localCtx =>
                {
                    var devolution = await localCtx.CreditDevolutions
                        .FirstOrDefaultAsync(d => d.Id == request.DevolutionId);

                    if (devolution == null)
                        throw new InvalidOperationException($"Devolução '{request.DevolutionId}' não encontrada.");

                    if (devolution.Status == CreditDevolutionStatus.Settled)
                        throw new DevolutionInvalidStateException("Uma devolução liquidada não pode ser cancelada pelo fluxo normal.");

                    if (devolution.Status == CreditDevolutionStatus.Cancelled)
                    {
                        return new CancelDevolutionResponseDto
                        {
                            DevolutionId = devolution.Id,
                            Status = devolution.Status,
                            CancelledAtUtc = devolution.CancelledAtUtc ?? DateTime.UtcNow,
                            Message = "Devolução já cancelada previamente."
                        };
                    }

                    devolution.Status = CreditDevolutionStatus.Cancelled;
                    devolution.CancelledAtUtc = DateTime.UtcNow;

                    if (!string.IsNullOrEmpty(devolution.RefundId))
                    {
                        var refund = await localCtx.CreditPaymentRefunds
                            .FirstOrDefaultAsync(r => r.Id == devolution.RefundId);

                        if (refund != null)
                        {
                            refund.Status = CreditRefundStatus.Cancelled;
                            refund.CancelledAtUtc = DateTime.UtcNow;
                        }

                        // Restaura a aplicação original do pagamento de forma exata nas parcelas
                        var payment = await localCtx.CreditPayments
                            .Include(p => p.Allocations)
                            .FirstOrDefaultAsync(p => p.Id == devolution.PaymentId);

                        if (payment != null)
                        {
                            payment.Status = CreditPaymentStatus.Active;

                            foreach (var alloc in payment.Allocations)
                            {
                                var inst = await localCtx.CreditInstallments
                                    .FirstOrDefaultAsync(i => i.Id == alloc.InstallmentId);

                                if (inst != null)
                                {
                                    inst.PaidAmountCents += alloc.AllocatedAmount;
                                    if (inst.PaidAmountCents > inst.TotalAmountCents)
                                        throw new FinancialInvariantViolationException($"Saldo pago da parcela #{inst.InstallmentNumber} excederia o total após cancelamento do estorno.");

                                    if (inst.PaidAmountCents == inst.TotalAmountCents)
                                        inst.Status = CreditInstallmentStatus.Paid;
                                    else if (inst.PaidAmountCents > 0)
                                        inst.Status = CreditInstallmentStatus.PartiallyPaid;
                                    else
                                        inst.Status = CreditInstallmentStatus.Pending;
                                }
                            }

                            // Restaura excedente original, se havia
                            var surplus = await localCtx.CreditSurplusBalances
                                .FirstOrDefaultAsync(s => s.SourcePaymentId == payment.Id);

                            if (surplus != null && surplus.RemainingSurplusCents == 0 && surplus.OriginalSurplusCents > 0)
                            {
                                surplus.RemainingSurplusCents = surplus.OriginalSurplusCents;
                                surplus.Status = CreditSurplusStatus.Available;
                            }
                        }
                    }

                    localCtx.CreditTransactionAudits.Add(new CreditTransactionAudit
                    {
                        ContractId = devolution.ContractId,
                        OperationType = "CancelDevolution",
                        DetailsJson = JsonSerializer.Serialize(new
                        {
                            DevolutionId = devolution.Id,
                            Reason = request.Reason
                        })
                    });

                    await localCtx.SaveChangesAsync();

                    return new CancelDevolutionResponseDto
                    {
                        DevolutionId = devolution.Id,
                        Status = devolution.Status,
                        CancelledAtUtc = devolution.CancelledAtUtc.Value,
                        Message = "Devolução cancelada com sucesso."
                    };
                });
        }

        public async Task<CancelRefundAuthResponseDto> CancelRefundAuthAsync(CancelRefundAuthRequestDto request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (!await _proService.IsProAsync())
                throw new ProLicenseRequiredException();

            if (string.IsNullOrWhiteSpace(request.RefundId))
                throw new ArgumentException("RefundId é obrigatório.");

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                throw new ArgumentException("IdempotencyKey é obrigatória.");

            string canonical = request.Canonicalize();

            return await _executor.ExecuteTransactionalAsync(
                "CreditRefund.CancelAuth",
                request.IdempotencyKey,
                canonical,
                async localCtx =>
                {
                    var refund = await localCtx.CreditPaymentRefunds
                        .FirstOrDefaultAsync(r => r.Id == request.RefundId);

                    if (refund == null)
                        throw new InvalidOperationException($"Estorno '{request.RefundId}' não encontrado.");

                    if (refund.Status == CreditRefundStatus.Settled)
                        throw new DevolutionInvalidStateException("Não é permitido cancelar um estorno cuja devolução já foi liquidada.");

                    if (refund.Status == CreditRefundStatus.Cancelled)
                    {
                        return new CancelRefundAuthResponseDto
                        {
                            RefundId = refund.Id,
                            Status = refund.Status,
                            CancelledAtUtc = refund.CancelledAtUtc ?? DateTime.UtcNow,
                            Message = "Autorização de estorno já cancelada previamente."
                        };
                    }

                    refund.Status = CreditRefundStatus.Cancelled;
                    refund.CancelledAtUtc = DateTime.UtcNow;

                    var devolution = await localCtx.CreditDevolutions
                        .FirstOrDefaultAsync(d => d.RefundId == refund.Id);

                    if (devolution != null)
                    {
                        devolution.Status = CreditDevolutionStatus.Cancelled;
                        devolution.CancelledAtUtc = DateTime.UtcNow;
                    }

                    // Restaura dívida e alocações do pagamento
                    var payment = await localCtx.CreditPayments
                        .Include(p => p.Allocations)
                        .FirstOrDefaultAsync(p => p.Id == refund.PaymentId);

                    if (payment != null)
                    {
                        payment.Status = CreditPaymentStatus.Active;

                        foreach (var alloc in payment.Allocations)
                        {
                            var inst = await localCtx.CreditInstallments
                                .FirstOrDefaultAsync(i => i.Id == alloc.InstallmentId);

                            if (inst != null)
                            {
                                inst.PaidAmountCents += alloc.AllocatedAmount;
                                if (inst.PaidAmountCents == inst.TotalAmountCents)
                                    inst.Status = CreditInstallmentStatus.Paid;
                                else if (inst.PaidAmountCents > 0)
                                    inst.Status = CreditInstallmentStatus.PartiallyPaid;
                                else
                                    inst.Status = CreditInstallmentStatus.Pending;
                            }
                        }

                        var surplus = await localCtx.CreditSurplusBalances
                            .FirstOrDefaultAsync(s => s.SourcePaymentId == payment.Id);

                        if (surplus != null && surplus.RemainingSurplusCents == 0 && surplus.OriginalSurplusCents > 0)
                        {
                            surplus.RemainingSurplusCents = surplus.OriginalSurplusCents;
                            surplus.Status = CreditSurplusStatus.Available;
                        }
                    }

                    localCtx.CreditTransactionAudits.Add(new CreditTransactionAudit
                    {
                        ContractId = payment?.ContractId,
                        OperationType = "CancelRefundAuth",
                        DetailsJson = JsonSerializer.Serialize(new
                        {
                            RefundId = refund.Id,
                            Reason = request.Reason
                        })
                    });

                    await localCtx.SaveChangesAsync();

                    return new CancelRefundAuthResponseDto
                    {
                        RefundId = refund.Id,
                        Status = refund.Status,
                        CancelledAtUtc = refund.CancelledAtUtc.Value,
                        Message = "Autorização de estorno cancelada e dívida restaurada com sucesso."
                    };
                });
        }

        public async Task<CreditPaymentRefund?> GetRefundByPaymentIdAsync(string paymentId)
        {
            return await _context.CreditPaymentRefunds
                .AsNoTracking()
                .Include(r => r.Devolution)
                .FirstOrDefaultAsync(r => r.PaymentId == paymentId);
        }

        public async Task<CreditDevolution?> GetDevolutionByIdAsync(string devolutionId)
        {
            return await _context.CreditDevolutions
                .AsNoTracking()
                .Include(d => d.CashOutflow)
                .FirstOrDefaultAsync(d => d.Id == devolutionId);
        }

        public async Task<List<CreditDevolution>> GetDevolutionsByPaymentIdAsync(string paymentId)
        {
            return await _context.CreditDevolutions
                .AsNoTracking()
                .Include(d => d.CashOutflow)
                .Where(d => d.PaymentId == paymentId)
                .OrderByDescending(d => d.AuthorizedAtUtc)
                .ToListAsync();
        }

        public async Task<List<CreditCashOutflow>> GetCashOutflowsByContractAsync(string contractId)
        {
            return await _context.CreditCashOutflows
                .AsNoTracking()
                .Where(o => o.ContractId == contractId)
                .OrderByDescending(o => o.OutflowDate)
                .ToListAsync();
        }

        public async Task<List<CreditPaymentRefund>> GetAllRefundsAsync()
        {
            return await _context.CreditPaymentRefunds
                .AsNoTracking()
                .Include(r => r.Payment)
                    .ThenInclude(p => p!.Contract)
                        .ThenInclude(c => c!.Client)
                .Include(r => r.Devolution)
                    .ThenInclude(d => d!.CashOutflow)
                .OrderByDescending(r => r.AuthorizedAtUtc)
                .ToListAsync();
        }

        public async Task<List<CreditDevolution>> GetAllDevolutionsAsync()
        {
            return await _context.CreditDevolutions
                .AsNoTracking()
                .Include(d => d.Payment)
                    .ThenInclude(p => p!.Contract)
                        .ThenInclude(c => c!.Client)
                .Include(d => d.CashOutflow)
                .OrderByDescending(d => d.AuthorizedAtUtc)
                .ToListAsync();
        }
    }
}
