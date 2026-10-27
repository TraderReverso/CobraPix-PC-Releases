using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using CobraPix_PC.Data;
using CobraPix_PC.Data.Credit;
using CobraPix_PC.Models;
using CobraPix_PC.Models.Credit;
using CobraPix_PC.Models.Credit.Dtos;
using CobraPix_PC.Services;
using CobraPix_PC.Services.Credit;
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.ViewModels;
using CobraPix_PC.ViewModels.Modules;

namespace CobraPix_PC.Services.Testing
{
    /// <summary>
    /// Suíte automatizada de testes do módulo Crédito & Giro.
    /// Executa 100% em arquivos de banco SQLite temporários isolados em disco,
    /// sem tocar no banco de produção (%LOCALAPPDATA%\CobraPix\cobrapix.db).
    /// </summary>
    public static class CreditGiroTests
    {
        public static async Task<System.Collections.Generic.List<string>> RunAllTestsAsync()
        {
            var results = new System.Collections.Generic.List<string>();
            string? sandboxRoot = Environment.GetEnvironmentVariable(DatabasePath.SandboxRootEnvVar);
            string baseTempDir = !string.IsNullOrWhiteSpace(sandboxRoot) ? sandboxRoot.Trim() : Path.GetTempPath();
            Directory.CreateDirectory(baseTempDir);
            string tempDbPath = Path.Combine(baseTempDir, $"cobrapix_credit_test_{Guid.NewGuid():N}.db");
            var connectionString = $"Data Source={tempDbPath}";
            string? previousDbPathEnv = Environment.GetEnvironmentVariable(DatabasePath.DatabasePathEnvVar);

            try
            {
                Environment.SetEnvironmentVariable(DatabasePath.DatabasePathEnvVar, tempDbPath);

                // Garante que o ambiente de teste esteja devidamente declarado
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COBRAPIX_ALLOW_TEST_DATABASE")))
                {
                    Environment.SetEnvironmentVariable("COBRAPIX_ALLOW_TEST_DATABASE", "1");
                }

                string sessionToken = Environment.GetEnvironmentVariable("COBRAPIX_SYNTHETIC_TEST_TOKEN") ?? Guid.NewGuid().ToString("N");
                Environment.SetEnvironmentVariable("COBRAPIX_SYNTHETIC_TEST_TOKEN", sessionToken);

                var options = new DbContextOptionsBuilder<CobraPixDbContext>()
                    .UseSqlite(connectionString)
                    .Options;

                results.Add("INICIANDO SUÍTE DE TESTES: CRÉDITO & GIRO (ETAPA 1)");
                results.Add($"Ambiente Isolado: {tempDbPath}");

                // -------------------------------------------------------------
                // TESTE 1: Migração Isolada e Preservação do Banco Legado
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    // Cria apenas o banco base
                    await ctx.Database.EnsureCreatedAsync();

                    // Identifica explicitamente o ambiente sintético legítimo e seu token efêmero
                    ctx.AppSettings.Add(new AppSetting { Key = "DatabaseEnvironment", Value = "SYNTHETIC_TEST" });
                    ctx.AppSettings.Add(new AppSetting { Key = "SyntheticSessionToken", Value = sessionToken });
                    await ctx.SaveChangesAsync();

                    // Insere dados legados com identificador sintético comprovado
                    var client = new Client
                    {
                        Name = "Empresa ABC Comércio - Cliente Teste Sintético",
                        Phone = "11988887777",
                        CreatedAt = DateTime.UtcNow
                    };
                    ctx.Clients.Add(client);
                    await ctx.SaveChangesAsync();

                    var sale = new Sale
                    {
                        ClientId = client.Id,
                        TotalAmount = 500.00m,
                        InstallmentCount = 2,
                        SaleDate = DateTime.Today,
                        Description = "Venda Legada Sintética",
                        CreatedAt = DateTime.UtcNow
                    };
                    ctx.Sales.Add(sale);
                    await ctx.SaveChangesAsync();

                    // Aplica a migração do módulo de crédito
                    await CreditDatabaseMigrator.ApplyCreditMigrationsAsync(ctx);

                    // Verifica se dados legados continuam intactos
                    var loadedClient = await ctx.Clients.FindAsync(client.Id);
                    if (loadedClient == null || loadedClient.Name != "Empresa ABC Comércio - Cliente Teste Sintético")
                        throw new Exception("TESTE 1 FALHOU: Cliente legado foi corrompido pela migração.");

                    var loadedSale = await ctx.Sales.FindAsync(sale.Id);
                    if (loadedSale == null || loadedSale.TotalAmount != 500.00m)
                        throw new Exception("TESTE 1 FALHOU: Venda legada foi corrompida pela migração.");

                    results.Add("TEST_1_MIGRATION_ISOLATED: PASS");
                    Console.WriteLine("TEST_1_MIGRATION_ISOLATED: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2: Bloqueio de Licença PRO para Usuários FREE
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Free, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);

                    var client = await ctx.Clients.FirstAsync();
                    var req = new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-TEST-FREE-01",
                        PrincipalAmountCents = 100000,
                        TotalInterestCents = 20000,
                        InstallmentsCount = 4,
                        FirstDueDate = DateTime.Today.AddDays(30),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "FREE-KEY-01"
                    };

                    bool blocked = false;
                    try
                    {
                        await contractSvc.CreateFixedContractAsync(req);
                    }
                    catch (ProLicenseRequiredException)
                    {
                        blocked = true;
                    }

                    if (!blocked)
                        throw new Exception("TESTE 2 FALHOU: Usuário FREE conseguiu criar contrato de crédito sem plano PRO.");

                    results.Add("TEST_2_PRO_LICENSE_ENFORCEMENT: PASS");
                    Console.WriteLine("TEST_2_PRO_LICENSE_ENFORCEMENT: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3: Criação de Contrato com Partição Exata em Centavos
                // -------------------------------------------------------------
                string contractId;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    // R$ 100,00 de Principal (10.000 centavos) + R$ 30,01 de Juros (3.001 centavos) em 3 parcelas
                    var req = new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-2026-0001",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 3001,
                        InstallmentsCount = 3,
                        FirstDueDate = new DateTime(2026, 1, 31),
                        ContractDate = new DateTime(2026, 1, 15),
                        Notes = "Contrato de Teste Exato de Centavos",
                        IdempotencyKey = "CREATE-KEY-001"
                    };

                    var res = await contractSvc.CreateFixedContractAsync(req);
                    contractId = res.ContractId;

                    if (res.Status != CreditContractStatus.Draft || res.InstallmentsGenerated != 3)
                        throw new Exception("TESTE 3 FALHOU: Contrato não gerado em status Draft ou com quantidade incorreta de parcelas.");

                    var installments = await ctx.CreditInstallments
                        .Where(i => i.ContractId == contractId)
                        .OrderBy(i => i.InstallmentNumber)
                        .ToListAsync();

                    if (installments.Count != 3)
                        throw new Exception("TESTE 3 FALHOU: Esperado 3 parcelas no banco.");

                    // Parcela 1: 3.334 principal + 1.000 juros = 4.334 total
                    if (installments[0].NominalPrincipalCents != 3334 ||
                        installments[0].NominalInterestCents != 1000 ||
                        installments[0].TotalAmountCents != 4334)
                    {
                        throw new Exception($"TESTE 3 FALHOU na Parcela 1: P={installments[0].NominalPrincipalCents}, J={installments[0].NominalInterestCents}, T={installments[0].TotalAmountCents}");
                    }

                    // Parcela 2: 3.333 principal + 1.001 juros = 4.334 total
                    if (installments[1].NominalPrincipalCents != 3333 ||
                        installments[1].NominalInterestCents != 1001 ||
                        installments[1].TotalAmountCents != 4334)
                    {
                        throw new Exception($"TESTE 3 FALHOU na Parcela 2: P={installments[1].NominalPrincipalCents}, J={installments[1].NominalInterestCents}, T={installments[1].TotalAmountCents}");
                    }

                    // Parcela 3: 3.333 principal + 1.000 juros = 4.333 total
                    if (installments[2].NominalPrincipalCents != 3333 ||
                        installments[2].NominalInterestCents != 1000 ||
                        installments[2].TotalAmountCents != 4333)
                    {
                        throw new Exception($"TESTE 3 FALHOU na Parcela 3: P={installments[2].NominalPrincipalCents}, J={installments[2].NominalInterestCents}, T={installments[2].TotalAmountCents}");
                    }

                    // Soma exata dos componentes
                    long totalPrincipalSum = installments.Sum(i => i.NominalPrincipalCents);
                    long totalInterestSum = installments.Sum(i => i.NominalInterestCents);
                    long totalAmountSum = installments.Sum(i => i.TotalAmountCents);

                    if (totalPrincipalSum != 10000 || totalInterestSum != 3001 || totalAmountSum != 13001)
                        throw new Exception($"TESTE 3 FALHOU na soma exata: P={totalPrincipalSum}, J={totalInterestSum}, T={totalAmountSum}");

                    results.Add("TEST_3_CONTRACT_CREATION_EXACT_CENTS: PASS");
                    Console.WriteLine("TEST_3_CONTRACT_CREATION_EXACT_CENTS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 4: Vencimentos em Dias Civis Estritos (Sem Deslocamento)
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var installments = await ctx.CreditInstallments
                        .Where(i => i.ContractId == contractId)
                        .OrderBy(i => i.InstallmentNumber)
                        .ToListAsync();

                    // 31/01/2026 foi um Sábado -> deve permanecer rigorosamente 31/01/2026
                    if (installments[0].DueDate != new DateTime(2026, 1, 31))
                        throw new Exception($"TESTE 4 FALHOU: Parcela 1 deslocou vencimento ({installments[0].DueDate})");

                    // 28/02/2026 foi um Sábado -> deve permanecer rigorosamente 28/02/2026
                    if (installments[1].DueDate != new DateTime(2026, 2, 28))
                        throw new Exception($"TESTE 4 FALHOU: Parcela 2 deslocou vencimento ({installments[1].DueDate})");

                    // 31/03/2026 -> 31/03/2026
                    if (installments[2].DueDate != new DateTime(2026, 3, 31))
                        throw new Exception($"TESTE 4 FALHOU: Parcela 3 deslocou vencimento ({installments[2].DueDate})");

                    results.Add("TEST_4_CIVIL_DATE_VENCIMENTOS: PASS");
                    Console.WriteLine("TEST_4_CIVIL_DATE_VENCIMENTOS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 5: Desembolso Financeiro e Transição de Estado
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);

                    var disburseReq = new DisburseContractRequestDto
                    {
                        ContractId = contractId,
                        DisbursementDate = new DateTime(2026, 1, 15),
                        PaymentMethod = PaymentMethod.Pix,
                        Notes = "Desembolso via Pix Comercial",
                        IdempotencyKey = "DISBURSE-KEY-001"
                    };

                    var disburseRes = await contractSvc.DisburseContractAsync(disburseReq);
                    if (disburseRes.ContractStatus != CreditContractStatus.Active || disburseRes.DisbursedAmountCents != 10000)
                        throw new Exception("TESTE 5 FALHOU: Desembolso não atualizou status para Active ou valor incorreto.");

                    // Verificar registro no banco
                    var disburseRecord = await ctx.CreditDisbursements.FirstOrDefaultAsync(d => d.ContractId == contractId);
                    if (disburseRecord == null || disburseRecord.DisbursedAmountCents != 10000)
                        throw new Exception("TESTE 5 FALHOU: Registro de CreditDisbursement não localizado no SQLite.");

                    // Tentativa de segundo desembolso no mesmo contrato: deve ser rejeitado!
                    bool duplicateBlocked = false;
                    try
                    {
                        await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                        {
                            ContractId = contractId,
                            DisbursementDate = DateTime.Today,
                            IdempotencyKey = "DISBURSE-KEY-DUP"
                        });
                    }
                    catch (InvalidOperationException)
                    {
                        duplicateBlocked = true;
                    }

                    if (!duplicateBlocked)
                        throw new Exception("TESTE 5 FALHOU: Permitiu segundo desembolso no mesmo contrato.");

                    results.Add("TEST_5_DISBURSEMENT_LIFECYCLE: PASS");
                    Console.WriteLine("TEST_5_DISBURSEMENT_LIFECYCLE: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 6: Regra de Cancelamento (Permitido sem pagamentos)
                // -------------------------------------------------------------
                string contractIdToCancel;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    // Cria um novo contrato para testar cancelamento permitido
                    var res = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-2026-CANCEL-01",
                        PrincipalAmountCents = 50000,
                        TotalInterestCents = 10000,
                        InstallmentsCount = 2,
                        FirstDueDate = DateTime.Today.AddDays(15),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CREATE-CANCEL-KEY"
                    });
                    contractIdToCancel = res.ContractId;

