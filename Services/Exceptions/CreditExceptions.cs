using System;

namespace CobraPix_PC.Services.Exceptions
{
    public class ProLicenseRequiredException : InvalidOperationException
    {
        public ProLicenseRequiredException(string message = "O módulo Crédito & Giro é exclusivo do CobraPix PRO.")
            : base(message)
        {
        }
    }

    public class IdempotencyConflictException : InvalidOperationException
    {
        public IdempotencyConflictException(string message = "Chave de idempotência reutilizada com payload divergente.")
            : base(message)
        {
        }
    }

    public class FinancialInvariantViolationException : InvalidOperationException
    {
        public FinancialInvariantViolationException(string message)
            : base(message)
        {
        }
    }

    public class CreditOperationPreconditionException : InvalidOperationException
    {
        public CreditOperationPreconditionException(string message)
            : base(message)
        {
        }
    }

    public class DependentSurplusConsumptionsActiveException : CreditOperationPreconditionException
    {
        public DependentSurplusConsumptionsActiveException(string message = "Não é permitido estornar pagamento com consumos ativos de excedente. Reverter os consumos primeiro.")
            : base(message)
        {
        }
    }

    public class PaymentAlreadyRefundedException : InvalidOperationException
    {
        public PaymentAlreadyRefundedException(string message = "Pagamento já possui estorno registrado ou não está no estado Ativo.")
            : base(message)
        {
        }
    }

    public class ExceededEligibleDevolutionAmountException : InvalidOperationException
    {
        public ExceededEligibleDevolutionAmountException(string message = "O valor solicitado para devolução excede o valor elegível do pagamento.")
            : base(message)
        {
        }
    }

    public class DevolutionInvalidStateException : InvalidOperationException
    {
        public DevolutionInvalidStateException(string message)
            : base(message)
        {
        }
    }
}
