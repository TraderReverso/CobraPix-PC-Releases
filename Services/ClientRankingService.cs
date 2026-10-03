using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;
using CobraPix_PC.ViewModels.Modules;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Serviço oficial de cálculo da classificação e ranqueamento de clientes do CobraPix PC.
    /// Opera com regras 100% determinísticas e objetivas baseadas na pontualidade e comportamento financeiro,
    /// sem scores arbitrários, sem influência de ordem alfabética ou faturamento isolado, e sem posições competitivas (1º/2º/3º).
    /// </summary>
    public class ClientRankingService : IClientRankingService
    {
        private readonly CobraPixDbContext _context;

        public ClientRankingService(CobraPixDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public ClientClassification CalculateClassification(Client client)
        {
            var classification = new ClientClassification();

            if (client == null)
            {
                return classification;
            }

            int onTimePaidCount = 0;
            int latePaidCount = 0;
            int currentOverdueCount = 0;
            decimal currentOverdueAmount = 0m;
            decimal totalPaidAmount = 0m;
            int totalPaymentsCount = 0;

            DateTime today = DateTime.Today;

            // 1. Análise de Parcelas de Vendas
            if (client.Sales != null && client.Sales.Count > 0)
            {
                foreach (var sale in client.Sales)
                {
                    if (sale.Installments == null) continue;

                    foreach (var inst in sale.Installments)
                    {
                        decimal paidSoFar = inst.Payments != null && inst.Payments.Count > 0
                            ? inst.Payments.Sum(p => p.AmountPaid)
                            : 0m;
                        decimal remaining = Math.Max(0m, inst.Amount - paidSoFar);
                        bool isPaid = inst.Status == InstallmentStatus.Paid || remaining == 0m;

                        if (isPaid)
                        {
                            totalPaidAmount += (inst.Amount - remaining);

                            // Avaliação de pontualidade da parcela paga
                            if (inst.Payments != null && inst.Payments.Count > 0)
                            {
                                totalPaymentsCount += inst.Payments.Count;
                                DateTime lastPaymentDate = inst.Payments.Max(p => p.PaymentDate);

                                if (lastPaymentDate.Date <= inst.DueDate.Date)
                                {
                                    onTimePaidCount++;
                                }
                                else
                                {
                                    latePaidCount++;
                                }
                            }
                            else
                            {
                                // Parcela marcada como paga sem registro detalhado de pagamentos
                                totalPaymentsCount++;
                                onTimePaidCount++;
                            }
                        }
                        else
                        {
                            // Parcela em aberto
                            bool isOverdue = inst.Status == InstallmentStatus.Overdue || inst.DueDate.Date < today;

                            if (isOverdue)
                            {
                                currentOverdueCount++;
                                currentOverdueAmount += remaining > 0 ? remaining : inst.Amount;
                            }

                            if (inst.Payments != null && inst.Payments.Count > 0)
                            {
                                totalPaymentsCount += inst.Payments.Count;
                                totalPaidAmount += paidSoFar;
                            }
                        }
                    }
                }
            }

            // 2. Análise de Mensalidades (quando aplicável)
            if (client.MonthlyCustomer != null && client.MonthlyCustomer.Payments != null)
            {
                var monthly = client.MonthlyCustomer;
                totalPaymentsCount += monthly.Payments.Count;
                totalPaidAmount += monthly.Payments.Sum(p => p.Amount);

                foreach (var payment in monthly.Payments)
                {
                    // Mensalidades registradas como pagas são consideradas pontuais
                    onTimePaidCount++;
                }

                // Verifica se há mensalidade vencida não paga no mês atual
                if (monthly.IsActive)
                {
                    int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
                    DateTime expectedDue = new DateTime(today.Year, today.Month, Math.Min(monthly.DueDay, daysInMonth));
                    if (expectedDue < today)
                    {
                        bool hasPaidThisMonth = monthly.Payments.Any(p => p.ReferenceYear == today.Year && p.ReferenceMonth == today.Month);
                        if (!hasPaidThisMonth)
                        {
                            currentOverdueCount++;
                            currentOverdueAmount += monthly.MonthlyAmount;
                        }
                    }
                }
            }

            int totalPaidCount = onTimePaidCount + latePaidCount;
            int totalEvaluatedCount = totalPaidCount + currentOverdueCount;

            classification.TotalPaidCount = totalPaidCount;
            classification.OnTimePaidCount = onTimePaidCount;
            classification.LatePaidCount = latePaidCount;
            classification.CurrentOverdueCount = currentOverdueCount;
            classification.CurrentOverdueAmount = currentOverdueAmount;

            // 3. Atribuição Determinística da Classificação
            if (totalEvaluatedCount == 0)
            {
                // Cliente sem histórico suficiente de obrigações vencidas ou pagas
                classification.Rank = ClientRank.C;
                classification.Description = "Médio";
                classification.ColorHex = "#EAB308";
                classification.DetailReason = "Sem histórico suficiente de pagamento";
                classification.HasSufficientHistory = false;
                classification.OnTimePercentage = 0m;
                return classification;
            }

            classification.HasSufficientHistory = true;
            decimal onTimeRate = Math.Round((decimal)onTimePaidCount / totalEvaluatedCount * 100m, 1);
            classification.OnTimePercentage = onTimeRate;

            // S — Pagador Ouro: Nível máximo de pontualidade
            // Requer histórico consistente comprovado (ao menos 2 pagamentos), ZERO atrasos atuais e 100% no prazo
            if (currentOverdueCount == 0 && latePaidCount == 0 && totalPaidCount >= 2)
            {
                classification.Rank = ClientRank.S;
                classification.Description = "Pagador Ouro";
                classification.ColorHex = "#F59E0B";
                classification.DetailReason = "Histórico consistente com 100% de pontualidade";
                return classification;
            }

            // A — Top: Excelente pontualidade (>= 80%) e zero atrasos atuais
            if (currentOverdueCount == 0 && onTimeRate >= 80m && totalPaidCount >= 1)
            {
                classification.Rank = ClientRank.A;
                classification.Description = "Top";
                classification.ColorHex = "#10B981";
                classification.DetailReason = $"{onTimeRate:0.#}% das obrigações pagas no prazo";
                return classification;
            }

            // B — Bom: Boa pontualidade (>= 65%) com no máximo 1 atraso pontual
            if (currentOverdueCount <= 1 && onTimeRate >= 65m)
            {
                classification.Rank = ClientRank.B;
                classification.Description = "Bom";
                classification.ColorHex = "#3B82F6";
                classification.DetailReason = $"{onTimeRate:0.#}% de pontualidade com boa regularidade";
                return classification;
            }

            // C — Médio: Pontualidade intermediária (>= 45%)
            if (onTimeRate >= 45m)
            {
                classification.Rank = ClientRank.C;
                classification.Description = "Médio";
                classification.ColorHex = "#EAB308";
                classification.DetailReason = $"{onTimeRate:0.#}% de pontualidade; histórico intermediário";
                return classification;
            }

            // D — Mais ou menos: Histórico irregular com atrasos recorrentes (>= 20%)
            if (onTimeRate >= 20m || (totalPaidCount > 0 && currentOverdueCount <= 2))
            {
                classification.Rank = ClientRank.D;
                classification.Description = "Mais ou menos";
                classification.ColorHex = "#F97316";
                classification.DetailReason = "Atrasos recorrentes nas obrigações financeiras";
                return classification;
            }

            // E — Ruim: Inadimplência crônica ou múltiplas obrigações em atraso sem quitação
            classification.Rank = ClientRank.E;
            classification.Description = "Ruim";
            classification.ColorHex = "#EF4444";
            classification.DetailReason = "Inadimplência recorrente ou histórico crítico de atraso";
            return classification;
        }

        public async Task<List<RanqueamentoItemViewModel>> GetRankedClientsAsync()
        {
            var clients = await _context.Clients
                .AsNoTracking()
                .Include(c => c.Sales)
                    .ThenInclude(s => s.Installments)
                        .ThenInclude(i => i.Payments)
                .Include(c => c.MonthlyCustomer)
                    .ThenInclude(m => m!.Payments)
                .ToListAsync();

            var rankedItems = new List<RanqueamentoItemViewModel>();

            foreach (var client in clients)
            {
                var classification = CalculateClassification(client);

                bool isQuitado = ProService.IsClientQuitado(client);
                bool isActive = ProService.IsClientActive(client);

                int salesCount = client.Sales?.Count ?? 0;
                decimal totalSoldAmount = client.Sales?.Sum(s => s.TotalAmount) ?? 0m;

                int paidInstallmentsCount = client.Sales?
                    .SelectMany(s => s.Installments ?? Enumerable.Empty<Installment>())
                    .Count(i => i.Status == InstallmentStatus.Paid || (i.Amount - (i.Payments != null ? i.Payments.Sum(p => p.AmountPaid) : 0m)) <= 0m) ?? 0;

                int openInstallmentsCount = client.Sales?
                    .SelectMany(s => s.Installments ?? Enumerable.Empty<Installment>())
                    .Count(i => i.Status != InstallmentStatus.Paid && (i.Amount - (i.Payments != null ? i.Payments.Sum(p => p.AmountPaid) : 0m)) > 0m) ?? 0;

                decimal totalOpenAmount = client.Sales?
                    .SelectMany(s => s.Installments ?? Enumerable.Empty<Installment>())
                    .Where(i => i.Status != InstallmentStatus.Paid)
                    .Sum(i => Math.Max(0m, i.Amount - (i.Payments != null ? i.Payments.Sum(p => p.AmountPaid) : 0m))) ?? 0m;

                decimal totalPaidAmount = (client.Sales?
                    .SelectMany(s => s.Installments ?? Enumerable.Empty<Installment>())
                    .SelectMany(i => i.Payments ?? Enumerable.Empty<InstallmentPayment>())
                    .Sum(p => p.AmountPaid) ?? 0m) +
                    (client.MonthlyCustomer?.Payments?.Sum(p => p.Amount) ?? 0m);

                int paymentsCount = (client.Sales?
                    .SelectMany(s => s.Installments ?? Enumerable.Empty<Installment>())
                    .SelectMany(i => i.Payments ?? Enumerable.Empty<InstallmentPayment>())
                    .Count() ?? 0) +
                    (client.MonthlyCustomer?.Payments?.Count ?? 0);

                DateTime? lastPurchaseDate = client.Sales?.Any() == true
                    ? client.Sales.Max(s => s.SaleDate)
                    : null;

                rankedItems.Add(new RanqueamentoItemViewModel
                {
                    ClientId = client.Id,
                    ClientName = client.Name,
                    Phone = client.Phone,
                    SalesCount = salesCount,
                    TotalSoldAmount = totalSoldAmount,
                    PaidInstallmentsCount = paidInstallmentsCount,
                    TotalPaidAmount = totalPaidAmount,
                    OpenInstallmentsCount = openInstallmentsCount,
                    TotalOpenAmount = totalOpenAmount,
                    PaymentsCount = paymentsCount,
                    LastPurchaseDate = lastPurchaseDate,
                    IsActive = isActive,
                    IsQuitado = isQuitado,
                    IsArchived = client.IsArchived,
                    Rank = classification.Rank,
                    RankDescription = classification.Description,
                    RankColorHex = classification.ColorHex,
                    OnTimePercentage = classification.OnTimePercentage,
                    PaymentHistoryDetail = classification.DetailReason,
                    HasSufficientHistory = classification.HasSufficientHistory,
                    OverdueCount = classification.CurrentOverdueCount,
                    OverdueAmount = classification.CurrentOverdueAmount
                });
            }

            // Ordenação padrão: classificação de S até E, e dentro do mesmo rank em ordem alfabética por nome
            return rankedItems
                .OrderBy(item => item.Rank)
                .ThenBy(item => item.ClientName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }
}
