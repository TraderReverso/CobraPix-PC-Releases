using System;

namespace CobraPix_PC.Models.Credit
{
    /// <summary>
    /// Tipos de contratos de crédito disponíveis no módulo Crédito & Giro.
    /// </summary>
    public enum CreditContractType
    {
        /// <summary>
        /// Empréstimo com parcelas fixas predeterminadas (CDC / Mútuo Fixo).
        /// </summary>
        FixedInstallments = 1,

        /// <summary>
        /// Linha de capital de giro rotativo com ciclos periódicos de apuração.
        /// </summary>
        RevolvingCredit = 2
    }

    /// <summary>
    /// Ciclo de vida do contrato de crédito.
    /// </summary>
    public enum CreditContractStatus
    {
        /// <summary>
        /// Contrato cadastrado, aguardando liberação de desembolso.
        /// </summary>
        Draft = 1,

        /// <summary>
        /// Desembolso efetuado e contrato ativo com obrigações em aberto.
        /// </summary>
        Active = 2,

        /// <summary>
        /// Contrato totalmente liquidado (todas as obrigações pagas).
        /// </summary>
        Completed = 3,

        /// <summary>
        /// Contrato cancelado antes de qualquer pagamento (ou cancelamento permitido).
        /// </summary>
        Cancelled = 4,

        /// <summary>
        /// Contrato inadimplente com atraso crítico.
        /// </summary>
        Defaulted = 5
    }

    /// <summary>
    /// Estado da obrigação / parcela de crédito.
    /// </summary>
    public enum CreditInstallmentStatus
    {
        /// <summary>
        /// Obrigação pendente (nenhuma amortização ativa).
        /// </summary>
        Pending = 1,

        /// <summary>
        /// Obrigação totalmente quitada.
        /// </summary>
        Paid = 2,

        /// <summary>
        /// Obrigação com amortização parcial ativa.
        /// </summary>
        PartiallyPaid = 3,

        /// <summary>
        /// Obrigação cancelada (por cancelamento de contrato).
        /// </summary>
        Cancelled = 4
    }

    /// <summary>
    /// Estado do ciclo de crédito rotativo.
    /// </summary>
    public enum CreditCycleStatus
    {
        Open = 1,
        Closed = 2,
        Paid = 3,
        PartiallyPaid = 4,
        Overdue = 5
    }

    /// <summary>
    /// Estado do pagamento no módulo de crédito.
    /// </summary>
    public enum CreditPaymentStatus
    {
        /// <summary>
        /// Pagamento ativo e efetivado.
        /// </summary>
        Active = 1,

        /// <summary>
        /// Estorno autorizado: dívida reaberta, saída de caixa pendente de liquidação.
        /// </summary>
        ReversalAuthorized = 2,

        /// <summary>
        /// Estorno liquidado: saída de caixa efetivada, estado terminal.
        /// </summary>
        ReversalSettled = 3
    }

    /// <summary>
    /// Estado do saldo de excedente gerado por pagamento a maior.
    /// </summary>
    public enum CreditSurplusStatus
    {
        Available = 1,
        PartiallyConsumed = 2,
        FullyConsumed = 3
    }

    /// <summary>
    /// Estado do estorno de pagamento.
    /// </summary>
    public enum CreditRefundStatus
    {
        Authorized = 1,
        Settled = 2,
        Cancelled = 3
    }

    /// <summary>
    /// Ciclo de vida da intenção de pagamento / operação idempotente.
    /// </summary>
    public enum CreditIntentStatus
    {
        InFlight = 1,
        Completed = 2,
        Cancelled = 3
    }

    /// <summary>
    /// Ciclo de vida da devolução de caixa: Authorized -> Settled ou Authorized -> Cancelled.
    /// </summary>
    public enum CreditDevolutionStatus
    {
        Authorized = 1,
        Settled = 2,
        Cancelled = 3
    }
}