                    // Desembolsa o contrato
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = contractIdToCancel,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISBURSE-CANCEL-KEY"
                    });

                    // Como NÃO há nenhum pagamento registrado, o cancelamento DEVE ser aceito
                    var cancelRes = await contractSvc.CancelContractAsync(new CancelContractRequestDto
                    {
                        ContractId = contractIdToCancel,
                        Reason = "Cliente desistiu dentro do prazo legal antes de pagar",
                        IdempotencyKey = "CANCEL-KEY-001"
                    });

                    if (cancelRes.Status != CreditContractStatus.Cancelled)
                        throw new Exception("TESTE 6 FALHOU: Cancelamento não transicionou contrato para Cancelled.");

                    var contractLoaded = await ctx.CreditContracts
                        .Include(c => c.Installments)
                        .FirstAsync(c => c.Id == contractIdToCancel);

                    if (contractLoaded.Status != CreditContractStatus.Cancelled || contractLoaded.CancelledAtUtc == null)
                        throw new Exception("TESTE 6 FALHOU: Dados de cancelamento não persistidos no contrato.");

                    if (contractLoaded.Installments.Any(i => i.Status != CreditInstallmentStatus.Cancelled))
                        throw new Exception("TESTE 6 FALHOU: Parcelas do contrato cancelado não foram canceladas.");

                    results.Add("TEST_6_DISBURSEMENT_CANCELLATION_ALLOWED: PASS");
                    Console.WriteLine("TEST_6_DISBURSEMENT_CANCELLATION_ALLOWED: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 7: Regra de Cancelamento (Proibido quando HÁ pagamentos)
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);

                    // Simula um pagamento existente no contractId (ativo)
                    var simulatedPayment = new CreditPayment
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = contractId,
                        PaymentDate = DateTime.Today,
                        AmountCents = 4335,
                        Status = CreditPaymentStatus.Active,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    ctx.CreditPayments.Add(simulatedPayment);
                    await ctx.SaveChangesAsync();

                    // Tenta cancelar contractId que possui pagamento histórico
                    bool cancelBlocked = false;
                    try
                    {
                        await contractSvc.CancelContractAsync(new CancelContractRequestDto
                        {
                            ContractId = contractId,
                            Reason = "Tentativa de cancelamento indevido com pagamento existente",
                            IdempotencyKey = "CANCEL-ILLEGAL-KEY"
                        });
                    }
                    catch (CreditOperationPreconditionException)
                    {
                        cancelBlocked = true;
                    }

                    if (!cancelBlocked)
                        throw new Exception("TESTE 7 FALHOU: Cancelamento foi permitido mesmo havendo pagamentos vinculados ao contrato.");

                    results.Add("TEST_7_CANCELLATION_BLOCKED_WITH_PAYMENTS: PASS");
                    Console.WriteLine("TEST_7_CANCELLATION_BLOCKED_WITH_PAYMENTS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 8: Idempotência Persistente (Mesmo Payload vs Divergente)
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    var originalReq = new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-IDEM-001",
                        PrincipalAmountCents = 80000,
                        TotalInterestCents = 15000,
                        InstallmentsCount = 4,
                        FirstDueDate = DateTime.Today.AddDays(30),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "IDEMPOTENCY-FIXED-KEY"
                    };

                    // 1ª execução
                    var firstRes = await contractSvc.CreateFixedContractAsync(originalReq);

                    // 2ª execução com EXATAMENTE o mesmo payload e chave:
                    var secondRes = await contractSvc.CreateFixedContractAsync(originalReq);

                    if (firstRes.ContractId != secondRes.ContractId)
                        throw new Exception("TESTE 8 FALHOU: Repetição idempotente gerou um contrato diferente.");

                    int contractCount = await ctx.CreditContracts.CountAsync(c => c.ContractNumber == "CTR-IDEM-001");
                    if (contractCount != 1)
                        throw new Exception($"TESTE 8 FALHOU: Encontrado {contractCount} contratos duplicados com a mesma chave.");

                    // 3ª execução com MESMA chave, mas PAYLOAD DIVERGENTE (valor diferente)
                    var divergentReq = new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-IDEM-001",
                        PrincipalAmountCents = 999999, // Divergente!
                        TotalInterestCents = 15000,
                        InstallmentsCount = 4,
                        FirstDueDate = DateTime.Today.AddDays(30),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "IDEMPOTENCY-FIXED-KEY"
                    };

                    bool conflictCaught = false;
                    try
                    {
                        await contractSvc.CreateFixedContractAsync(divergentReq);
                    }
                    catch (IdempotencyConflictException)
                    {
                        conflictCaught = true;
                    }

                    if (!conflictCaught)
                        throw new Exception("TESTE 8 FALHOU: Reutilização de chave com payload divergente não disparou IdempotencyConflictException.");

                    results.Add("TEST_8_IDEMPOTENCY_PERSISTENCE_AND_CONFLICT: PASS");
                    Console.WriteLine("TEST_8_IDEMPOTENCY_PERSISTENCE_AND_CONFLICT: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 9: Limpeza do ChangeTracker após Exceção
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var executor = new CreditTransactionExecutor(ctx);

                    try
                    {
                        await executor.ExecuteTransactionalAsync<string>(
                            "TestFailure",
                            "KEY-FAIL-01",
                            "payload-fail",
                            async c =>
                            {
                                c.CreditContracts.Add(new CreditContract
                                {
                                    Id = "dirty-entity-id",
                                    ContractNumber = "DIRTY-01"
                                });
                                await Task.Yield();
                                throw new ApplicationException("Simulação de falha controlada no meio da transação");
                            });
                    }
                    catch (ApplicationException)
                    {
                        // Esperado
                    }

                    // Verifica se ChangeTracker foi completamente limpo
                    int trackedCount = ctx.ChangeTracker.Entries().Count();
                    if (trackedCount != 0)
                        throw new Exception($"TESTE 9 FALHOU: ChangeTracker reteve {trackedCount} entidades sujas após rollback.");

                    results.Add("TEST_9_CHANGE_TRACKER_CLEANUP_ON_FAILURE: PASS");
                    Console.WriteLine("TEST_9_CHANGE_TRACKER_CLEANUP_ON_FAILURE: PASS");
                }

                // =============================================================
                // ETAPA 2: SUÍTE DE PAGAMENTOS E CONSUMO DE EXCEDENTES
                // =============================================================
                results.Add("--------------------------------------------------");
                results.Add("INICIANDO TESTES DA ETAPA 2: PAGAMENTOS E EXCEDENTES");
                results.Add("--------------------------------------------------");
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine("INICIANDO TESTES DA ETAPA 2: PAGAMENTOS E EXCEDENTES");
                Console.WriteLine("--------------------------------------------------");

                var proService = new ProService(new CobraPixDbContext(options));
                await proService.SetPlanAsync(PlanType.Pro, "Test");

                // -------------------------------------------------------------
                // TESTE 2.1: Pagamento parcial menor que os juros remanescentes
                // -------------------------------------------------------------
                string contractE2_1_Id;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paymentSvc = new CreditPaymentService(ctx, proService);
                    var client = await ctx.Clients.FirstAsync();

                    // Parcela: Principal 70,00 (7.000 cents), Juros 30,00 (3.000 cents), Total 100,00
                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E2-01",
                        PrincipalAmountCents = 7000,
                        TotalInterestCents = 3000,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(10),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E2-01-KEY"
                    });
                    contractE2_1_Id = cRes.ContractId;
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = contractE2_1_Id,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E2-01-KEY"
                    });

                    // Pagamento de R$ 20,00 (2.000 cents) -> menor que os R$ 30,00 de juros
                    var payRes = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = contractE2_1_Id,
                        AmountCents = 2000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E2-01-KEY"
                    });

                    if (payRes.TotalInterestAllocatedCents != 2000 || payRes.TotalPrincipalAllocatedCents != 0)
                        throw new Exception($"TESTE 2.1 FALHOU: Apropriação de juros incorreta (J={payRes.TotalInterestAllocatedCents}, P={payRes.TotalPrincipalAllocatedCents}).");

                    var inst = await ctx.CreditInstallments.FirstAsync(i => i.ContractId == contractE2_1_Id);
                    if (inst.PaidAmountCents != 2000 || inst.Status != CreditInstallmentStatus.PartiallyPaid)
                        throw new Exception($"TESTE 2.1 FALHOU: Parcela com status ou saldo incorreto (Pago={inst.PaidAmountCents}, Status={inst.Status}).");

                    results.Add("TEST_STAGE2_01_PARTIAL_PAYMENT_LESS_THAN_INTEREST: PASS");
                    Console.WriteLine("TEST_STAGE2_01_PARTIAL_PAYMENT_LESS_THAN_INTEREST: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.2: Pagamento que quita os juros e amortiza parte do principal
                // -------------------------------------------------------------
                string contractE2_2_Id;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paymentSvc = new CreditPaymentService(ctx, proService);
                    var client = await ctx.Clients.FirstAsync();

                    // Parcela: Principal 70,00 (7.000 cents), Juros 30,00 (3.000 cents)
                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E2-02",
                        PrincipalAmountCents = 7000,
                        TotalInterestCents = 3000,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(10),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E2-02-KEY"
                    });
                    contractE2_2_Id = cRes.ContractId;
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = contractE2_2_Id,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E2-02-KEY"
                    });

                    // Pagamento de R$ 50,00 (5.000 cents) -> quita 3.000 de juros + 2.000 de principal
                    var payRes = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = contractE2_2_Id,
                        AmountCents = 5000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E2-02-KEY"
                    });

                    if (payRes.TotalInterestAllocatedCents != 3000 || payRes.TotalPrincipalAllocatedCents != 2000)
                        throw new Exception($"TESTE 2.2 FALHOU: Alocação Regra A divergente (J={payRes.TotalInterestAllocatedCents}, P={payRes.TotalPrincipalAllocatedCents}).");

                    var inst = await ctx.CreditInstallments.FirstAsync(i => i.ContractId == contractE2_2_Id);
                    if (inst.PaidAmountCents != 5000 || inst.Status != CreditInstallmentStatus.PartiallyPaid)
                        throw new Exception($"TESTE 2.2 FALHOU: Estado da parcela incorreto.");

                    results.Add("TEST_STAGE2_02_PAYMENT_SETTLES_INTEREST_AND_PART_PRINCIPAL: PASS");
                    Console.WriteLine("TEST_STAGE2_02_PAYMENT_SETTLES_INTEREST_AND_PART_PRINCIPAL: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.3: Pagamentos múltiplos na mesma parcela
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var paymentSvc = new CreditPaymentService(ctx, proService);

                    // Continuando em contractE2_2_Id (falta 5.000 de principal e 0 de juros)
                    var payRes2 = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = contractE2_2_Id,
                        AmountCents = 5000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E2-02-KEY-PART2"
                    });

                    if (payRes2.TotalInterestAllocatedCents != 0 || payRes2.TotalPrincipalAllocatedCents != 5000)
                        throw new Exception($"TESTE 2.3 FALHOU: 2º pagamento deveria alocar 0 de juros e 5000 de principal (J={payRes2.TotalInterestAllocatedCents}, P={payRes2.TotalPrincipalAllocatedCents}).");

                    var inst = await ctx.CreditInstallments.FirstAsync(i => i.ContractId == contractE2_2_Id);
                    if (inst.PaidAmountCents != 10000 || inst.Status != CreditInstallmentStatus.Paid)
                        throw new Exception($"TESTE 2.3 FALHOU: Parcela deveria estar totalmente Paid (Pago={inst.PaidAmountCents}, Status={inst.Status}).");

                    var contract = await ctx.CreditContracts.FirstAsync(c => c.Id == contractE2_2_Id);
                    if (contract.Status != CreditContractStatus.Completed)
                        throw new Exception("TESTE 2.3 FALHOU: Contrato não transicionou para Completed após quitação.");

                    results.Add("TEST_STAGE2_03_MULTIPLE_PAYMENTS_SAME_INSTALLMENT: PASS");
                    Console.WriteLine("TEST_STAGE2_03_MULTIPLE_PAYMENTS_SAME_INSTALLMENT: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.4: Pagamento de várias parcelas em ordem de vencimento
                // -------------------------------------------------------------
                string contractE2_4_Id;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paymentSvc = new CreditPaymentService(ctx, proService);
                    var client = await ctx.Clients.FirstAsync();

                    // 2 parcelas: cada uma com P=5.000, J=2.000 (Total 7.000 cada -> 14.000 total)
                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E2-04",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 4000,
                        InstallmentsCount = 2,
                        FirstDueDate = DateTime.Today.AddDays(15),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E2-04-KEY"
                    });
                    contractE2_4_Id = cRes.ContractId;
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = contractE2_4_Id,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E2-04-KEY"
                    });

                    // Pagamento de R$ 100,00 (10.000 cents):
                    // Cobre integralmente a Parcela 1 (7.000 cents: 2.000 juros + 5.000 principal)
                    // Cobre parcialmente a Parcela 2 (3.000 cents restantes: 2.000 juros + 1.000 principal)
                    var payRes = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = contractE2_4_Id,
                        AmountCents = 10000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E2-04-KEY"
                    });

                    if (payRes.TotalInterestAllocatedCents != 4000 || payRes.TotalPrincipalAllocatedCents != 6000)
                        throw new Exception($"TESTE 2.4 FALHOU: Alocação total diverge (J={payRes.TotalInterestAllocatedCents}, P={payRes.TotalPrincipalAllocatedCents}).");

                    var insts = await ctx.CreditInstallments
                        .Where(i => i.ContractId == contractE2_4_Id)
                        .OrderBy(i => i.InstallmentNumber)
                        .ToListAsync();

                    if (insts[0].Status != CreditInstallmentStatus.Paid || insts[0].PaidAmountCents != 7000)
                        throw new Exception("TESTE 2.4 FALHOU: Parcela 1 não foi quitada.");

                    if (insts[1].Status != CreditInstallmentStatus.PartiallyPaid || insts[1].PaidAmountCents != 3000)
                        throw new Exception("TESTE 2.4 FALHOU: Parcela 2 não está parcialmente paga com 3.000 cents.");

                    results.Add("TEST_STAGE2_04_PAYMENT_ACROSS_MULTIPLE_INSTALLMENTS: PASS");
                    Console.WriteLine("TEST_STAGE2_04_PAYMENT_ACROSS_MULTIPLE_INSTALLMENTS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.5: Criação e consumo parcial de excedente
                // -------------------------------------------------------------
                string contractE2_5_Id;
                string surplusBalanceId;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paymentSvc = new CreditPaymentService(ctx, proService);
                    var client = await ctx.Clients.FirstAsync();

                    // 2 parcelas de R$ 50,00 cada (Total 100,00)
                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E2-05",
                        PrincipalAmountCents = 8000,
                        TotalInterestCents = 2000,
                        InstallmentsCount = 2,
                        FirstDueDate = DateTime.Today.AddDays(15),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E2-05-KEY"
                    });
                    contractE2_5_Id = cRes.ContractId;
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = contractE2_5_Id,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E2-05-KEY"
                    });

                    // Pagamento de R$ 70,00 (7.000 cents):
                    // Parcela 1 exige 5.000 cents -> Quita a Parcela 1
                    // Restam 2.000 cents -> Amortiza parte da Parcela 2
                    // Em vez disso, vamos pagar R$ 120,00 (12.000 cents) para quitar AMBAS as parcelas (10.000) e gerar R$ 20,00 (2.000 cents) de EXCEDENTE!
                    var payRes = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = contractE2_5_Id,
                        AmountCents = 12000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E2-05-KEY"
                    });

                    if (payRes.SurplusGeneratedCents != 2000)
                        throw new Exception($"TESTE 2.5 FALHOU: Excedente esperado 2.000, obtido {payRes.SurplusGeneratedCents}.");

                    var surplusList = await paymentSvc.GetSurplusBalancesByContractAsync(contractE2_5_Id);
                    if (surplusList.Count != 1 || surplusList[0].RemainingSurplusCents != 2000)
                        throw new Exception("TESTE 2.5 FALHOU: Bolsa de excedente não registrada corretamente.");

                    surplusBalanceId = surplusList[0].Id;
                    results.Add("TEST_STAGE2_05_CREATION_OF_SURPLUS: PASS");
                    Console.WriteLine("TEST_STAGE2_05_CREATION_OF_SURPLUS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.6: Consumo de excedente com apropriação de juros primeiro
                // -------------------------------------------------------------
                string consumptionId;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paymentSvc = new CreditPaymentService(ctx, proService);
                    var client = await ctx.Clients.FirstAsync();

                    // Novo contrato para testar consumo de excedente do mesmo contrato:
                    // Criamos um contrato com 2 parcelas, pagamos a 1ª com excedente e consumimos na 2ª
                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E2-06",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 4000,
                        InstallmentsCount = 2,
                        FirstDueDate = DateTime.Today.AddDays(15),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E2-06-KEY"
                    });
                    string cId = cRes.ContractId;
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E2-06-KEY"
                    });

                    // Pagamento 1: Paga 9.000 cents (Parcela 1 consome 7.000 cents e sobram 2.000 de excedente)
                    // Mas wait: se pagar 9.000, Parcela 2 está aberta e receberia 2.000!
                    // Para gerar excedente com Parcela 2 aberta, pagamos R$ 160,00 (16.000 cents) -> quita as 2 parcelas (14.000) e sobram 2.000!
                    // Ou: contrato onde a Parcela 2 vence depois, e o usuário consome o excedente gerado
                    // Vamos criar um contrato com 3 parcelas de R$ 70,00 (Total 210,00).
                    // Parcela 1: 70,00 (P=50, J=20)
                    // Parcela 2: 70,00 (P=50, J=20)
                    // Parcela 3: 70,00 (P=50, J=20)
                    // Pagamento de R$ 160,00 quita Parcela 1 (70) e Parcela 2 (70) e deixa Parcela 3 recebendo R$ 20,00.
                    // Para testar consumo de excedente:
                    // Podemos pagar a parcela 1 gerando excedente caso a requisição seja direcionada, OU se o contrato for totalmente quitado e tiver nova parcela adicionada, OU um pagamento de R$ 100 numa parcela de R$ 70 quando apenas 1 parcela está aberta.
                    // Criamos contrato de 1 parcela de R$ 70,00:
                    // Paga R$ 100,00 -> quita a parcela de R$ 70,00 e gera R$ 30,00 de excedente.
                    var cResA = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E2-06-A",
                        PrincipalAmountCents = 5000,
                        TotalInterestCents = 2000,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(10),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E2-06A-KEY"
                    });
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cResA.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E2-06A-KEY"
                    });

                    // Paga R$ 100,00 (10.000 cents) -> Parcela 1 consome 7.000 cents e sobram 3.000 de excedente na bolsa!
                    var pResA = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cResA.ContractId,
                        AmountCents = 10000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E2-06A-KEY"
                    });
                    if (pResA.SurplusGeneratedCents != 3000)
                        throw new Exception("Falha ao gerar bolsa de excedente de 3.000 cents.");

                    var surplusBolsa = (await paymentSvc.GetSurplusBalancesByContractAsync(cResA.ContractId)).First();

                    // Adiciona uma 2ª parcela aberta no mesmo contrato (ou simula obrigação aberta)
                    var inst2 = new CreditInstallment
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = cResA.ContractId,
                        InstallmentNumber = 2,
                        DueDate = DateTime.Today.AddDays(40),
                        NominalPrincipalCents = 3500,
                        NominalInterestCents = 1500,
                        TotalAmountCents = 5000,
                        PaidAmountCents = 0,
                        Status = CreditInstallmentStatus.Pending,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    ctx.CreditInstallments.Add(inst2);
                    // Reabre contrato para Active
                    var ctrLoaded = await ctx.CreditContracts.FindAsync(cResA.ContractId);
                    ctrLoaded!.Status = CreditContractStatus.Active;
                    ctrLoaded.DisbursedPrincipalCents += 3500;
                    ctrLoaded.TotalNominalInterestCents += 1500;
                    ctrLoaded.TotalAmountCents += 5000;
                    await ctx.SaveChangesAsync();

                    // Consome R$ 25,00 (2.500 cents) da bolsa na parcela 2:
                    // Parcela 2 tem J=1.500 e P=3.500.
                    // REGRA A NO CONSUMO: consome 1.500 em juros e 1.000 em principal!
                    var consRes = await paymentSvc.ConsumeSurplusAsync(new ConsumeSurplusRequestDto
                    {
                        SurplusBalanceId = surplusBolsa.Id,
                        TargetInstallmentId = inst2.Id,
                        AmountCents = 2500,
                        IdempotencyKey = "CONS-E2-06-KEY"
                    });

                    if (consRes.InterestConsumedCents != 1500 || consRes.PrincipalConsumedCents != 1000)
                        throw new Exception($"TESTE 2.6 FALHOU: Regra A no consumo violada (J={consRes.InterestConsumedCents}, P={consRes.PrincipalConsumedCents}).");

                    if (consRes.RemainingSurplusCents != 500)
                        throw new Exception($"TESTE 2.6 FALHOU: Saldo restante da bolsa esperado 500, obtido {consRes.RemainingSurplusCents}.");

                    consumptionId = consRes.ConsumptionId;
                    results.Add("TEST_STAGE2_06_SURPLUS_CONSUMPTION_APPROPRIATES_INTEREST_FIRST: PASS");
                    Console.WriteLine("TEST_STAGE2_06_SURPLUS_CONSUMPTION_APPROPRIATES_INTEREST_FIRST: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.7: Reversão de consumo mantendo pagamentos independentes
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var paymentSvc = new CreditPaymentService(ctx, proService);

                    var cons = await ctx.CreditSurplusConsumptions
                        .Include(c => c.TargetInstallment)
                        .FirstAsync(c => c.Id == consumptionId);

                    var inst = cons.TargetInstallment!;

                    // Registra um pagamento em dinheiro independente de R$ 10,00 (1.000 cents) na mesma parcela
                    var allocDinheiro = new CreditPaymentInstallmentAlloc
                    {
                        Id = Guid.NewGuid().ToString(),
                        PaymentId = (await ctx.CreditPayments.FirstAsync(p => p.ContractId == inst.ContractId)).Id,
                        InstallmentId = inst.Id,
                        AllocatedAmount = 1000,
                        InterestAllocated = 0,
                        PrincipalAllocated = 1000,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    ctx.CreditPaymentInstallmentAllocs.Add(allocDinheiro);
                    inst.PaidAmountCents += 1000;
                    await ctx.SaveChangesAsync();

                    // Total pago na parcela agora é 2.500 (consumo) + 1.000 (dinheiro) = 3.500 cents
                    if (inst.PaidAmountCents != 3500)
                        throw new Exception("Falha na configuração do teste 2.7.");

                    // Executa a REVERSÃO do consumo de excedente de 2.500 cents
                    var revRes = await paymentSvc.ReverseSurplusConsumptionAsync(new ReverseSurplusConsumptionRequestDto
                    {
                        ConsumptionId = consumptionId,
                        Reason = "Cancelamento do consumo de teste",
                        IdempotencyKey = "REV-E2-07-KEY"
                    });

                    // A bolsa deve ter seus 2.500 cents restaurados (500 + 2.500 = 3.000 cents)
                    if (revRes.RestoredSurplusBalanceCents != 3000)
                        throw new Exception($"TESTE 2.7 FALHOU: Saldo restaurado esperado 3.000, obtido {revRes.RestoredSurplusBalanceCents}.");

                    // A parcela destino deve reter os 1.000 cents do pagamento em dinheiro independente e ficar PartiallyPaid!
                    var reloadedInst = await ctx.CreditInstallments.FindAsync(inst.Id);
                    if (reloadedInst!.PaidAmountCents != 1000 || reloadedInst.Status != CreditInstallmentStatus.PartiallyPaid)
                        throw new Exception($"TESTE 2.7 FALHOU: Parcela não manteve o pagamento independente (Pago={reloadedInst.PaidAmountCents}, Status={reloadedInst.Status}).");

                    results.Add("TEST_STAGE2_07_REVERSAL_OF_CONSUMPTION_PRESERVES_INDEPENDENT_PAYMENTS: PASS");
                    Console.WriteLine("TEST_STAGE2_07_REVERSAL_OF_CONSUMPTION_PRESERVES_INDEPENDENT_PAYMENTS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.8: Bloqueio de sobrealocação no consumo
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var paymentSvc = new CreditPaymentService(ctx, proService);

                    var cons = await ctx.CreditSurplusConsumptions.FirstAsync(c => c.Id == consumptionId);
                    var surplus = await ctx.CreditSurplusBalances.FirstAsync(s => s.Id == cons.SurplusBalanceId);
                    var inst = await ctx.CreditInstallments.FirstAsync(i => i.Id == cons.TargetInstallmentId);

                    // Saldo remanescente da parcela é 5.000 - 1.000 = 4.000 cents.
                    // Tenta consumir 4.500 cents (superior ao saldo remanescente de 4.000)
                    // Primeiro garante que a bolsa tem saldo suficiente
                    surplus.RemainingSurplusCents = 10000;
                    await ctx.SaveChangesAsync();

                    bool blocked = false;
                    try
                    {
                        await paymentSvc.ConsumeSurplusAsync(new ConsumeSurplusRequestDto
                        {
                            SurplusBalanceId = surplus.Id,
                            TargetInstallmentId = inst.Id,
                            AmountCents = 4500, // 4.500 > 4.000
                            IdempotencyKey = "OVERALLOC-KEY"
                        });
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("Sobrealocação rejeitada"))
                    {
                        blocked = true;
                    }

                    if (!blocked)
                        throw new Exception("TESTE 2.8 FALHOU: Permitiu sobrealocação no consumo de excedente.");

                    results.Add("TEST_STAGE2_08_OVERALLOCATION_BLOCKED: PASS");
                    Console.WriteLine("TEST_STAGE2_08_OVERALLOCATION_BLOCKED: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.9: Repetição idempotente com payload idêntico
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var paymentSvc = new CreditPaymentService(ctx, proService);
                    var client = await ctx.Clients.FirstAsync();
                    var contractSvc = new CreditContractService(ctx, proService);

                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E2-09",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 2000,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(10),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E2-09-KEY"
                    });
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cRes.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E2-09-KEY"
                    });

                    var req = new PaymentRequestDto
                    {
                        ContractId = cRes.ContractId,
                        AmountCents = 5000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "IDEM-PAY-09"
                    };

                    var res1 = await paymentSvc.RegisterPaymentAsync(req);
                    var res2 = await paymentSvc.RegisterPaymentAsync(req);

                    if (res1.PaymentId != res2.PaymentId)
                        throw new Exception("TESTE 2.9 FALHOU: Retentativa gerou IDs diferentes.");

                    int paymentCount = await ctx.CreditPayments.CountAsync(p => p.ContractId == cRes.ContractId);
                    if (paymentCount != 1)
                        throw new Exception($"TESTE 2.9 FALHOU: Criou {paymentCount} pagamentos em vez de 1.");

                    results.Add("TEST_STAGE2_09_IDEMPOTENT_RETRY_SAME_PAYLOAD: PASS");
                    Console.WriteLine("TEST_STAGE2_09_IDEMPOTENT_RETRY_SAME_PAYLOAD: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.10: Conflito de chave com payload divergente
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var paymentSvc = new CreditPaymentService(ctx, proService);
                    var contract = await ctx.CreditContracts.FirstAsync(c => c.ContractNumber == "CTR-E2-09");

                    var divergentReq = new PaymentRequestDto
                    {
                        ContractId = contract.Id,
                        AmountCents = 99999, // Divergente de 5000
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "IDEM-PAY-09"
                    };

                    bool conflictCaught = false;
                    try
                    {
                        await paymentSvc.RegisterPaymentAsync(divergentReq);
                    }
                    catch (IdempotencyConflictException)
                    {
                        conflictCaught = true;
                    }

                    if (!conflictCaught)
                        throw new Exception("TESTE 2.10 FALHOU: Reutilização de chave com payload diferente não disparou IdempotencyConflictException.");

                    results.Add("TEST_STAGE2_10_IDEMPOTENCY_CONFLICT_DIVERGENT_PAYLOAD: PASS");
                    Console.WriteLine("TEST_STAGE2_10_IDEMPOTENCY_CONFLICT_DIVERGENT_PAYLOAD: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.11: Duas requisições concorrentes sob BEGIN IMMEDIATE
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var client = await ctx.Clients.FirstAsync();
                    var contractSvc = new CreditContractService(ctx, proService);

                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E2-11",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 2000,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(10),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E2-11-KEY"
                    });
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cRes.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E2-11-KEY"
                    });

                    // Duas conexões paralelas tentando registrar pagamento
                    var task1 = Task.Run(async () =>
                    {
                        using var localCtx = new CobraPixDbContext(options);
                        var localPaySvc = new CreditPaymentService(localCtx, new ProService(localCtx));
                        return await localPaySvc.RegisterPaymentAsync(new PaymentRequestDto
                        {
                            ContractId = cRes.ContractId,
                            AmountCents = 4000,
                            PaymentDate = DateTime.Today,
                            IdempotencyKey = "CONCUR-P1"
                        });
                    });

                    var task2 = Task.Run(async () =>
                    {
                        using var localCtx = new CobraPixDbContext(options);
                        var localPaySvc = new CreditPaymentService(localCtx, new ProService(localCtx));
                        return await localPaySvc.RegisterPaymentAsync(new PaymentRequestDto
                        {
                            ContractId = cRes.ContractId,
                            AmountCents = 6000,
                            PaymentDate = DateTime.Today,
                            IdempotencyKey = "CONCUR-P2"
                        });
                    });

                    var resultsConcur = await Task.WhenAll(task1, task2);
                    if (resultsConcur.Length != 2)
                        throw new Exception("TESTE 2.11 FALHOU na execução concorrente.");

                    using (var verifyCtx = new CobraPixDbContext(options))
                    {
                        var inst = await verifyCtx.CreditInstallments.FirstAsync(i => i.ContractId == cRes.ContractId);
                        if (inst.PaidAmountCents != 10000 || inst.Status != CreditInstallmentStatus.PartiallyPaid)
                            throw new Exception($"TESTE 2.11 FALHOU: Saldo total pós-concorrência incorreto ({inst.PaidAmountCents} != 10000).");
                    }

                    results.Add("TEST_STAGE2_11_CONCURRENT_REQUESTS_SERIALIZED: PASS");
                    Console.WriteLine("TEST_STAGE2_11_CONCURRENT_REQUESTS_SERIALIZED: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.12: Falha transacional sem persistência parcial
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var executor = new CreditTransactionExecutor(ctx);
                    var anyContract = await ctx.CreditContracts.FirstAsync();
                    int prePaymentCount = await ctx.CreditPayments.CountAsync();

                    try
                    {
                        await executor.ExecuteTransactionalAsync<string>(
                            "TestPartialFailure",
                            "FAIL-PARTIAL-KEY",
                            "fail-payload",
                            async localCtx =>
                            {
                                localCtx.CreditPayments.Add(new CreditPayment
                                {
                                    Id = "phantom-payment-id",
                                    ContractId = anyContract.Id,
                                    PaymentDate = DateTime.Today,
                                    AmountCents = 5000,
                                    Status = CreditPaymentStatus.Active,
                                    CreatedAtUtc = DateTime.UtcNow
                                });
                                await localCtx.SaveChangesAsync();

                                throw new ApplicationException("Simulando crash ou erro fatal antes do commit");
                            });
                    }
                    catch (ApplicationException)
                    {
                        // Esperado
                    }

                    int postPaymentCount = await ctx.CreditPayments.CountAsync();
                    if (postPaymentCount != prePaymentCount)
                        throw new Exception("TESTE 2.12 FALHOU: Pagamento fantasma foi persistido após rollback.");

                    results.Add("TEST_STAGE2_12_TRANSACTIONAL_FAILURE_NO_PARTIAL_PERSISTENCE: PASS");
                    Console.WriteLine("TEST_STAGE2_12_TRANSACTIONAL_FAILURE_NO_PARTIAL_PERSISTENCE: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.13: Nenhuma duplicação do movimento financeiro
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var payments = await ctx.CreditPayments.ToListAsync();
                    var paymentIds = payments.Select(p => p.Id).ToList();
                    var distinctIds = paymentIds.Distinct().ToList();

                    if (paymentIds.Count != distinctIds.Count)
                        throw new Exception("TESTE 2.13 FALHOU: IDs de pagamentos duplicados detectados.");

                    results.Add("TEST_STAGE2_13_NO_DUPLICATION_OF_FINANCIAL_MOVEMENT: PASS");
                    Console.WriteLine("TEST_STAGE2_13_NO_DUPLICATION_OF_FINANCIAL_MOVEMENT: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 2.14: Preservação das métricas e do histórico financeiro legado
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    // Assegura que nenhuma das operações do módulo de crédito tocou ou alterou as tabelas legadas
                    var client = await ctx.Clients.FirstAsync();
                    var salesCount = await ctx.Sales.CountAsync();
                    var installmentPaymentsCount = await ctx.InstallmentPayments.CountAsync();
                    var monthlyPaymentsCount = await ctx.MonthlyPayments.CountAsync();

                    if (salesCount != 1)
                        throw new Exception($"TESTE 2.14 FALHOU: Quantidade de vendas legadas alterada ({salesCount} != 1).");

                    if (installmentPaymentsCount != 0 || monthlyPaymentsCount != 0)
                        throw new Exception("TESTE 2.14 FALHOU: Pagamentos legados foram alterados.");

                    results.Add("TEST_STAGE2_14_PRESERVATION_OF_LEGACY_FINANCIAL_METRICS: PASS");
                    Console.WriteLine("TEST_STAGE2_14_PRESERVATION_OF_LEGACY_FINANCIAL_METRICS: PASS");
                }

                // =============================================================
                // ETAPA 3: ESTORNOS, REVERSÕES E DEVOLUÇÕES DE CAIXA
                // =============================================================
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine("INICIANDO TESTES DA ETAPA 3: ESTORNOS E DEVOLUÇÕES");
                Console.WriteLine("--------------------------------------------------");

                // -------------------------------------------------------------
                // TESTE 3.01: Autorizar estorno sem gerar saída de caixa
                // -------------------------------------------------------------
                string testPaymentId301 = string.Empty;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var client = await ctx.Clients.FirstAsync();
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paySvc = new CreditPaymentService(ctx, proService);
                    var refundSvc = new CreditRefundService(ctx, proService);

                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E3-01",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 2000,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(15),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E3-01-KEY"
                    });
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cRes.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E3-01-KEY"
                    });

                    var pRes = await paySvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cRes.ContractId,
                        AmountCents = 12000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E3-01-KEY"
                    });
                    testPaymentId301 = pRes.PaymentId;

                    var refRes = await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                    {
                        PaymentId = testPaymentId301,
                        Reason = "Erro operacional de digitação",
                        IdempotencyKey = "REF-E3-01-KEY"
                    });

                    if (refRes.Status != CreditRefundStatus.Authorized)
                        throw new Exception("TESTE 3.01 FALHOU: Estorno não foi autorizado corretamente.");

                    // Verificar se o caixa NÃO foi movimentado
                    int outflowCount = await ctx.CreditCashOutflows.CountAsync();
                    if (outflowCount != 0)
                        throw new Exception("TESTE 3.01 FALHOU: Saída de caixa gerada indevidamente na autorização de estorno.");

                    // Verificar reabertura da dívida
                    using (var vCtx = new CobraPixDbContext(options))
                    {
                        var inst = await vCtx.CreditInstallments.FirstAsync(i => i.ContractId == cRes.ContractId);
                        if (inst.PaidAmountCents != 0 || inst.Status != CreditInstallmentStatus.Pending)
                            throw new Exception("TESTE 3.01 FALHOU: Obrigação não reaberta após autorização de estorno.");
                    }

                    results.Add("TEST_STAGE3_01_AUTHORIZE_REFUND_WITHOUT_CASH_OUTFLOW: PASS");
                    Console.WriteLine("TEST_STAGE3_01_AUTHORIZE_REFUND_WITHOUT_CASH_OUTFLOW: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.02: Estornar pagamento e reabrir a dívida com alocações históricas preservadas
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var client = await ctx.Clients.FirstAsync();
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paySvc = new CreditPaymentService(ctx, proService);
                    var refundSvc = new CreditRefundService(ctx, proService);

                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E3-02",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 2000,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(20),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E3-02-KEY"
                    });
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cRes.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E3-02-KEY"
                    });

                    // Pagamento parcial de 5000: juros = 2000, principal = 3000
                    var pRes = await paySvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cRes.ContractId,
                        AmountCents = 5000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E3-02-KEY"
                    });

                    await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                    {
                        PaymentId = pRes.PaymentId,
                        Reason = "Estorno parcial histórico",
                        IdempotencyKey = "REF-E3-02-KEY"
                    });

                    using (var vCtx = new CobraPixDbContext(options))
                    {
                        var inst = await vCtx.CreditInstallments.FirstAsync(i => i.ContractId == cRes.ContractId);
                        if (inst.PaidAmountCents != 0 || inst.Status != CreditInstallmentStatus.Pending)
                            throw new Exception("TESTE 3.02 FALHOU: Parcela não reaberta para zero após estorno de alocação histórica.");
                    }

                    results.Add("TEST_STAGE3_02_REFUND_PRESERVES_HISTORICAL_ALLOCATIONS: PASS");
                    Console.WriteLine("TEST_STAGE3_02_REFUND_PRESERVES_HISTORICAL_ALLOCATIONS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.03: Bloquear estorno quando houver consumo ativo do excedente originado pelo pagamento
                // -------------------------------------------------------------
                string testPaymentId303 = string.Empty;
                string testConsumptionId303 = string.Empty;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var client = await ctx.Clients.FirstAsync();
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paySvc = new CreditPaymentService(ctx, proService);
                    var refundSvc = new CreditRefundService(ctx, proService);

                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E3-03",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 0,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(10),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E3-03-KEY"
                    });
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cRes.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E3-03-KEY"
                    });

                    // Pagamento a maior: 15000 (10000 quita parcela, 5000 vira excedente)
                    var pRes = await paySvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cRes.ContractId,
                        AmountCents = 15000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E3-03-KEY"
                    });
                    testPaymentId303 = pRes.PaymentId;

                    var surplus = await ctx.CreditSurplusBalances.FirstAsync(s => s.SourcePaymentId == testPaymentId303);

                    // Adiciona parcela 2 no mesmo contrato para consumir o excedente dentro do escopo V1
                    var inst2 = new CreditInstallment
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = cRes.ContractId,
                        InstallmentNumber = 2,
                        DueDate = DateTime.Today.AddDays(25),
                        NominalPrincipalCents = 5000,
                        NominalInterestCents = 0,
                        TotalAmountCents = 5000,
                        PaidAmountCents = 0,
                        Status = CreditInstallmentStatus.Pending,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    ctx.CreditInstallments.Add(inst2);

                    var ctrLoaded = await ctx.CreditContracts.FindAsync(cRes.ContractId);
                    ctrLoaded!.DisbursedPrincipalCents += 5000;
                    ctrLoaded.TotalAmountCents += 5000;
                    await ctx.SaveChangesAsync();

                    var consRes = await paySvc.ConsumeSurplusAsync(new ConsumeSurplusRequestDto
                    {
                        SurplusBalanceId = surplus.Id,
                        TargetInstallmentId = inst2.Id,
                        AmountCents = 2000,
                        IdempotencyKey = "CONS-E3-03-KEY"
                    });
                    testConsumptionId303 = consRes.ConsumptionId;

                    // Tentativa de estorno deve ser bloqueada
                    bool blocked = false;
                    try
                    {
                        await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                        {
                            PaymentId = testPaymentId303,
                            Reason = "Tentativa com excedente consumido",
                            IdempotencyKey = "REF-E3-03-FAIL"
                        });
                    }
                    catch (DependentSurplusConsumptionsActiveException)
                    {
                        blocked = true;
                    }

                    if (!blocked)
                        throw new Exception("TESTE 3.03 FALHOU: Estorno com excedente consumido ativamente não foi bloqueado.");

                    results.Add("TEST_STAGE3_03_BLOCK_REFUND_WHEN_DEPENDENT_CONSUMPTIONS_ACTIVE: PASS");
                    Console.WriteLine("TEST_STAGE3_03_BLOCK_REFUND_WHEN_DEPENDENT_CONSUMPTIONS_ACTIVE: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.04: Permitir estorno após a reversão dos consumos dependentes
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var paySvc = new CreditPaymentService(ctx, proService);
                    var refundSvc = new CreditRefundService(ctx, proService);

                    // Reverte o consumo dependente do teste 3.03
                    await paySvc.ReverseSurplusConsumptionAsync(new ReverseSurplusConsumptionRequestDto
                    {
                        ConsumptionId = testConsumptionId303,
                        Reason = "Desfazendo consumo para viabilizar estorno do pagamento de origem",
                        IdempotencyKey = "REV-E3-04-KEY"
                    });

                    // Agora o estorno deve ser autorizado com sucesso
                    var refRes = await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                    {
                        PaymentId = testPaymentId303,
                        Reason = "Estorno após reversão prévia",
                        IdempotencyKey = "REF-E3-04-OK"
                    });

                    if (refRes.Status != CreditRefundStatus.Authorized)
                        throw new Exception("TESTE 3.04 FALHOU: Estorno não foi autorizado após reversão de dependências.");

                    results.Add("TEST_STAGE3_04_ALLOW_REFUND_AFTER_REVERSING_DEPENDENT_CONSUMPTIONS: PASS");
                    Console.WriteLine("TEST_STAGE3_04_ALLOW_REFUND_AFTER_REVERSING_DEPENDENT_CONSUMPTIONS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.05: Preservar pagamentos independentes ao estornar outro pagamento
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var client = await ctx.Clients.FirstAsync();
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paySvc = new CreditPaymentService(ctx, proService);
                    var refundSvc = new CreditRefundService(ctx, proService);

                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E3-05",
                        PrincipalAmountCents = 12000,
                        TotalInterestCents = 0,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(30),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E3-05-KEY"
                    });
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cRes.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E3-05-KEY"
                    });

                    // Pagamento 1: 3000
                    var p1 = await paySvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cRes.ContractId,
                        AmountCents = 3000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E3-05-P1"
                    });

                    // Pagamento 2: 4000
                    var p2 = await paySvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cRes.ContractId,
                        AmountCents = 4000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E3-05-P2"
                    });

                    // Estorna apenas Pagamento 1
                    await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                    {
                        PaymentId = p1.PaymentId,
                        Reason = "Estorno do P1",
                        IdempotencyKey = "REF-E3-05-P1"
                    });

                    using (var vCtx = new CobraPixDbContext(options))
                    {
                        var inst = await vCtx.CreditInstallments.FirstAsync(i => i.ContractId == cRes.ContractId);
                        if (inst.PaidAmountCents != 4000 || inst.Status != CreditInstallmentStatus.PartiallyPaid)
                            throw new Exception($"TESTE 3.05 FALHOU: Saldo da parcela incorreto ({inst.PaidAmountCents} != 4000).");

                        var pay2Rec = await vCtx.CreditPayments.FirstAsync(p => p.Id == p2.PaymentId);
                        if (pay2Rec.Status != CreditPaymentStatus.Active)
                            throw new Exception("TESTE 3.05 FALHOU: Pagamento 2 teve seu estado indevidamente alterado.");
                    }

                    results.Add("TEST_STAGE3_05_PRESERVE_INDEPENDENT_PAYMENTS: PASS");
                    Console.WriteLine("TEST_STAGE3_05_PRESERVE_INDEPENDENT_PAYMENTS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.06: Impedir estorno duplicado
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var refundSvc = new CreditRefundService(ctx, proService);

                    bool duplicateBlocked = false;
                    try
                    {
                        await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                        {
                            PaymentId = testPaymentId301,
                            Reason = "Segunda solicitação de estorno",
                            IdempotencyKey = "REF-E3-06-DUP"
                        });
                    }
                    catch (PaymentAlreadyRefundedException)
                    {
                        duplicateBlocked = true;
                    }

                    if (!duplicateBlocked)
                        throw new Exception("TESTE 3.06 FALHOU: Estorno duplicado não foi bloqueado.");

                    results.Add("TEST_STAGE3_06_PREVENT_DUPLICATE_REFUND: PASS");
                    Console.WriteLine("TEST_STAGE3_06_PREVENT_DUPLICATE_REFUND: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.07: Repetir solicitação com a mesma chave e payload e retornar o resultado original
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var refundSvc = new CreditRefundService(ctx, proService);

                    var replayRes = await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                    {
                        PaymentId = testPaymentId301,
                        Reason = "Erro operacional de digitação",
                        IdempotencyKey = "REF-E3-01-KEY" // Mesma chave e mesmo payload do teste 3.01
                    });

                    if (replayRes.Status != CreditRefundStatus.Authorized)
                        throw new Exception("TESTE 3.07 FALHOU: Resposta idempotente retornou estado inconsistente.");

                    results.Add("TEST_STAGE3_07_IDEMPOTENT_RETRY_SAME_PAYLOAD: PASS");
                    Console.WriteLine("TEST_STAGE3_07_IDEMPOTENT_RETRY_SAME_PAYLOAD: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.08: Rejeitar reutilização da chave com payload divergente
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var refundSvc = new CreditRefundService(ctx, proService);

                    bool conflictCaught = false;
                    try
                    {
                        await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                        {
                            PaymentId = testPaymentId301,
                            Reason = "Razão divergente para disparar conflito",
                            IdempotencyKey = "REF-E3-01-KEY" // Mesma chave, payload divergente
                        });
                    }
                    catch (IdempotencyConflictException)
                    {
                        conflictCaught = true;
                    }

                    if (!conflictCaught)
                        throw new Exception("TESTE 3.08 FALHOU: IdempotencyConflictException não foi disparada.");

                    results.Add("TEST_STAGE3_08_IDEMPOTENCY_CONFLICT_DIVERGENT_PAYLOAD: PASS");
                    Console.WriteLine("TEST_STAGE3_08_IDEMPOTENCY_CONFLICT_DIVERGENT_PAYLOAD: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.09: Autorizar devolução sem movimentação de caixa
                // -------------------------------------------------------------
                string testDevolutionId309 = string.Empty;
                string testPaymentId309 = string.Empty;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var client = await ctx.Clients.FirstAsync();
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paySvc = new CreditPaymentService(ctx, proService);
                    var refundSvc = new CreditRefundService(ctx, proService);

                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E3-09",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 0,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(10),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E3-09-KEY"
                    });
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cRes.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E3-09-KEY"
                    });

                    var pRes = await paySvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cRes.ContractId,
                        AmountCents = 10000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E3-09-KEY"
                    });
                    testPaymentId309 = pRes.PaymentId;

                    int preOutflows = await ctx.CreditCashOutflows.CountAsync();

                    var devRes = await refundSvc.AuthorizeDevolutionAsync(new AuthorizeDevolutionRequestDto
                    {
                        PaymentId = testPaymentId309,
                        AmountCents = 5000,
                        Reason = "Devolução parcial autorizada",
                        IdempotencyKey = "DEV-E3-09-KEY"
                    });
                    testDevolutionId309 = devRes.DevolutionId;

                    if (devRes.Status != CreditDevolutionStatus.Authorized)
                        throw new Exception("TESTE 3.09 FALHOU: Devolução não autorizada corretamente.");

                    int postOutflows = await ctx.CreditCashOutflows.CountAsync();
                    if (postOutflows != preOutflows)
                        throw new Exception("TESTE 3.09 FALHOU: Caixa movimentado durante autorização de devolução.");

                    results.Add("TEST_STAGE3_09_AUTHORIZE_DEVOLUTION_WITHOUT_CASH_MOVEMENT: PASS");
                    Console.WriteLine("TEST_STAGE3_09_AUTHORIZE_DEVOLUTION_WITHOUT_CASH_MOVEMENT: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.10: Liquidar devolução e registrar uma única saída de caixa
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var refundSvc = new CreditRefundService(ctx, proService);

                    int preOutflows = await ctx.CreditCashOutflows.CountAsync();

                    var settleRes = await refundSvc.SettleDevolutionAsync(new SettleDevolutionRequestDto
                    {
                        DevolutionId = testDevolutionId309,
                        SettlementDate = DateTime.Today,
                        PaymentMethod = PaymentMethod.Pix,
                        IdempotencyKey = "SETTLE-E3-10-KEY"
                    });

                    if (settleRes.Status != CreditDevolutionStatus.Settled)
                        throw new Exception("TESTE 3.10 FALHOU: Devolução não transitou para Settled.");

                    int postOutflows = await ctx.CreditCashOutflows.CountAsync();
                    if (postOutflows != preOutflows + 1)
                        throw new Exception("TESTE 3.10 FALHOU: Exatamente uma saída de caixa deveria ter sido gerada.");

                    var outflow = await ctx.CreditCashOutflows.FirstAsync(o => o.DevolutionId == testDevolutionId309);
                    if (outflow.AmountCents != 5000)
                        throw new Exception("TESTE 3.10 FALHOU: Valor da saída de caixa incorreto.");

                    results.Add("TEST_STAGE3_10_SETTLE_DEVOLUTION_SINGLE_CASH_OUTFLOW: PASS");
                    Console.WriteLine("TEST_STAGE3_10_SETTLE_DEVOLUTION_SINGLE_CASH_OUTFLOW: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.11: Repetir liquidação sem duplicar a saída
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var refundSvc = new CreditRefundService(ctx, proService);

                    int preOutflows = await ctx.CreditCashOutflows.CountAsync();

                    var repeatRes = await refundSvc.SettleDevolutionAsync(new SettleDevolutionRequestDto
                    {
                        DevolutionId = testDevolutionId309,
                        SettlementDate = DateTime.Today,
                        PaymentMethod = PaymentMethod.Pix,
                        IdempotencyKey = "SETTLE-E3-10-KEY" // Mesma chave
                    });

                    if (repeatRes.Status != CreditDevolutionStatus.Settled)
                        throw new Exception("TESTE 3.11 FALHOU: Resposta de liquidação repetida inconsistente.");

                    int postOutflows = await ctx.CreditCashOutflows.CountAsync();
                    if (postOutflows != preOutflows)
                        throw new Exception("TESTE 3.11 FALHOU: Saída financeira duplicada detectada na repetição de liquidação.");

                    results.Add("TEST_STAGE3_11_REPEAT_SETTLEMENT_NO_DUPLICATION: PASS");
                    Console.WriteLine("TEST_STAGE3_11_REPEAT_SETTLEMENT_NO_DUPLICATION: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.12: Cancelar devolução autorizada e impedir liquidação posterior
                // -------------------------------------------------------------
                string testDevolutionId312 = string.Empty;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var refundSvc = new CreditRefundService(ctx, proService);

                    // Devolução para os 5000 restantes do pagamento do teste 3.09
                    var devRes = await refundSvc.AuthorizeDevolutionAsync(new AuthorizeDevolutionRequestDto
                    {
                        PaymentId = testPaymentId309,
                        AmountCents = 5000,
                        Reason = "Devolução que será cancelada",
                        IdempotencyKey = "DEV-E3-12-KEY"
                    });
                    testDevolutionId312 = devRes.DevolutionId;

                    var cancelRes = await refundSvc.CancelDevolutionAsync(new CancelDevolutionRequestDto
                    {
                        DevolutionId = testDevolutionId312,
                        Reason = "Cancelamento pelo operador",
                        IdempotencyKey = "CANCEL-E3-12-KEY"
                    });

                    if (cancelRes.Status != CreditDevolutionStatus.Cancelled)
                        throw new Exception("TESTE 3.12 FALHOU: Devolução não transitou para Cancelled.");

                    bool settleBlocked = false;
                    try
                    {
                        await refundSvc.SettleDevolutionAsync(new SettleDevolutionRequestDto
                        {
                            DevolutionId = testDevolutionId312,
                            IdempotencyKey = "SETTLE-E3-12-FAIL"
                        });
                    }
                    catch (DevolutionInvalidStateException)
                    {
                        settleBlocked = true;
                    }

                    if (!settleBlocked)
                        throw new Exception("TESTE 3.12 FALHOU: Liquidação de devolução cancelada não foi impedida.");

                    results.Add("TEST_STAGE3_12_CANCEL_DEVOLUTION_BLOCKS_SETTLEMENT: PASS");
                    Console.WriteLine("TEST_STAGE3_12_CANCEL_DEVOLUTION_BLOCKS_SETTLEMENT: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.13: Impedir cancelamento de devolução liquidada
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var refundSvc = new CreditRefundService(ctx, proService);

                    bool cancelBlocked = false;
                    try
                    {
                        await refundSvc.CancelDevolutionAsync(new CancelDevolutionRequestDto
                        {
                            DevolutionId = testDevolutionId309, // Devolução liquidada no teste 3.10
                            Reason = "Tentativa ilegal de cancelar devolução liquidada",
                            IdempotencyKey = "CANCEL-E3-13-FAIL"
                        });
                    }
                    catch (DevolutionInvalidStateException)
                    {
                        cancelBlocked = true;
                    }

                    if (!cancelBlocked)
                        throw new Exception("TESTE 3.13 FALHOU: Cancelamento de devolução liquidada não foi impedido.");

                    results.Add("TEST_STAGE3_13_PREVENT_CANCEL_OF_SETTLED_DEVOLUTION: PASS");
                    Console.WriteLine("TEST_STAGE3_13_PREVENT_CANCEL_OF_SETTLED_DEVOLUTION: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.14: Bloquear devoluções acima do valor elegível
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var client = await ctx.Clients.FirstAsync();
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paySvc = new CreditPaymentService(ctx, proService);
                    var refundSvc = new CreditRefundService(ctx, proService);

                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E3-14",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 0,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(10),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E3-14-KEY"
                    });
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cRes.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E3-14-KEY"
                    });

                    var pRes = await paySvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cRes.ContractId,
                        AmountCents = 10000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E3-14-KEY"
                    });

                    bool blocked = false;
                    try
                    {
                        await refundSvc.AuthorizeDevolutionAsync(new AuthorizeDevolutionRequestDto
                        {
                            PaymentId = pRes.PaymentId,
                            AmountCents = 15000, // 15000 > 10000 elegível
                            Reason = "Excesso de valor",
                            IdempotencyKey = "DEV-E3-14-FAIL"
                        });
                    }
                    catch (ExceededEligibleDevolutionAmountException)
                    {
                        blocked = true;
                    }

                    if (!blocked)
                        throw new Exception("TESTE 3.14 FALHOU: Devolução acima do valor elegível não foi bloqueada.");

                    results.Add("TEST_STAGE3_14_BLOCK_DEVOLUTIONS_EXCEEDING_ELIGIBLE_AMOUNT: PASS");
                    Console.WriteLine("TEST_STAGE3_14_BLOCK_DEVOLUTIONS_EXCEEDING_ELIGIBLE_AMOUNT: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.15: Impedir duas devoluções concorrentes que, somadas, excedam o limite
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var client = await ctx.Clients.FirstAsync();
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paySvc = new CreditPaymentService(ctx, proService);

                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E3-15",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 0,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(10),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E3-15-KEY"
                    });
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cRes.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E3-15-KEY"
                    });

                    var pRes = await paySvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cRes.ContractId,
                        AmountCents = 10000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E3-15-KEY"
                    });

                    // Duas conexões paralelas tentando autorizar 6000 cada (6000 + 6000 = 12000 > 10000)
                    var task1 = Task.Run(async () =>
                    {
                        using var localCtx = new CobraPixDbContext(options);
                        var localRefundSvc = new CreditRefundService(localCtx, new ProService(localCtx));
                        try
                        {
                            return await localRefundSvc.AuthorizeDevolutionAsync(new AuthorizeDevolutionRequestDto
                            {
                                PaymentId = pRes.PaymentId,
                                AmountCents = 6000,
                                Reason = "Devolução Concorrente 1",
                                IdempotencyKey = "CONCUR-DEV-1"
                            });
                        }
                        catch (ExceededEligibleDevolutionAmountException)
                        {
                            return null;
                        }
                    });

                    var task2 = Task.Run(async () =>
                    {
                        using var localCtx = new CobraPixDbContext(options);
                        var localRefundSvc = new CreditRefundService(localCtx, new ProService(localCtx));
                        try
                        {
                            return await localRefundSvc.AuthorizeDevolutionAsync(new AuthorizeDevolutionRequestDto
                            {
                                PaymentId = pRes.PaymentId,
                                AmountCents = 6000,
                                Reason = "Devolução Concorrente 2",
                                IdempotencyKey = "CONCUR-DEV-2"
                            });
                        }
                        catch (ExceededEligibleDevolutionAmountException)
                        {
                            return null;
                        }
                    });

                    var resultsConcurDev = await Task.WhenAll(task1, task2);
                    int successes = resultsConcurDev.Count(r => r != null);
                    int failures = resultsConcurDev.Count(r => r == null);

                    if (successes != 1 || failures != 1)
                        throw new Exception($"TESTE 3.15 FALHOU: Exatamente uma devolução concorrente deveria ter sucesso e a outra falhar ({successes} sucessos, {failures} falhas).");

                    using (var vCtx = new CobraPixDbContext(options))
                    {
                        long totalAuthorized = await vCtx.CreditDevolutions
                            .Where(d => d.PaymentId == pRes.PaymentId)
                            .SumAsync(d => d.AuthorizedAmountCents);

                        if (totalAuthorized != 6000)
                            throw new Exception($"TESTE 3.15 FALHOU: Total autorizado pós-concorrência incorreto ({totalAuthorized} != 6000).");
                    }

                    results.Add("TEST_STAGE3_15_CONCURRENT_DEVOLUTIONS_SERIALIZED_LIMIT_ENFORCED: PASS");
                    Console.WriteLine("TEST_STAGE3_15_CONCURRENT_DEVOLUTIONS_SERIALIZED_LIMIT_ENFORCED: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.16: Demonstrar rollback completo se a movimentação de caixa falhar
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var client = await ctx.Clients.FirstAsync();
                    var contractSvc = new CreditContractService(ctx, proService);
                    var paySvc = new CreditPaymentService(ctx, proService);
                    var refundSvc = new CreditRefundService(ctx, proService);
                    var executor = new CreditTransactionExecutor(ctx);

                    var cRes = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-E3-16",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 0,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(10),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "CTR-E3-16-KEY"
                    });
                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cRes.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = "DISB-E3-16-KEY"
                    });

                    var pRes = await paySvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cRes.ContractId,
                        AmountCents = 10000,
                        PaymentDate = DateTime.Today,
                        IdempotencyKey = "PAY-E3-16-KEY"
                    });

                    var devRes = await refundSvc.AuthorizeDevolutionAsync(new AuthorizeDevolutionRequestDto
                    {
                        PaymentId = pRes.PaymentId,
                        AmountCents = 10000,
                        Reason = "Devolução para teste de rollback",
                        IdempotencyKey = "DEV-E3-16-KEY"
                    });

                    int preOutflows = await ctx.CreditCashOutflows.CountAsync();

                    // Simulando falha durante a gravação da saída de caixa dentro de transação atômica
                    try
                    {
                        await executor.ExecuteTransactionalAsync<string>(
                            "TestRollbackOutflow",
                            "FAIL-OUTFLOW-KEY",
                            "fail-payload",
                            async localCtx =>
                            {
                                var dev = await localCtx.CreditDevolutions.FirstAsync(d => d.Id == devRes.DevolutionId);
                                dev.Status = CreditDevolutionStatus.Settled;

                                localCtx.CreditCashOutflows.Add(new CreditCashOutflow
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    DevolutionId = dev.Id,
                                    PaymentId = pRes.PaymentId,
                                    ContractId = cRes.ContractId,
                                    AmountCents = 10000,
                                    OutflowDate = DateTime.Today,
                                    PaymentMethod = PaymentMethod.Pix,
                                    Description = "Outflow fantasma",
                                    CreatedAtUtc = DateTime.UtcNow
                                });
                                await localCtx.SaveChangesAsync();

                                throw new ApplicationException("Simulando falha de rede/dispositivo durante envio do comprovante bancário");
                            });
                    }
                    catch (ApplicationException)
                    {
                        // Esperado
                    }

                    int postOutflows = await ctx.CreditCashOutflows.CountAsync();
                    if (postOutflows != preOutflows)
                        throw new Exception("TESTE 3.16 FALHOU: Saída de caixa foi persistida após falha transacional.");

                    using (var vCtx = new CobraPixDbContext(options))
                    {
                        var dev = await vCtx.CreditDevolutions.FirstAsync(d => d.Id == devRes.DevolutionId);
                        if (dev.Status != CreditDevolutionStatus.Authorized)
                            throw new Exception("TESTE 3.16 FALHOU: Estado da devolução foi corrompido após rollback.");
                    }

                    results.Add("TEST_STAGE3_16_TRANSACTION_ROLLBACK_ON_CASH_FAILURE: PASS");
                    Console.WriteLine("TEST_STAGE3_16_TRANSACTION_ROLLBACK_ON_CASH_FAILURE: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.17: Verificar a consistência das chaves estrangeiras e das invariantes financeiras
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var devolutions = await ctx.CreditDevolutions.ToListAsync();
                    var outflows = await ctx.CreditCashOutflows.ToListAsync();

                    foreach (var d in devolutions)
                    {
                        if (d.AuthorizedAmountCents <= 0)
                            throw new Exception("TESTE 3.17 FALHOU: Devolução com valor não positivo detectada.");

                        bool contractExists = await ctx.CreditContracts.AnyAsync(c => c.Id == d.ContractId);
                        bool paymentExists = await ctx.CreditPayments.AnyAsync(p => p.Id == d.PaymentId);
                        if (!contractExists || !paymentExists)
                            throw new Exception("TESTE 3.17 FALHOU: Violação de integridade referencial em CreditDevolutions.");
                    }

                    foreach (var o in outflows)
                    {
                        if (o.AmountCents <= 0)
                            throw new Exception("TESTE 3.17 FALHOU: Saída de caixa com valor não positivo detectada.");

                        bool devExists = await ctx.CreditDevolutions.AnyAsync(d => d.Id == o.DevolutionId);
                        if (!devExists)
                            throw new Exception("TESTE 3.17 FALHOU: Violação de integridade referencial em CreditCashOutflows.");
                    }

                    results.Add("TEST_STAGE3_17_FOREIGN_KEY_AND_INVARIANTS_CONSISTENCY: PASS");
                    Console.WriteLine("TEST_STAGE3_17_FOREIGN_KEY_AND_INVARIANTS_CONSISTENCY: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 3.18: Confirmar que as métricas legadas permanecem preservadas
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var finSvc = new FinancialHistoryService(ctx);
                    var summary = await finSvc.GetFinancialSummaryAsync();

                    if (summary.InstallmentPaymentsCount != 0 || summary.MonthlyPaymentsCount != 0 || summary.TotalReceived != 0)
                        throw new Exception("TESTE 3.18 FALHOU: Métricas legadas foram alteradas ou corrompidas pelo módulo de crédito.");

                    var salesCount = await ctx.Sales.CountAsync();
                    if (salesCount != 1)
                        throw new Exception($"TESTE 3.18 FALHOU: Contagem de vendas legadas alterada ({salesCount} != 1).");

                    results.Add("TEST_STAGE3_18_PRESERVATION_OF_LEGACY_FINANCIAL_METRICS: PASS");
                    Console.WriteLine("TEST_STAGE3_18_PRESERVATION_OF_LEGACY_FINANCIAL_METRICS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 4.1: Bloqueio PRO na Interface e ViewModel
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Free, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();

                    if (vm.IsPro || !vm.IsFree)
                        throw new Exception("TESTE 4.1 FALHOU: ViewModel indicou acesso PRO para usuário FREE.");

                    if (string.IsNullOrEmpty(vm.ErrorMessage) || !vm.ErrorMessage.Contains("exclusivo para assinantes CobraPix PRO"))
                        throw new Exception("TESTE 4.1 FALHOU: Mensagem de bloqueio PRO ausente no ViewModel.");

                    await vm.CreateContractAsync();
                    if (string.IsNullOrEmpty(vm.ErrorMessage) || !vm.ErrorMessage.Contains("bloqueada"))
                        throw new Exception("TESTE 4.1 FALHOU: Criação de contrato não bloqueada para usuário FREE.");

                    // Ativa plano PRO e re-inicializa
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");
                    await vm.InitializeAsync();

                    if (!vm.IsPro || vm.IsFree)
                        throw new Exception("TESTE 4.1 FALHOU: ViewModel não reconheceu licença PRO ativa.");

                    results.Add("TEST_STAGE4_01_PRO_BLOCK_AND_ACCESS: PASS");
                    Console.WriteLine("TEST_STAGE4_01_PRO_BLOCK_AND_ACCESS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 4.2: Criação e Validação de Contrato no ViewModel
                // -------------------------------------------------------------
                string uiContractId = string.Empty;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();

                    var client = await ctx.Clients.FirstAsync();

                    // Validação: Cliente nulo
                    vm.SelectedClient = null;
                    await vm.CreateContractAsync();
                    if (string.IsNullOrEmpty(vm.ErrorMessage) || !vm.ErrorMessage.Contains("cliente"))
                        throw new Exception("TESTE 4.2 FALHOU: Validação de cliente nulo não funcionou.");

                    // Validação: Número de contrato vazio
                    vm.SelectedClient = client;
                    vm.NewContractNumber = "   ";
                    await vm.CreateContractAsync();
                    if (string.IsNullOrEmpty(vm.ErrorMessage) || !vm.ErrorMessage.Contains("número"))
                        throw new Exception("TESTE 4.2 FALHOU: Validação de número vazio não funcionou.");

                    // Validação: Principal inválido
                    vm.NewContractNumber = "CTR-UI-01";
                    vm.NewPrincipalAmount = 0m;
                    await vm.CreateContractAsync();
                    if (string.IsNullOrEmpty(vm.ErrorMessage) || !vm.ErrorMessage.Contains("maior que zero"))
                        throw new Exception("TESTE 4.2 FALHOU: Validação de principal zerado não funcionou.");

                    // Criação válida: R$ 600,00 principal + R$ 60,00 juros em 3 parcelas
                    vm.NewPrincipalAmount = 600.00m;
                    vm.NewTotalInterest = 60.00m;
                    vm.NewInstallmentsCount = 3;
                    vm.NewFirstDueDate = DateTime.Today.AddDays(30);
                    vm.NewContractDate = DateTime.Today;
                    vm.NewNotes = "Contrato criado via ViewModel";
                    await vm.CreateContractAsync();

                    if (string.IsNullOrEmpty(vm.SuccessMessage))
                        throw new Exception("TESTE 4.2 FALHOU: Contrato não reportou sucesso na criação.");

                    var created = vm.Contracts.FirstOrDefault(c => c.ContractNumber == "CTR-UI-01");
                    if (created == null)
                        throw new Exception("TESTE 4.2 FALHOU: Contrato criado não encontrado na lista do ViewModel.");

                    uiContractId = created.Id;
                    vm.SelectedContract = created;
                    if (!vm.HasSelectedContract || !vm.CanDisburseSelectedContract)
                        throw new Exception("TESTE 4.2 FALHOU: Estado de seleção do contrato inválido.");

                    results.Add("TEST_STAGE4_02_CONTRACT_CREATION_VALIDATION_AND_SELECTION: PASS");
                    Console.WriteLine("TEST_STAGE4_02_CONTRACT_CREATION_VALIDATION_AND_SELECTION: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 4.3: Desembolso e Cancelamento de Desembolso no ViewModel
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();
                    await vm.LoadContractsAsync();

                    var target = vm.Contracts.First(c => c.Id == uiContractId);
                    vm.SelectedContract = target;

                    // Desembolsa
                    vm.DisbursementDate = DateTime.Today;
                    vm.DisbursementMethod = PaymentMethod.Pix;
                    await vm.DisburseContractAsync();

                    if (vm.SelectedContract.Status != CreditContractStatus.Active)
                        throw new Exception("TESTE 4.3 FALHOU: Contrato não passou para Active após desembolso.");

                    // Cancela desembolso (permitido pois não há pagamentos)
                    vm.CancellationReason = "Cliente optou por rever o fluxo de caixa";
                    await vm.CancelContractAsync();

                    if (vm.SelectedContract.Status != CreditContractStatus.Cancelled)
                        throw new Exception("TESTE 4.3 FALHOU: Desembolso não foi cancelado.");

                    // Cria um novo contrato ativo específico para pagamentos: R$ 300,00 + R$ 30,00 juros em 3 parcelas
                    var client = await ctx.Clients.FirstAsync();
                    vm.SelectedClient = client;
                    vm.NewContractNumber = "CTR-UI-PAY";
                    vm.NewPrincipalAmount = 300.00m;
                    vm.NewTotalInterest = 30.00m;
                    vm.NewInstallmentsCount = 3; // Parcelas de R$ 110,00 (R$ 100 princ + R$ 10 juros)
                    vm.NewFirstDueDate = DateTime.Today.AddDays(30);
                    vm.NewContractDate = DateTime.Today;
                    await vm.CreateContractAsync();

                    var payContract = vm.Contracts.First(c => c.ContractNumber == "CTR-UI-PAY");
                    vm.SelectedContract = payContract;
                    await vm.DisburseContractAsync();

                    if (vm.SelectedContract == null || vm.SelectedContract.Status != CreditContractStatus.Active)
                        throw new Exception("TESTE 4.3 FALHOU: Contrato CTR-UI-PAY não ativado.");

                    results.Add("TEST_STAGE4_03_DISBURSEMENT_AND_CANCELLATION: PASS");
                    Console.WriteLine("TEST_STAGE4_03_DISBURSEMENT_AND_CANCELLATION: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 4.4: Preview de Pagamento sob Regra A no ViewModel
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();
                    await vm.LoadContractsAsync();

                    var payContract = vm.Contracts.First(c => c.ContractNumber == "CTR-UI-PAY");
                    vm.PaymentContractId = payContract.Id;
                    vm.PaymentAmount = 15.00m; // 1500 centavos

                    await vm.PreviewPaymentAsync();

                    if (vm.PaymentPreview == null)
                        throw new Exception("TESTE 4.4 FALHOU: Preview de pagamento nulo.");

                    // Parcela 1 tem R$ 10,00 de juros (1000 centavos) e R$ 100,00 de principal (10000 centavos).
                    // Regra A: R$ 10,00 juros + R$ 5,00 principal = R$ 15,00
                    if (vm.PaymentPreview.TotalInterestAllocatedCents != 1000)
                        throw new Exception($"TESTE 4.4 FALHOU: Juros no preview incorretos ({vm.PaymentPreview.TotalInterestAllocatedCents} != 1000).");

                    if (vm.PaymentPreview.TotalPrincipalAllocatedCents != 500)
                        throw new Exception($"TESTE 4.4 FALHOU: Principal no preview incorreto ({vm.PaymentPreview.TotalPrincipalAllocatedCents} != 500).");

                    if (vm.PaymentPreview.SurplusGeneratedCents != 0)
                        throw new Exception("TESTE 4.4 FALHOU: Excedente inesperado no preview.");

                    results.Add("TEST_STAGE4_04_PAYMENT_PREVIEW_REGRA_A: PASS");
                    Console.WriteLine("TEST_STAGE4_04_PAYMENT_PREVIEW_REGRA_A: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 4.5: Registro de Pagamento e Listagem no ViewModel
                // -------------------------------------------------------------
                string p1Id = string.Empty;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();
                    await vm.LoadContractsAsync();

                    var payContract = vm.Contracts.First(c => c.ContractNumber == "CTR-UI-PAY");
                    vm.PaymentContractId = payContract.Id;
                    vm.PaymentAmount = 15.00m;
                    vm.PaymentMethod = PaymentMethod.Pix;
                    vm.PaymentNotes = "Pagamento parcial UI";

                    await vm.RegisterPaymentAsync();

                    if (string.IsNullOrEmpty(vm.SuccessMessage))
                        throw new Exception("TESTE 4.5 FALHOU: Pagamento não exibiu mensagem de sucesso.");

                    var p = vm.Payments.FirstOrDefault(x => x.ContractId == payContract.Id && x.AmountCents == 1500);
                    if (p == null)
                        throw new Exception("TESTE 4.5 FALHOU: Pagamento de R$ 15,00 não encontrado na lista.");

                    p1Id = p.Id;

                    // Verifica alocação no banco isolado
                    var alloc = await ctx.CreditPaymentInstallmentAllocs.FirstOrDefaultAsync(a => a.PaymentId == p1Id);
                    if (alloc == null || alloc.InterestAllocated != 1000 || alloc.PrincipalAllocated != 500)
                        throw new Exception("TESTE 4.5 FALHOU: Alocação do pagamento persistida incorretamente.");

                    results.Add("TEST_STAGE4_05_PAYMENT_REGISTRATION_AND_LISTING: PASS");
                    Console.WriteLine("TEST_STAGE4_05_PAYMENT_REGISTRATION_AND_LISTING: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 4.6: Ciclo da Seção 4 — Cancelamento de Autorização de Estorno Restaura a Dívida
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();
                    await vm.LoadContractsAsync();

                    var payContract = vm.Contracts.First(c => c.ContractNumber == "CTR-UI-PAY");

                    // Registra segundo pagamento de R$ 95,00 (quitação total da Parcela 1: 15 + 95 = 110)
                    vm.PaymentContractId = payContract.Id;
                    vm.PaymentAmount = 95.00m;
                    vm.PaymentMethod = PaymentMethod.Pix;
                    vm.PaymentNotes = "Quitação Parcela 1";
                    await vm.RegisterPaymentAsync();

                    var p95 = vm.Payments.First(x => x.ContractId == payContract.Id && x.AmountCents == 9500);

                    // Verifica que parcela 1 ficou completamente Quitada (Paid)
                    var inst1 = await ctx.CreditInstallments.AsNoTracking().FirstAsync(i => i.ContractId == payContract.Id && i.InstallmentNumber == 1);
                    if (inst1.Status != CreditInstallmentStatus.Paid || inst1.PaidAmountCents != 11000)
                        throw new Exception($"TESTE 4.6 FALHOU: Parcela 1 não ficou Paid ({inst1.Status}, {inst1.PaidAmountCents}).");

                    // Passo 1: Autoriza estorno do pagamento de R$ 95,00
                    vm.RefundPaymentId = p95.Id;
                    vm.RefundReason = "Solicitação de estorno pelo cliente";
                    await vm.AuthorizeRefundAsync();

                    // Verifica que devolução foi criada em status Authorized
                    var dev = vm.Devolutions.FirstOrDefault(d => d.PaymentId == p95.Id);
                    if (dev == null || dev.Status != CreditDevolutionStatus.Authorized)
                        throw new Exception("TESTE 4.6 FALHOU: Devolução não foi autorizada.");

                    // Verifica que dívida reabriu: parcela 1 agora tem apenas R$ 15,00 pagos e status PartiallyPaid
                    using (var checkCtx = new CobraPixDbContext(options))
                    {
                        var inst1Reopened = await checkCtx.CreditInstallments.FirstAsync(i => i.Id == inst1.Id);
                        if (inst1Reopened.Status != CreditInstallmentStatus.PartiallyPaid || inst1Reopened.PaidAmountCents != 1500)
                            throw new Exception($"TESTE 4.6 FALHOU: Dívida não reabriu corretamente após estorno ({inst1Reopened.Status}, {inst1Reopened.PaidAmountCents}).");
                    }

                    // Passo 2: Cancela a devolução / autorização de estorno antes da liquidação financeira!
                    vm.SelectedDevolution = dev;
                    vm.CancelDevolutionReason = "Cliente reconsiderou e manteve o pagamento original";
                    await vm.CancelDevolutionAsync();

                    // Verifica resultados da restauração (Seção 4):
                    using (var checkCtx = new CobraPixDbContext(options))
                    {
                        // 1. Devolução cancelada
                        var devDb = await checkCtx.CreditDevolutions.FirstAsync(d => d.Id == dev.Id);
                        if (devDb.Status != CreditDevolutionStatus.Cancelled)
                            throw new Exception($"TESTE 4.6 FALHOU: Status da devolução não é Cancelled ({devDb.Status}).");

                        // 2. Pagamento restaurado para Active
                        var payDb = await checkCtx.CreditPayments.FirstAsync(p => p.Id == p95.Id);
                        if (payDb.Status != CreditPaymentStatus.Active)
                            throw new Exception($"TESTE 4.6 FALHOU: Status do pagamento não foi restaurado para Active ({payDb.Status}).");

                        // 3. Parcela 1 restaurada exatamente para Paid com R$ 110,00 (11.000 centavos) pagos!
                        var inst1Restored = await checkCtx.CreditInstallments.FirstAsync(i => i.Id == inst1.Id);
                        if (inst1Restored.Status != CreditInstallmentStatus.Paid || inst1Restored.PaidAmountCents != 11000)
                            throw new Exception($"TESTE 4.6 FALHOU: Saldo e status da parcela não foram restaurados ({inst1Restored.Status}, {inst1Restored.PaidAmountCents} != 11000).");

                        // 4. Primeiro pagamento (R$ 15,00) permaneceu 100% íntegro e ativo
                        var p15Db = await checkCtx.CreditPayments.FirstAsync(p => p.Id == p1Id);
                        if (p15Db.Status != CreditPaymentStatus.Active)
                            throw new Exception("TESTE 4.6 FALHOU: Pagamento anterior foi corrompido.");
                    }

                    results.Add("TEST_STAGE4_06_SECTION4_REFUND_AUTH_CANCELLATION_RESTORES_DEBT: PASS");
                    Console.WriteLine("TEST_STAGE4_06_SECTION4_REFUND_AUTH_CANCELLATION_RESTORES_DEBT: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 4.7: Consumo e Reversão de Excedente no ViewModel
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();

                    var client = await ctx.Clients.FirstAsync();
                    vm.SelectedClient = client;

                    // Cria contrato de 1 parcela de R$ 120,00 (P=100, J=20)
                    vm.NewContractNumber = "CTR-UI-SURP";
                    vm.NewPrincipalAmount = 100.00m;
                    vm.NewTotalInterest = 20.00m;
                    vm.NewInstallmentsCount = 1;
                    vm.NewFirstDueDate = DateTime.Today.AddDays(15);
                    vm.NewContractDate = DateTime.Today;
                    await vm.CreateContractAsync();

                    var surpContract = vm.Contracts.First(c => c.ContractNumber == "CTR-UI-SURP");
                    vm.SelectedContract = surpContract;
                    await vm.DisburseContractAsync();

                    // Pagamento de R$ 170,00: R$ 120,00 quita a Parcela 1 e sobram R$ 50,00 (5.000 cents) de excedente!
                    vm.PaymentContractId = surpContract.Id;
                    vm.PaymentAmount = 170.00m;
                    vm.PaymentMethod = PaymentMethod.Pix;
                    vm.PaymentNotes = "Pagamento com excedente";
                    await vm.RegisterPaymentAsync();

                    await vm.LoadSurplusBalancesAsync();
                    var surplus = vm.SurplusBalances.FirstOrDefault(s => s.ContractId == surpContract.Id && s.RemainingSurplusCents >= 5000);
                    if (surplus == null)
                        throw new Exception("TESTE 4.7 FALHOU: Saldo excedente de R$ 50,00 não foi gerado ou listado.");

                    // Adiciona Parcela 2 no mesmo contrato para testar consumo sob Regra A:
                    var inst2 = new CreditInstallment
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = surpContract.Id,
                        InstallmentNumber = 2,
                        DueDate = DateTime.Today.AddDays(45),
                        NominalPrincipalCents = 4000,
                        NominalInterestCents = 1000,
                        TotalAmountCents = 5000,
                        PaidAmountCents = 0,
                        Status = CreditInstallmentStatus.Pending,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    ctx.CreditInstallments.Add(inst2);
                    var ctrDb = await ctx.CreditContracts.FindAsync(surpContract.Id);
                    ctrDb!.Status = CreditContractStatus.Active;
                    ctrDb.DisbursedPrincipalCents += 4000;
                    ctrDb.TotalNominalInterestCents += 1000;
                    ctrDb.TotalAmountCents += 5000;
                    await ctx.SaveChangesAsync();

                    // Consome R$ 20,00 (2.000 cents) do excedente na Parcela 2 (que tem 1.000 juros e 4.000 principal):
                    // Regra A no consumo: 1.000 juros + 1.000 principal!
                    vm.SelectedSurplus = surplus;
                    vm.ConsumeTargetInstallmentId = inst2.Id;
                    vm.ConsumeAmount = 20.00m;

                    await vm.ConsumeSurplusAsync();

                    if (string.IsNullOrEmpty(vm.SuccessMessage))
                        throw new Exception("TESTE 4.7 FALHOU: Consumo de excedente não exibiu sucesso.");

                    using (var checkCtx = new CobraPixDbContext(options))
                    {
                        var updatedInst2 = await checkCtx.CreditInstallments.FirstAsync(i => i.Id == inst2.Id);
                        if (updatedInst2.PaidAmountCents != 2000)
                            throw new Exception($"TESTE 4.7 FALHOU: Valor pago da parcela 2 incorreto ({updatedInst2.PaidAmountCents} != 2000).");

                        var consumptionRecord = await checkCtx.CreditSurplusConsumptions.FirstOrDefaultAsync(c => c.SurplusBalanceId == surplus.Id && c.TargetInstallmentId == inst2.Id);
                        if (consumptionRecord == null || consumptionRecord.InterestConsumed != 1000 || consumptionRecord.PrincipalConsumed != 1000)
                            throw new Exception("TESTE 4.7 FALHOU: Registro de consumo com componentes inválidos.");

                        var updatedSurplus = await checkCtx.CreditSurplusBalances.FirstAsync(s => s.Id == surplus.Id);
                        if (updatedSurplus.RemainingSurplusCents != 3000)
                            throw new Exception($"TESTE 4.7 FALHOU: Saldo excedente restante incorreto ({updatedSurplus.RemainingSurplusCents} != 3000).");
                    }

                    // Reverte o consumo de excedente via ViewModel
                    await vm.LoadSurplusBalancesAsync();
                    surplus = vm.SurplusBalances.First(s => s.Id == surplus.Id);
                    vm.SelectedSurplus = surplus;
                    vm.SelectedConsumption = surplus.Consumptions.FirstOrDefault(c => !c.IsReversed);
                    if (vm.SelectedConsumption == null)
                        throw new Exception("TESTE 4.7 FALHOU: Consumo ativo não encontrado para reversão.");

                    await vm.ReverseSurplusAsync();

                    using (var checkCtx = new CobraPixDbContext(options))
                    {
                        var revertedInst2 = await checkCtx.CreditInstallments.FirstAsync(i => i.Id == inst2.Id);
                        if (revertedInst2.PaidAmountCents != 0)
                            throw new Exception($"TESTE 4.7 FALHOU: Dívida da parcela 2 não foi reaberta após reversão ({revertedInst2.PaidAmountCents} != 0).");

                        var restoredSurplus = await checkCtx.CreditSurplusBalances.FirstAsync(s => s.Id == surplus.Id);
                        if (restoredSurplus.RemainingSurplusCents != 5000)
                            throw new Exception($"TESTE 4.7 FALHOU: Saldo excedente não restaurado ({restoredSurplus.RemainingSurplusCents} != 5000).");
                    }

                    results.Add("TEST_STAGE4_07_SURPLUS_CONSUMPTION_AND_REVERSAL: PASS");
                    Console.WriteLine("TEST_STAGE4_07_SURPLUS_CONSUMPTION_AND_REVERSAL: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 4.8: Liquidação de Devolução com Saída Única de Caixa no ViewModel
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();
                    await vm.LoadPaymentsAsync();

                    // Localiza o primeiro pagamento de R$ 15,00
                    var p15 = vm.Payments.First(p => p.Id == p1Id);
                    vm.RefundPaymentId = p15.Id;
                    vm.RefundReason = "Estorno definitivo com devolução de caixa";

                    await vm.AuthorizeRefundAsync();

                    await vm.LoadRefundsAndDevolutionsAsync();
                    var dev15 = vm.Devolutions.FirstOrDefault(d => d.PaymentId == p15.Id && d.Status == CreditDevolutionStatus.Authorized);
                    if (dev15 == null)
                        throw new Exception("TESTE 4.8 FALHOU: Devolução de R$ 15,00 não foi autorizada.");

                    // Liquida devolução via ViewModel
                    vm.SelectedDevolution = dev15;
                    vm.SettlementPaymentMethod = PaymentMethod.Pix;
                    await vm.SettleDevolutionAsync();

                    if (string.IsNullOrEmpty(vm.SuccessMessage) || !vm.SuccessMessage.Contains("liquidada com sucesso"))
                        throw new Exception("TESTE 4.8 FALHOU: Liquidação não reportou sucesso.");

                    using (var checkCtx = new CobraPixDbContext(options))
                    {
                        var devDb = await checkCtx.CreditDevolutions.FirstAsync(d => d.Id == dev15.Id);
                        if (devDb.Status != CreditDevolutionStatus.Settled)
                            throw new Exception($"TESTE 4.8 FALHOU: Devolução não ficou Settled ({devDb.Status}).");

                        var outflows = await checkCtx.CreditCashOutflows.Where(o => o.DevolutionId == dev15.Id).ToListAsync();
                        if (outflows.Count != 1 || outflows[0].AmountCents != 1500)
                            throw new Exception($"TESTE 4.8 FALHOU: Saída única de caixa não confirmada (Count={outflows.Count}).");
                    }

                    results.Add("TEST_STAGE4_08_DEVOLUTION_LIQUIDATION_SINGLE_CASHOUTFLOW: PASS");
                    Console.WriteLine("TEST_STAGE4_08_DEVOLUTION_LIQUIDATION_SINGLE_CASHOUTFLOW: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 4.9: Regressão de Bloqueio Mútuo entre Devolução Parcial e Estorno Total
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);

                    var client = await ctx.Clients.FirstAsync();
                    var cDto = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-TEST-REG-409",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 2000,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(30),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cDto.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    var payDto = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cDto.ContractId,
                        AmountCents = 6000,
                        PaymentDate = DateTime.Today,
                        PaymentMethod = PaymentMethod.Pix,
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    // 1. Autoriza devolução parcial de 2000 centavos
                    var devDto = await refundSvc.AuthorizeDevolutionAsync(new AuthorizeDevolutionRequestDto
                    {
                        PaymentId = payDto.PaymentId,
                        AmountCents = 2000,
                        Reason = "Devolução parcial de teste",
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    // 2. Tentativa de estorno total sobre o pagamento com devolução ativa deve ser rejeitada
                    bool blockedFullRefund = false;
                    try
                    {
                        await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                        {
                            PaymentId = payDto.PaymentId,
                            Reason = "Tentativa de estorno total conflitante",
                            IdempotencyKey = Guid.NewGuid().ToString()
                        });
                    }
                    catch (PaymentAlreadyRefundedException)
                    {
                        blockedFullRefund = true;
                    }

                    if (!blockedFullRefund)
                        throw new Exception("TESTE 4.9 FALHOU: Estorno total não foi bloqueado na presença de devolução ativa.");

                    // 3. Cancela a devolução parcial
                    await refundSvc.CancelDevolutionAsync(new CancelDevolutionRequestDto
                    {
                        DevolutionId = devDto.DevolutionId,
                        Reason = "Cancelamento para permitir estorno total",
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    // 4. Agora o estorno total deve ser aceito
                    var refDto = await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                    {
                        PaymentId = payDto.PaymentId,
                        Reason = "Estorno total agora liberado",
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    if (refDto.Status != CreditRefundStatus.Authorized)
                        throw new Exception("TESTE 4.9 FALHOU: Estorno total não foi autorizado após cancelamento da devolução.");

                    // 5. Nova tentativa de devolução sobre pagamento estornado deve ser bloqueada
                    bool blockedDevAfterRefund = false;
                    try
                    {
                        await refundSvc.AuthorizeDevolutionAsync(new AuthorizeDevolutionRequestDto
                        {
                            PaymentId = payDto.PaymentId,
                            AmountCents = 1000,
                            Reason = "Tentativa de devolução sobre pagamento estornado",
                            IdempotencyKey = Guid.NewGuid().ToString()
                        });
                    }
                    catch (PaymentAlreadyRefundedException)
                    {
                        blockedDevAfterRefund = true;
                    }

                    if (!blockedDevAfterRefund)
                        throw new Exception("TESTE 4.9 FALHOU: Devolução não foi bloqueada sobre pagamento estornado.");

                    results.Add("TEST_STAGE4_09_MUTUAL_EXCLUSION_DEVOLUTION_AND_REFUND: PASS");
                    Console.WriteLine("TEST_STAGE4_09_MUTUAL_EXCLUSION_DEVOLUTION_AND_REFUND: PASS");
                }

                // -------------------------------------------------------------
                // TESTE 4.10: Recomposição Limpa de Obrigações e Independência entre Pagamentos
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");

                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);

                    var client = await ctx.Clients.FirstAsync();
                    var cDto = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-TEST-REG-410",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 2000,
                        InstallmentsCount = 1,
                        FirstDueDate = DateTime.Today.AddDays(30),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                    {
                        ContractId = cDto.ContractId,
                        DisbursementDate = DateTime.Today,
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    // Pagamento 1: R$ 40,00 (aloca 2000 juros + 2000 principal)
                    var p1 = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cDto.ContractId,
                        AmountCents = 4000,
                        PaymentDate = DateTime.Today,
                        PaymentMethod = PaymentMethod.Pix,
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    // Pagamento 2: R$ 30,00 (aloca 0 juros + 3000 principal)
                    var p2 = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                    {
                        ContractId = cDto.ContractId,
                        AmountCents = 3000,
                        PaymentDate = DateTime.Today,
                        PaymentMethod = PaymentMethod.Pix,
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    // Verifica estado inicial: parcela com 7000 pagos de 12000
                    using (var checkCtx = new CobraPixDbContext(options))
                    {
                        var inst = await checkCtx.CreditInstallments.FirstAsync(i => i.ContractId == cDto.ContractId);
                        if (inst.PaidAmountCents != 7000 || inst.Status != CreditInstallmentStatus.PartiallyPaid)
                            throw new Exception($"TESTE 4.10 FALHOU: Saldo inicial incorreto ({inst.PaidAmountCents} != 7000).");
                    }

                    // Estorna apenas o Pagamento 2 (R$ 30,00)
                    var refP2 = await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                    {
                        PaymentId = p2.PaymentId,
                        Reason = "Estorno do pagamento 2 de R$ 30,00",
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    // Verifica recomposição: Pagamento 1 permanece intacto (4000 pagos, PartiallyPaid)
                    using (var checkCtx = new CobraPixDbContext(options))
                    {
                        var inst = await checkCtx.CreditInstallments.FirstAsync(i => i.ContractId == cDto.ContractId);
                        if (inst.PaidAmountCents != 4000)
                            throw new Exception($"TESTE 4.10 FALHOU: Parcela não reteve exatamente os 4000 pagos do pagamento 1 ({inst.PaidAmountCents} != 4000).");
                        if (inst.Status != CreditInstallmentStatus.PartiallyPaid)
                            throw new Exception($"TESTE 4.10 FALHOU: Estado da parcela deveria ser PartiallyPaid, foi {inst.Status}.");
                    }

                    // Cancela o estorno do Pagamento 2 -> restaura perfeitamente os 7000
                    await refundSvc.CancelRefundAuthAsync(new CancelRefundAuthRequestDto
                    {
                        RefundId = refP2.RefundId,
                        Reason = "Cancelamento do estorno do pagamento 2",
                        IdempotencyKey = Guid.NewGuid().ToString()
                    });

                    using (var checkCtx = new CobraPixDbContext(options))
                    {
                        var inst = await checkCtx.CreditInstallments.FirstAsync(i => i.ContractId == cDto.ContractId);
                        if (inst.PaidAmountCents != 7000 || inst.Status != CreditInstallmentStatus.PartiallyPaid)
                            throw new Exception($"TESTE 4.10 FALHOU: Parcela não foi restaurada para 7000 após cancelamento ({inst.PaidAmountCents} != 7000).");
                    }

                    results.Add("TEST_STAGE4_10_INDEPENDENT_PAYMENTS_CLEAN_RECOMPOSITION: PASS");
                    Console.WriteLine("TEST_STAGE4_10_INDEPENDENT_PAYMENTS_CLEAN_RECOMPOSITION: PASS");
                }

                // =============================================================
                // ETAPA 5: PERIODICIDADE CONFIGURÁVEL, VENCIMENTOS E CENTAVOS
                // =============================================================
                results.Add("INICIANDO ETAPA 5: PERIODICIDADE CONFIGURÁVEL, VENCIMENTOS E CENTAVOS");

                // -------------------------------------------------------------
                // TESTE STAGE5_01: Periodicidade Diária (Saltos de 1 dia civil)
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    var res = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-S5-DAILY",
                        PrincipalAmountCents = 30000,
                        TotalInterestCents = 6000,
                        InstallmentsCount = 3,
                        Periodicity = CreditPeriodicity.Daily,
                        FirstDueDate = new DateTime(2026, 5, 1),
                        ContractDate = new DateTime(2026, 4, 30),
                        IdempotencyKey = "KEY-S5-DAILY-1"
                    });

                    var insts = await ctx.CreditInstallments.Where(i => i.ContractId == res.ContractId).OrderBy(i => i.InstallmentNumber).ToListAsync();
                    if (insts[0].DueDate != new DateTime(2026, 5, 1) ||
                        insts[1].DueDate != new DateTime(2026, 5, 2) ||
                        insts[2].DueDate != new DateTime(2026, 5, 3))
                        throw new Exception("TEST_STAGE5_01 FALHOU: Vencimentos diários incorretos.");

                    var dbContract = await ctx.CreditContracts.FindAsync(res.ContractId);
                    if (dbContract!.Periodicity != CreditPeriodicity.Daily || dbContract.CustomIntervalDays != null)
                        throw new Exception("TEST_STAGE5_01 FALHOU: Metadados de periodicidade diária divergentes no banco.");

                    results.Add("TEST_STAGE5_01_PERIODICITY_DAILY: PASS");
                    Console.WriteLine("TEST_STAGE5_01_PERIODICITY_DAILY: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE5_02: Periodicidade Semanal (Saltos de 7 dias civis)
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    var res = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-S5-WEEKLY",
                        PrincipalAmountCents = 40000,
                        TotalInterestCents = 8000,
                        InstallmentsCount = 4,
                        Periodicity = CreditPeriodicity.Weekly,
                        FirstDueDate = new DateTime(2026, 5, 1),
                        ContractDate = new DateTime(2026, 4, 30),
                        IdempotencyKey = "KEY-S5-WEEKLY-1"
                    });

                    var insts = await ctx.CreditInstallments.Where(i => i.ContractId == res.ContractId).OrderBy(i => i.InstallmentNumber).ToListAsync();
                    if (insts[0].DueDate != new DateTime(2026, 5, 1) ||
                        insts[1].DueDate != new DateTime(2026, 5, 8) ||
                        insts[2].DueDate != new DateTime(2026, 5, 15) ||
                        insts[3].DueDate != new DateTime(2026, 5, 22))
                        throw new Exception("TEST_STAGE5_02 FALHOU: Vencimentos semanais incorretos.");

                    results.Add("TEST_STAGE5_02_PERIODICITY_WEEKLY: PASS");
                    Console.WriteLine("TEST_STAGE5_02_PERIODICITY_WEEKLY: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE5_03: Periodicidade Quinzenal (Saltos de 15 dias civis fixos)
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    var res = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-S5-BIWEEKLY",
                        PrincipalAmountCents = 30000,
                        TotalInterestCents = 6000,
                        InstallmentsCount = 3,
                        Periodicity = CreditPeriodicity.Biweekly,
                        FirstDueDate = new DateTime(2026, 5, 1),
                        ContractDate = new DateTime(2026, 4, 30),
                        IdempotencyKey = "KEY-S5-BIWEEKLY-1"
                    });

                    var insts = await ctx.CreditInstallments.Where(i => i.ContractId == res.ContractId).OrderBy(i => i.InstallmentNumber).ToListAsync();
                    if (insts[0].DueDate != new DateTime(2026, 5, 1) ||
                        insts[1].DueDate != new DateTime(2026, 5, 16) ||
                        insts[2].DueDate != new DateTime(2026, 5, 31))
                        throw new Exception("TEST_STAGE5_03 FALHOU: Vencimentos quinzenais (15 dias fixos) incorretos.");

                    results.Add("TEST_STAGE5_03_PERIODICITY_BIWEEKLY: PASS");
                    Console.WriteLine("TEST_STAGE5_03_PERIODICITY_BIWEEKLY: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE5_04: Periodicidade Personalizada em Dias
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    var res = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-S5-CUSTOM",
                        PrincipalAmountCents = 30000,
                        TotalInterestCents = 6000,
                        InstallmentsCount = 3,
                        Periodicity = CreditPeriodicity.CustomDays,
                        CustomIntervalDays = 10,
                        FirstDueDate = new DateTime(2026, 5, 1),
                        ContractDate = new DateTime(2026, 4, 30),
                        IdempotencyKey = "KEY-S5-CUSTOM-1"
                    });

                    var insts = await ctx.CreditInstallments.Where(i => i.ContractId == res.ContractId).OrderBy(i => i.InstallmentNumber).ToListAsync();
                    if (insts[0].DueDate != new DateTime(2026, 5, 1) ||
                        insts[1].DueDate != new DateTime(2026, 5, 11) ||
                        insts[2].DueDate != new DateTime(2026, 5, 21))
                        throw new Exception("TEST_STAGE5_04 FALHOU: Vencimentos personalizados (10 dias) incorretos.");

                    var dbContract = await ctx.CreditContracts.FindAsync(res.ContractId);
                    if (dbContract!.Periodicity != CreditPeriodicity.CustomDays || dbContract.CustomIntervalDays != 10)
                        throw new Exception("TEST_STAGE5_04 FALHOU: Metadados de intervalo customizado divergentes no banco.");

                    results.Add("TEST_STAGE5_04_PERIODICITY_CUSTOM_DAYS: PASS");
                    Console.WriteLine("TEST_STAGE5_04_PERIODICITY_CUSTOM_DAYS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE5_05: Mensal com Fim de Mês e Fevereiro em Ano Bissexto (2024)
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    var res = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-S5-LEAP",
                        PrincipalAmountCents = 40000,
                        TotalInterestCents = 8000,
                        InstallmentsCount = 4,
                        Periodicity = CreditPeriodicity.Monthly,
                        FirstDueDate = new DateTime(2024, 1, 31),
                        ContractDate = new DateTime(2024, 1, 15),
                        IdempotencyKey = "KEY-S5-LEAP-1"
                    });

                    var insts = await ctx.CreditInstallments.Where(i => i.ContractId == res.ContractId).OrderBy(i => i.InstallmentNumber).ToListAsync();
                    if (insts[0].DueDate != new DateTime(2024, 1, 31) ||
                        insts[1].DueDate != new DateTime(2024, 2, 29) ||
                        insts[2].DueDate != new DateTime(2024, 3, 31) ||
                        insts[3].DueDate != new DateTime(2024, 4, 30))
                        throw new Exception($"TEST_STAGE5_05 FALHOU: Ancoragem de ano bissexto incorreta ({insts[1].DueDate}, {insts[2].DueDate}).");

                    results.Add("TEST_STAGE5_05_MONTHLY_LEAP_YEAR_FEBRUARY: PASS");
                    Console.WriteLine("TEST_STAGE5_05_MONTHLY_LEAP_YEAR_FEBRUARY: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE5_06: Mensal com Fim de Mês e Fevereiro em Ano Comum (2026)
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    var res = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-S5-COMMON",
                        PrincipalAmountCents = 40000,
                        TotalInterestCents = 8000,
                        InstallmentsCount = 4,
                        Periodicity = CreditPeriodicity.Monthly,
                        FirstDueDate = new DateTime(2026, 1, 31),
                        ContractDate = new DateTime(2026, 1, 15),
                        IdempotencyKey = "KEY-S5-COMMON-1"
                    });

                    var insts = await ctx.CreditInstallments.Where(i => i.ContractId == res.ContractId).OrderBy(i => i.InstallmentNumber).ToListAsync();
                    if (insts[0].DueDate != new DateTime(2026, 1, 31) ||
                        insts[1].DueDate != new DateTime(2026, 2, 28) ||
                        insts[2].DueDate != new DateTime(2026, 3, 31) ||
                        insts[3].DueDate != new DateTime(2026, 4, 30))
                        throw new Exception($"TEST_STAGE5_06 FALHOU: Ancoragem de ano comum incorreta ({insts[1].DueDate}, {insts[2].DueDate}).");

                    results.Add("TEST_STAGE5_06_MONTHLY_COMMON_YEAR_FEBRUARY: PASS");
                    Console.WriteLine("TEST_STAGE5_06_MONTHLY_COMMON_YEAR_FEBRUARY: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE5_07: Resíduos Acumulados na Mesma Parcela (remP + remJ >= N)
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    var res = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-S5-ACCUM",
                        PrincipalAmountCents = 10001,
                        TotalInterestCents = 3002,
                        InstallmentsCount = 3,
                        Periodicity = CreditPeriodicity.Monthly,
                        FirstDueDate = new DateTime(2026, 6, 1),
                        ContractDate = new DateTime(2026, 5, 20),
                        IdempotencyKey = "KEY-S5-ACCUM-1"
                    });

                    var insts = await ctx.CreditInstallments.Where(i => i.ContractId == res.ContractId).OrderBy(i => i.InstallmentNumber).ToListAsync();

                    if (insts[0].NominalPrincipalCents != 3334 || insts[0].NominalInterestCents != 1001 || insts[0].TotalAmountCents != 4335)
                        throw new Exception($"TEST_STAGE5_07 FALHOU P1: P={insts[0].NominalPrincipalCents}, J={insts[0].NominalInterestCents}, T={insts[0].TotalAmountCents}");

                    if (insts[1].NominalPrincipalCents != 3334 || insts[1].NominalInterestCents != 1000 || insts[1].TotalAmountCents != 4334)
                        throw new Exception($"TEST_STAGE5_07 FALHOU P2: P={insts[1].NominalPrincipalCents}, J={insts[1].NominalInterestCents}, T={insts[1].TotalAmountCents}");

                    if (insts[2].NominalPrincipalCents != 3333 || insts[2].NominalInterestCents != 1001 || insts[2].TotalAmountCents != 4334)
                        throw new Exception($"TEST_STAGE5_07 FALHOU P3: P={insts[2].NominalPrincipalCents}, J={insts[2].NominalInterestCents}, T={insts[2].TotalAmountCents}");

                    long pSum = insts.Sum(i => i.NominalPrincipalCents);
                    long jSum = insts.Sum(i => i.NominalInterestCents);
                    long tSum = insts.Sum(i => i.TotalAmountCents);

                    if (pSum != 10001 || jSum != 3002 || tSum != 13003)
                        throw new Exception($"TEST_STAGE5_07 FALHOU na soma exata: P={pSum}, J={jSum}, T={tSum}");

                    long diffMaxMin = insts.Max(i => i.TotalAmountCents) - insts.Min(i => i.TotalAmountCents);
                    if (diffMaxMin > 1)
                        throw new Exception($"TEST_STAGE5_07 FALHOU: Variação máxima entre parcelas excedeu 1 centavo ({diffMaxMin}).");

                    results.Add("TEST_STAGE5_07_RESIDUE_ACCUMULATION_SAME_INSTALLMENT: PASS");
                    Console.WriteLine("TEST_STAGE5_07_RESIDUE_ACCUMULATION_SAME_INSTALLMENT: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE5_08: Matriz Exaustiva de Centavos (Variações Múltiplas de Resíduo)
                // -------------------------------------------------------------
                {
                    (long P, long J, int N)[] testCases = new (long, long, int)[]
                    {
                        (10000, 2000, 1),
                        (10000, 2000, 3),
                        (10001, 2000, 3),
                        (10000, 2001, 3),
                        (10001, 2001, 3),
                        (10002, 2002, 3),
                        (50000, 12345, 7),
                        (99999, 33333, 11),
                        (123456, 78910, 17),
                        (1000000, 250000, 36)
                    };

                    foreach (var tc in testCases)
                    {
                        var parts = CreditContractService.DistributeInstallments(tc.P, tc.J, tc.N);
                        long sumP = parts.Sum(p => p.principal);
                        long sumJ = parts.Sum(p => p.interest);
                        long sumT = parts.Sum(p => p.total);
                        long maxT = parts.Max(p => p.total);
                        long minT = parts.Min(p => p.total);

                        if (sumP != tc.P || sumJ != tc.J || sumT != (tc.P + tc.J))
                            throw new Exception($"TEST_STAGE5_08 FALHOU para P={tc.P}, J={tc.J}, N={tc.N}: Soma incorreta!");

                        if (maxT - minT > 1)
                            throw new Exception($"TEST_STAGE5_08 FALHOU para P={tc.P}, J={tc.J}, N={tc.N}: Delta ({maxT - minT}) > 1 centavo!");
                    }

                    results.Add("TEST_STAGE5_08_EXHAUSTIVE_RESIDUE_MATRIX: PASS");
                    Console.WriteLine("TEST_STAGE5_08_EXHAUSTIVE_RESIDUE_MATRIX: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE5_09: Idempotência com Mesma Chave e Rejeição de Divergência
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    string idemKey = "IDEM-TEST-STAGE5-09";
                    var reqA = new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-S5-IDEM-A",
                        PrincipalAmountCents = 50000,
                        TotalInterestCents = 10000,
                        InstallmentsCount = 5,
                        Periodicity = CreditPeriodicity.Monthly,
                        FirstDueDate = new DateTime(2026, 7, 1),
                        ContractDate = new DateTime(2026, 6, 1),
                        IdempotencyKey = idemKey
                    };

                    var resA1 = await contractSvc.CreateFixedContractAsync(reqA);

                    var resA2 = await contractSvc.CreateFixedContractAsync(reqA);
                    if (resA1.ContractId != resA2.ContractId)
                        throw new Exception("TEST_STAGE5_09 FALHOU: Idempotência idêntica retornou ID diferente.");

                    var reqDiffPeriodicity = new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-S5-IDEM-A",
                        PrincipalAmountCents = 50000,
                        TotalInterestCents = 10000,
                        InstallmentsCount = 5,
                        Periodicity = CreditPeriodicity.Weekly,
                        FirstDueDate = new DateTime(2026, 7, 1),
                        ContractDate = new DateTime(2026, 6, 1),
                        IdempotencyKey = idemKey
                    };

                    bool conflictThrown = false;
                    try
                    {
                        await contractSvc.CreateFixedContractAsync(reqDiffPeriodicity);
                    }
                    catch (IdempotencyConflictException)
                    {
                        conflictThrown = true;
                    }

                    if (!conflictThrown)
                        throw new Exception("TEST_STAGE5_09 FALHOU: Não rejeitou reuso de chave com periodicidade alterada.");

                    var reqDiffCustom = new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-S5-IDEM-A",
                        PrincipalAmountCents = 50000,
                        TotalInterestCents = 10000,
                        InstallmentsCount = 5,
                        Periodicity = CreditPeriodicity.CustomDays,
                        CustomIntervalDays = 20,
                        FirstDueDate = new DateTime(2026, 7, 1),
                        ContractDate = new DateTime(2026, 6, 1),
                        IdempotencyKey = idemKey
                    };

                    bool conflictCustomThrown = false;
                    try
                    {
                        await contractSvc.CreateFixedContractAsync(reqDiffCustom);
                    }
                    catch (IdempotencyConflictException)
                    {
                        conflictCustomThrown = true;
                    }

                    if (!conflictCustomThrown)
                        throw new Exception("TEST_STAGE5_09 FALHOU: Não rejeitou reuso de chave com intervalo personalizado divergente.");

                    results.Add("TEST_STAGE5_09_IDEMPOTENCY_IDENTICAL_AND_CONFLICT: PASS");
                    Console.WriteLine("TEST_STAGE5_09_IDEMPOTENCY_IDENTICAL_AND_CONFLICT: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE5_10: Validações de Limite (CustomDays e Parcelas)
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var client = await ctx.Clients.FirstAsync();

                    bool customNullBlocked = false;
                    try
                    {
                        await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                        {
                            ClientId = client.Id,
                            ContractNumber = "CTR-FAIL-1",
                            PrincipalAmountCents = 10000,
                            TotalInterestCents = 1000,
                            InstallmentsCount = 2,
                            Periodicity = CreditPeriodicity.CustomDays,
                            CustomIntervalDays = null,
                            FirstDueDate = DateTime.Today.AddDays(10),
                            IdempotencyKey = "KEY-FAIL-1"
                        });
                    }
                    catch (ArgumentException) { customNullBlocked = true; }
                    if (!customNullBlocked) throw new Exception("TEST_STAGE5_10 FALHOU: Permitiu CustomDays sem dias definidos.");

                    bool customZeroBlocked = false;
                    try
                    {
                        await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                        {
                            ClientId = client.Id,
                            ContractNumber = "CTR-FAIL-2",
                            PrincipalAmountCents = 10000,
                            TotalInterestCents = 1000,
                            InstallmentsCount = 2,
                            Periodicity = CreditPeriodicity.CustomDays,
                            CustomIntervalDays = 0,
                            FirstDueDate = DateTime.Today.AddDays(10),
                            IdempotencyKey = "KEY-FAIL-2"
                        });
                    }
                    catch (ArgumentException) { customZeroBlocked = true; }
                    if (!customZeroBlocked) throw new Exception("TEST_STAGE5_10 FALHOU: Permitiu CustomDays com 0 dias.");

                    bool excessInstallmentsBlocked = false;
                    try
                    {
                        await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                        {
                            ClientId = client.Id,
                            ContractNumber = "CTR-FAIL-3",
                            PrincipalAmountCents = 10000,
                            TotalInterestCents = 1000,
                            InstallmentsCount = 361,
                            Periodicity = CreditPeriodicity.Monthly,
                            FirstDueDate = DateTime.Today.AddDays(10),
                            IdempotencyKey = "KEY-FAIL-3"
                        });
                    }
                    catch (ArgumentException) { excessInstallmentsBlocked = true; }
                    if (!excessInstallmentsBlocked) throw new Exception("TEST_STAGE5_10 FALHOU: Permitiu mais de 360 parcelas.");

                    results.Add("TEST_STAGE5_10_VALIDATION_LIMITS: PASS");
                    Console.WriteLine("TEST_STAGE5_10_VALIDATION_LIMITS: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE5_11: Migração Versão 3 Idempotente e Contratos Antigos
                // -------------------------------------------------------------
                {
                    string migTestDb = Path.Combine(baseTempDir, $"cobrapix_mig3_test_{Guid.NewGuid():N}.db");
                    try
                    {
                        var migOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                            .UseSqlite($"Data Source={migTestDb}")
                            .Options;

                        using (var migCtx = new CobraPixDbContext(migOpts))
                        {
                            await migCtx.Database.EnsureCreatedAsync();

                            // Carimbo mandatório de ambiente sintético de homologação para validação da trava de segurança
                            string curToken = Environment.GetEnvironmentVariable("COBRAPIX_SYNTHETIC_TEST_TOKEN") ?? sessionToken;
                            migCtx.AppSettings.Add(new AppSetting { Key = "DatabaseEnvironment", Value = "SYNTHETIC_TEST" });
                            migCtx.AppSettings.Add(new AppSetting { Key = "SyntheticSessionToken", Value = curToken });
                            await migCtx.SaveChangesAsync();

                            // Executa a migração aditiva
                            await CreditDatabaseMigrator.ApplyCreditMigrationsAsync(migCtx);

                            // Verifica que a versão 3 está gravada
                            using var conn = (SqliteConnection)migCtx.Database.GetDbConnection();
                            await conn.OpenAsync();
                            using var cmd = conn.CreateCommand();
                            cmd.CommandText = "SELECT MAX(Version) FROM CreditSchemaVersions;";
                            int ver = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                            if (ver < 3) throw new Exception($"TEST_STAGE5_11 FALHOU: Versão de schema após migração ({ver}) é menor que 3.");

                            // Testa idempotência reexecutando a migração
                            await CreditDatabaseMigrator.ApplyCreditMigrationsAsync(migCtx);

                            // Insere cliente e contrato legado (simulando contrato gravado)
                            var client = new Client { Name = "Cliente Legado", Phone = "11999999999", CreatedAt = DateTime.Now };
                            migCtx.Clients.Add(client);
                            await migCtx.SaveChangesAsync();

                            var legacyContract = new CreditContract
                            {
                                ClientId = client.Id,
                                ContractNumber = "CTR-LEGACY-001",
                                ContractType = CreditContractType.FixedInstallments,
                                Status = CreditContractStatus.Draft,
                                DisbursedPrincipalCents = 100000,
                                TotalNominalInterestCents = 20000,
                                TotalAmountCents = 120000,
                                ContractDate = DateTime.Today,
                                CreatedAtUtc = DateTime.UtcNow
                            };
                            migCtx.CreditContracts.Add(legacyContract);
                            await migCtx.SaveChangesAsync();

                            // Recarrega contrato e valida defaults retrocompatíveis
                            var reloaded = await migCtx.CreditContracts.FirstAsync(c => c.ContractNumber == "CTR-LEGACY-001");
                            if (reloaded.Periodicity != CreditPeriodicity.Monthly || reloaded.CustomIntervalDays != null)
                                throw new Exception("TEST_STAGE5_11 FALHOU: Contrato legado não manteve default Monthly ou CustomIntervalDays não é nulo.");
                        }

                        results.Add("TEST_STAGE5_11_MIGRATION_VERSION_3_IDEMPOTENCY_AND_LEGACY: PASS");
                        Console.WriteLine("TEST_STAGE5_11_MIGRATION_VERSION_3_IDEMPOTENCY_AND_LEGACY: PASS");
                    }
                    finally
                    {
                        SqliteConnection.ClearAllPools();
                        try { if (File.Exists(migTestDb)) File.Delete(migTestDb); } catch { }
                    }
                }

                // =============================================================
                // ETAPA 6: SUÍTE DE INTEGRAÇÃO UI, USABILIDADE E CICLO DE CONTRATOS (VÍDEO 2026-10-12)
                // =============================================================
                results.Add("--------------------------------------------------");
                results.Add("INICIANDO TESTES DA ETAPA 6: UI, FEEDBACK E USABILIDADE");
                results.Add("--------------------------------------------------");
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine("INICIANDO TESTES DA ETAPA 6: UI, FEEDBACK E USABILIDADE");
                Console.WriteLine("--------------------------------------------------");

                // -------------------------------------------------------------
                // TESTE STAGE6_01: Feedback de Comandos e Exibição de Validação/Exceções na UI
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx, proSvc);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();

                    // 1. Sem cliente selecionado
                    vm.SelectedClient = null;
                    await vm.CreateContractAsync();
                    if (!vm.HasError || string.IsNullOrWhiteSpace(vm.ErrorMessage) || !vm.ErrorMessage.Contains("Selecione um cliente"))
                        throw new Exception("TEST_STAGE6_01 FALHOU: Validação de cliente nulo não gerou HasError=true ou mensagem apropriada.");
                    if (vm.HasSuccess)
                        throw new Exception("TEST_STAGE6_01 FALHOU: HasSuccess não deve ser verdadeiro em erro.");

                    // Cria cliente para os próximos testes
                    var clientSintetico = new Client { Name = "Vitor0 - Cliente Teste Sintético", Phone = "11988887777", CreatedAt = DateTime.UtcNow };
                    ctx.Clients.Add(clientSintetico);
                    await ctx.SaveChangesAsync();
                    await vm.LoadClientsAsync();
                    vm.SelectedClient = vm.Clients.First(c => c.Name == clientSintetico.Name);

                    // 2. Sem número de contrato
                    vm.NewContractNumber = "";
                    await vm.CreateContractAsync();
                    if (!vm.HasError || !vm.ErrorMessage.Contains("número/código"))
                        throw new Exception("TEST_STAGE6_01 FALHOU: Validação de número de contrato vazio não gerou HasError=true.");

                    // 3. Principal zerado ou negativo
                    vm.NewContractNumber = "CTR-VAL-01";
                    vm.NewPrincipalAmount = 0;
                    await vm.CreateContractAsync();
                    if (!vm.HasError || !vm.ErrorMessage.Contains("valor principal"))
                        throw new Exception("TEST_STAGE6_01 FALHOU: Validação de principal <= 0 falhou.");

                    // 4. Parcelas < 1
                    vm.NewPrincipalAmount = 1000m;
                    vm.NewInstallmentsCount = 0;
                    await vm.CreateContractAsync();
                    if (!vm.HasError || !vm.ErrorMessage.Contains("parcelas"))
                        throw new Exception("TEST_STAGE6_01 FALHOU: Validação de quantidade de parcelas falhou.");

                    // 5. Periodicidade personalizada sem dias
                    vm.NewInstallmentsCount = 1;
                    vm.NewPeriodicity = CreditPeriodicity.CustomDays;
                    vm.NewCustomIntervalDays = null;
                    await vm.CreateContractAsync();
                    if (!vm.HasError || !vm.ErrorMessage.Contains("periodicidade personalizada"))
                        throw new Exception("TEST_STAGE6_01 FALHOU: Validação de CustomDays nulo falhou.");

                    // 6. Limpeza manual de mensagens
                    vm.ClearMessages();
                    if (vm.HasError || vm.HasSuccess || !string.IsNullOrEmpty(vm.ErrorMessage) || !string.IsNullOrEmpty(vm.SuccessMessage))
                        throw new Exception("TEST_STAGE6_01 FALHOU: ClearMessages não redefiniu flags e mensagens.");

                    results.Add("TEST_STAGE6_01_UI_COMMAND_ERROR_FEEDBACK_AND_VALIDATION: PASS");
                    Console.WriteLine("TEST_STAGE6_01_UI_COMMAND_ERROR_FEEDBACK_AND_VALIDATION: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE6_02: Criação de Contrato com Valores do Vídeo, Limpeza e Auto-Seleção
                // -------------------------------------------------------------
                string videoContractId;
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx, proSvc);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();
                    await vm.LoadClientsAsync();

                    var client = vm.Clients.First(c => c.Name.Contains("Vitor0"));
                    vm.SelectedClient = client;

                    // Parâmetros exatos do vídeo Gravando 2026-10-12 213321.mp4:
                    vm.NewContractNumber = "1";
                    vm.NewPrincipalAmount = 1000m;
                    vm.NewTotalInterest = 200m;
                    vm.NewInstallmentsCount = 1;
                    vm.NewFirstDueDate = new DateTime(2026, 11, 11);
                    vm.NewPeriodicity = CreditPeriodicity.Monthly;
                    vm.NewContractDate = new DateTime(2026, 10, 12);
                    vm.NewNotes = "teste";

                    await vm.CreateContractAsync();

                    if (vm.HasError)
                        throw new Exception($"TEST_STAGE6_02 FALHOU com erro inesperado: {vm.ErrorMessage}");
                    if (!vm.HasSuccess || string.IsNullOrWhiteSpace(vm.SuccessMessage) || !vm.SuccessMessage.Contains("criado com sucesso"))
                        throw new Exception("TEST_STAGE6_02 FALHOU: HasSuccess não é true ou mensagem de sucesso ausente.");

                    // Limpeza do formulário
                    if (!string.IsNullOrEmpty(vm.NewContractNumber) || !string.IsNullOrEmpty(vm.NewNotes))
                        throw new Exception("TEST_STAGE6_02 FALHOU: Formulário de novo contrato não foi limpo após salvar.");

                    // Auto-seleção na grade
                    if (vm.SelectedContract == null || vm.SelectedContract.ContractNumber != "1")
                        throw new Exception("TEST_STAGE6_02 FALHOU: Contrato recém-criado não foi selecionado automaticamente.");

                    if (vm.SelectedPaymentContract == null || vm.SelectedPaymentContract.ContractNumber != "1")
                        throw new Exception("TEST_STAGE6_02 FALHOU: Contrato não sincronizou com SelectedPaymentContract.");

                    if (!vm.CanDisburseSelectedContract)
                        throw new Exception("TEST_STAGE6_02 FALHOU: CanDisburseSelectedContract deveria ser verdadeiro para Draft.");

                    videoContractId = vm.SelectedContract.Id;

                    // Formatação monetária nos modelos
                    if (vm.SelectedContract.FormattedDisbursedPrincipal != "R$ 1.000,00" ||
                        vm.SelectedContract.FormattedTotalNominalInterest != "R$ 200,00" ||
                        vm.SelectedContract.FormattedTotalAmount != "R$ 1.200,00")
                    {
                        throw new Exception($"TEST_STAGE6_02 FALHOU: Formatação de moeda incorreta ({vm.SelectedContract.FormattedTotalAmount}).");
                    }

                    if (!vm.SelectedContract.DisplaySummary.Contains("#1") || !vm.SelectedContract.DisplaySummary.Contains("R$ 1.200,00"))
                        throw new Exception($"TEST_STAGE6_02 FALHOU: DisplaySummary incorreto ({vm.SelectedContract.DisplaySummary}).");

                    results.Add("TEST_STAGE6_02_CONTRACT_CREATION_FORM_CLEANUP_AND_SELECTION: PASS");
                    Console.WriteLine("TEST_STAGE6_02_CONTRACT_CREATION_FORM_CLEANUP_AND_SELECTION: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE6_03: Ciclo Completo: Rascunho -> Desembolso -> Prévia -> Pagamento Quitado
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx, proSvc);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();
                    await vm.LoadContractsAsync();

                    var contract = vm.Contracts.First(c => c.Id == videoContractId);
                    vm.SelectedContract = contract;

                    // 1. Desembolso
                    await vm.DisburseContractAsync();
                    if (vm.HasError) throw new Exception($"TEST_STAGE6_03 FALHOU no desembolso: {vm.ErrorMessage}");
                    if (vm.SelectedContract.Status != CreditContractStatus.Active)
                        throw new Exception($"TEST_STAGE6_03 FALHOU: Status após desembolso esperado Active, obtido {vm.SelectedContract.Status}.");
                    if (vm.CanDisburseSelectedContract)
                        throw new Exception("TEST_STAGE6_03 FALHOU: CanDisburseSelectedContract deveria ser falso após desembolso.");

                    // 2. Simulação / Prévia de Pagamento (Regra A)
                    vm.SelectedPaymentContract = vm.SelectedContract;
                    vm.PaymentContractId = vm.SelectedContract.Id;
                    vm.PaymentAmount = 1200m;
                    await vm.PreviewPaymentAsync();

                    if (vm.PaymentPreview == null)
                        throw new Exception("TEST_STAGE6_03 FALHOU: PaymentPreview é nulo após simulação.");
                    if (vm.PaymentPreview.TotalInterestAllocatedCents != 20000 ||
                        vm.PaymentPreview.TotalPrincipalAllocatedCents != 100000 ||
                        vm.PaymentPreview.SurplusGeneratedCents != 0)
                    {
                        throw new Exception($"TEST_STAGE6_03 FALHOU na prévia: J={vm.PaymentPreview.TotalInterestAllocatedCents}, P={vm.PaymentPreview.TotalPrincipalAllocatedCents}, S={vm.PaymentPreview.SurplusGeneratedCents}");
                    }

                    // 3. Gravação do Pagamento
                    await vm.RegisterPaymentAsync();
                    if (vm.HasError) throw new Exception($"TEST_STAGE6_03 FALHOU na gravação do pagamento: {vm.ErrorMessage}");
                    if (!vm.HasSuccess || vm.SuccessMessage == null || !vm.SuccessMessage.Contains("sucesso"))
                        throw new Exception("TEST_STAGE6_03 FALHOU: Mensagem de sucesso ausente após gravação do pagamento.");

                    // 4. Confirmação no banco e atualização da lista
                    await vm.LoadContractsAsync();
                    var updatedContract = vm.Contracts.First(c => c.Id == videoContractId);
                    if (updatedContract.Status != CreditContractStatus.Completed)
                        throw new Exception($"TEST_STAGE6_03 FALHOU: Status do contrato esperado Completed, obtido {updatedContract.Status}.");

                    await vm.LoadPaymentsAsync();
                    var pay = vm.Payments.First(p => p.ContractId == videoContractId);
                    if (pay.FormattedAmount != "R$ 1.200,00")
                        throw new Exception($"TEST_STAGE6_03 FALHOU: FormattedAmount do pagamento incorreto ({pay.FormattedAmount}).");

                    results.Add("TEST_STAGE6_03_FULL_LIFECYCLE_DRAFT_DISBURSE_PAYMENT: PASS");
                    Console.WriteLine("TEST_STAGE6_03_FULL_LIFECYCLE_DRAFT_DISBURSE_PAYMENT: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE6_04: Usabilidade e Resolução Flexível do Contrato em Pagamentos
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx, proSvc);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();
                    await vm.LoadContractsAsync();

                    // Cria um novo contrato com código legível específico
                    var clientResolve = new Client { Name = "Cliente Resolucao Teste Sintetico", Phone = "11977776666", CreatedAt = DateTime.UtcNow };
                    ctx.Clients.Add(clientResolve);
                    await ctx.SaveChangesAsync();
                    await vm.LoadClientsAsync();

                    vm.SelectedClient = vm.Clients.First(c => c.Name == clientResolve.Name);
                    vm.NewContractNumber = "CTR-RES-01";
                    vm.NewPrincipalAmount = 500m;
                    vm.NewTotalInterest = 50m;
                    vm.NewInstallmentsCount = 1;
                    vm.NewFirstDueDate = DateTime.Today.AddDays(10);
                    vm.NewPeriodicity = CreditPeriodicity.Monthly;
                    await vm.CreateContractAsync();

                    if (vm.HasError) throw new Exception($"TEST_STAGE6_04 falhou ao criar contrato: {vm.ErrorMessage}");
                    var created = vm.Contracts.First(c => c.ContractNumber == "CTR-RES-01");
                    vm.SelectedContract = created;
                    await vm.DisburseContractAsync();

                    // Teste A: Resolução por GUID exato
                    var resA = vm.ResolveContract(created.Id);
                    if (resA?.Id != created.Id) throw new Exception("TEST_STAGE6_04 FALHOU: Resolução por GUID falhou.");

                    // Teste B: Resolução por Número do Contrato (ex: 'CTR-RES-01')
                    var resB = vm.ResolveContract("CTR-RES-01");
                    if (resB?.Id != created.Id) throw new Exception("TEST_STAGE6_04 FALHOU: Resolução por número de contrato falhou.");

                    // Teste C: Resolução por Nome do Cliente
                    var resC = vm.ResolveContract("Cliente Resolucao Teste Sintetico");
                    if (resC?.Id != created.Id) throw new Exception("TEST_STAGE6_04 FALHOU: Resolução por nome do cliente falhou.");

                    // Teste D: Pagamento digitando apenas o número legível no campo PaymentContractId
                    vm.PaymentContractId = "CTR-RES-01";
                    vm.PaymentAmount = 550m;
                    await vm.PreviewPaymentAsync();
                    if (vm.HasError) throw new Exception($"TEST_STAGE6_04 FALHOU na prévia com número legível: {vm.ErrorMessage}");
                    if (vm.SelectedPaymentContract?.Id != created.Id)
                        throw new Exception("TEST_STAGE6_04 FALHOU: SelectedPaymentContract não sincronizou na prévia.");

                    await vm.RegisterPaymentAsync();
                    if (vm.HasError) throw new Exception($"TEST_STAGE6_04 FALHOU no registro com número legível: {vm.ErrorMessage}");

                    results.Add("TEST_STAGE6_04_PAYMENT_CONTRACT_USABILITY_RESOLUTION: PASS");
                    Console.WriteLine("TEST_STAGE6_04_PAYMENT_CONTRACT_USABILITY_RESOLUTION: PASS");
                }

                // -------------------------------------------------------------
                // TESTE STAGE6_05: Validação das Novas Periodicidades e Valores da Interface
                // -------------------------------------------------------------
                using (var ctx = new CobraPixDbContext(options))
                {
                    var proSvc = new ProService(ctx);
                    await proSvc.SetPlanAsync(PlanType.Pro, "Test");
                    var contractSvc = new CreditContractService(ctx, proSvc);
                    var paymentSvc = new CreditPaymentService(ctx, proSvc);
                    var refundSvc = new CreditRefundService(ctx, proSvc);
                    var clientSvc = new ClientService(ctx, proSvc);

                    var vm = new CreditoGiroViewModel(contractSvc, paymentSvc, refundSvc, proSvc, clientSvc);
                    await vm.InitializeAsync();
                    await vm.LoadClientsAsync();

                    var client = vm.Clients.First();
                    vm.SelectedClient = client;

                    // Matriz de testes de periodicidade exibidas no vídeo:
                    var periodicitiesToTest = new (CreditPeriodicity periodicity, int? customDays, int installments, string code)[]
                    {
                        (CreditPeriodicity.Daily, null, 1, "CTR-PER-DAILY"),
                        (CreditPeriodicity.Weekly, null, 4, "CTR-PER-WEEKLY"),
                        (CreditPeriodicity.Biweekly, null, 2, "CTR-PER-BIWEEKLY"),
                        (CreditPeriodicity.Monthly, null, 1, "CTR-PER-MONTHLY"),
                        (CreditPeriodicity.CustomDays, 15, 3, "CTR-PER-CUSTOM15")
                    };

                    foreach (var tc in periodicitiesToTest)
                    {
                        vm.NewContractNumber = tc.code;
                        vm.NewPrincipalAmount = 1000m;
                        vm.NewTotalInterest = 200m;
                        vm.NewInstallmentsCount = tc.installments;
                        vm.NewPeriodicity = tc.periodicity;
                        vm.NewCustomIntervalDays = tc.customDays;
                        vm.NewFirstDueDate = DateTime.Today.AddDays(10);
                        vm.NewContractDate = DateTime.Today;

                        await vm.CreateContractAsync();
                        if (vm.HasError)
                            throw new Exception($"TEST_STAGE6_05 FALHOU ao criar contrato com periodicidade {tc.periodicity}: {vm.ErrorMessage}");

                        var saved = vm.Contracts.FirstOrDefault(c => c.ContractNumber == tc.code);
                        if (saved == null)
                            throw new Exception($"TEST_STAGE6_05 FALHOU: Contrato {tc.code} não listado na grade.");
                        if (saved.Periodicity != tc.periodicity)
                            throw new Exception($"TEST_STAGE6_05 FALHOU: Periodicidade divergente no contrato {tc.code} ({saved.Periodicity} != {tc.periodicity}).");
                        if (saved.CustomIntervalDays != tc.customDays)
                            throw new Exception($"TEST_STAGE6_05 FALHOU: CustomIntervalDays divergente no contrato {tc.code}.");
                    }

                    results.Add("TEST_STAGE6_05_ALL_PERIODICITIES_WITH_INTERFACE_VALUES: PASS");
                    Console.WriteLine("TEST_STAGE6_05_ALL_PERIODICITIES_WITH_INTERFACE_VALUES: PASS");
                }

                results.Add("TODOS OS TESTES DE CRÉDITO & GIRO (ETAPAS 1, 2, 3, 4, 5 E 6) FORAM APROVADOS COM SUCESSO!");
                Console.WriteLine("==================================================");
                Console.WriteLine("TODOS OS TESTES DE CRÉDITO & GIRO (ETAPAS 1, 2, 3, 4, 5 E 6) FORAM APROVADOS COM SUCESSO!");
                Console.WriteLine("==================================================");
                return results;
            }
            finally
            {
                Environment.SetEnvironmentVariable(DatabasePath.DatabasePathEnvVar, previousDbPathEnv);

                // Limpeza segura dos pools SQLite e exclusão do banco temporário isolado
                SqliteConnection.ClearAllPools();
                try
                {
                    if (File.Exists(tempDbPath))
                        File.Delete(tempDbPath);
                }
                catch { }
            }
        }

        /// <summary>
        /// Suíte automatizada de testes negativos de segurança e isolamento físico do Grupo 2A.
        /// Valida todas as travas contra fallback, junctions, fugas de sandbox, escrita em produção,
        /// DbConnection arbitrária, isolamento de recibos/sons/Registro e propagação de exceções de segurança.
        /// </summary>
        public static async Task<List<string>> RunIsolationSecurityTestsAsync(string? explicitSandboxRoot = null)
        {
            var results = new List<string>();

            // Salva estado das variáveis de ambiente para restauração estrita no finally
            string? prevMode = Environment.GetEnvironmentVariable(DatabasePath.ExecutionModeEnvVar);
            string? prevAllow = Environment.GetEnvironmentVariable(DatabasePath.AllowTestDbEnvVar);
            string? prevSandbox = Environment.GetEnvironmentVariable(DatabasePath.SandboxRootEnvVar);
            string? prevDb = Environment.GetEnvironmentVariable(DatabasePath.DatabasePathEnvVar);
            string? prevBackupDir = Environment.GetEnvironmentVariable(BackupService.BackupDirectoryEnvVar);
            string? prevReceiptsDir = Environment.GetEnvironmentVariable(ReceiptPdfService.ReceiptsDirectoryEnvVar);
            string? prevSoundsDir = Environment.GetEnvironmentVariable(ConfiguracoesViewModel.SoundsDirectoryEnvVar);

            string scratchRoot = !string.IsNullOrWhiteSpace(explicitSandboxRoot)
                ? Path.Combine(Path.GetFullPath(explicitSandboxRoot), "sec_sandbox_" + Guid.NewGuid().ToString("N"))
                : Path.GetFullPath(Path.Combine(Path.GetTempPath(), "cobrapix_sec_sandbox_" + Guid.NewGuid().ToString("N")));

            string confinedDir = Path.Combine(scratchRoot, "confined");
            string outsideDir = Path.Combine(scratchRoot, "outside");
            string junctionEscape = Path.Combine(confinedDir, "junction_to_outside");

            Directory.CreateDirectory(confinedDir);
            Directory.CreateDirectory(outsideDir);

            // Cria junction NTFS dentro de confinedDir apontando para outsideDir
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionEscape}\" \"{outsideDir}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit();
            }

            // Snapshot do valor real do Registro do Windows antes de qualquer teste
            string? initialRegistryValue = null;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: false);
                initialRegistryValue = key?.GetValue("CobraPixPC") as string;
            }
            catch { }

            try
            {
                string prodDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CobraPix");
                string prodDb = Path.Combine(prodDir, "cobrapix.db");

                // Configuração inicial de ambiente de teste confinado
                Environment.SetEnvironmentVariable(DatabasePath.ExecutionModeEnvVar, "TEST");
                Environment.SetEnvironmentVariable(DatabasePath.AllowTestDbEnvVar, "1");
                Environment.SetEnvironmentVariable(DatabasePath.SandboxRootEnvVar, confinedDir);

                results.Add("INICIANDO SUÍTE DE TESTES NEGATIVOS DE ISOLAMENTO E SEGURANÇA (GRUPO 2A)");
                results.Add($"Sandbox Confinada: {confinedDir}");

                // =========================================================================
                // CATEGORIA 4: Configuração de teste incompleta ou ausente (Fail-Closed)
                // =========================================================================
                Environment.SetEnvironmentVariable(DatabasePath.DatabasePathEnvVar, null);
                Environment.SetEnvironmentVariable(BackupService.BackupDirectoryEnvVar, null);
                Environment.SetEnvironmentVariable(ReceiptPdfService.ReceiptsDirectoryEnvVar, null);
                Environment.SetEnvironmentVariable(ConfiguracoesViewModel.SoundsDirectoryEnvVar, null);

                AssertThrows<InvalidOperationException>(results, "4a. DatabasePath.GetDatabaseFilePath fail-closed sem COBRAPIX_DATABASE_PATH", () =>
                {
                    _ = DatabasePath.GetDatabaseFilePath();
                });

                AssertThrows<InvalidOperationException>(results, "4b. DatabaseInitializer.Initialize fail-closed sem COBRAPIX_DATABASE_PATH", () =>
                {
                    DatabaseInitializer.Initialize();
                });

                var unconfiguredBackupSvc = new BackupService();
                AssertThrows<InvalidOperationException>(results, "4c. BackupService.GetAutoBackupDirectory fail-closed sem variaveis", () =>
                {
                    _ = unconfiguredBackupSvc.GetAutoBackupDirectory();
                });

                AssertThrows<InvalidOperationException>(results, "4d. BackupService.GetCurrentDatabasePath fail-closed sem variaveis", () =>
                {
                    _ = unconfiguredBackupSvc.GetCurrentDatabasePath();
                });

                var unconfiguredPdfSvc = new ReceiptPdfService();
                AssertThrows<InvalidOperationException>(results, "4e. ReceiptPdfService.GetReceiptsDirectory fail-closed sem COBRAPIX_RECEIPTS_DIRECTORY", () =>
                {
                    _ = unconfiguredPdfSvc.GetReceiptsDirectory();
                });

                AssertThrows<InvalidOperationException>(results, "4f. ConfiguracoesViewModel.GetSoundsDirectory fail-closed sem COBRAPIX_SOUNDS_DIRECTORY", () =>
                {
                    _ = ConfiguracoesViewModel.GetSoundsDirectory();
                });

                CreditProductionSafetyGuard.SetTestProductionPathOverride(null);
                AssertThrows<InvalidOperationException>(results, "4g. CreditProductionSafetyGuard.GetSentinelFilePath fail-closed sem override", () =>
                {
                    _ = CreditProductionSafetyGuard.GetSentinelFilePath();
                });

                // =========================================================================
                // CATEGORIA 5: Fallback de App.Services / CobraPixDbContext e Conexões
                // =========================================================================
                AssertThrows<InvalidOperationException>(results, "5a. new CobraPixDbContext() bloqueia fallback implicito sem COBRAPIX_DATABASE_PATH", () =>
                {
                    using var ctx = new CobraPixDbContext();
                });

                AssertThrows<InvalidOperationException>(results, "5b. new CobraPixDbContext(options) bloqueia conexao apontando para banco de producao", () =>
                {
                    var prodOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite($"Data Source={prodDb}")
                        .Options;
                    using var ctx = new CobraPixDbContext(prodOpts);
                });

                string outsideDb = Path.Combine(outsideDir, "outside_ctx.db");
                AssertThrows<InvalidOperationException>(results, "5c. new CobraPixDbContext(options) bloqueia conexao fora da sandbox", () =>
                {
                    var outOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite($"Data Source={outsideDb}")
                        .Options;
                    using var ctx = new CobraPixDbContext(outOpts);
                });

                AssertThrows<InvalidOperationException>(results, "5d. DI ServiceProvider bloqueia resolucao implicita de CobraPixDbContext sem base descartavel", () =>
                {
                    var services = new ServiceCollection();
                    services.AddDbContext<CobraPixDbContext>();
                    using var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    _ = scope.ServiceProvider.GetRequiredService<CobraPixDbContext>();
                });

                // Validação de DbConnection pré-instanciada (Tarefa 2)
                using (var memConn = new SqliteConnection("Data Source=:memory:"))
                {
                    var memOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite(memConn)
                        .Options;
                    using (var memCtx = new CobraPixDbContext(memOpts))
                    {
                        results.Add("[PASS] 5e. DbConnection em memória permitida com sucesso");
                    }
                }

                // Configura banco de dados descartável válido dentro da sandbox para os testes seguintes
                string validTestDb = Path.Combine(confinedDir, "test_disposable.db");
                string validBackupDir = Path.Combine(confinedDir, "backups");
                string validReceiptsDir = Path.Combine(confinedDir, "receipts");
                string validSoundsDir = Path.Combine(confinedDir, "sounds");

                Environment.SetEnvironmentVariable(DatabasePath.DatabasePathEnvVar, validTestDb);
                Environment.SetEnvironmentVariable(BackupService.BackupDirectoryEnvVar, validBackupDir);
                Environment.SetEnvironmentVariable(ReceiptPdfService.ReceiptsDirectoryEnvVar, validReceiptsDir);
                Environment.SetEnvironmentVariable(ConfiguracoesViewModel.SoundsDirectoryEnvVar, validSoundsDir);

                var validOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                    .UseSqlite($"Data Source={validTestDb}")
                    .Options;

                using (var initCtx = new CobraPixDbContext(validOpts))
                {
                    await initCtx.Database.EnsureCreatedAsync();
                    initCtx.Clients.Add(new Client { Name = "Cliente Isolado G2A", Phone = "11999998888", CreatedAt = DateTime.Now });
                    await initCtx.SaveChangesAsync();
                }

                using (var inSandboxConn = new SqliteConnection($"Data Source={validTestDb}"))
                {
                    var inSandboxOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite(inSandboxConn)
                        .Options;
                    using (var inSandboxCtx = new CobraPixDbContext(inSandboxOpts))
                    {
                        results.Add("[PASS] 5f. DbConnection dentro da sandbox permitida com sucesso");
                    }
                }

                using (var prodConn = new SqliteConnection($"Data Source={prodDb}"))
                {
                    var prodConnOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite(prodConn)
                        .Options;
                    AssertThrows<InvalidOperationException>(results, "5g. new CobraPixDbContext(options) bloqueia DbConnection apontando para o banco de producao", () =>
                    {
                        using var prodCtx = new CobraPixDbContext(prodConnOpts);
                    });
                }

                string outsideDbConn = Path.Combine(outsideDir, "outside_dbconn.db");
                using (var outConn = new SqliteConnection($"Data Source={outsideDbConn}"))
                {
                    var outConnOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite(outConn)
                        .Options;
                    AssertThrows<InvalidOperationException>(results, "5h. new CobraPixDbContext(options) bloqueia DbConnection fora da sandbox", () =>
                    {
                        using var outCtx = new CobraPixDbContext(outConnOpts);
                    });
                }

                // Testes adversariais: tentativa de contornar bloqueio de produção com Mode=Memory
                using (var prodMemConn = new SqliteConnection($"Data Source={prodDb};Mode=Memory;"))
                {
                    var prodMemOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite(prodMemConn)
                        .Options;
                    AssertThrows<InvalidOperationException>(results, "5i. new CobraPixDbContext(options) bloqueia DbConnection de producao mesmo com Mode=Memory", () =>
                    {
                        using var prodMemCtx = new CobraPixDbContext(prodMemOpts);
                    });
                }

                var prodMemConnStrOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                    .UseSqlite($"Data Source={prodDb};Mode=Memory;")
                    .Options;
                AssertThrows<InvalidOperationException>(results, "5j. new CobraPixDbContext(options) bloqueia ConnectionString de producao mesmo com Mode=Memory", () =>
                {
                    using var prodMemCtx = new CobraPixDbContext(prodMemConnStrOpts);
                });

                // Testes de banco em memória com URI legítima
                var uriOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                    .UseSqlite("Data Source=file:memdb_opts?mode=memory&cache=shared")
                    .Options;
                using (var uriCtx = new CobraPixDbContext(uriOpts))
                {
                    results.Add("[PASS] 5k. DbContextOptions com URI em memória permitida com sucesso");
                }

                using (var uriConn = new SqliteConnection("Data Source=file:memdb_conn?mode=memory&cache=shared"))
                {
                    var uriConnOpts = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite(uriConn)
                        .Options;
                    using (var uriConnCtx = new CobraPixDbContext(uriConnOpts))
                    {
                        results.Add("[PASS] 5l. DbConnection com URI em memória permitida com sucesso");
                    }
                }

                // Prepara backups de teste (.cpb e .json) dentro da sandbox
                string validCpbPath = Path.Combine(validBackupDir, "valid_backup.cpb");
                string validJsonPath = Path.Combine(validBackupDir, "valid_android.json");
                Directory.CreateDirectory(validBackupDir);

                using (var setupCtx = new CobraPixDbContext(validOpts))
                {
                    var bSvc = new BackupService(setupCtx);
                    var cpbRes = await bSvc.CreateBackupAsync(validCpbPath, validTestDb);
                    if (!cpbRes.Success || !File.Exists(validCpbPath))
                        throw new Exception("Falha ao preparar valid_backup.cpb na sandbox.");
                }
                File.WriteAllText(validJsonPath, "{\"clients\":[],\"sales\":[],\"installments\":[],\"payments\":[]}");

                // =========================================================================
                // CATEGORIA 1: Caminho de restauração apontando diretamente para produção
                // =========================================================================
                using (var ctx = new CobraPixDbContext(validOpts))
                {
                    var backupSvc = new BackupService(ctx);
                    var androidSvc = new AndroidImportService(ctx);

                    await AssertThrowsAsync<InvalidOperationException>(results, "1a. RestoreBackupAsync(.cpb) bloqueia banco de producao antes de qualquer escrita", async () =>
                    {
                        await backupSvc.RestoreBackupAsync(validCpbPath, prodDb);
                    });

                    await AssertThrowsAsync<InvalidOperationException>(results, "1b. RestoreBackupAsync(.json) bloqueia banco de producao antes de delegar ao AndroidImportService", async () =>
                    {
                        await backupSvc.RestoreBackupAsync(validJsonPath, prodDb);
                    });

                    await AssertThrowsAsync<InvalidOperationException>(results, "1c. AndroidImportService.ImportAsync bloqueia banco de producao diretamente", async () =>
                    {
                        await androidSvc.ImportAsync(validJsonPath, prodDb);
                    });

                    await AssertThrowsAsync<InvalidOperationException>(results, "1d. CreateBackupAsync bloqueia gravacao de backup dentro do diretorio de producao", async () =>
                    {
                        await backupSvc.CreateBackupAsync(Path.Combine(prodDir, "evil_backup.cpb"), validTestDb);
                    });

                    await AssertThrowsAsync<InvalidOperationException>(results, "1e. CreateBackupAsync bloqueia leitura do banco de producao como origem em modo de teste", async () =>
                    {
                        await backupSvc.CreateBackupAsync(Path.Combine(validBackupDir, "from_prod.cpb"), prodDb);
                    });
                }

                // =========================================================================
                // CATEGORIA 2: Caminho equivalente por junction ou symlink
                // =========================================================================
                using (var ctx = new CobraPixDbContext(validOpts))
                {
                    var backupSvc = new BackupService(ctx);
                    var androidSvc = new AndroidImportService(ctx);
                    string junctionTargetDb = Path.Combine(junctionEscape, "junction_target.db");

                    await AssertThrowsAsync<InvalidOperationException>(results, "2a. RestoreBackupAsync(.cpb) bloqueia destino via NTFS junction que aponta para fora da sandbox", async () =>
                    {
                        await backupSvc.RestoreBackupAsync(validCpbPath, junctionTargetDb);
                    });

                    await AssertThrowsAsync<InvalidOperationException>(results, "2b. AndroidImportService.ImportAsync bloqueia destino via NTFS junction", async () =>
                    {
                        await androidSvc.ImportAsync(validJsonPath, junctionTargetDb);
                    });

                    AssertThrows<InvalidOperationException>(results, "2c. BackupService.AssertSafeBackupDirectory bloqueia diretorio via NTFS junction", () =>
                    {
                        BackupService.AssertSafeBackupDirectory(junctionEscape);
                    });

                    if (Directory.EnumerateFileSystemEntries(outsideDir).Any())
                    {
                        throw new Exception("FALHA DE ISOLAMENTO: Arquivos ou diretorios foram criados em outsideDir atraves da junction!");
                    }
                    results.Add("[PASS] 2d. Comprovado zero efeitos colaterais em outsideDir apos bloqueio de junction");
                }

                // =========================================================================
                // CATEGORIA 3: Caminho que escape da sandbox
                // =========================================================================
                using (var ctx = new CobraPixDbContext(validOpts))
                {
                    var backupSvc = new BackupService(ctx);
                    string relEscapeDb = Path.Combine(confinedDir, "..", "outside", "escaped_rel.db");
                    string absEscapeDb = Path.Combine(outsideDir, "escaped_abs.db");

                    await AssertThrowsAsync<InvalidOperationException>(results, "3a. RestoreBackupAsync bloqueia caminho relativo com '..' escapando da sandbox", async () =>
                    {
                        await backupSvc.RestoreBackupAsync(validCpbPath, relEscapeDb);
                    });

                    await AssertThrowsAsync<InvalidOperationException>(results, "3b. RestoreBackupAsync bloqueia caminho absoluto fora da sandbox", async () =>
                    {
                        await backupSvc.RestoreBackupAsync(validCpbPath, absEscapeDb);
                    });

                    AssertThrows<InvalidOperationException>(results, "3c. DatabasePath.GetDatabaseFilePath bloqueia travessia relativa '..'", () =>
                    {
                        Environment.SetEnvironmentVariable(DatabasePath.DatabasePathEnvVar, relEscapeDb);
                        try
                        {
                            _ = DatabasePath.GetDatabaseFilePath();
                        }
                        finally
                        {
                            Environment.SetEnvironmentVariable(DatabasePath.DatabasePathEnvVar, validTestDb);
                        }
                    });

                    if (Directory.EnumerateFileSystemEntries(outsideDir).Any())
                    {
                        throw new Exception("FALHA DE ISOLAMENTO: Arquivos foram criados em outsideDir apos tentativa de escape!");
                    }
                    results.Add("[PASS] 3d. Comprovado zero escrita fora da sandbox apos bloqueio de caminhos relativos e absolutos");
                }

                // =========================================================================
                // CATEGORIA 6: Inicialização de ViewModels sem efeitos no Registro do Windows
                // =========================================================================
                using (var ctx = new CobraPixDbContext(validOpts))
                {
                    ConfiguracoesViewModel.ResetTestSimulatedRegistry(false);
                    var paySvc = new PaymentService(ctx);
                    var monSvc = new MonthlyCustomerService(ctx);
                    var notifSvc = new NotificationService(ctx, paySvc, monSvc);
                    var backupSvc = new BackupService(ctx);
                    var proSvc = new ProService(ctx);
                    var themeSvc = new ThemeService(ctx);

                    var configVm = new ConfiguracoesViewModel(notifSvc, backupSvc, proSvc, themeSvc);
                    await Task.Delay(50);

                    string? afterCtorRegistryValue = ReadRegistryRunValueInternal();
                    if (!string.Equals(initialRegistryValue, afterCtorRegistryValue, StringComparison.Ordinal))
                    {
                        throw new Exception($"FALHA DE REGISTRO: Construtor de ConfiguracoesViewModel alterou HKCU Run! Antes='{initialRegistryValue}', Depois='{afterCtorRegistryValue}'");
                    }

                    await configVm.SetStartWithWindowsAsync(true);
                    if (!ConfiguracoesViewModel.GetTestSimulatedRegistryState())
                        throw new Exception("Estado simulado de registro em memoria deveria ser true.");

                    string? afterEnableRegistryValue = ReadRegistryRunValueInternal();
                    if (!string.Equals(initialRegistryValue, afterEnableRegistryValue, StringComparison.Ordinal))
                    {
                        throw new Exception($"FALHA DE REGISTRO: SetStartWithWindowsAsync(true) alterou HKCU Run! Antes='{initialRegistryValue}', Depois='{afterEnableRegistryValue}'");
                    }

                    await configVm.SetStartWithWindowsAsync(false);
                    if (ConfiguracoesViewModel.GetTestSimulatedRegistryState())
                        throw new Exception("Estado simulado de registro em memoria deveria ser false.");

                    string? afterDisableRegistryValue = ReadRegistryRunValueInternal();
                    if (!string.Equals(initialRegistryValue, afterDisableRegistryValue, StringComparison.Ordinal))
                    {
                        throw new Exception($"FALHA DE REGISTRO: SetStartWithWindowsAsync(false) alterou HKCU Run! Antes='{initialRegistryValue}', Depois='{afterDisableRegistryValue}'");
                    }

                    results.Add("[PASS] 6. Inicializacao de ConfiguracoesViewModel e SetStartWithWindowsAsync sem efeito no Registro real");
                }

                // =========================================================================
                // CATEGORIA 7: Tentativa de gravação de recibos ou sons fora da sandbox
                // =========================================================================
                using (var ctx = new CobraPixDbContext(validOpts))
                {
                    var pdfSvc = new ReceiptPdfService(ctx);
                    var dummyTx = new FinancialTransaction
                    {
                        Id = 1,
                        ClientName = "Cliente Recibo",
                        Description = "Parcela 1/1",
                        Amount = 100m,
                        OriginalAmount = 100m,
                        TotalPaid = 100m,
                        RemainingBalance = 0m,
                        PaymentDate = DateTime.Now,
                        TransactionType = FinancialTransactionType.Installment,
                        PaymentMethod = PaymentMethod.Pix
                    };

                    string outsidePdf = Path.Combine(outsideDir, "recibo_fora.pdf");
                    string junctionPdf = Path.Combine(junctionEscape, "recibo_junction.pdf");
                    string prodPdf = Path.Combine(prodDir, "receipts", "recibo_prod.pdf");

                    AssertThrows<InvalidOperationException>(results, "7a. ReceiptPdfService.GenerateAndSaveReceipt bloqueia caminho fora da sandbox", () =>
                    {
                        pdfSvc.GenerateAndSaveReceipt(dummyTx, outsidePdf);
                    });

                    AssertThrows<InvalidOperationException>(results, "7b. ReceiptPdfService.GenerateAndSaveReceipt bloqueia caminho via NTFS junction", () =>
                    {
                        pdfSvc.GenerateAndSaveReceipt(dummyTx, junctionPdf);
                    });

                    AssertThrows<InvalidOperationException>(results, "7c. ReceiptPdfService.GenerateAndSaveReceipt bloqueia pasta de recibos de producao", () =>
                    {
                        pdfSvc.GenerateAndSaveReceipt(dummyTx, prodPdf);
                    });

                    if (File.Exists(outsidePdf) || Directory.EnumerateFileSystemEntries(outsideDir).Any())
                        throw new Exception("FALHA: Recibo PDF foi gravado fora da sandbox!");

                    string validWavSource = Path.Combine(confinedDir, "source_beep.wav");
                    File.WriteAllBytes(validWavSource, new byte[] { 0x52, 0x49, 0x46, 0x46 });

                    var paySvc = new PaymentService(ctx);
                    var monSvc = new MonthlyCustomerService(ctx);
                    var notifSvc = new NotificationService(ctx, paySvc, monSvc);
                    var backupSvc = new BackupService(ctx);
                    var proSvc = new ProService(ctx);
                    var themeSvc = new ThemeService(ctx);
                    var configVm = new ConfiguracoesViewModel(notifSvc, backupSvc, proSvc, themeSvc);

                    Environment.SetEnvironmentVariable(ConfiguracoesViewModel.SoundsDirectoryEnvVar, Path.Combine(outsideDir, "sounds"));
                    configVm.SelectAudioFileDialogFunc = () => validWavSource;
                    await AssertThrowsAsync<InvalidOperationException>(results, "7d. SelectCustomSoundAsync bloqueia COBRAPIX_SOUNDS_DIRECTORY fora da sandbox", async () =>
                    {
                        await configVm.SelectCustomSoundAsync();
                    });

                    Environment.SetEnvironmentVariable(ConfiguracoesViewModel.SoundsDirectoryEnvVar, Path.Combine(junctionEscape, "sounds"));
                    await AssertThrowsAsync<InvalidOperationException>(results, "7e. SelectCustomSoundAsync bloqueia COBRAPIX_SOUNDS_DIRECTORY via NTFS junction", async () =>
                    {
                        await configVm.SelectCustomSoundAsync();
                    });

                    Environment.SetEnvironmentVariable(ConfiguracoesViewModel.SoundsDirectoryEnvVar, validSoundsDir);
                    string outsideWav = Path.Combine(outsideDir, "outside.wav");
                    configVm.SelectAudioFileDialogFunc = () => outsideWav;
                    await AssertThrowsAsync<InvalidOperationException>(results, "7f. SelectCustomSoundAsync bloqueia arquivo de origem fora da sandbox", async () =>
                    {
                        await configVm.SelectCustomSoundAsync();
                    });

                    if (Directory.EnumerateFileSystemEntries(outsideDir).Any())
                        throw new Exception("FALHA: Arquivos ou pastas de som foram criados em outsideDir!");

                    results.Add("[PASS] 7g. Comprovado zero escrita de recibos ou sons fora da sandbox");
                }

                // =========================================================================
                // CATEGORIA 8: Erro de diretório no backup automático vs Falha de Segurança
                // =========================================================================
                string invalidDirInsideSandbox;
                using (var ctx = new CobraPixDbContext(validOpts))
                {
                    var backupSvc = new BackupService(ctx);
                    await backupSvc.SetAutoBackupEnabledAsync(true);

                    string blockingFile = Path.Combine(confinedDir, "arquivo_bloqueante.tmp");
                    File.WriteAllText(blockingFile, "bloqueio");
                    invalidDirInsideSandbox = Path.Combine(blockingFile, "subpasta_impossivel");

                    var resOperationalError = await backupSvc.CheckAndPerformDailyAutoBackupAsync(
                        customSourceDb: validTestDb,
                        customTargetDir: invalidDirInsideSandbox,
                        force: true);

                    if (resOperationalError == null || resOperationalError.Success)
                    {
                        throw new Exception("8a. CheckAndPerformDailyAutoBackupAsync deveria retornar BackupResult { Success = false } para erro operacional de diretorio.");
                    }

                    var recordedError = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == BackupService.SettingKeyLastAutoBackupError);
                    if (recordedError == null || string.IsNullOrWhiteSpace(recordedError.Value))
                    {
                        throw new Exception("8a. CheckAndPerformDailyAutoBackupAsync nao registrou SettingKeyLastAutoBackupError no banco.");
                    }
                    results.Add($"[PASS] 8a. Erro operacional de diretorio no auto-backup tratado e registrado ({recordedError.Value})");

                    await AssertThrowsAsync<InvalidOperationException>(results, "8b. CheckAndPerformDailyAutoBackupAsync NAO oculta falha de seguranca (diretorio de producao)", async () =>
                    {
                        await backupSvc.CheckAndPerformDailyAutoBackupAsync(
                            customSourceDb: validTestDb,
                            customTargetDir: Path.Combine(prodDir, "auto_backups"),
                            force: true);
                    });

                    await AssertThrowsAsync<InvalidOperationException>(results, "8c. CheckAndPerformDailyAutoBackupAsync NAO oculta falha de seguranca (fuga via junction)", async () =>
                    {
                        await backupSvc.CheckAndPerformDailyAutoBackupAsync(
                            customSourceDb: validTestDb,
                            customTargetDir: junctionEscape,
                            force: true);
                    });
                }

                // =========================================================================
                // CATEGORIA 9: RunAutoBackupNowAsync - Exceção de Segurança vs Erro Operacional (Tarefa 3)
                // =========================================================================
                using (var ctx = new CobraPixDbContext(validOpts))
                {
                    var paySvc = new PaymentService(ctx);
                    var monSvc = new MonthlyCustomerService(ctx);
                    var notifSvc = new NotificationService(ctx, paySvc, monSvc);
                    var backupSvc = new BackupService(ctx);
                    var proSvc = new ProService(ctx);
                    var themeSvc = new ThemeService(ctx);

                    // 9a. RunAutoBackupNowAsync relança [SEGURANÇA DE EXECUÇÃO]
                    Environment.SetEnvironmentVariable(BackupService.BackupDirectoryEnvVar, Path.Combine(prodDir, "auto_backups"));
                    var configVmSecurity = new ConfiguracoesViewModel(notifSvc, backupSvc, proSvc, themeSvc);
                    await AssertThrowsAsync<InvalidOperationException>(results, "9a. RunAutoBackupNowAsync propaga [SEGURANÇA DE EXECUÇÃO]", async () =>
                    {
                        await configVmSecurity.RunAutoBackupNowAsync();
                    });

                    // 9b. RunAutoBackupNowAsync trata erro operacional comum sem relançar exceção
                    Environment.SetEnvironmentVariable(BackupService.BackupDirectoryEnvVar, invalidDirInsideSandbox);
                    var configVmOperational = new ConfiguracoesViewModel(notifSvc, backupSvc, proSvc, themeSvc);
                    await configVmOperational.RunAutoBackupNowAsync();
                    if (string.IsNullOrWhiteSpace(configVmOperational.LastAutoBackupInfo) || !configVmOperational.LastAutoBackupInfo.Contains("Diretório de backup inacessível"))
                    {
                        throw new Exception("9b. RunAutoBackupNowAsync deveria atualizar LastAutoBackupInfo com mensagem de erro operacional.");
                    }
                    results.Add("[PASS] 9b. RunAutoBackupNowAsync tratou erro operacional comum sem relancar excecao");

                    // Restaura diretório de backup válido para garantir hermeticidade
                    Environment.SetEnvironmentVariable(BackupService.BackupDirectoryEnvVar, validBackupDir);
                }

                // =========================================================================
                // CATEGORIA 10: OpenAutoBackupFolder em modo de teste (Tarefa 4)
                // =========================================================================
                using (var ctx = new CobraPixDbContext(validOpts))
                {
                    var paySvc = new PaymentService(ctx);
                    var monSvc = new MonthlyCustomerService(ctx);
                    var notifSvc = new NotificationService(ctx, paySvc, monSvc);
                    var backupSvc = new BackupService(ctx);
                    var proSvc = new ProService(ctx);
                    var themeSvc = new ThemeService(ctx);
                    var configVm = new ConfiguracoesViewModel(notifSvc, backupSvc, proSvc, themeSvc);

                    bool processAttempted = false;
                    configVm.ProcessStarterFunc = _ =>
                    {
                        processAttempted = true;
                        throw new InvalidOperationException("VIOLAÇÃO DE SEGURANÇA: ProcessStarterFunc foi invocado indevidamente em modo de teste!");
                    };

                    // 10a. OpenAutoBackupFolder encerra de forma no-op em modo de teste sem iniciar processos (com diretório nulo/vazio)
                    configVm.OpenAutoBackupFolder();
                    if (processAttempted)
                    {
                        throw new Exception("10a FALHOU: OpenAutoBackupFolder tentou iniciar processo externo em modo de teste!");
                    }

                    // 10b. OpenAutoBackupFolder encerra de forma no-op mesmo quando há diretório configurado
                    await configVm.LoadAutoBackupInfoAsync();
                    configVm.OpenAutoBackupFolder();
                    if (processAttempted)
                    {
                        throw new Exception("10b FALHOU: OpenAutoBackupFolder tentou iniciar processo externo com diretório configurado!");
                    }

                    results.Add("[PASS] 10a. OpenAutoBackupFolder encerrou de forma no-op em modo de teste sem iniciar processos externos (verificado via interceptador)");
                }

                results.Add("==================================================");
                results.Add("TODOS OS TESTES NEGATIVOS DE ISOLAMENTO E SEGURANÇA FORAM APROVADOS COM SUCESSO!");
                results.Add("==================================================");
                return results;
            }
            finally
            {
                // Restauração das variáveis de ambiente
                Environment.SetEnvironmentVariable(DatabasePath.ExecutionModeEnvVar, prevMode);
                Environment.SetEnvironmentVariable(DatabasePath.AllowTestDbEnvVar, prevAllow);
                Environment.SetEnvironmentVariable(DatabasePath.SandboxRootEnvVar, prevSandbox);
                Environment.SetEnvironmentVariable(DatabasePath.DatabasePathEnvVar, prevDb);
                Environment.SetEnvironmentVariable(BackupService.BackupDirectoryEnvVar, prevBackupDir);
                Environment.SetEnvironmentVariable(ReceiptPdfService.ReceiptsDirectoryEnvVar, prevReceiptsDir);
                Environment.SetEnvironmentVariable(ConfiguracoesViewModel.SoundsDirectoryEnvVar, prevSoundsDir);
                CreditProductionSafetyGuard.SetTestProductionPathOverride(null);

                SqliteConnection.ClearAllPools();
                try
                {
                    if (Directory.Exists(junctionEscape))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c rmdir \"{junctionEscape}\"")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        System.Diagnostics.Process.Start(psi)?.WaitForExit();
                    }
                    if (Directory.Exists(scratchRoot))
                    {
                        Directory.Delete(scratchRoot, true);
                    }
                }
                catch { }
            }
        }

        private static string? ReadRegistryRunValueInternal()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: false);
                return key?.GetValue("CobraPixPC") as string;
            }
            catch
            {
                return null;
            }
        }

        private static void AssertThrows<TException>(List<string> results, string testName, Action action, bool requireSecurityMarker = true) where TException : Exception
        {
            bool threw = false;
            try
            {
                action();
            }
            catch (TException ex)
            {
                threw = true;
                if (requireSecurityMarker && typeof(TException) == typeof(InvalidOperationException))
                {
                    if (!ex.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
                    {
                        throw new Exception($"FAILED: {testName} lançou {ex.GetType().Name}, mas a mensagem não contém '[SEGURANÇA DE EXECUÇÃO]'. Mensagem: {ex.Message}");
                    }
                }
                results.Add($"[PASS] {testName} -> {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
            if (!threw)
            {
                throw new Exception($"FAILED: {testName} did NOT throw {typeof(TException).Name}");
            }
        }

        private static async Task AssertThrowsAsync<TException>(List<string> results, string testName, Func<Task> action, bool requireSecurityMarker = true) where TException : Exception
        {
            bool threw = false;
            try
            {
                await action();
            }
            catch (TException ex)
            {
                threw = true;
                if (requireSecurityMarker && typeof(TException) == typeof(InvalidOperationException))
                {
                    if (!ex.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
                    {
                        throw new Exception($"FAILED: {testName} lançou {ex.GetType().Name}, mas a mensagem não contém '[SEGURANÇA DE EXECUÇÃO]'. Mensagem: {ex.Message}");
                    }
                }
                results.Add($"[PASS] {testName} -> {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
            if (!threw)
            {
                throw new Exception($"FAILED: {testName} did NOT throw {typeof(TException).Name}");
            }
        }
    }
}
