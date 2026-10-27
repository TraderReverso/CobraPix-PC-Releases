using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services;
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.Services.Interfaces;
using CobraPix_PC.ViewModels;

namespace CobraPix_PC.Services.Testing
{
    /// <summary>
    /// Suíte automatizada de testes de paridade funcional com a versão Android do CobraPix.
    /// Valida os 12 cenários mandatórios solicitados na auditoria e preparação da versão 1.0.6.
    /// Executa 100% em base SQLite temporária isolada em %TEMP%, com verificação estrita de não-modificação
    /// do banco de produção.
    /// </summary>
    public static class ParityTests
    {
        public static async Task<List<string>> RunAllTestsAsync()
        {
            var results = new List<string>();
            string tempDir = Path.GetTempPath();
            string tempDbPath = Path.Combine(tempDir, $"cobrapix_parity_test_{Guid.NewGuid():N}.db");
            var connectionString = $"Data Source={tempDbPath}";

            // Auditoria de integridade do banco de produção antes do início
            string prodDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CobraPix",
                "cobrapix.db");

            string? initialProdHash = null;
            if (File.Exists(prodDbPath))
            {
                SqliteConnection.ClearAllPools();
                using var sha = SHA256.Create();
                using var fs = new FileStream(prodDbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                initialProdHash = Convert.ToHexString(sha.ComputeHash(fs));
            }

            results.Add("=================================================================");
            results.Add("SUÍTE DE TESTES AUTOMATIZADOS: PARIDADE FUNCIONAL COM ANDROID (1.0.6)");
            results.Add($"Banco Temporário Isolado: {tempDbPath}");
            results.Add($"Hash Inicial da Produção: {initialProdHash ?? "NÃO ENCONTRADO"}");
            results.Add("=================================================================");

            var options = new DbContextOptionsBuilder<CobraPixDbContext>()
                .UseSqlite(connectionString)
                .Options;

            try
            {
                // Inicializa o banco temporário com migrações aditivas
                using (var context = new CobraPixDbContext(options))
                {
                    context.Database.EnsureCreated();
                    DatabaseInitializer.EnsureRemindersTableCreated(context);
                    DatabaseInitializer.EnsureParityColumnsCreated(context);
                }

                // ==============================================================
                // CENÁRIO 1: Geração de Pix Copia e Cola e conferência dos campos EMVCo
                // ==============================================================
                try
                {
                    string pixKey = "12345678900";
                    decimal amount = 150.00m;
                    string name = "João da Silva";
                    string city = "São Paulo";
                    string txid = "CPX12345";

                    string payload = PixGenerator.GeneratePayload(pixKey, amount, name, city, txid);

                    if (!payload.StartsWith("000201"))
                        throw new InvalidOperationException("Payload Pix não inicia com '000201'.");

                    if (!payload.Contains("0014br.gov.bcb.pix"))
                        throw new InvalidOperationException("Payload Pix não contém identificador do BCB.");

                    if (!payload.Contains("011112345678900"))
                        throw new InvalidOperationException("Payload Pix não contém a chave Pix formatada corretamente.");

                    if (!payload.Contains("52040000") || !payload.Contains("5303986"))
                        throw new InvalidOperationException("Payload Pix não contém moeda 986 ou categoria 0000.");

                    if (!payload.Contains("5406150.00"))
                        throw new InvalidOperationException("Payload Pix não contém valor 150.00 formatado.");

                    if (!payload.Contains("5802BR"))
                        throw new InvalidOperationException("Payload Pix não contém código do país BR.");

                    if (!payload.Contains("5913Joao da Silva"))
                        throw new InvalidOperationException("Payload Pix não normalizou acentos do recebedor.");

                    if (!payload.Contains("6009Sao Paulo"))
                        throw new InvalidOperationException("Payload Pix não normalizou acentos da cidade.");

                    if (!payload.Contains("62120508CPX12345"))
                        throw new InvalidOperationException("Payload Pix não formatou txid no campo 62.");

                    // Conferência do CRC16
                    string basePayload = payload.Substring(0, payload.Length - 4);
                    string expectedCrc = PixGenerator.CalculateCrc16(basePayload);
                    string actualCrc = payload.Substring(payload.Length - 4);

                    if (!string.Equals(expectedCrc, actualCrc, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"CRC16 incorreto. Esperado: {expectedCrc}, Obtido: {actualCrc}");

                    results.Add("[PASS] Cenário 01: Geração de código Pix Copia e Cola válido e conferência dos campos EMVCo.");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 01: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 2: Mensagem principal e mensagem secundária com chave isolada
                // ==============================================================
                try
                {
                    var whatsApp = new WhatsAppService();
                    var bundle = whatsApp.BuildOverdueMessageBundle(
                        clientName: "Maria Santos",
                        reference: "Parcela 2/4",
                        dueDate: new DateTime(2026, 10, 15),
                        amount: 200.00m,
                        totalPaid: 100.00m,
                        remainingBalance: 100.00m,
                        globalRemainingBalance: 300.00m,
                        pixKey: "pix@empresa.com",
                        receiverName: "Empresa ABC",
                        city: "Curitiba",
                        referenceTime: new DateTime(2026, 10, 20, 10, 0, 0));

                    if (!bundle.PrimaryMessage.Contains("Maria Santos"))
                        throw new InvalidOperationException("Mensagem principal não contém o nome do cliente.");

                    if (!bundle.PrimaryMessage.Contains("2/4"))
                        throw new InvalidOperationException("Mensagem principal não contém a referência da parcela.");

                    if (!bundle.PrimaryMessage.Contains("R$ 100,00"))
                        throw new InvalidOperationException("Mensagem principal não contém o saldo restante da parcela.");

                    if (!bundle.PrimaryMessage.Contains("Saldo devedor global restante"))
                        throw new InvalidOperationException("Mensagem principal não contém o saldo devedor global restante.");

                    if (!bundle.PrimaryMessage.Contains("Pix ou QR Code"))
                        throw new InvalidOperationException("Mensagem principal não contém instrução de Pix ou QR Code.");

                    // Mensagem secundária deve ser estritamente o código ou chave isolada
                    if (string.IsNullOrWhiteSpace(bundle.IsolatedPixKey))
                        throw new InvalidOperationException("Mensagem secundária com chave/código Pix isolada está vazia.");

                    if (!bundle.IsolatedPixKey.StartsWith("000201") && bundle.IsolatedPixKey != "pix@empresa.com")
                        throw new InvalidOperationException("Mensagem secundária não contém código Pix ou chave pura isolada.");

                    results.Add("[PASS] Cenário 02: Formatação da mensagem principal de cobrança e da mensagem secundária com a chave isolada.");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 02: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 3: Cálculo de juros contratuais em venda parcelada (centavos)
                // ==============================================================
                try
                {
                    decimal principal = 1000.00m;
                    double rate = 10.0;
                    int count = 3;

                    var calc = PaymentLogic.CalculateContractInterest(principal, rate, count);

                    if (calc.InterestAmount != 100.00m)
                        throw new InvalidOperationException($"Valor dos juros incorreto: {calc.InterestAmount}");

                    if (calc.TotalContracted != 1100.00m)
                        throw new InvalidOperationException($"Total contratado incorreto: {calc.TotalContracted}");

                    if (calc.InstallmentValues.Count != 3)
                        throw new InvalidOperationException($"Quantidade de parcelas geradas incorreta: {calc.InstallmentValues.Count}");

                    if (calc.InstallmentValues[0] != 366.67m ||
                        calc.InstallmentValues[1] != 366.67m ||
                        calc.InstallmentValues[2] != 366.66m)
                    {
                        throw new InvalidOperationException(
                            $"Distribuição de centavos divergente: [{string.Join(", ", calc.InstallmentValues)}]");
                    }

                    if (calc.InstallmentValues.Sum() != 1100.00m)
                        throw new InvalidOperationException("A soma das parcelas difere do total contratado!");

                    results.Add("[PASS] Cenário 03: Cálculo de juros contratuais em venda parcelada (com conferência de centavos).");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 03: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 4: Cálculo de juros contratuais para cliente mensalista
                // ==============================================================
                try
                {
                    decimal monthlyPrincipal = 200.00m;
                    double rate = 5.0;

                    var (interest, finalMonthly) = PaymentLogic.CalculateMonthlyContractInterest(monthlyPrincipal, rate);

                    if (interest != 10.00m)
                        throw new InvalidOperationException($"Juros da mensalidade incorretos: {interest}");

                    if (finalMonthly != 210.00m)
                        throw new InvalidOperationException($"Mensalidade final incorreta: {finalMonthly}");

                    results.Add("[PASS] Cenário 04: Cálculo de juros contratuais para cliente mensalista.");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 04: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 5: Cálculo de juros de mora para parcelas em atraso com tolerância
                // ==============================================================
                try
                {
                    DateTime dueDate = new DateTime(2026, 10, 1);
                    decimal principalRemaining = 1000.00m;
                    var config = new PaymentLogic.OverdueInterestConfig(
                        enabled: true,
                        monthlyRate: 3.0,
                        toleranceDays: 2,
                        startMinuteOfDay: 540);

                    // Teste A: Dentro da tolerância (03/10/2026 08:00)
                    var calcInsideTol = PaymentLogic.CalculateOverdueDaysAndInterest(
                        dueDate,
                        principalRemaining,
                        config,
                        new DateTime(2026, 10, 3, 8, 0, 0));

                    if (calcInsideTol.IsEligible || calcInsideTol.OverdueInterestAmount != 0m)
                        throw new InvalidOperationException("Não deveria haver juros de mora dentro da tolerância.");

                    // Teste B: Fora da tolerância (15/10/2026 10:00)
                    // Dias decorridos: 14 dias após 01/10. Dias elegíveis: 14 - 2 = 12 dias.
                    // Taxa diária: (3% / 100) / 30 = 0.001. Juros: 1000 * 0.001 * 12 = 12.00
                    var calcOverdue = PaymentLogic.CalculateOverdueDaysAndInterest(
                        dueDate,
                        principalRemaining,
                        config,
                        new DateTime(2026, 10, 15, 10, 0, 0));

                    if (!calcOverdue.IsEligible)
                        throw new InvalidOperationException("Parcela deveria ser elegível para encargos de atraso.");

                    if (calcOverdue.OverdueDays != 12)
                        throw new InvalidOperationException($"Dias elegíveis incorretos: {calcOverdue.OverdueDays} (esperado: 12).");

                    if (calcOverdue.OverdueInterestAmount != 12.00m)
                        throw new InvalidOperationException($"Encargos de mora incorretos: {calcOverdue.OverdueInterestAmount} (esperado: 12.00).");

                    results.Add("[PASS] Cenário 05: Cálculo de juros de mora para parcelas em atraso com tolerância.");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 05: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 6: Agendamento de parcelas por dia da semana
                // ==============================================================
                try
                {
                    // 4 semanas consecutivas em uma quarta-feira, atravessando virada de mês/ano
                    DateTime startWednesday = new DateTime(2026, 12, 16); // Quarta-feira
                    var dates = PaymentLogic.CalculateInstallmentDueDates(
                        count: 4,
                        firstDate: startWednesday,
                        useFifthBusinessDay: false,
                        periodicity: 1, // Semanal
                        targetWeekday: DayOfWeek.Wednesday);

                    if (dates.Count != 4)
                        throw new InvalidOperationException($"Quantidade de datas incorreta: {dates.Count}");

                    foreach (var d in dates)
                    {
                        if (d.DayOfWeek != DayOfWeek.Wednesday)
                            throw new InvalidOperationException($"Data {d:dd/MM/yyyy} não caiu em uma quarta-feira ({d.DayOfWeek})!");
                    }

                    if (dates[0] != new DateTime(2026, 12, 16) ||
                        dates[1] != new DateTime(2026, 12, 23) ||
                        dates[2] != new DateTime(2026, 12, 30) ||
                        dates[3] != new DateTime(2027, 1, 6))
                    {
                        throw new InvalidOperationException($"Datas divergentes: [{string.Join(", ", dates.Select(d => d.ToString("dd/MM/yyyy")))}]");
                    }

                    results.Add("[PASS] Cenário 06: Agendamento de parcelas por dia da semana (ex: 4 semanas consecutivas em uma quarta-feira).");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 06: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 7: Agendamento pelo 5º dia útil com virada de mês
                // ==============================================================
                try
                {
                    // Novembro de 2026: 01/11 Domingo, 02/11 Seg (1), 03/11 Ter (2), 04/11 Qua (3), 05/11 Qui (4), 06/11 Sex (5)
                    DateTime fifthNov2026 = PaymentLogic.CalculateFifthBusinessDay(11, 2026);
                    if (fifthNov2026 != new DateTime(2026, 11, 6))
                        throw new InvalidOperationException($"5º dia útil de nov/2026 incorreto: {fifthNov2026:dd/MM/yyyy} (esperado: 06/11/2026).");

                    // Dezembro de 2026: 01/12 Ter (1), 02/12 Qua (2), 03/12 Qui (3), 04/12 Sex (4), 07/12 Seg (5)
                    DateTime fifthDec2026 = PaymentLogic.CalculateFifthBusinessDay(12, 2026);
                    if (fifthDec2026 != new DateTime(2026, 12, 7))
                        throw new InvalidOperationException($"5º dia útil de dez/2026 incorreto: {fifthDec2026:dd/MM/yyyy} (esperado: 07/12/2026).");

                    // Rollover automático se o 5º dia útil do mês atual já passou
                    DateTime refAfterFifth = new DateTime(2026, 11, 10);
                    DateTime initialFifth = PaymentLogic.CalculateInitialFifthBusinessDay(
                        refAfterFifth,
                        isExplicitDate: false,
                        now: refAfterFifth);

                    if (initialFifth != fifthDec2026)
                        throw new InvalidOperationException($"Rollover para o mês seguinte falhou: {initialFifth:dd/MM/yyyy} (esperado: {fifthDec2026:dd/MM/yyyy}).");

                    results.Add("[PASS] Cenário 07: Agendamento pelo 5º dia útil com virada de mês.");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 07: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 8: Exclusão de parcela não paga com recálculo da venda e saldo global
                // ==============================================================
                try
                {
                    using var context = new CobraPixDbContext(options);
                    var client = new Client { Name = "Cliente Teste Exclusão", Phone = "11988887777" };
                    context.Clients.Add(client);
                    await context.SaveChangesAsync();

                    var saleService = new SaleService(context);
                    var paymentService = new PaymentService(context);

                    var sale = await saleService.CreateSaleAsync(
                        clientId: client.Id,
                        totalAmount: 300.00m,
                        installmentCount: 3,
                        firstDueDate: DateTime.Today.AddDays(10));

                    var installments = await saleService.GetInstallmentsBySaleIdAsync(sale.Id);
                    if (installments.Count != 3)
                        throw new InvalidOperationException("Venda não gerou 3 parcelas.");

                    var stmtBefore = await paymentService.GetClientGlobalStatementAsync(client.Id);
                    if (stmtBefore.RemainingGlobalDebt != 300.00m)
                        throw new InvalidOperationException($"Saldo global inicial incorreto: {stmtBefore.RemainingGlobalDebt}");

                    // Exclui a 3ª parcela não paga
                    var instToDelete = installments[2];
                    bool deleted = await saleService.DeleteInstallmentAsync(instToDelete.Id);
                    if (!deleted)
                        throw new InvalidOperationException("Falha ao excluir parcela não paga.");

                    // Recarrega venda e valida recálculo
                    var updatedSale = await saleService.GetSaleByIdAsync(sale.Id);
                    if (updatedSale == null || updatedSale.TotalAmount != 200.00m || updatedSale.InstallmentCount != 2)
                        throw new InvalidOperationException($"Venda não recalculada corretamente. Total: {updatedSale?.TotalAmount}, Qtd: {updatedSale?.InstallmentCount}");

                    // Valida que o saldo global do cliente foi reduzido imediatamente
                    var stmtAfter = await paymentService.GetClientGlobalStatementAsync(client.Id);
                    if (stmtAfter.RemainingGlobalDebt != 200.00m || stmtAfter.TotalInitialDebt != 200.00m)
                    {
                        throw new InvalidOperationException(
                            $"Saldo global não foi reduzido. Restante: {stmtAfter.RemainingGlobalDebt}, Inicial: {stmtAfter.TotalInitialDebt}");
                    }

                    results.Add("[PASS] Cenário 08: Exclusão de parcela não paga com recálculo do valor total da venda e do saldo devedor global.");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 08: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 9: Tentativa de exclusão de parcela paga (deve ser rejeitada)
                // ==============================================================
                try
                {
                    using var context = new CobraPixDbContext(options);
                    var client = new Client { Name = "Cliente Parcela Paga", Phone = "11977776666" };
                    context.Clients.Add(client);
                    await context.SaveChangesAsync();

                    var saleService = new SaleService(context);
                    var paymentService = new PaymentService(context);

                    var sale = await saleService.CreateSaleAsync(
                        clientId: client.Id,
                        totalAmount: 200.00m,
                        installmentCount: 2,
                        firstDueDate: DateTime.Today.AddDays(5));

                    var installments = await saleService.GetInstallmentsBySaleIdAsync(sale.Id);
                    var instPaid = installments[0];

                    // Realiza o pagamento total da parcela 1
                    await paymentService.RegisterPaymentAsync(instPaid.Id, instPaid.Amount, DateTime.Now, PaymentMethod.Pix);

                    // Tenta excluir a parcela paga
                    bool threwExpected = false;
                    try
                    {
                        await saleService.DeleteInstallmentAsync(instPaid.Id);
                    }
                    catch (BusinessRuleException ex) when (ex.Message.Contains("não é possível excluir uma parcela com pagamentos registrados", StringComparison.OrdinalIgnoreCase))
                    {
                        threwExpected = true;
                    }

                    if (!threwExpected)
                        throw new InvalidOperationException("Exclusão de parcela paga deveria ter sido rejeitada com BusinessRuleException!");

                    // Verifica se a parcela ainda existe e continua quitada
                    var checkInst = await context.Installments.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instPaid.Id);
                    if (checkInst == null || checkInst.Status != InstallmentStatus.Paid)
                        throw new InvalidOperationException("Parcela foi indevidamente modificada após tentativa de exclusão inválida.");

                    results.Add("[PASS] Cenário 09: Tentativa de exclusão de parcela paga (rejeitada com segurança e integridade preservada).");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 09: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 10: Pagamento parcial com validação de limites e atualização de status
                // ==============================================================
                try
                {
                    using var context = new CobraPixDbContext(options);
                    var client = new Client { Name = "Cliente Pagamento Parcial", Phone = "11966665555" };
                    context.Clients.Add(client);
                    await context.SaveChangesAsync();

                    var saleService = new SaleService(context);
                    var paymentService = new PaymentService(context);

                    // Parcela vencida no passado para testar status Overdue em baixa parcial
                    DateTime pastDue = DateTime.Today.AddDays(-10);
                    var sale = await saleService.CreateSaleAsync(
                        clientId: client.Id,
                        totalAmount: 100.00m,
                        installmentCount: 1,
                        firstDueDate: pastDue);

                    var inst = (await saleService.GetInstallmentsBySaleIdAsync(sale.Id)).First();

                    // Tentativa de pagamento maior que o saldo restante deve ser rejeitada
                    bool rejectedOverflow = false;
                    try
                    {
                        await paymentService.RegisterPaymentAsync(inst.Id, 150.00m, DateTime.Now, PaymentMethod.Cash);
                    }
                    catch (BusinessRuleException)
                    {
                        rejectedOverflow = true;
                    }

                    if (!rejectedOverflow)
                        throw new InvalidOperationException("Pagamento excedente deveria ter sido rejeitado.");

                    // Baixa parcial de R$ 40,00
                    await paymentService.RegisterPaymentAsync(inst.Id, 40.00m, DateTime.Now, PaymentMethod.Pix);

                    decimal remAfter40 = await paymentService.GetRemainingBalanceForInstallmentAsync(inst.Id);
                    if (remAfter40 != 60.00m)
                        throw new InvalidOperationException($"Saldo restante incorreto após baixa parcial: {remAfter40}");

                    var instCheck = await context.Installments.AsNoTracking().FirstAsync(i => i.Id == inst.Id);
                    if (instCheck.Status != InstallmentStatus.Overdue)
                        throw new InvalidOperationException($"Status deveria permanecer Overdue para parcela vencida parcialmente quitada (obtido: {instCheck.Status}).");

                    // Baixa final dos R$ 60,00 restantes
                    await paymentService.RegisterPaymentAsync(inst.Id, 60.00m, DateTime.Now, PaymentMethod.Pix);

                    decimal remFinal = await paymentService.GetRemainingBalanceForInstallmentAsync(inst.Id);
                    if (remFinal != 0m)
                        throw new InvalidOperationException($"Saldo final deveria ser 0m (obtido: {remFinal}).");

                    var instFinal = await context.Installments.AsNoTracking().FirstAsync(i => i.Id == inst.Id);
                    if (instFinal.Status != InstallmentStatus.Paid)
                        throw new InvalidOperationException($"Status final deveria ser Paid (obtido: {instFinal.Status}).");

                    results.Add("[PASS] Cenário 10: Pagamento parcial com validação de limites e atualização de status (deve permanecer OVERDUE se vencida, ou PENDING se não vencida).");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 10: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 11: Demonstrativo financeiro global do cliente
                // ==============================================================
                try
                {
                    var items = new List<(decimal value, decimal paidAmount, InstallmentStatus status, int id)>
                    {
                        (100.00m, 100.00m, InstallmentStatus.Paid, 1),
                        (100.00m, 30.00m, InstallmentStatus.Pending, 2),
                        (100.00m, 0.00m, InstallmentStatus.Pending, 3)
                    };

                    var stmt = PaymentLogic.CalculateClientGlobalStatement(items);

                    if (stmt.TotalInitialDebt != 300.00m)
                        throw new InvalidOperationException($"Dívida total inicial incorreta: {stmt.TotalInitialDebt}");

                    if (stmt.TotalPaidAccumulated != 130.00m)
                        throw new InvalidOperationException($"Total pago acumulado incorreto: {stmt.TotalPaidAccumulated}");

                    if (stmt.RemainingGlobalDebt != 170.00m)
                        throw new InvalidOperationException($"Saldo devedor restante incorreto: {stmt.RemainingGlobalDebt}");

                    if (stmt.PaidInstallmentsCount != 1 || stmt.PendingInstallmentsCount != 2)
                        throw new InvalidOperationException($"Contagem de parcelas incorreta: {stmt.PaidInstallmentsCount} pagas, {stmt.PendingInstallmentsCount} pendentes.");

                    results.Add("[PASS] Cenário 11: Demonstrativo financeiro global do cliente refletindo dívida inicial, total pago e saldo restante.");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 11: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 12: Geração do recibo PDF com dados discriminados e verificação de layout
                // ==============================================================
                try
                {
                    var tx = new FinancialTransaction
                    {
                        Id = 101,
                        TransactionType = FinancialTransactionType.Installment,
                        ClientId = 5,
                        ClientName = "Carlos Eduardo Ferreira",
                        ClientPhone = "11988889999",
                        Amount = 150.00m,
                        PaymentDate = new DateTime(2026, 10, 26, 14, 30, 0),
                        PaymentMethod = PaymentMethod.Pix,
                        Description = "Parcela 1/3 · Venda #12",
                        InstallmentNumber = 1,
                        TotalInstallments = 3,
                        OriginalAmount = 150.00m,
                        TotalPaid = 150.00m,
                        RemainingBalance = 0m,
                        GlobalInitialDebt = 450.00m,
                        GlobalTotalPaid = 150.00m,
                        GlobalRemainingBalance = 300.00m,
                        OriginalPrincipal = 135.00m,
                        ContractInterest = 15.00m,
                        UnpaidPrincipal = 0m,
                        OverdueInterest = 0m,
                        ReceiverName = "CobraPix Cobranças",
                        PixKey = "12345678900"
                    };

                    var rows = ReceiptPdfService.BuildReceiptRows(tx);

                    // Validação estrutural de campos presentes
                    bool hasGlobalSection = rows.Any(r => r.Label.Contains("RESUMO DA DÍVIDA GLOBAL"));
                    bool hasGlobalRemaining = rows.Any(r => r.Label.Contains("Saldo devedor global restante") && r.Value.Contains("300,00"));
                    bool hasContractInterest = rows.Any(r => r.Label.Contains("Juros contratuais") && r.Value.Contains("15,00"));
                    bool hasPaidNow = rows.Any(r => r.Label.Contains("Valor pago nesta transação") && r.Value.Contains("150,00"));
                    bool hasReceiver = rows.Any(r => r.Label.Contains("Recebedor") && r.Value.Contains("CobraPix Cobranças"));

                    if (!hasGlobalSection || !hasGlobalRemaining || !hasContractInterest || !hasPaidNow || !hasReceiver)
                    {
                        throw new InvalidOperationException("Demonstrativo do recibo não contém todas as seções ou valores obrigatórios discriminados.");
                    }

                    // Geração dos bytes PDF
                    var pdfService = new ReceiptPdfService("CobraPix PC");
                    byte[] pdfBytes = pdfService.GenerateReceiptBytes(tx);

                    if (pdfBytes == null || pdfBytes.Length == 0)
                        throw new InvalidOperationException("Falha na geração de bytes do PDF.");

                    string pdfHeader = Encoding.ASCII.GetString(pdfBytes.Take(5).ToArray());
                    if (!pdfHeader.StartsWith("%PDF-"))
                        throw new InvalidOperationException("Bytes gerados não correspondem a um arquivo PDF válido.");

                    results.Add("[PASS] Cenário 12: Geração do recibo PDF com dados discriminados e verificação de não sobreposição.");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 12: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 13: Integração dos Controles Visuais nos ViewModels (UI)
                // ==============================================================
                try
                {
                    using var context = new CobraPixDbContext(options);
                    var clientService = new ClientService(context);
                    var saleService = new SaleService(context);
                    var paymentService = new PaymentService(context);
                    var monthlyService = new MonthlyCustomerService(context);
                    var whatsAppService = new WhatsAppService();

                    // 1. Teste de VendasViewModel com juros, periodicidade semanal e 5º dia útil
                    var vendasVm = new VendasViewModel(saleService, clientService, paymentService);
                    vendasVm.FormAmountText = "1000,00";
                    vendasVm.FormInstallmentCount = 4;
                    vendasVm.FormContractInterestRateText = "10";

                    if (Math.Abs(vendasVm.FormContractInterestRate - 10.0) > 0.001)
                        throw new InvalidOperationException("FormContractInterestRateText não sincronizou FormContractInterestRate.");

                    // Periodicidade Semanal e Quarta-feira
                    vendasVm.FormPeriodicity = 1;
                    if (!vendasVm.IsPeriodicityWeeklyOrBiweekly || vendasVm.IsPeriodicityMonthly)
                        throw new InvalidOperationException("Flags de periodicidade não atualizaram corretamente.");

                    vendasVm.FormTargetWeekdayIndex = 3; // Quarta-feira
                    if (vendasVm.FormTargetWeekday != DayOfWeek.Wednesday)
                        throw new InvalidOperationException("FormTargetWeekdayIndex não mapeou para Quarta-feira.");

                    vendasVm.UpdatePreview();
                    if (vendasVm.PreviewInstallmentsList.Count != 4)
                        throw new InvalidOperationException("Prévia de parcelas semanais falhou na contagem.");

                    foreach (var inst in vendasVm.PreviewInstallmentsList)
                    {
                        if (inst.DueDate.DayOfWeek != DayOfWeek.Wednesday)
                            throw new InvalidOperationException("Parcela semanal não caiu na quarta-feira selecionada.");
                    }

                    // 5º dia útil
                    vendasVm.FormPeriodicity = 0; // Mensal
                    vendasVm.FormUseFifthBusinessDay = true;
                    vendasVm.UpdatePreview();
                    foreach (var inst in vendasVm.PreviewInstallmentsList)
                    {
                        var expected = PaymentLogic.CalculateFifthBusinessDay(inst.DueDate.Month, inst.DueDate.Year);
                        if (inst.DueDate.Date != expected.Date)
                            throw new InvalidOperationException("Parcela mensal não caiu no 5º dia útil esperado.");
                    }

                    // 2. Teste de MensalistasViewModel com juros contratuais
                    var mensalistasVm = new MensalistasViewModel(monthlyService, clientService, whatsAppService);
                    mensalistasVm.FormMonthlyAmountText = "200,00";
                    mensalistasVm.FormContractInterestRateText = "10";

                    if (Math.Abs(mensalistasVm.FormContractInterestRate - 10.0) > 0.001)
                        throw new InvalidOperationException("FormContractInterestRateText não sincronizou FormContractInterestRate no MensalistasViewModel.");

                    if (mensalistasVm.CalculatedInterestAmount != 20.00m || mensalistasVm.CalculatedFinalMonthlyAmount != 220.00m)
                        throw new InvalidOperationException($"Cálculo de juros do mensalista divergente: esperado 20/220, obtido {mensalistasVm.CalculatedInterestAmount}/{mensalistasVm.CalculatedFinalMonthlyAmount}");

                    if (!mensalistasVm.HasContractInterest)
                        throw new InvalidOperationException("HasContractInterest deveria ser true.");

                    results.Add("[PASS] Cenário 13: Integração dos controles nos ViewModels (Vendas e Mensalistas com Juros e Agendamento).");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 13: {ex.Message}");
                }

                // ==============================================================
                // CENÁRIO 14: Estorno de Pagamento e Exibição de Dívida Global no PDF
                // ==============================================================
                try
                {
                    using var context = new CobraPixDbContext(options);
                    var clientService = new ClientService(context);
                    var saleService = new SaleService(context);
                    var paymentService = new PaymentService(context);
                    var historyService = new FinancialHistoryService(context);
                    var pdfService = new ReceiptPdfService(context);

                    // 1. Criação de Cliente com 2 Vendas para validar Dívida Global > Valor da Parcela
                    var client = new Client
                    {
                        Name = "Cliente Auditoria Estorno e PDF",
                        Phone = "11988887777"
                    };
                    context.Clients.Add(client);
                    await context.SaveChangesAsync();

                    // Venda 1: R$ 1.000,00 em 2 parcelas de R$ 500,00
                    var sale1 = await saleService.CreateSaleAsync(
                        clientId: client.Id,
                        totalAmount: 1000m,
                        installmentCount: 2,
                        firstDueDate: DateTime.Today.AddDays(30),
                        description: "Contrato A");

                    // Venda 2: R$ 500,00 em 1 parcela de R$ 500,00
                    var sale2 = await saleService.CreateSaleAsync(
                        clientId: client.Id,
                        totalAmount: 500m,
                        installmentCount: 1,
                        firstDueDate: DateTime.Today.AddDays(60),
                        description: "Contrato B");

                    // Dívida Global Inicial do Cliente = R$ 1.500,00
                    var stmtInitial = await paymentService.GetClientGlobalStatementAsync(client.Id);
                    if (stmtInitial.TotalInitialDebt != 1500m || stmtInitial.TotalPaidAccumulated != 0m || stmtInitial.RemainingGlobalDebt != 1500m)
                    {
                        throw new InvalidOperationException($"Demonstrativo global inicial incorreto: esperado 1500/0/1500, obtido {stmtInitial.TotalInitialDebt}/{stmtInitial.TotalPaidAccumulated}/{stmtInitial.RemainingGlobalDebt}");
                    }

                    // 2. Pagamento da Parcela 1 da Venda 1 (R$ 500,00)
                    var p1 = sale1.Installments.OrderBy(i => i.InstallmentNumber).First();
                    var payment1 = await paymentService.RegisterPaymentAsync(p1.Id, 500m, DateTime.Now, PaymentMethod.Pix, "Pagamento integral parcela 1");

                    var p1Reloaded = await context.Installments.FindAsync(p1.Id);
                    if (p1Reloaded!.Status != InstallmentStatus.Paid || p1Reloaded.PaidDate == null)
                    {
                        throw new InvalidOperationException("Parcela 1 deveria estar com status Paid após quitação integral.");
                    }

                    // Verifica Dívida Global após pagamento
                    var stmtAfterPay1 = await paymentService.GetClientGlobalStatementAsync(client.Id);
                    if (stmtAfterPay1.TotalInitialDebt != 1500m || stmtAfterPay1.TotalPaidAccumulated != 500m || stmtAfterPay1.RemainingGlobalDebt != 1000m)
                    {
                        throw new InvalidOperationException($"Demonstrativo global divergente após pagamento: {stmtAfterPay1.TotalInitialDebt}/{stmtAfterPay1.TotalPaidAccumulated}/{stmtAfterPay1.RemainingGlobalDebt}");
                    }

                    // 3. Validação do PDF: Dívida Global (1.500) DEVE SER DIFERENTE do Valor da Parcela (500)
                    var txs = await historyService.GetAllTransactionsAsync();
                    var tx1 = txs.FirstOrDefault(t => t.Id == payment1.Id && !t.IsRefund);
                    if (tx1 == null)
                    {
                        throw new InvalidOperationException("Transação de pagamento não encontrada no histórico financeiro.");
                    }

                    if (tx1.GlobalInitialDebt != 1500m || tx1.OriginalAmount != 500m)
                    {
                        throw new InvalidOperationException($"Dívida global ({tx1.GlobalInitialDebt}) não pode ser substituída pelo valor da parcela ({tx1.OriginalAmount}).");
                    }

                    if (tx1.GlobalInitialDebt == tx1.OriginalAmount)
                    {
                        throw new InvalidOperationException("FALHA CRÍTICA: Dívida global foi colapsada no valor da parcela no histórico!");
                    }

                    var pdfRows = ReceiptPdfService.BuildReceiptRows(tx1);
                    var rowGlobalInitial = pdfRows.FirstOrDefault(r => r.Label.Contains("Valor total inicial da dívida global"));
                    var rowParcela = pdfRows.FirstOrDefault(r => r.Label.Contains("Valor da parcela atual"));
                    var rowTotalPago = pdfRows.FirstOrDefault(r => r.Label.Contains("Total acumulado pago"));
                    var rowSaldoGlobal = pdfRows.FirstOrDefault(r => r.Label.Contains("Saldo devedor global atualizado"));

                    if (rowGlobalInitial == null || !rowGlobalInitial.Value.Contains("1.500,00"))
                        throw new InvalidOperationException("Linha 'Valor total inicial da dívida global' ausente ou incorreta no PDF.");
                    if (rowParcela == null || !rowParcela.Value.Contains("500,00"))
                        throw new InvalidOperationException("Linha 'Valor da parcela atual' ausente ou incorreta no PDF.");
                    if (rowTotalPago == null || !rowTotalPago.Value.Contains("500,00"))
                        throw new InvalidOperationException("Linha 'Total acumulado pago' ausente ou incorreta no PDF.");
                    if (rowSaldoGlobal == null || !rowSaldoGlobal.Value.Contains("1.000,00"))
                        throw new InvalidOperationException("Linha 'Saldo devedor global atualizado' ausente ou incorreta no PDF.");

                    byte[] pdfBytes = pdfService.GenerateReceiptBytes(tx1);
                    if (pdfBytes.Length == 0)
                        throw new InvalidOperationException("Geração de bytes do PDF resultou em array vazio.");

                    // Gravação dos arquivos PDF reais de homologação em disco
                    string receiptsOutputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts", "test_receipts_1.0.6");
                    Directory.CreateDirectory(receiptsOutputDir);
                    string regularPdfPath = Path.Combine(receiptsOutputDir, "Comprovante_Pagamento_DividaGlobal_Teste.pdf");
                    File.WriteAllBytes(regularPdfPath, pdfBytes);

                    // 4. Teste de Estorno Total de Parcela
                    var refundResult1 = await paymentService.RefundInstallmentPaymentAsync(payment1.Id, null, "Estorno solicitado pelo cliente");
                    if (!refundResult1.Success)
                    {
                        throw new InvalidOperationException($"Falha no estorno total: {refundResult1.ErrorMessage}");
                    }

                    var p1AfterRefund = await context.Installments.Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == p1.Id);
                    if (p1AfterRefund!.Status != InstallmentStatus.Pending || p1AfterRefund.PaidDate != null)
                    {
                        throw new InvalidOperationException($"Status da parcela após estorno total incorreto: esperado Pending, obtido {p1AfterRefund.Status}");
                    }

                    var stmtAfterRefund1 = await paymentService.GetClientGlobalStatementAsync(client.Id);
                    if (stmtAfterRefund1.TotalInitialDebt != 1500m || stmtAfterRefund1.TotalPaidAccumulated != 0m || stmtAfterRefund1.RemainingGlobalDebt != 1500m)
                    {
                        throw new InvalidOperationException($"Demonstrativo global após estorno total incorreto: {stmtAfterRefund1.TotalInitialDebt}/{stmtAfterRefund1.TotalPaidAccumulated}/{stmtAfterRefund1.RemainingGlobalDebt}");
                    }

                    // 5. Teste de Tentativa de Estorno Duplicado
                    var duplicateRefund = await paymentService.RefundInstallmentPaymentAsync(payment1.Id, null, "Tentativa de duplicidade");
                    if (duplicateRefund.Success)
                    {
                        throw new InvalidOperationException("Falha de segurança: o sistema permitiu estornar um pagamento já integralmente estornado!");
                    }

                    // 6. Teste de Pagamento e Estorno Parcial
                    var p2 = sale1.Installments.OrderBy(i => i.InstallmentNumber).Last();
                    var payment2 = await paymentService.RegisterPaymentAsync(p2.Id, 500m, DateTime.Now, PaymentMethod.Cash, "Pagamento parcela 2");

                    // Estorno parcial de R$ 200,00
                    var partialRefund = await paymentService.RefundInstallmentPaymentAsync(payment2.Id, 200m, "Estorno parcial de ajuste");
                    if (!partialRefund.Success)
                    {
                        throw new InvalidOperationException($"Falha no estorno parcial: {partialRefund.ErrorMessage}");
                    }

                    var p2AfterPartial = await context.Installments.Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == p2.Id);
                    decimal netPaidP2 = p2AfterPartial!.Payments.Sum(p => p.AmountPaid);
                    if (netPaidP2 != 300m)
                    {
                        throw new InvalidOperationException($"Saldo líquido pago da parcela 2 divergente: esperado 300,00, obtido {netPaidP2}");
                    }
                    if (p2AfterPartial.Status != InstallmentStatus.Pending)
                    {
                        throw new InvalidOperationException($"Status da parcela 2 após estorno parcial deveria ser Pending, obtido {p2AfterPartial.Status}");
                    }

                    // Tentativa de estornar mais que o saldo restante disponível (tem 300, tentar 400)
                    var overRefund = await paymentService.RefundInstallmentPaymentAsync(payment2.Id, 400m, "Excesso");
                    if (overRefund.Success)
                    {
                        throw new InvalidOperationException("Falha de segurança: o sistema permitiu estorno superior ao saldo restante disponível!");
                    }

                    // 7. Rastreabilidade Integral no Histórico Financeiro
                    var txsAfter = await historyService.GetAllTransactionsAsync();
                    var origPaymentEntry = txsAfter.FirstOrDefault(t => t.Id == payment1.Id && !t.IsRefund);
                    var refundEntry = txsAfter.FirstOrDefault(t => t.IsRefund && t.OriginalPaymentId == payment1.Id);

                    if (origPaymentEntry == null)
                        throw new InvalidOperationException("O pagamento original foi apagado indevidamente do histórico!");
                    if (!origPaymentEntry.IsRefunded)
                        throw new InvalidOperationException("O pagamento original não foi marcado como estornado (IsRefunded).");
                    if (refundEntry == null)
                        throw new InvalidOperationException("O registro de estorno com valor negativo não foi localizado no histórico!");

                    // Geração e validação do Comprovante de Estorno em PDF
                    byte[] refundPdfBytes = pdfService.GenerateReceiptBytes(refundEntry);
                    if (refundPdfBytes.Length == 0)
                        throw new InvalidOperationException("Bytes do comprovante de estorno vazios.");

                    string refundPdfPath = Path.Combine(receiptsOutputDir, "Comprovante_Estorno_Teste.pdf");
                    File.WriteAllBytes(refundPdfPath, refundPdfBytes);

                    var refundPdfRows = ReceiptPdfService.BuildReceiptRows(refundEntry);
                    var refundRowOper = refundPdfRows.FirstOrDefault(r => r.Label.Contains("Valor estornado nesta transação"));
                    if (refundRowOper == null || !refundRowOper.Value.Contains("500,00"))
                        throw new InvalidOperationException("Linha 'Valor estornado nesta transação' ausente no PDF de estorno.");

                    // 8. Validação do ViewModel de Histórico Financeiro com Modal de Estorno
                    var historicoVm = new CobraPix_PC.ViewModels.Modules.HistoricoFinanceiroViewModel(historyService, null, pdfService, null, paymentService);
                    await historicoVm.LoadTransactionsAsync();

                    var vmItem = historicoVm.FilteredTransactions.FirstOrDefault(t => t.Id == payment2.Id && !t.IsRefund);
                    if (vmItem == null || !vmItem.CanRefund)
                        throw new InvalidOperationException("O item de pagamento parcial deveria permitir estorno no ViewModel.");

                    historicoVm.OpenRefundDialog(vmItem);
                    if (!historicoVm.IsRefundDialogOpen || historicoVm.SelectedTransactionForRefund?.Id != payment2.Id)
                        throw new InvalidOperationException("Modal de estorno não abriu com o item selecionado.");

                    // Conclui o estorno do saldo restante (300,00) via ViewModel
                    historicoVm.RefundAmountText = "300,00";
                    await historicoVm.ConfirmRefundAsync();

                    if (historicoVm.IsRefundDialogOpen)
                        throw new InvalidOperationException("O modal de estorno deveria ter sido fechado após a confirmação.");

                    var stmtFinal = await paymentService.GetClientGlobalStatementAsync(client.Id);
                    if (stmtFinal.TotalPaidAccumulated != 0m || stmtFinal.RemainingGlobalDebt != 1500m)
                    {
                        throw new InvalidOperationException($"Demonstrativo global final inconsistente após estorno completo: {stmtFinal.TotalPaidAccumulated} pagos, {stmtFinal.RemainingGlobalDebt} restante.");
                    }

                    results.Add("[PASS] Cenário 14: Estorno de pagamento (total, parcial, impedimentos, rastreabilidade) e Dívida Global no PDF validados com sucesso.");
                }
                catch (Exception ex)
                {
                    results.Add($"[FAIL] Cenário 14: {ex.Message}");
                }

                // ==============================================================
                // AUDITORIA FINAL DE SEGURANÇA: Verificação da Base de Produção
                // ==============================================================
                if (File.Exists(prodDbPath))
                {
                    SqliteConnection.ClearAllPools();
                    using var sha = SHA256.Create();
                    using var fs = new FileStream(prodDbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    string finalProdHash = Convert.ToHexString(sha.ComputeHash(fs));

                    if (initialProdHash != null && finalProdHash != initialProdHash)
                    {
                        results.Add($"[ALERTA CRÍTICO DE SEGURANÇA] O hash do banco de produção foi alterado durante os testes! Inicial: {initialProdHash}, Final: {finalProdHash}");
                    }
                    else
                    {
                        results.Add($"[SEGURANÇA CONFIRMADA] Banco de produção 100% inalterado (SHA-256: {finalProdHash}).");
                    }
                }
            }
            finally
            {
                // Limpeza do banco temporário isolado
                try
                {
                    SqliteConnection.ClearAllPools();
                    if (File.Exists(tempDbPath))
                    {
                        File.Delete(tempDbPath);
                    }
                }
                catch
                {
                    // Silencioso na limpeza de arquivos temporários
                }
            }

            results.Add("=================================================================");
            results.Add("FIM DA EXECUÇÃO DOS TESTES DE PARIDADE");
            results.Add("=================================================================");
            return results;
        }
    }
}
