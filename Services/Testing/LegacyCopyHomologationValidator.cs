using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Data.Credit;
using CobraPix_PC.Models;
using CobraPix_PC.Models.Credit;
using CobraPix_PC.Models.Credit.Dtos;
using CobraPix_PC.Services;
using CobraPix_PC.Services.Credit;
using CobraPix_PC.Services.Exceptions;

namespace CobraPix_PC.Services.Testing
{
    /// <summary>
    /// Validador de Homologação Controlada com Cópia da Base Legada.
    /// Executa 100% contra a cópia em diretório isolado, garantindo inviolabilidade
    /// absoluta do banco de produção real (%LOCALAPPDATA%\CobraPix\cobrapix.db).
    /// </summary>
    public static class LegacyCopyHomologationValidator
    {
        public static async Task<List<string>> RunHomologationAsync(string? explicitCopyPath = null)
        {
            var logs = new List<string>();

            // ──────────────────────────────────────────────────────────
            // 1. PREPARAÇÃO SEGURA E ISOLAMENTO
            // ──────────────────────────────────────────────────────────
            string prodDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CobraPix",
                "cobrapix.db");

            if (!File.Exists(prodDbPath))
            {
                throw new FileNotFoundException($"Banco de produção original não encontrado em: {prodDbPath}");
            }

            // Registra tamanho e SHA-256 do original antes de qualquer coisa
            long origSizeBefore = new FileInfo(prodDbPath).Length;
            string origSha256Before = ComputeFileSha256(prodDbPath);

            logs.Add("==================================================================");
            logs.Add("COBRAPIX PC — HOMOLOGAÇÃO CONTROLADA COM CÓPIA DA BASE LEGADA");
            logs.Add("==================================================================");
            logs.Add($"1. [SEGURANÇA] Banco Original (Produção): {prodDbPath}");
            logs.Add($"   - Tamanho original: {origSizeBefore} bytes");
            logs.Add($"   - SHA-256 original: {origSha256Before}");

            // Define caminho da cópia de homologação
            string homologDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CobraPix_Homologacao");

            if (!Directory.Exists(homologDir))
            {
                Directory.CreateDirectory(homologDir);
            }

            string copyPath = explicitCopyPath ?? Path.Combine(homologDir, "cobrapix_homolog_copy.db");

            // Se o arquivo da cópia não existir ou tiver hash divergente do original, recria cópia idêntica
            if (!File.Exists(copyPath) || ComputeFileSha256(copyPath) != origSha256Before)
            {
                File.Copy(prodDbPath, copyPath, overwrite: true);
            }

            long copySizeBefore = new FileInfo(copyPath).Length;
            string copySha256Before = ComputeFileSha256(copyPath);

            logs.Add($"2. [ISOLAMENTO] Cópia de Homologação: {copyPath}");
            logs.Add($"   - Tamanho da cópia: {copySizeBefore} bytes");
            logs.Add($"   - SHA-256 da cópia: {copySha256Before}");

            if (origSha256Before != copySha256Before)
            {
                throw new InvalidOperationException("FALHA CRÍTICA DE ISOLAMENTO: A cópia não coincide byte a byte com o original.");
            }
            logs.Add("   - Verificação de paridade byte a byte da cópia: PASS");

            // Configuração das opções de DbContext apontando EXCLUSIVAMENTE para a cópia
            var copyOptions = new DbContextOptionsBuilder<CobraPixDbContext>()
                .UseSqlite($"Data Source={copyPath}")
                .Options;

            // ──────────────────────────────────────────────────────────
            // 2. VERIFICAÇÕES ANTERIORES À MIGRAÇÃO (NA CÓPIA)
            // ──────────────────────────────────────────────────────────
            logs.Add("------------------------------------------------------------------");
            logs.Add("2. VERIFICAÇÕES ANTERIORES À MIGRAÇÃO (NA CÓPIA DE HOMOLOGAÇÃO)");
            logs.Add("------------------------------------------------------------------");

            var preMigrationLegacyCounts = new Dictionary<string, int>();
            var preMigrationTables = new List<string>();
            var preMigrationIndexes = new List<string>();

