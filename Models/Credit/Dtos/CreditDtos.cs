using System;
using System.Collections.Generic;
using CobraPix_PC.Models;

namespace CobraPix_PC.Models.Credit.Dtos
{
    public class CreateContractRequestDto
    {
        public int ClientId { get; set; }
        public string ContractNumber { get; set; } = string.Empty;
        public CreditContractType ContractType { get; set; } = CreditContractType.FixedInstallments;
        public CreditPeriodicity Periodicity { get; set; } = CreditPeriodicity.Monthly;
        public int? CustomIntervalDays { get; set; }
        public long PrincipalAmountCents { get; set; }
        public long TotalInterestCents { get; set; }
        public int InstallmentsCount { get; set; }
        public DateTime FirstDueDate { get; set; }
        public DateTime ContractDate { get; set; }
        public string? Notes { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{ClientId}|{ContractNumber.Trim()}|{(int)ContractType}|{PrincipalAmountCents}|{TotalInterestCents}|{InstallmentsCount}|{FirstDueDate:yyyy-MM-dd}|{ContractDate:yyyy-MM-dd}|{(int)Periodicity}|{CustomIntervalDays?.ToString() ?? "null"}|{(Notes ?? string.Empty).Trim()}";
        }
    }

    public class CreateContractResponseDto
    {
        public string ContractId { get; set; } = string.Empty;
        public string ContractNumber { get; set; } = string.Empty;
        public CreditContractStatus Status { get; set; }
        public CreditPeriodicity Periodicity { get; set; } = CreditPeriodicity.Monthly;
        public int? CustomIntervalDays { get; set; }
        public long TotalAmountCents { get; set; }
        public int InstallmentsGenerated { get; set; }
    }

    public class DisburseContractRequestDto
    {
        public string ContractId { get; set; } = string.Empty;
        public DateTime DisbursementDate { get; set; }
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Pix;
        public string? Notes { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{ContractId}|{DisbursementDate:yyyy-MM-dd}|{(int)PaymentMethod}|{(Notes ?? string.Empty).Trim()}";
        }
    }

    public class DisburseContractResponseDto
    {
        public string DisbursementId { get; set; } = string.Empty;
        public string ContractId { get; set; } = string.Empty;
        public long DisbursedAmountCents { get; set; }
        public CreditContractStatus ContractStatus { get; set; }
    }

