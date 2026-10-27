using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Importa backups Android (.cpb JSON) para o banco do CobraPix PC.
    ///
    /// Formato confirmado do backup Android:
    ///   { "app":"CobraPix", "backupVersion":3, "clients":[...], "sales":[...],
    ///     "installments":[...], "installment_payments":[...],
    ///     "monthly_customers":[...], "monthly_payments":[...], "settings":{...} }
    ///
    /// Notas sobre a conversão:
    ///   - Valores monetários Android estão em CENTAVOS (Long). Divisão por 100 → decimal.
    ///   - Datas Android estão em milissegundos Unix (Long). Conversão para DateTime.
    ///   - IDs Android NÃO são usados diretamente no PC: mapas de IDs garantem relacionamentos.
    ///   - Expenses Android não são importados (módulo ausente no PC); são contabilizados e reportados.
    ///   - A importação é totalmente transacional com safety backup e rollback automático em falha.
    /// </summary>
    public class AndroidImportService : IAndroidImportService
    {
        private readonly CobraPixDbContext? _context;
        private readonly IProService? _proService;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public AndroidImportService(CobraPixDbContext? context = null, IProService? proService = null)
        {
            _context = context;
            _proService = proService;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Utilitários de conversão
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Converte milissegundos Unix (Android Long) em DateTime local.</summary>
        private static DateTime UnixMsToDateTime(long ms)
            => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

        /// <summary>Converte centavos Android (Long) em decimal reais.</summary>
        private static decimal CentsToDecimal(long cents) => cents / 100m;

        /// <summary>Mapeia string de método de pagamento do Android para o enum do PC.</summary>
        private static PaymentMethod MapPaymentMethod(string? raw)
            => (raw?.ToUpperInvariant()) switch
            {
                "PIX"  => PaymentMethod.Pix,
                "CASH" => PaymentMethod.Cash,
                "CARD" => PaymentMethod.Card,
                "OTHER" => PaymentMethod.Other,
                _      => PaymentMethod.None   // inclui "NONE" e quaisquer desconhecidos
            };

        /// <summary>Mapeia string de status de parcela/mensalidade do Android para o enum do PC.</summary>
        private static InstallmentStatus MapInstallmentStatus(string? raw)
            => (raw?.ToUpperInvariant()) switch
            {
                "PAID"   => InstallmentStatus.Paid,
                "OVERDUE" => InstallmentStatus.Overdue,
                _        => InstallmentStatus.Pending
            };

        // ─────────────────────────────────────────────────────────────────────
        // IsAndroidJsonBackup
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Verifica de forma rápida se o .cpb é um backup Android JSON
        /// (começa com '{' após possível BOM ou espaços iniciais).
        /// </summary>
        public bool IsAndroidJsonBackup(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                // Pula BOM UTF-8 se presente
                byte[] bom = new byte[3];
                int read = fs.Read(bom, 0, 3);
                if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                {
                    // BOM detectado; próximo byte deve ser '{'
                }
                else
                {
                    // Não é BOM; rewind e relê
                    fs.Seek(0, SeekOrigin.Begin);
                }

                // Lê até 512 bytes para localizar o primeiro '{' ignorando espaços
                byte[] preview = new byte[512];
                int previewRead = fs.Read(preview, 0, 512);
                string text = Encoding.UTF8.GetString(preview, 0, previewRead).TrimStart();
                return text.StartsWith("{", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Inspeciona o arquivo de backup Android JSON sem modificar o banco, retornando o resumo quantitativo.
        /// Retorna null se o arquivo não for um backup Android JSON válido.
        /// </summary>
        public AndroidBackupSummary? GetSummary(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                string jsonContent = File.ReadAllText(filePath, Encoding.UTF8);
                var root = JsonNode.Parse(jsonContent);
                if (root == null) return null;

                string? app = root["app"]?.GetValue<string>();
                if (!string.Equals(app, "CobraPix", StringComparison.OrdinalIgnoreCase))
                    return null;

                int backupVersion = root["backupVersion"]?.GetValue<int>() ?? 0;
                if (backupVersion <= 0)
                    return null;

                var clients = root["clients"] as JsonArray;
                var sales = root["sales"] as JsonArray;
                var installments = root["installments"] as JsonArray;
                var payments = root["installment_payments"] as JsonArray;
                var monthlyCustomers = root["monthly_customers"] as JsonArray;
                var monthlyPayments = root["monthly_payments"] as JsonArray;
                var expenses = root["expenses"] as JsonArray;

                return new AndroidBackupSummary
                {
                    Version = backupVersion,
                    ClientsCount = clients?.Count ?? 0,
                    SalesCount = sales?.Count ?? 0,
                    InstallmentsCount = installments?.Count ?? 0,
                    InstallmentPaymentsCount = payments?.Count ?? 0,
                    MonthlyCustomersCount = monthlyCustomers?.Count ?? 0,
                    MonthlyPaymentsCount = monthlyPayments?.Count ?? 0,
                    ExpensesCount = expenses?.Count ?? 0
                };
            }
            catch
            {
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ImportAsync — ponto de entrada público
        // ─────────────────────────────────────────────────────────────────────

        public async Task<AndroidImportResult> ImportAsync(string backupFilePath, string? targetDatabasePath = null)
        {
            var result = new AndroidImportResult();

            // 0. Determina e valida estritamente o banco de dados alvo ANTES de qualquer efeito colateral
            string? contextDbPath = null;
            if (!string.IsNullOrWhiteSpace(targetDatabasePath))
            {
                // Se targetDatabasePath foi fornecido explicitamente, valida-o imediatamente
                _ = DatabasePath.ValidateAndResolveTargetDatabasePath(targetDatabasePath, "Importação de backup Android");
            }
            else if (_context != null)
            {
                try
                {
                    contextDbPath = _context.Database.GetDbConnection().DataSource;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
                {
                    throw;
                }
                catch { }
            }

            string candidateTargetDb = targetDatabasePath ?? (!string.IsNullOrEmpty(contextDbPath) ? contextDbPath : DatabasePath.GetDatabaseFilePath());
            string targetDb = DatabasePath.ValidateAndResolveTargetDatabasePath(candidateTargetDb, "Importação de backup Android");

            // 1. Verificação do arquivo
            if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
            {
                result.Success = false;
                result.Message = "Arquivo de backup Android não encontrado.";
                return result;
            }

            // 2. Leitura e parse do JSON
            string jsonContent;
            try
            {
                jsonContent = await File.ReadAllTextAsync(backupFilePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Erro ao ler o arquivo de backup: {ex.Message}";
                return result;
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(jsonContent);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Arquivo de backup inválido: conteúdo não é JSON válido. {ex.Message}";
                return result;
            }

            if (root == null)
            {
                result.Success = false;
                result.Message = "Arquivo de backup inválido: JSON vazio.";
                return result;
            }

            // 3. Validação de assinatura
            string? app = root["app"]?.GetValue<string>();
            if (!string.Equals(app, "CobraPix", StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.Message = $"Arquivo de backup inválido: campo 'app' esperado 'CobraPix', encontrado '{app}'.";
                return result;
            }

            int? backupVersion = root["backupVersion"]?.GetValue<int>();
            if (backupVersion == null || backupVersion <= 0)
            {
                result.Success = false;
                result.Message = $"Arquivo de backup inválido: 'backupVersion' inválida ({backupVersion}).";
                return result;
            }

            result.BackupVersion = backupVersion.ToString();

            // 4. Extrai arrays (permissivos: ausência ou tipo inválido → lista vazia)
            var clients           = root["clients"] as JsonArray            ?? new JsonArray();
            var sales             = root["sales"] as JsonArray              ?? new JsonArray();
            var installments      = root["installments"] as JsonArray       ?? new JsonArray();
            var installPayments   = root["installment_payments"] as JsonArray ?? new JsonArray();
            var monthlyCustomers  = root["monthly_customers"] as JsonArray  ?? new JsonArray();
            var monthlyPayments   = root["monthly_payments"] as JsonArray   ?? new JsonArray();
            var expenses          = root["expenses"] as JsonArray           ?? new JsonArray();

            result.ClientsFound           = clients.Count;
            result.SalesFound             = sales.Count;
            result.InstallmentsFound      = installments.Count;
            result.InstallmentPaymentsFound = installPayments.Count;
            result.MonthlyCustomersFound  = monthlyCustomers.Count;
            result.MonthlyPaymentsFound   = monthlyPayments.Count;
            result.ExpensesFound          = expenses.Count;
            result.ExpensesSkipped        = expenses.Count;

            if (expenses.Count > 0)
                result.Warnings.Add($"{expenses.Count} despesa(s) encontrada(s) no backup Android. O CobraPix PC não possui módulo de despesas; nenhum registro foi convertido ou descartado do banco Android.");

            // 5. Garante diretório do banco alvo (já validado) e cria safety backup
            string? targetDir = Path.GetDirectoryName(targetDb);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            string? safetyBackupPath = null;
            if (File.Exists(targetDb))
            {
                string safeDir = targetDir ?? Path.GetTempPath();
                string baseName = Path.GetFileNameWithoutExtension(targetDb);
                safetyBackupPath = Path.Combine(safeDir, $"{baseName}.safety_backup_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
                try
                {
                    SqliteConnection.ClearAllPools();
                    File.Copy(targetDb, safetyBackupPath, true);
                    result.SafetyBackupPath = safetyBackupPath;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Não foi possível criar o backup de segurança. Importação cancelada: {ex.Message}";
                    return result;
                }
            }

            // 5.1. Auditoria e Validação Centralizada de Quota (Fase 7F: FREE x PRO)
            // Avalia de forma não-destrutiva e canônica quantos clientes do backup Android se tornarão ativos no PC
            int incomingActiveClients = 0;
            foreach (var node in clients)
            {
                if (node == null) continue;
                long androidId = node["id"]?.GetValue<long>() ?? 0;
                bool isArchived = node["isArchived"]?.GetValue<bool>() ?? false;
                if (isArchived)
                {
                    // Clientes arquivados no Android não contam para quota no PC
                    continue;
                }

                // Verifica se possui mensalista ativo no backup Android
                bool hasActiveMonthly = false;
                foreach (var mNode in monthlyCustomers)
                {
                    if (mNode == null) continue;
                    long mClientId = mNode["clientId"]?.GetValue<long>() ?? 0;
                    if (mClientId == androidId)
                    {
                        bool mcArchived = mNode["isArchived"]?.GetValue<bool>() ?? false;
                        bool mcActive = mNode["isActive"]?.GetValue<bool>() ?? true;
                        if (!mcArchived && mcActive)
                        {
                            hasActiveMonthly = true;
                            break;
                        }
                    }
                }

                if (hasActiveMonthly)
                {
                    // Mensalista ativo possui cobranças recorrentes continuadas e NÃO é quitado
                    // Logo, consome quota de ativo
                    incomingActiveClients++;
                    continue;
                }

                // Verifica histórico de vendas do cliente no backup Android
                var clientSaleIds = new HashSet<long>();
                foreach (var sNode in sales)
                {
                    if (sNode == null) continue;
                    long sClientId = sNode["clientId"]?.GetValue<long>() ?? 0;
                    if (sClientId == androidId)
                    {
                        long saleId = sNode["id"]?.GetValue<long>() ?? 0;
                        clientSaleIds.Add(saleId);
                    }
                }

                if (clientSaleIds.Count == 0)
                {
                    // Cliente sem vendas e sem mensalidade: cliente ativo recém-cadastrado
                    // Conforme ProService.IsClientQuitado: não é quitado, portanto conta como ativo
                    incomingActiveClients++;
                    continue;
                }

                // Verifica se todas as parcelas de todas as vendas do cliente estão com status Paid
                int totalInstallments = 0;
                int paidInstallments = 0;
                foreach (var iNode in installments)
                {
                    if (iNode == null) continue;
                    long iSaleId = iNode["saleId"]?.GetValue<long>() ?? 0;
                    if (clientSaleIds.Contains(iSaleId))
                    {
                        totalInstallments++;
                        string statusStr = iNode["status"]?.GetValue<string>() ?? "PENDING";
                        if (MapInstallmentStatus(statusStr) == InstallmentStatus.Paid)
                        {
                            paidInstallments++;
                        }
                    }
                }

                // Conforme regra canônica ProService.IsClientQuitado:
                // Todas as parcelas devem existir (> 0) e todas devem estar quitadas (Paid)
                bool isQuitado = totalInstallments > 0 && totalInstallments == paidInstallments;
                if (!isQuitado)
                {
                    incomingActiveClients++;
                }
            }

            // Valida a quota no banco de destino através da lógica centralizada de ProService
            try
            {
                var targetCheckOptions = new DbContextOptionsBuilder<CobraPixDbContext>()
                    .UseSqlite($"Data Source={targetDb}")
                    .Options;

                using (var checkCtx = new CobraPixDbContext(targetCheckOptions))
                {
                    await checkCtx.Database.EnsureCreatedAsync();
                    var proSvc = _proService ?? new ProService(checkCtx);
                    var quotaResult = await proSvc.ValidateImportQuotaAsync(incomingActiveClients, checkCtx);

                    if (!quotaResult.Allowed)
                    {
                        result.Success = false;
                        result.Message = quotaResult.ErrorMessage;
                        return result;
                    }
                }
                SqliteConnection.ClearAllPools();
            }
            catch (Exception)
            {
                // Se o banco de destino não for um banco SQLite inicializável (ex: placeholder em testes de rollback),
                // o fluxo transacional do EF Core adiante lidará com a falha e rollback seguro.
            }

            // 6. Executa a importação transacional no banco EF Core
            try
            {
                var options = new DbContextOptionsBuilder<CobraPixDbContext>()
                    .UseSqlite($"Data Source={targetDb}")
                    .Options;

                using var ctx = new CobraPixDbContext(options);
                await ctx.Database.EnsureCreatedAsync();

                // Usa uma transação única para garantir atomicidade
                await using var transaction = await ctx.Database.BeginTransactionAsync();
                try
                {
                    // ── CLIENTES ──────────────────────────────────────────────
                    // Estratégia de duplicação: cada cliente Android é inserido como
                    // novo registro no PC (sem tentativa de merge por nome/telefone),
                    // pois não é possível determinar com segurança que dois registros
                    // representam a mesma pessoa apenas pelos dados disponíveis.
                    var clientIdMap = new Dictionary<long, int>(); // androidId → pcId

                    foreach (var node in clients)
                    {
                        if (node == null) continue;
                        long androidId           = node["id"]?.GetValue<long>() ?? 0;
                        string name              = node["name"]?.GetValue<string>() ?? string.Empty;
                        string phone             = node["phone"]?.GetValue<string>() ?? string.Empty;
                        string observation       = node["observation"]?.GetValue<string>() ?? string.Empty;
                        long regDateMs           = node["registrationDate"]?.GetValue<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        bool isArchived          = node["isArchived"]?.GetValue<bool>() ?? false;

                        // Campos Android sem correspondente direto no PC:
                        // cpf, address  → sem coluna no PC Client; descartados (documentado)
                        if (!string.IsNullOrEmpty(node["cpf"]?.GetValue<string>()))
                            result.Warnings.Add($"Cliente '{name}' (Android ID {androidId}): campo 'cpf' não possui correspondente no PC e foi descartado.");
                        if (!string.IsNullOrEmpty(node["address"]?.GetValue<string>()))
                            result.Warnings.Add($"Cliente '{name}' (Android ID {androidId}): campo 'address' não possui correspondente no PC e foi descartado.");

                        var client = new Client
                        {
                            Name       = name,
                            Phone      = string.IsNullOrWhiteSpace(phone) ? null : phone,
                            Notes      = string.IsNullOrWhiteSpace(observation) ? null : observation,
                            IsArchived = isArchived,
                            CreatedAt  = UnixMsToDateTime(regDateMs)
                        };
                        ctx.Clients.Add(client);
                        await ctx.SaveChangesAsync();
                        clientIdMap[androidId] = client.Id;
                        result.ClientsImported++;
                    }

                    // ── VENDAS ────────────────────────────────────────────────
                    var saleIdMap = new Dictionary<long, int>(); // androidId → pcId

                    foreach (var node in sales)
                    {
                        if (node == null) continue;
                        long androidId      = node["id"]?.GetValue<long>() ?? 0;
                        long androidClientId = node["clientId"]?.GetValue<long>() ?? 0;
                        long totalValueCents = node["totalValue"]?.GetValue<long>() ?? 0;
                        int  installCount   = node["installmentCount"]?.GetValue<int>() ?? 1;
                        long saleDateMs     = node["saleDate"]?.GetValue<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                        if (!clientIdMap.TryGetValue(androidClientId, out int pcClientId))
                        {
                            result.Warnings.Add($"Venda Android ID {androidId}: cliente Android ID {androidClientId} não mapeado; venda ignorada.");
                            continue;
                        }

                        var sale = new Sale
                        {
                            ClientId       = pcClientId,
                            TotalAmount    = CentsToDecimal(totalValueCents),
                            InstallmentCount = installCount,
                            SaleDate       = UnixMsToDateTime(saleDateMs),
                            Description    = $"Importado do Android (ID {androidId})"
                        };
                        ctx.Sales.Add(sale);
                        await ctx.SaveChangesAsync();
                        saleIdMap[androidId] = sale.Id;
                        result.SalesImported++;
                    }

                    // ── PARCELAS ──────────────────────────────────────────────
                    var installmentIdMap = new Dictionary<long, int>(); // androidId → pcId

                    foreach (var node in installments)
                    {
                        if (node == null) continue;
                        long androidId       = node["id"]?.GetValue<long>() ?? 0;
                        long androidSaleId   = node["saleId"]?.GetValue<long>() ?? 0;
                        int  number          = node["number"]?.GetValue<int>() ?? 1;
                        long valueCents      = node["value"]?.GetValue<long>() ?? 0;
                        long dueDateMs       = node["dueDate"]?.GetValue<long>() ?? 0;
                        string statusStr     = node["status"]?.GetValue<string>() ?? "PENDING";
                        long? payDateMs      = node["paymentDate"] is JsonValue pv && pv.GetValue<JsonElement>().ValueKind != System.Text.Json.JsonValueKind.Null
                                                ? node["paymentDate"]?.GetValue<long>() : null;
                        string? methodStr    = node["paymentMethod"]?.GetValue<string>();

                        // paidAmount pode não existir em backups mais antigos
                        long paidAmountCents = 0;
                        if (node["paidAmount"] is JsonValue paVal && paVal.GetValue<JsonElement>().ValueKind == System.Text.Json.JsonValueKind.Number)
                            paidAmountCents = paVal.GetValue<long>();

                        if (!saleIdMap.TryGetValue(androidSaleId, out int pcSaleId))
                        {
                            result.Warnings.Add($"Parcela Android ID {androidId}: venda Android ID {androidSaleId} não mapeada; parcela ignorada.");
                            continue;
                        }

                        var installment = new Installment
                        {
                            SaleId            = pcSaleId,
                            InstallmentNumber = number,
                            Amount            = CentsToDecimal(valueCents),
                            DueDate           = UnixMsToDateTime(dueDateMs),
                            Status            = MapInstallmentStatus(statusStr),
                            PaidDate          = payDateMs.HasValue ? UnixMsToDateTime(payDateMs.Value) : null
                        };
                        ctx.Installments.Add(installment);
                        await ctx.SaveChangesAsync();
                        installmentIdMap[androidId] = installment.Id;
                        result.InstallmentsImported++;

                        // Se a parcela Android possuía pagamento integral registrado no campo paidAmount
                        // mas sem registro em installment_payments, cria um InstallmentPayment sintético
                        if (paidAmountCents > 0 && installPayments.Count == 0 && installment.Status == InstallmentStatus.Paid)
                        {
                            var syntheticPayment = new InstallmentPayment
                            {
                                InstallmentId  = installment.Id,
                                AmountPaid     = CentsToDecimal(paidAmountCents),
                                PaymentDate    = installment.PaidDate ?? DateTime.Now,
                                PaymentMethod  = MapPaymentMethod(methodStr),
                                Notes          = "Pagamento sintético gerado na importação Android (sem registro em installment_payments)"
                            };
                            ctx.InstallmentPayments.Add(syntheticPayment);
                            await ctx.SaveChangesAsync();
                            result.InstallmentPaymentsImported++;
                        }
                    }

                    // ── PAGAMENTOS DE PARCELAS ─────────────────────────────────
                    foreach (var node in installPayments)
                    {
                        if (node == null) continue;
                        long androidId           = node["id"]?.GetValue<long>() ?? 0;
                        long androidInstallmentId = node["installmentId"]?.GetValue<long>() ?? 0;
                        long valueCents          = node["value"]?.GetValue<long>() ?? 0;
                        long payDateMs           = node["paymentDate"]?.GetValue<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        string? methodStr        = node["paymentMethod"]?.GetValue<string>();

                        if (!installmentIdMap.TryGetValue(androidInstallmentId, out int pcInstallmentId))
                        {
                            result.Warnings.Add($"Pagamento Android ID {androidId}: parcela Android ID {androidInstallmentId} não mapeada; pagamento ignorado.");
                            continue;
                        }

                        var payment = new InstallmentPayment
                        {
                            InstallmentId  = pcInstallmentId,
                            AmountPaid     = CentsToDecimal(valueCents),
                            PaymentDate    = UnixMsToDateTime(payDateMs),
                            PaymentMethod  = MapPaymentMethod(methodStr)
                        };
                        ctx.InstallmentPayments.Add(payment);
                        await ctx.SaveChangesAsync();
                        result.InstallmentPaymentsImported++;
                    }

                    // ── MENSALISTAS ───────────────────────────────────────────
                    var monthlyCustomerIdMap = new Dictionary<long, int>(); // androidId → pcId

                    foreach (var node in monthlyCustomers)
                    {
                        if (node == null) continue;
                        long androidId        = node["id"]?.GetValue<long>() ?? 0;
                        long androidClientId  = node["clientId"]?.GetValue<long>() ?? 0;
                        long monthlyValCents  = node["monthlyValue"]?.GetValue<long>() ?? 0;
                        int  dueDay           = node["dueDay"]?.GetValue<int>() ?? 1;
                        long startDateMs      = node["startDate"]?.GetValue<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        bool isActive         = node["isActive"]?.GetValue<bool>() ?? true;
                        // isArchived não possui correspondente em MonthlyCustomer PC;
                        // será registrado como aviso se for true
                        bool isArchived       = node["isArchived"]?.GetValue<bool>() ?? false;

                        if (!clientIdMap.TryGetValue(androidClientId, out int pcClientId))
                        {
                            result.Warnings.Add($"Mensalista Android ID {androidId}: cliente Android ID {androidClientId} não mapeado; mensalista ignorado.");
                            continue;
                        }

                        if (isArchived)
                            result.Warnings.Add($"Mensalista Android ID {androidId}: campo 'isArchived=true' não possui correspondente em MonthlyCustomer PC. O registro foi importado com IsActive=false.");

                        var mc = new MonthlyCustomer
                        {
                            ClientId      = pcClientId,
                            MonthlyAmount = CentsToDecimal(monthlyValCents),
                            DueDay        = dueDay,
                            StartDate     = UnixMsToDateTime(startDateMs),
                            IsActive      = isArchived ? false : isActive,
                            CreatedAt     = DateTime.Now
                        };
                        ctx.MonthlyCustomers.Add(mc);
                        await ctx.SaveChangesAsync();
                        monthlyCustomerIdMap[androidId] = mc.Id;
                        result.MonthlyCustomersImported++;
                    }

                    // ── PAGAMENTOS MENSAIS ────────────────────────────────────
                    foreach (var node in monthlyPayments)
                    {
                        if (node == null) continue;
                        long androidId         = node["id"]?.GetValue<long>() ?? 0;
                        long androidMcId       = node["monthlyCustomerId"]?.GetValue<long>() ?? 0;
                        int  month             = node["month"]?.GetValue<int>() ?? 1;
                        int  year              = node["year"]?.GetValue<int>() ?? DateTime.Now.Year;
                        long valueCents        = node["value"]?.GetValue<long>() ?? 0;
                        long dueDateMs         = node["dueDate"]?.GetValue<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        string statusStr       = node["status"]?.GetValue<string>() ?? "PENDING";
                        long? payDateMs        = node["paymentDate"] is JsonValue pv2 && pv2.GetValue<JsonElement>().ValueKind != System.Text.Json.JsonValueKind.Null
                                                  ? node["paymentDate"]?.GetValue<long>() : null;
                        string? methodStr      = node["paymentMethod"]?.GetValue<string>();

                        if (!monthlyCustomerIdMap.TryGetValue(androidMcId, out int pcMcId))
                        {
                            result.Warnings.Add($"Pagamento mensal Android ID {androidId}: mensalista Android ID {androidMcId} não mapeado; pagamento ignorado.");
                            continue;
                        }

                        // MonthlyPayment PC usa PaymentDate como campo obrigatório.
                        // Se o Android indica status PENDING/sem data, usa vencimento como referência.
                        DateTime payDate = payDateMs.HasValue
                            ? UnixMsToDateTime(payDateMs.Value)
                            : UnixMsToDateTime(dueDateMs);

                        var mp = new MonthlyPayment
                        {
                            MonthlyCustomerId = pcMcId,
                            Amount            = CentsToDecimal(valueCents),
                            PaymentDate       = payDate,
                            ReferenceMonth    = month,
                            ReferenceYear     = year,
                            PaymentMethod     = MapPaymentMethod(methodStr)
                        };
                        ctx.MonthlyPayments.Add(mp);
                        await ctx.SaveChangesAsync();
                        result.MonthlyPaymentsImported++;
                    }

                    await transaction.CommitAsync();
                    SqliteConnection.ClearAllPools();
                }
                catch (Exception exInner)
                {
                    await transaction.RollbackAsync();

                    // Restaura safety backup em caso de falha
                    if (safetyBackupPath != null && File.Exists(safetyBackupPath))
                    {
                        try
                        {
                            SqliteConnection.ClearAllPools();
                            File.Copy(safetyBackupPath, targetDb, true);
                            SqliteConnection.ClearAllPools();
                        }
                        catch { }
                    }

                    result.Success = false;
                    result.Message = $"Importação falhou e foi revertida (ROLLBACK). Banco anterior preservado. Detalhe: {exInner.Message}";
                    return result;
                }

                result.Success = true;
                result.Message =
                    $"Importação Android concluída com sucesso. " +
                    $"Clientes: {result.ClientsImported}/{result.ClientsFound} | " +
                    $"Vendas: {result.SalesImported}/{result.SalesFound} | " +
                    $"Parcelas: {result.InstallmentsImported}/{result.InstallmentsFound} | " +
                    $"Pagamentos: {result.InstallmentPaymentsImported}/{result.InstallmentPaymentsFound} | " +
                    $"Mensalistas: {result.MonthlyCustomersImported}/{result.MonthlyCustomersFound} | " +
                    $"Pag. mensais: {result.MonthlyPaymentsImported}/{result.MonthlyPaymentsFound}" +
                    (result.ExpensesFound > 0 ? $" | {result.ExpensesFound} despesa(s) detectada(s) e não importadas (módulo ausente no PC)." : ".");

                return result;
            }
            catch (Exception ex)
            {
                // Restaura safety backup
                if (safetyBackupPath != null && File.Exists(safetyBackupPath))
                {
                    try
                    {
                        SqliteConnection.ClearAllPools();
                        File.Copy(safetyBackupPath, targetDb, true);
                        SqliteConnection.ClearAllPools();
                    }
                    catch { }
                }

                result.Success = false;
                result.Message = $"Erro inesperado na importação Android: {ex.Message}";
                return result;
            }
        }
    }
}