            using (var preConn = new SqliteConnection($"Data Source={copyPath}"))
            {
                await preConn.OpenAsync();

                // PRAGMA integrity_check
                using (var cmd = preConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA integrity_check;";
                    var intCheck = (string?)await cmd.ExecuteScalarAsync();
                    if (intCheck != "ok")
                        throw new InvalidOperationException($"PRAGMA integrity_check falhou na base copiada: {intCheck}");
                    logs.Add($"   - PRAGMA integrity_check (pré-migração): {intCheck}");
                }

                // PRAGMA foreign_key_check
                using (var cmd = preConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_key_check;";
                    using var fkReader = await cmd.ExecuteReaderAsync();
                    int fkErrors = 0;
                    while (await fkReader.ReadAsync()) fkErrors++;
                    if (fkErrors > 0)
                        throw new InvalidOperationException($"PRAGMA foreign_key_check acusou {fkErrors} inconsistências na base legada.");
                    logs.Add($"   - PRAGMA foreign_key_check (pré-migração): ok (0 inconsistências)");
                }

                // Lista tabelas e índices existentes
                using (var cmd = preConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT type, name FROM sqlite_master WHERE type IN ('table', 'index') AND name NOT LIKE 'sqlite_%' ORDER BY type, name;";
                    using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        string type = rdr.GetString(0);
                        string name = rdr.GetString(1);
                        if (type == "table") preMigrationTables.Add(name);
                        else if (type == "index") preMigrationIndexes.Add(name);
                    }
                }

                logs.Add($"   - Tabelas legadas existentes ({preMigrationTables.Count}): {string.Join(", ", preMigrationTables)}");
                logs.Add($"   - Índices legados existentes ({preMigrationIndexes.Count})");

                // Contagem de registros nas tabelas legadas
                foreach (var tbl in preMigrationTables)
                {
                    using var cmd = preConn.CreateCommand();
                    cmd.CommandText = $"SELECT COUNT(*) FROM [{tbl}];";
                    int cnt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    preMigrationLegacyCounts[tbl] = cnt;
                    logs.Add($"     * Tabela [{tbl}]: {cnt} registros");
                }

                // Verifica se tabela de versão de crédito já existia
                bool hadCreditVersion = preMigrationTables.Contains("CreditSchemaVersions");
                logs.Add($"   - Módulo de crédito já existente antes da migração: {hadCreditVersion}");
            }

            // ──────────────────────────────────────────────────────────
            // 3. MIGRAÇÃO E COMPATIBILIDADE NA CÓPIA
            // ──────────────────────────────────────────────────────────
            logs.Add("------------------------------------------------------------------");
            logs.Add("3. EXECUÇÃO DA MIGRAÇÃO NA CÓPIA DE HOMOLOGAÇÃO");
            logs.Add("------------------------------------------------------------------");

            using (var migCtx = new CobraPixDbContext(copyOptions))
            {
                // Aplica migração de crédito aditiva segura (sem EnsureCreated)
                await CreditDatabaseMigrator.ApplyCreditMigrationsAsync(migCtx);
                logs.Add("   - CreditDatabaseMigrator.ApplyCreditMigrationsAsync executado com sucesso.");
            }

            // Inspeção pós-migração
            var postMigrationLegacyCounts = new Dictionary<string, int>();
            var postMigrationTables = new List<string>();
            int creditVersionApplied = 0;