    public class CancelContractRequestDto
    {
        public string ContractId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{ContractId}|{Reason.Trim()}";
        }
    }

    public class CancelContractResponseDto
    {
        public string ContractId { get; set; } = string.Empty;
        public CreditContractStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class PaymentRequestDto
    {
        public string ContractId { get; set; } = string.Empty;
        public long AmountCents { get; set; }
        public DateTime PaymentDate { get; set; }
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Pix;
        public string? Notes { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{ContractId}|{AmountCents}|{PaymentDate:yyyy-MM-dd}|{(int)PaymentMethod}|{(Notes ?? string.Empty).Trim()}";
        }
    }

    public class PaymentResultDto
    {
        public string PaymentId { get; set; } = string.Empty;
        public string ContractId { get; set; } = string.Empty;
        public long AmountPaidCents { get; set; }
        public long TotalInterestAllocatedCents { get; set; }
        public long TotalPrincipalAllocatedCents { get; set; }
        public long SurplusGeneratedCents { get; set; }
        public string Status { get; set; } = "Success";
    }

    public class ConsumeSurplusRequestDto
    {
        public string SurplusBalanceId { get; set; } = string.Empty;
        public string? TargetInstallmentId { get; set; }
        public string? TargetCycleId { get; set; }
        public long AmountCents { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{SurplusBalanceId}|{TargetInstallmentId ?? string.Empty}|{TargetCycleId ?? string.Empty}|{AmountCents}";
        }
    }

    public class ReverseSurplusConsumptionRequestDto
    {
        public string ConsumptionId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{ConsumptionId}|{Reason.Trim()}";
        }
    }

    public class AuthorizeRefundRequestDto
    {
        public string PaymentId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{PaymentId}|{Reason.Trim()}";
        }
    }

    public class SettleRefundRequestDto
    {
        public string RefundId { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{RefundId.Trim()}";
        }
    }

    public class CancelRefundAuthRequestDto
    {
        public string RefundId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{RefundId}|{Reason.Trim()}";
        }
    }

    public class ConsumeSurplusResponseDto
    {
        public string ConsumptionId { get; set; } = string.Empty;
        public string SurplusBalanceId { get; set; } = string.Empty;
        public string? TargetInstallmentId { get; set; }
        public long ConsumedAmountCents { get; set; }
        public long InterestConsumedCents { get; set; }
        public long PrincipalConsumedCents { get; set; }
        public long RemainingSurplusCents { get; set; }
        public CreditInstallmentStatus TargetInstallmentStatus { get; set; }
    }

    public class ReverseSurplusConsumptionResponseDto
    {
        public string ReversalId { get; set; } = string.Empty;
        public string ConsumptionId { get; set; } = string.Empty;
        public long ReversedAmountCents { get; set; }
        public long RestoredSurplusBalanceCents { get; set; }
        public CreditInstallmentStatus TargetInstallmentNewStatus { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class CreditSurplusBalanceDto
    {
        public string Id { get; set; } = string.Empty;
        public string ContractId { get; set; } = string.Empty;
        public long OriginalSurplusCents { get; set; }
        public long RemainingSurplusCents { get; set; }
        public CreditSurplusStatus Status { get; set; }
    }

    public class CreditPaymentDto
    {
        public string Id { get; set; } = string.Empty;
        public string ContractId { get; set; } = string.Empty;
        public DateTime PaymentDate { get; set; }
        public long AmountCents { get; set; }
        public CreditPaymentStatus Status { get; set; }
    }

    public class AuthorizePaymentRefundResponseDto
    {
        public string RefundId { get; set; } = string.Empty;
        public string PaymentId { get; set; } = string.Empty;
        public string DevolutionId { get; set; } = string.Empty;
        public long AuthorizedAmountCents { get; set; }
        public CreditRefundStatus Status { get; set; }
        public int ReopenedInstallmentsCount { get; set; }
        public DateTime AuthorizedAtUtc { get; set; }
    }

    public class SettleRefundResponseDto
    {
        public string RefundId { get; set; } = string.Empty;
        public string DevolutionId { get; set; } = string.Empty;
        public string CashOutflowId { get; set; } = string.Empty;
        public long AmountCents { get; set; }
        public CreditRefundStatus Status { get; set; }
        public DateTime SettledAtUtc { get; set; }
    }

    public class CancelRefundAuthResponseDto
    {
        public string RefundId { get; set; } = string.Empty;
        public CreditRefundStatus Status { get; set; }
        public DateTime CancelledAtUtc { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class AuthorizeDevolutionRequestDto
    {
        public string PaymentId { get; set; } = string.Empty;
        public long AmountCents { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{PaymentId}|{AmountCents}|{Reason.Trim()}";
        }
    }

    public class AuthorizeDevolutionResponseDto
    {
        public string DevolutionId { get; set; } = string.Empty;
        public string PaymentId { get; set; } = string.Empty;
        public string ContractId { get; set; } = string.Empty;
        public long AuthorizedAmountCents { get; set; }
        public CreditDevolutionStatus Status { get; set; }
        public DateTime AuthorizedAtUtc { get; set; }
    }

    public class SettleDevolutionRequestDto
    {
        public string DevolutionId { get; set; } = string.Empty;
        public DateTime? SettlementDate { get; set; }
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Pix;
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{DevolutionId}|{(SettlementDate.HasValue ? SettlementDate.Value.ToString("yyyy-MM-dd") : string.Empty)}|{(int)PaymentMethod}";
        }
    }

    public class SettleDevolutionResponseDto
    {
        public string DevolutionId { get; set; } = string.Empty;
        public string CashOutflowId { get; set; } = string.Empty;
        public long AmountCents { get; set; }
        public CreditDevolutionStatus Status { get; set; }
        public DateTime SettledAtUtc { get; set; }
        public DateTime OutflowDate { get; set; }
    }

    public class CancelDevolutionRequestDto
    {
        public string DevolutionId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;

        public string Canonicalize()
        {
            return $"{DevolutionId}|{Reason.Trim()}";
        }
    }

    public class CancelDevolutionResponseDto
    {
        public string DevolutionId { get; set; } = string.Empty;
        public CreditDevolutionStatus Status { get; set; }
        public DateTime CancelledAtUtc { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class CreditDevolutionDto
    {
        public string Id { get; set; } = string.Empty;
        public string? RefundId { get; set; }
        public string PaymentId { get; set; } = string.Empty;
        public string ContractId { get; set; } = string.Empty;
        public long AuthorizedAmountCents { get; set; }
        public CreditDevolutionStatus Status { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime AuthorizedAtUtc { get; set; }
        public DateTime? SettledAtUtc { get; set; }
        public DateTime? CancelledAtUtc { get; set; }
    }

    public class CreditCashOutflowDto
    {
        public string Id { get; set; } = string.Empty;
        public string DevolutionId { get; set; } = string.Empty;
        public string PaymentId { get; set; } = string.Empty;
        public string ContractId { get; set; } = string.Empty;
        public long AmountCents { get; set; }
        public DateTime OutflowDate { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
    }

    public class PaymentPreviewDto
    {
        public long TotalAmountCents { get; set; }
        public long TotalInterestAllocatedCents { get; set; }
        public long TotalPrincipalAllocatedCents { get; set; }
        public long SurplusGeneratedCents { get; set; }
        public List<PaymentPreviewInstallmentDto> Installments { get; set; } = new();
    }

    public class PaymentPreviewInstallmentDto
    {
        public int InstallmentNumber { get; set; }
        public DateTime DueDate { get; set; }
        public long TotalAmountCents { get; set; }
        public long OutstandingBeforeCents { get; set; }
        public long InterestAllocatedCents { get; set; }
        public long PrincipalAllocatedCents { get; set; }
        public long AllocatedAmountCents { get; set; }
        public long RemainingAfterCents { get; set; }
    }
}
