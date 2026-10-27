using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly CobraPixDbContext _context;

        public PaymentService(CobraPixDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<InstallmentPayment> RegisterPaymentAsync(
            int installmentId,
            decimal amountPaid,
            DateTime paymentDate,
            PaymentMethod paymentMethod,
            string? notes = null)
        {
            if (installmentId <= 0)
            {
                throw new BusinessRuleException("Identificador de parcela inválido.");
            }

            var installment = await _context.Installments
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == installmentId);

            if (installment == null)
            {
                throw new BusinessRuleException($"Parcela com ID {installmentId} não encontrada.");
            }

            decimal currentTotalPaid = installment.Payments.Sum(p => p.AmountPaid);

            // Validação estrita via PaymentLogic
            var validation = PaymentLogic.ValidatePayment(installment.Amount, currentTotalPaid, amountPaid);
            if (!validation.IsValid)
            {
                throw new BusinessRuleException(validation.Reason!);
            }

            var payment = new InstallmentPayment
            {
                InstallmentId = installmentId,
                AmountPaid = amountPaid,
                PaymentDate = paymentDate,
                PaymentMethod = paymentMethod,
                Notes = notes?.Trim()
            };

            _context.InstallmentPayments.Add(payment);

            // Atualiza o status da parcela com base no novo saldo
            decimal remainingBalance = installment.Amount - currentTotalPaid;
            decimal newRemainingBalance = remainingBalance - amountPaid;

            if (newRemainingBalance == 0)
            {
                installment.Status = InstallmentStatus.Paid;
                installment.PaidDate = paymentDate;
            }
            else
            {
                // Parcela parcialmente paga: permanece PENDING se não venceu, ou OVERDUE se já venceu
                if (installment.DueDate.Date < DateTime.Today)
                {
                    installment.Status = InstallmentStatus.Overdue;
                }
                else
                {
                    installment.Status = InstallmentStatus.Pending;
                }

                installment.PaidDate = null;
            }

            await _context.SaveChangesAsync();
            DiagnosticLogger.Instance.LogInformation("PaymentService",
                $"Pagamento registrado para parcela #{installmentId}. Método: {paymentMethod}. Status resultante: {installment.Status}");
            return payment;
        }

        public async Task<int> UpdateOverdueInstallmentsAsync(DateTime? referenceDate = null)
        {
            var today = (referenceDate ?? DateTime.Today).Date;

            // Seleciona somente parcelas que estão pendentes e com vencimento anterior à data de referência
            // Parcelas PAID jamais são alteradas
            var overdueInstallments = await _context.Installments
                .Where(i => i.Status == InstallmentStatus.Pending && i.DueDate.Date < today)
                .ToListAsync();

            if (overdueInstallments.Count == 0)
            {
                return 0;
            }

            foreach (var installment in overdueInstallments)
            {
                installment.Status = InstallmentStatus.Overdue;
            }

            await _context.SaveChangesAsync();
            return overdueInstallments.Count;
        }

        public async Task<List<Installment>> GetAllInstallmentsAsync()
        {
            return await _context.Installments
                .AsNoTracking()
                .Include(i => i.Sale)
                    .ThenInclude(s => s.Client)
                .Include(i => i.Payments)
                .OrderBy(i => i.DueDate)
                .ThenBy(i => i.InstallmentNumber)
                .ToListAsync();
        }

        public async Task<List<Installment>> GetPendingInstallmentsAsync()
        {
            return await _context.Installments
                .AsNoTracking()
                .Where(i => i.Status == InstallmentStatus.Pending)
                .Include(i => i.Sale)
                    .ThenInclude(s => s.Client)
                .Include(i => i.Payments)
                .OrderBy(i => i.DueDate)
                .ToListAsync();
        }

        public async Task<List<Installment>> GetOverdueInstallmentsAsync()
        {
            return await _context.Installments
                .AsNoTracking()
                .Where(i => i.Status == InstallmentStatus.Overdue)
                .Include(i => i.Sale)
                    .ThenInclude(s => s.Client)
                .Include(i => i.Payments)
                .OrderBy(i => i.DueDate)
                .ToListAsync();
        }

        public async Task<List<Installment>> GetPaidInstallmentsAsync()
        {
            return await _context.Installments
                .AsNoTracking()
                .Where(i => i.Status == InstallmentStatus.Paid)
                .Include(i => i.Sale)
                    .ThenInclude(s => s.Client)
                .Include(i => i.Payments)
                .OrderByDescending(i => i.PaidDate)
                .ToListAsync();
        }

        public async Task<List<InstallmentPayment>> GetPaymentsByInstallmentIdAsync(int installmentId)
        {
            return await _context.InstallmentPayments
                .AsNoTracking()
                .Where(p => p.InstallmentId == installmentId)
                .OrderBy(p => p.PaymentDate)
                .ToListAsync();
        }

        public async Task<decimal> GetTotalPaidForInstallmentAsync(int installmentId)
        {
            return await _context.InstallmentPayments
                .Where(p => p.InstallmentId == installmentId)
                .SumAsync(p => p.AmountPaid);
        }

        public async Task<decimal> GetRemainingBalanceForInstallmentAsync(int installmentId)
        {
            var installment = await _context.Installments
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == installmentId);

            if (installment == null)
            {
                throw new BusinessRuleException($"Parcela com ID {installmentId} não encontrada.");
            }

            decimal totalPaid = installment.Payments.Sum(p => p.AmountPaid);
            return Math.Max(0m, installment.Amount - totalPaid);
        }

        public async Task<PaymentLogic.ClientGlobalStatement> GetClientGlobalStatementAsync(int clientId)
        {
            var clientSales = await _context.Sales
                .Where(s => s.ClientId == clientId)
                .Select(s => s.Id)
                .ToListAsync();

            var clientInstallments = await _context.Installments
                .Where(i => clientSales.Contains(i.SaleId))
                .Include(i => i.Payments)
                .ToListAsync();

            var mapped = clientInstallments.Select(i => (
                value: i.Amount,
                paidAmount: i.Payments.Sum(p => p.AmountPaid),
                status: i.Status,
                id: i.Id
            ));

            return PaymentLogic.CalculateClientGlobalStatement(mapped);
        }

        public async Task<PaymentLogic.PayableFinancialStatement> GetFinancialStatementAsync(int installmentId, DateTime? referenceTime = null)
        {
            var installment = await _context.Installments
                .Include(i => i.Payments)
                .Include(i => i.Sale)
                .FirstOrDefaultAsync(i => i.Id == installmentId);

            if (installment == null)
            {
                throw new BusinessRuleException($"Parcela com ID {installmentId} não encontrada.");
            }

            decimal totalPaid = installment.Payments.Sum(p => p.AmountPaid);

            // Obter configuração de juros de mora se disponível
            var overdueSetting = await _context.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "App.OverdueInterestRate");
            double monthlyRate = double.TryParse(overdueSetting?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : 0.0;

            var config = new PaymentLogic.OverdueInterestConfig(
                enabled: monthlyRate > 0.0,
                monthlyRate: monthlyRate,
                toleranceDays: 0,
                startMinuteOfDay: 540);

            return PaymentLogic.GenerateFinancialStatement(
                installment.Amount,
                installment.InstallmentNumber,
                installment.DueDate,
                totalPaid,
                installment.Sale,
                config,
                referenceTime);
        }

        public async Task<bool> DeleteInstallmentAsync(int installmentId)
        {
            if (installmentId <= 0)
            {
                throw new BusinessRuleException("Identificador de parcela inválido.");
            }

            var installment = await _context.Installments
                .Include(i => i.Payments)
                .Include(i => i.Sale)
                .FirstOrDefaultAsync(i => i.Id == installmentId);

            if (installment == null)
            {
                throw new BusinessRuleException($"Parcela com ID {installmentId} não encontrada.");
            }

            if (installment.Payments.Any(p => p.AmountPaid > 0) || installment.Status == InstallmentStatus.Paid)
            {
                throw new BusinessRuleException(
                    "Não é possível excluir uma parcela com pagamentos registrados. Estorne os pagamentos no histórico primeiro.");
            }

            int saleId = installment.SaleId;
            var sale = installment.Sale;

            _context.Installments.Remove(installment);
            await _context.SaveChangesAsync();

            if (sale != null)
            {
                var remaining = await _context.Installments
                    .Where(i => i.SaleId == saleId)
                    .ToListAsync();

                var (newTotal, newCount) = PaymentLogic.RecalculateSaleAfterInstallmentDeletion(
                    remaining.Select(i => i.Amount));

                sale.TotalAmount = newTotal;
                sale.InstallmentCount = newCount;
                await _context.SaveChangesAsync();

                DiagnosticLogger.Instance.LogInformation("PaymentService",
                    $"Parcela #{installmentId} excluída. Venda #{saleId} recalculada: {newCount} parcelas restantes, novo total: {newTotal:C2}.");
            }

            return true;
        }

        public async Task<RefundResult> RefundInstallmentPaymentAsync(int paymentId, decimal? refundAmount = null, string? reason = null)
        {
            if (paymentId <= 0)
            {
                return RefundResult.Fail("Identificador de pagamento inválido.");
            }

            var payment = await _context.InstallmentPayments
                .Include(p => p.Installment)
                    .ThenInclude(i => i.Sale)
                        .ThenInclude(s => s.Client)
                .FirstOrDefaultAsync(p => p.Id == paymentId);

            if (payment == null)
            {
                return RefundResult.Fail($"Pagamento com ID {paymentId} não encontrado.");
            }

            if (payment.AmountPaid <= 0m || (payment.Notes != null && payment.Notes.StartsWith("[ESTORNO:")))
            {
                return RefundResult.Fail("Não é possível estornar um registro que já é de estorno.");
            }

            var installment = payment.Installment;
            if (installment == null)
            {
                return RefundResult.Fail("Parcela vinculada ao pagamento não foi encontrada.");
            }

            // Busca todos os estornos já registrados para este pagamento específico
            var existingRefunds = await _context.InstallmentPayments
                .Where(p => p.InstallmentId == installment.Id &&
                            p.AmountPaid < 0m &&
                            p.Notes != null &&
                            p.Notes.Contains($"[ESTORNO:OriginalPaymentId={payment.Id};"))
                .ToListAsync();

            decimal alreadyRefunded = Math.Abs(existingRefunds.Sum(r => r.AmountPaid));
            decimal targetRefund = refundAmount.HasValue && refundAmount.Value > 0m
                ? refundAmount.Value
                : (payment.AmountPaid - alreadyRefunded);

            var validation = PaymentLogic.ValidateRefund(payment.AmountPaid, alreadyRefunded, targetRefund);
            if (!validation.IsValid)
            {
                return RefundResult.Fail(validation.Reason!);
            }

            // Registra novo pagamento negativo representando o estorno no histórico financeiro
            string reasonText = string.IsNullOrWhiteSpace(reason) ? "Estorno de pagamento realizado." : reason.Trim();
            var refundPayment = new InstallmentPayment
            {
                InstallmentId = installment.Id,
                AmountPaid = -targetRefund,
                PaymentDate = DateTime.Now,
                PaymentMethod = payment.PaymentMethod,
                Notes = $"[ESTORNO:OriginalPaymentId={payment.Id};Valor={targetRefund.ToString("F2", CultureInfo.InvariantCulture)}] {reasonText}"
            };

            _context.InstallmentPayments.Add(refundPayment);

            // Atualiza anotações do pagamento original mantendo rastreabilidade e sem apagar o histórico
            string refundStamp = $"[ESTORNADO em {DateTime.Now:dd/MM/yyyy HH:mm}: {targetRefund.ToString("C2", new CultureInfo("pt-BR"))}]";
            if (string.IsNullOrWhiteSpace(payment.Notes))
            {
                payment.Notes = refundStamp;
            }
            else if (!payment.Notes.Contains(refundStamp))
            {
                payment.Notes = $"{payment.Notes} {refundStamp}".Trim();
            }

            // Recalcula o status e valores da parcela
            var allPayments = await _context.InstallmentPayments
                .Where(p => p.InstallmentId == installment.Id)
                .ToListAsync();

            decimal netPaid = allPayments.Sum(p => p.AmountPaid) - targetRefund;
            if (netPaid < 0m) netPaid = 0m;

            var (newPaid, newStatus, newPaidDate) = PaymentLogic.ApplyRefund(
                installment.Amount,
                installment.DueDate,
                allPayments.Sum(p => p.AmountPaid),
                targetRefund,
                installment.PaidDate,
                DateTime.Today
            );

            installment.Status = newStatus;
            installment.PaidDate = newPaidDate;

            await _context.SaveChangesAsync();

            int clientId = installment.Sale?.ClientId ?? 0;
            var statement = clientId > 0
                ? await GetClientGlobalStatementAsync(clientId)
                : new PaymentLogic.ClientGlobalStatement(0m, 0m, 0m);

            DiagnosticLogger.Instance.LogInformation("PaymentService",
                $"Estorno de {targetRefund:C2} efetuado para parcela #{installment.Id} (Pagamento #{payment.Id}). Novo status da parcela: {installment.Status}");

            return RefundResult.Ok(targetRefund, refundPayment, installment, statement);
        }

        public async Task<RefundResult> RefundMonthlyPaymentAsync(int monthlyPaymentId, decimal? refundAmount = null, string? reason = null)
        {
            if (monthlyPaymentId <= 0)
            {
                return RefundResult.Fail("Identificador de mensalidade inválido.");
            }

            var payment = await _context.MonthlyPayments
                .Include(p => p.MonthlyCustomer)
                    .ThenInclude(m => m.Client)
                .FirstOrDefaultAsync(p => p.Id == monthlyPaymentId);

            if (payment == null)
            {
                return RefundResult.Fail($"Pagamento de mensalidade #{monthlyPaymentId} não encontrado.");
            }

            if (payment.Amount <= 0m || (payment.Notes != null && payment.Notes.StartsWith("[ESTORNO:")))
            {
                return RefundResult.Fail("Não é possível estornar um registro que já é de estorno.");
            }

            var existingRefunds = await _context.MonthlyPayments
                .Where(p => p.MonthlyCustomerId == payment.MonthlyCustomerId &&
                            p.Amount < 0m &&
                            p.Notes != null &&
                            p.Notes.Contains($"[ESTORNO:OriginalMonthlyPaymentId={payment.Id};"))
                .ToListAsync();

            decimal alreadyRefunded = Math.Abs(existingRefunds.Sum(r => r.Amount));
            decimal targetRefund = refundAmount.HasValue && refundAmount.Value > 0m
                ? refundAmount.Value
                : (payment.Amount - alreadyRefunded);

            var validation = PaymentLogic.ValidateRefund(payment.Amount, alreadyRefunded, targetRefund);
            if (!validation.IsValid)
            {
                return RefundResult.Fail(validation.Reason!);
            }

            string reasonText = string.IsNullOrWhiteSpace(reason) ? "Estorno de mensalidade realizado." : reason.Trim();
            var refundPayment = new MonthlyPayment
            {
                MonthlyCustomerId = payment.MonthlyCustomerId,
                Amount = -targetRefund,
                PaymentDate = DateTime.Now,
                ReferenceMonth = payment.ReferenceMonth,
                ReferenceYear = payment.ReferenceYear,
                PaymentMethod = payment.PaymentMethod,
                Notes = $"[ESTORNO:OriginalMonthlyPaymentId={payment.Id};Valor={targetRefund.ToString("F2", CultureInfo.InvariantCulture)}] {reasonText}"
            };

            _context.MonthlyPayments.Add(refundPayment);

            string refundStamp = $"[ESTORNADO em {DateTime.Now:dd/MM/yyyy HH:mm}: {targetRefund.ToString("C2", new CultureInfo("pt-BR"))}]";
            if (string.IsNullOrWhiteSpace(payment.Notes))
            {
                payment.Notes = refundStamp;
            }
            else if (!payment.Notes.Contains(refundStamp))
            {
                payment.Notes = $"{payment.Notes} {refundStamp}".Trim();
            }

            await _context.SaveChangesAsync();

            int clientId = payment.MonthlyCustomer?.ClientId ?? 0;
            var statement = clientId > 0
                ? await GetClientGlobalStatementAsync(clientId)
                : new PaymentLogic.ClientGlobalStatement(0m, 0m, 0m);

            DiagnosticLogger.Instance.LogInformation("PaymentService",
                $"Estorno de mensalidade de {targetRefund:C2} efetuado para mensalista #{payment.MonthlyCustomerId} (Pagamento #{payment.Id}).");

            return RefundResult.OkMonthly(targetRefund, refundPayment, statement);
        }
    }
}