            using (var postConn = new SqliteConnection($"Data Source={copyPath}"))
            {
                await postConn.OpenAsync();

                // PRAGMA integrity_check pós-migração
                using (var cmd = postConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA integrity_check;";
                    var intCheck = (string?)await cmd.ExecuteScalarAsync();
                    if (intCheck != "ok")
                        throw new InvalidOperationException($"PRAGMA integrity_check falhou pós-migração: {intCheck}");
                    logs.Add($"   - PRAGMA integrity_check (pós-migração): {intCheck}");
                }

                // PRAGMA foreign_key_check pós-migração
                using (var cmd = postConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_key_check;";
                    using var fkReader = await cmd.ExecuteReaderAsync();
                    int fkErrors = 0;
                    while (await fkReader.ReadAsync()) fkErrors++;
                    if (fkErrors > 0)
                        throw new InvalidOperationException($"PRAGMA foreign_key_check acusou {fkErrors} violações pós-migração.");
                    logs.Add($"   - PRAGMA foreign_key_check (pós-migração): ok (0 violações)");
                }

                // Versão registrada em CreditSchemaVersions
                using (var cmd = postConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM CreditSchemaVersions;";
                    creditVersionApplied = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    logs.Add($"   - Versão máxima em CreditSchemaVersions: {creditVersionApplied} (Esperado >= 2)");
                    if (creditVersionApplied < 2)
                        throw new InvalidOperationException($"Versão de esquema incorreta pós-migração: {creditVersionApplied}");
                }

                // Tabelas atuais
                using (var cmd = postConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                    using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        postMigrationTables.Add(rdr.GetString(0));
                    }
                }

                // Conferência de preservação exata dos registros legados
                foreach (var kvp in preMigrationLegacyCounts)
                {
                    using var cmd = postConn.CreateCommand();
                    cmd.CommandText = $"SELECT COUNT(*) FROM [{kvp.Key}];";
                    int postCnt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    postMigrationLegacyCounts[kvp.Key] = postCnt;

                    if (postCnt != kvp.Value)
                    {
                        throw new InvalidOperationException($"VIOLAÇÃO DE PRESERVAÇÃO LEGADA: Tabela {kvp.Key} tinha {kvp.Value} registros e agora tem {postCnt}.");
                    }
                    logs.Add($"   - Preservação da tabela [{kvp.Key}]: {kvp.Value} -> {postCnt} (INTACTA)");
                }

                // Confirma que as 15 tabelas de crédito foram criadas
                string[] expectedCreditTables = new[]
                {
                    "CreditContracts", "CreditDisbursements", "CreditInstallments", "CreditCycles",
                    "CreditPayments", "CreditPaymentInstallmentAllocs", "CreditSurplusBalances",
                    "CreditSurplusConsumptions", "CreditSurplusConsumptionReversals",
                    "CreditPaymentRefunds", "CreditDevolutions", "CreditCashOutflows",
                    "CreditIdempotencyRecords", "CreditPaymentIntents", "CreditTransactionAudits"
                };

                foreach (var ct in expectedCreditTables)
                {
                    if (!postMigrationTables.Contains(ct))
                        throw new InvalidOperationException($"Tabela de crédito obrigatória não encontrada pós-migração: {ct}");
                }
                logs.Add($"   - Criação confirmada de todas as {expectedCreditTables.Length} tabelas de Crédito & Giro.");
            }

            // ──────────────────────────────────────────────────────────
            // 4. TESTES FUNCIONAIS NA CÓPIA DE HOMOLOGAÇÃO (10 CENÁRIOS)
            // ──────────────────────────────────────────────────────────
            logs.Add("------------------------------------------------------------------");
            logs.Add("4. EXECUÇÃO DOS 10 CENÁRIOS FUNCIONAIS NA CÓPIA");
            logs.Add("------------------------------------------------------------------");

            using (var fnCtx = new CobraPixDbContext(copyOptions))
            {
                var proSvc = new ProService(fnCtx);
                var contractSvc = new CreditContractService(fnCtx, proSvc);
                var paymentSvc = new CreditPaymentService(fnCtx, proSvc);
                var refundSvc = new CreditRefundService(fnCtx, proSvc);

                // Localiza ou cria cliente para os testes
                var client = await fnCtx.Clients.FirstOrDefaultAsync();
                if (client == null)
                {
                    client = new Client { Name = "Cliente Homologação Legada", Phone = "11999990000", CreatedAt = DateTime.UtcNow };
                    fnCtx.Clients.Add(client);
                    await fnCtx.SaveChangesAsync();
                }

                // -------------------------------------------------------------
                // CENÁRIO 1: Criar Contrato e Gerar Parcelas
                // -------------------------------------------------------------
                logs.Add("--- CENÁRIO 1: Criar contrato e gerar parcelas ---");
                // Testa bloqueio FREE primeiro
                await proSvc.SetPlanAsync(PlanType.Free, "HomologFreeTest");
                bool blockedFree = false;
                try
                {
                    await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                    {
                        ClientId = client.Id,
                        ContractNumber = "CTR-HOMOLOG-001",
                        PrincipalAmountCents = 10000,
                        TotalInterestCents = 3000,
                        InstallmentsCount = 2,
                        FirstDueDate = DateTime.Today.AddDays(30),
                        ContractDate = DateTime.Today,
                        IdempotencyKey = "HOMOLOG-KEY-C1-FREE"
                    });
                }
                catch (ProLicenseRequiredException)
                {
                    blockedFree = true;
                }
                if (!blockedFree) throw new Exception("Cenário 1 Falhou: Licença FREE não bloqueou criação de contrato.");
                logs.Add("   * Bloqueio para licença FREE: CONFIRMADO");

                // Ativa PRO e cria contrato
                await proSvc.SetPlanAsync(PlanType.Pro, "Test");
                var c1Res = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                {
                    ClientId = client.Id,
                    ContractNumber = "CTR-HOMOLOG-001",
                    PrincipalAmountCents = 10000, // R$ 100,00
                    TotalInterestCents = 3000,    // R$ 30,00
                    InstallmentsCount = 2,
                    FirstDueDate = DateTime.Today.AddDays(30),
                    ContractDate = DateTime.Today,
                    IdempotencyKey = "HOMOLOG-KEY-C1-PRO"
                });

                var c1Db = await fnCtx.CreditContracts.Include(c => c.Installments).FirstAsync(c => c.Id == c1Res.ContractId);
                logs.Add($"   * Contrato #{c1Db.ContractNumber} criado. Status: {c1Db.Status}. Principal: R$ {c1Db.DisbursedPrincipalCents / 100.0:F2}. Juros: R$ {c1Db.TotalNominalInterestCents / 100.0:F2}. Parcelas: {c1Db.Installments.Count}.");
                if (c1Db.Status != CreditContractStatus.Draft) throw new Exception("Cenário 1 Falhou: Contrato deveria nascer como Draft.");
                logs.Add("   * Cenário 1: PASS");

                // -------------------------------------------------------------
                // CENÁRIO 2: Registrar Desembolso
                // -------------------------------------------------------------
                logs.Add("--- CENÁRIO 2: Registrar desembolso ---");
                var disbRes = await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                {
                    ContractId = c1Db.Id,
                    DisbursementDate = DateTime.Today,
                    IdempotencyKey = "HOMOLOG-KEY-C2-DISB"
                });
                var c1Disbursed = await fnCtx.CreditContracts.FirstAsync(c => c.Id == c1Db.Id);
                logs.Add($"   * Contrato desembolsado. Novo status: {c1Disbursed.Status} (Esperado: Active). Montante: R$ {disbRes.DisbursedAmountCents / 100.0:F2}.");
                if (c1Disbursed.Status != CreditContractStatus.Active) throw new Exception("Cenário 2 Falhou: Contrato não transicionou para Active.");
                logs.Add("   * Cenário 2: PASS");

                // -------------------------------------------------------------
                // CENÁRIO 3: Registrar Pagamento Parcial (Regra A: Juros primeiro, Principal depois)
                // -------------------------------------------------------------
                logs.Add("--- CENÁRIO 3: Pagamento parcial com Regra A (juros primeiro, principal depois) ---");
                // Parcela 1 tem: Principal = 5.000 cents (R$ 50), Juros = 1.500 cents (R$ 15). Total = R$ 65,00.
                // Pagamos R$ 20,00 (2.000 cents) -> Juros: 1.500, Principal: 500.
                var payC3 = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                {
                    ContractId = c1Db.Id,
                    AmountCents = 2000,
                    PaymentDate = DateTime.Today,
                    PaymentMethod = PaymentMethod.Pix,
                    IdempotencyKey = "HOMOLOG-KEY-C3-PAY"
                });
                var allocC3 = await fnCtx.CreditPaymentInstallmentAllocs.FirstAsync(a => a.PaymentId == payC3.PaymentId);
                logs.Add($"   * Pagamento R$ 20,00 registrado. Alocado em juros: R$ {allocC3.InterestAllocated / 100.0:F2}. Alocado em principal: R$ {allocC3.PrincipalAllocated / 100.0:F2}.");
                if (allocC3.InterestAllocated != 1500 || allocC3.PrincipalAllocated != 500)
                    throw new Exception($"Cenário 3 Falhou: Regra A violada. J={allocC3.InterestAllocated}, P={allocC3.PrincipalAllocated}");
                logs.Add("   * Cenário 3: PASS");

                // -------------------------------------------------------------
                // CENÁRIO 4: Registrar Pagamento que Gera Excedente
                // -------------------------------------------------------------
                logs.Add("--- CENÁRIO 4: Pagamento que gera excedente ---");
                // Saldo restante da parcela 1: Principal restante = 4.500 cents (R$ 45,00). Juros restantes = 0.
                // Parcela 2 tem: Principal = 5.000 cents, Juros = 1.500 cents. Total = 6.500 cents.
                // Saldo devedor total do contrato = 4.500 + 6.500 = 11.000 cents (R$ 110,00).
                // Pagamos R$ 130,00 (13.000 cents) -> Quita parcela 1, quita parcela 2, sobra R$ 20,00 (2.000 cents) de excedente!
                var payC4 = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                {
                    ContractId = c1Db.Id,
                    AmountCents = 13000,
                    PaymentDate = DateTime.Today,
                    PaymentMethod = PaymentMethod.Pix,
                    IdempotencyKey = "HOMOLOG-KEY-C4-PAY"
                });
                logs.Add($"   * Pagamento R$ 130,00 registrado. Excedente gerado: R$ {payC4.SurplusGeneratedCents / 100.0:F2}.");
                if (payC4.SurplusGeneratedCents != 2000)
                    throw new Exception($"Cenário 4 Falhou: Excedente esperado de 2.000 cents, obtido: {payC4.SurplusGeneratedCents}");
                var surplusBolsa = await fnCtx.CreditSurplusBalances.FirstAsync(s => s.SourcePaymentId == payC4.PaymentId);
                logs.Add($"   * Bolsa de excedente criada: ID {surplusBolsa.Id}. Saldo disponível: R$ {surplusBolsa.RemainingSurplusCents / 100.0:F2}.");
                logs.Add("   * Cenário 4: PASS");

                // -------------------------------------------------------------
                // CENÁRIO 5: Consumir e Reverter Excedente
                // -------------------------------------------------------------
                logs.Add("--- CENÁRIO 5: Consumir e reverter excedente ---");
                // Adiciona parcela 3 de R$ 30,00 (J=1.000, P=2.000) no mesmo contrato
                var inst3 = new CreditInstallment
                {
                    Id = Guid.NewGuid().ToString(),
                    ContractId = c1Db.Id,
                    InstallmentNumber = 3,
                    DueDate = DateTime.Today.AddDays(90),
                    NominalPrincipalCents = 2000,
                    NominalInterestCents = 1000,
                    TotalAmountCents = 3000,
                    PaidAmountCents = 0,
                    Status = CreditInstallmentStatus.Pending,
                    CreatedAtUtc = DateTime.UtcNow
                };
                fnCtx.CreditInstallments.Add(inst3);
                var c1Loaded = await fnCtx.CreditContracts.FindAsync(c1Db.Id);
                c1Loaded!.DisbursedPrincipalCents += 2000;
                c1Loaded.TotalNominalInterestCents += 1000;
                c1Loaded.TotalAmountCents += 3000;
                c1Loaded.Status = CreditContractStatus.Active;
                await fnCtx.SaveChangesAsync();

                // Consome R$ 15,00 da bolsa (1.500 cents): Regra A aplica 1.000 juros e 500 principal
                var consRes = await paymentSvc.ConsumeSurplusAsync(new ConsumeSurplusRequestDto
                {
                    SurplusBalanceId = surplusBolsa.Id,
                    TargetInstallmentId = inst3.Id,
                    AmountCents = 1500,
                    IdempotencyKey = "HOMOLOG-KEY-C5-CONS"
                });
                logs.Add($"   * Consumo de R$ 15,00 realizado. Juros consumidos: R$ {consRes.InterestConsumedCents / 100.0:F2}. Principal: R$ {consRes.PrincipalConsumedCents / 100.0:F2}. Sobra na bolsa: R$ {consRes.RemainingSurplusCents / 100.0:F2}.");

                // Reverte o consumo de excedente
                var revRes = await paymentSvc.ReverseSurplusConsumptionAsync(new ReverseSurplusConsumptionRequestDto
                {
                    ConsumptionId = consRes.ConsumptionId,
                    Reason = "Reversão homologada de teste",
                    IdempotencyKey = "HOMOLOG-KEY-C5-REV"
                });
                logs.Add($"   * Reversão do consumo efetuada. Saldo restaurado na bolsa: R$ {revRes.RestoredSurplusBalanceCents / 100.0:F2}.");
                if (revRes.RestoredSurplusBalanceCents != 2000)
                    throw new Exception("Cenário 5 Falhou: Saldo do excedente não foi integralmente restaurado para 2.000 cents.");
                logs.Add("   * Cenário 5: PASS");

                // -------------------------------------------------------------
                // CENÁRIO 6 & 7: Autorizar Estorno, Cancelar Devolução e Restaurar Dívida
                // -------------------------------------------------------------
                logs.Add("--- CENÁRIO 6 & 7: Autorizar estorno e cancelar devolução antes da liquidação ---");
                // Criamos contrato e pagamento dedicados para estorno limpo
                var cClean = await contractSvc.CreateFixedContractAsync(new CreateContractRequestDto
                {
                    ClientId = client.Id,
                    ContractNumber = "CTR-HOMOLOG-REF-01",
                    PrincipalAmountCents = 8000,
                    TotalInterestCents = 2000,
                    InstallmentsCount = 1,
                    FirstDueDate = DateTime.Today.AddDays(30),
                    ContractDate = DateTime.Today,
                    IdempotencyKey = "HOMOLOG-KEY-C6-CTR"
                });
                await contractSvc.DisburseContractAsync(new DisburseContractRequestDto
                {
                    ContractId = cClean.ContractId,
                    DisbursementDate = DateTime.Today,
                    IdempotencyKey = "HOMOLOG-KEY-C6-DISB"
                });

                var payClean = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                {
                    ContractId = cClean.ContractId,
                    AmountCents = 5000,
                    PaymentDate = DateTime.Today,
                    PaymentMethod = PaymentMethod.Pix,
                    IdempotencyKey = "HOMOLOG-KEY-C6-PAY"
                });

                // Verifica estado da parcela antes do estorno: 5.000 pagos
                var instClean = await fnCtx.CreditInstallments.FirstAsync(i => i.ContractId == cClean.ContractId);
                logs.Add($"   * Parcela antes do estorno: PaidAmount = R$ {instClean.PaidAmountCents / 100.0:F2}. Status: {instClean.Status}.");

                // Autoriza estorno total
                var refClean = await refundSvc.AuthorizePaymentRefundAsync(new AuthorizeRefundRequestDto
                {
                    PaymentId = payClean.PaymentId,
                    Reason = "Homologação de estorno",
                    IdempotencyKey = "HOMOLOG-KEY-C6-REF-AUTH"
                });
                logs.Add($"   * Estorno autorizado. RefundId: {refClean.RefundId}. Status: {refClean.Status}. Devolução criada: {refClean.DevolutionId}.");

                // Verifica que a dívida reabriu (PaidAmount = 0, Status = Pending)
                var instReopened = await fnCtx.CreditInstallments.FirstAsync(i => i.Id == instClean.Id);
                logs.Add($"   * Parcela após autorização do estorno: PaidAmount = R$ {instReopened.PaidAmountCents / 100.0:F2}. Status: {instReopened.Status} (Esperado: Pending).");
                if (instReopened.PaidAmountCents != 0 || instReopened.Status != CreditInstallmentStatus.Pending)
                    throw new Exception("Cenário 6 Falhou: Parcela não reabriu para 0 pagos.");

                // Cancela a autorização da devolução antes da liquidação
                var cancelDevRes = await refundSvc.CancelDevolutionAsync(new CancelDevolutionRequestDto
                {
                    DevolutionId = refClean.DevolutionId,
                    Reason = "Cancelamento homologado da devolução",
                    IdempotencyKey = "HOMOLOG-KEY-C7-CANCEL"
                });
                logs.Add($"   * Devolução cancelada. Status: {cancelDevRes.Status}.");

                // CENÁRIO 7: Confirma que a dívida e alocações originais foram restauradas exatamente
                var instRestored = await fnCtx.CreditInstallments.FirstAsync(i => i.Id == instClean.Id);
                logs.Add($"   * Parcela após cancelamento da devolução: PaidAmount = R$ {instRestored.PaidAmountCents / 100.0:F2}. Status: {instRestored.Status} (Esperado: 50.00 e PartiallyPaid).");
                if (instRestored.PaidAmountCents != 5000 || instRestored.Status != CreditInstallmentStatus.PartiallyPaid)
                    throw new Exception("Cenário 7 Falhou: Restauração exata das alocações falhou.");
                logs.Add("   * Cenário 6 & 7: PASS");

                // -------------------------------------------------------------
                // CENÁRIO 8: Autorizar e Liquidar Devolução (Única Saída de Caixa)
                // -------------------------------------------------------------
                logs.Add("--- CENÁRIO 8: Autorizar e liquidar devolução com saída única de caixa ---");
                // Criamos pagamento de R$ 30,00 e autorizamos devolução parcial de R$ 10,00
                var payDev = await paymentSvc.RegisterPaymentAsync(new PaymentRequestDto
                {
                    ContractId = cClean.ContractId,
                    AmountCents = 3000,
                    PaymentDate = DateTime.Today,
                    PaymentMethod = PaymentMethod.Pix,
                    IdempotencyKey = "HOMOLOG-KEY-C8-PAY"
                });

                int preOutflows = await fnCtx.CreditCashOutflows.CountAsync();

                var authDev = await refundSvc.AuthorizeDevolutionAsync(new AuthorizeDevolutionRequestDto
                {
                    PaymentId = payDev.PaymentId,
                    AmountCents = 1000,
                    Reason = "Devolução parcial para liquidação",
                    IdempotencyKey = "HOMOLOG-KEY-C8-DEV"
                });

                // Verifica que a autorização NÃO movimentou caixa
                int midOutflows = await fnCtx.CreditCashOutflows.CountAsync();
                if (midOutflows != preOutflows)
                    throw new Exception("Cenário 8 Falhou: Autorização de devolução movimentou caixa indevidamente.");
                logs.Add("   * Autorização de devolução concluída sem movimentação de caixa.");

                // Liquidação da devolução
                var settleDev = await refundSvc.SettleDevolutionAsync(new SettleDevolutionRequestDto
                {
                    DevolutionId = authDev.DevolutionId,
                    SettlementDate = DateTime.Today,
                    PaymentMethod = PaymentMethod.Pix,
                    IdempotencyKey = "HOMOLOG-KEY-C8-SETTLE"
                });
                logs.Add($"   * Devolução liquidada. Status: {settleDev.Status}. Saída de caixa ID: {settleDev.CashOutflowId}.");

                int postOutflows = await fnCtx.CreditCashOutflows.CountAsync();
                if (postOutflows != preOutflows + 1)
                    throw new Exception("Cenário 8 Falhou: Não foi gerada exatamente uma saída de caixa.");

                var outflowDb = await fnCtx.CreditCashOutflows.FirstAsync(o => o.DevolutionId == authDev.DevolutionId);
                logs.Add($"   * Saída de caixa conferida: R$ {outflowDb.AmountCents / 100.0:F2}. Data: {outflowDb.OutflowDate:yyyy-MM-dd}.");
                logs.Add("   * Cenário 8: PASS");

                // -------------------------------------------------------------
                // CENÁRIO 9: Bloquear Devoluções Acima do Valor Elegível
                // -------------------------------------------------------------
                logs.Add("--- CENÁRIO 9: Bloqueio de devoluções acima do valor elegível ---");
                // O pagamento payDev era de R$ 30,00 (3.000 cents). Já devolveu 1.000 cents. Saldo elegível = 2.000 cents.
                // Tentativa de devolver 2.001 cents deve ser barrada
                bool blockedOver = false;
                try
                {
                    await refundSvc.AuthorizeDevolutionAsync(new AuthorizeDevolutionRequestDto
                    {
                        PaymentId = payDev.PaymentId,
                        AmountCents = 2001,
                        Reason = "Tentativa de estourar limite elegível",
                        IdempotencyKey = "HOMOLOG-KEY-C9-OVER"
                    });
                }
                catch (ExceededEligibleDevolutionAmountException)
                {
                    blockedOver = true;
                }
                if (!blockedOver) throw new Exception("Cenário 9 Falhou: Devolução acima do saldo elegível não foi bloqueada.");
                logs.Add("   * Tentativa de devolver valor superior ao saldo elegível: BLOQUEADA COM SUCESSO");
                logs.Add("   * Cenário 9: PASS");
            }

            // -------------------------------------------------------------
            // CENÁRIO 10: Reiniciar Contexto e Confirmar Persistência em Disco
            // -------------------------------------------------------------
            logs.Add("--- CENÁRIO 10: Reinicialização e persistência em disco ---");
            // Limpa pools do SQLite para assegurar gravação em disco
            SqliteConnection.ClearAllPools();

            using (var reloadedCtx = new CobraPixDbContext(copyOptions))
            {
                var contractCount = await reloadedCtx.CreditContracts.CountAsync();
                var paymentCount = await reloadedCtx.CreditPayments.CountAsync();
                var devolutionsCount = await reloadedCtx.CreditDevolutions.CountAsync();
                var outflowCount = await reloadedCtx.CreditCashOutflows.CountAsync();

                logs.Add($"   * Dados recarregados do arquivo SQLite em disco:");
                logs.Add($"     - Contratos persistidos: {contractCount}");
                logs.Add($"     - Pagamentos persistidos: {paymentCount}");
                logs.Add($"     - Devoluções persistidas: {devolutionsCount}");
                logs.Add($"     - Saídas de caixa persistidas: {outflowCount}");

                if (contractCount < 2 || paymentCount < 3 || devolutionsCount < 2 || outflowCount < 1)
                    throw new Exception("Cenário 10 Falhou: Dados não persistiram corretamente no arquivo SQLite.");
                logs.Add("   * Cenário 10: PASS");
            }

            // ──────────────────────────────────────────────────────────
            // 5. COMPROVAÇÃO DE INVIOLABILIDADE DA BASE ORIGINAL
            // ──────────────────────────────────────────────────────────
            logs.Add("------------------------------------------------------------------");
            logs.Add("5. COMPROVAÇÃO DE INVIOLABILIDADE DA BASE ORIGINAL DE PRODUÇÃO");
            logs.Add("------------------------------------------------------------------");

            long origSizeAfter = new FileInfo(prodDbPath).Length;
            string origSha256After = ComputeFileSha256(prodDbPath);

            logs.Add($"   * Banco Original: {prodDbPath}");
            logs.Add($"   * SHA-256 Inicial: {origSha256Before}");
            logs.Add($"   * SHA-256 Final:   {origSha256After}");
            logs.Add($"   * Tamanho Inicial: {origSizeBefore} bytes");
            logs.Add($"   * Tamanho Final:   {origSizeAfter} bytes");

            if (origSha256Before != origSha256After || origSizeBefore != origSizeAfter)
            {
                throw new InvalidOperationException("FALHA GRAVE: A BASE DE PRODUÇÃO FOI ALTERADA!");
            }

            logs.Add("   * INVIOLABILIDADE CONFIRMADA: 100% IDÊNTICA BYTE A BYTE.");
            logs.Add("==================================================================");
            logs.Add("HOMOLOGAÇÃO CONTROLADA CONCLUÍDA COM SUCESSO: APROVADO PARA PLANEJAR A IMPLANTAÇÃO");
            logs.Add("==================================================================");

            return logs;
        }

        private static string ComputeFileSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        }
    }
}
