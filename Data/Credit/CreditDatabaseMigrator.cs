using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CobraPix_PC.Data.Credit
{
    /// <summary>
    /// Gerenciador de migração segura e versionada do módulo Crédito & Giro.
    /// Opera de forma estritamente aditiva e não-destrutiva, preservando 100% das tabelas e dados legados.
    /// Garante que PRAGMA foreign_keys = ON seja ativado em cada conexão.
    /// </summary>
    public static class CreditDatabaseMigrator
    {
        public const int CurrentCreditSchemaVersion = 3;

        public static async Task ApplyCreditMigrationsAsync(CobraPixDbContext dbContext)
        {
            var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
            string activeDbPath = connection.DataSource ?? DatabasePath.GetDatabaseFilePath();
            CobraPix_PC.Services.Credit.CreditProductionSafetyGuard.AssertOperationAllowed(activeDbPath, "ApplyCreditMigrations", dbContext);

            bool wasClosed = connection.State != System.Data.ConnectionState.Open;
            if (wasClosed)
            {
                await connection.OpenAsync();
            }

            try
            {
                // 1. Ativação mandatória de Foreign Keys no SQLite
                using (var cmdFk = connection.CreateCommand())
                {
                    cmdFk.CommandText = "PRAGMA foreign_keys = ON;";
                    await cmdFk.ExecuteNonQueryAsync();
                }

                // 2. Tabela de controle de versão do esquema de crédito
                using (var cmdVerTable = connection.CreateCommand())
                {
                    cmdVerTable.CommandText = @"
                        CREATE TABLE IF NOT EXISTS CreditSchemaVersions (
                             Version INTEGER PRIMARY KEY NOT NULL,
                             AppliedAtUtc TEXT NOT NULL,
                             Description TEXT NOT NULL
                        );";
                    await cmdVerTable.ExecuteNonQueryAsync();
                }

                // 3. Verificação de versão atual
                int appliedVersion = 0;
                using (var cmdCheck = connection.CreateCommand())
                {
                    cmdCheck.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM CreditSchemaVersions;";
                    var result = await cmdCheck.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        appliedVersion = Convert.ToInt32(result);
                    }
                }

                if (appliedVersion < 1)
                {
                    await ApplyVersion1Async(connection);
                }

                if (appliedVersion < 2)
                {
                    await ApplyVersion2Async(connection);
                }

                if (appliedVersion < 3)
                {
                    await ApplyVersion3Async(connection);
                }
            }
            finally
            {
                if (wasClosed)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private static async Task ApplyVersion1Async(SqliteConnection connection)
        {
            using var tx = connection.BeginTransaction();
            try
            {
                var sql = @"
                -- 1. Contratos de Crédito
                CREATE TABLE IF NOT EXISTS CreditContracts (
                    Id TEXT PRIMARY KEY NOT NULL,
                    ClientId INTEGER NOT NULL,
                    ContractNumber TEXT NOT NULL UNIQUE,
                    ContractType INTEGER NOT NULL CHECK(ContractType IN (1, 2)),
                    Periodicity INTEGER NOT NULL DEFAULT 4 CHECK(Periodicity IN (1, 2, 3, 4, 5)),
                    CustomIntervalDays INTEGER NULL,
                    Status INTEGER NOT NULL CHECK(Status IN (1, 2, 3, 4, 5)),
                    DisbursedPrincipalCents INTEGER NOT NULL CHECK(DisbursedPrincipalCents > 0),
                    TotalNominalInterestCents INTEGER NOT NULL CHECK(TotalNominalInterestCents >= 0),
                    TotalAmountCents INTEGER NOT NULL CHECK(TotalAmountCents >= DisbursedPrincipalCents),
                    ContractDate TEXT NOT NULL,
                    Notes TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    CancelledAtUtc TEXT NULL,
                    CancellationReason TEXT NULL,
                    FOREIGN KEY (ClientId) REFERENCES Clients(Id) ON DELETE RESTRICT
                );

                CREATE INDEX IF NOT EXISTS IX_CreditContracts_ClientId ON CreditContracts(ClientId);
                CREATE INDEX IF NOT EXISTS IX_CreditContracts_Status ON CreditContracts(Status);
                CREATE INDEX IF NOT EXISTS IX_CreditContracts_ContractDate ON CreditContracts(ContractDate);

                -- 2. Desembolsos (1:1 com Contrato)
                CREATE TABLE IF NOT EXISTS CreditDisbursements (
                    Id TEXT PRIMARY KEY NOT NULL,
                    ContractId TEXT NOT NULL UNIQUE,
                    DisbursedAmountCents INTEGER NOT NULL CHECK(DisbursedAmountCents > 0),
                    DisbursementDate TEXT NOT NULL,
                    PaymentMethod INTEGER NOT NULL,
                    Notes TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (ContractId) REFERENCES CreditContracts(Id) ON DELETE RESTRICT
                );

                -- 3. Parcelas Fixas
                CREATE TABLE IF NOT EXISTS CreditInstallments (
                    Id TEXT PRIMARY KEY NOT NULL,
                    ContractId TEXT NOT NULL,
                    InstallmentNumber INTEGER NOT NULL CHECK(InstallmentNumber > 0),
                    DueDate TEXT NOT NULL,
                    NominalPrincipalCents INTEGER NOT NULL CHECK(NominalPrincipalCents >= 0),
                    NominalInterestCents INTEGER NOT NULL CHECK(NominalInterestCents >= 0),
                    TotalAmountCents INTEGER NOT NULL CHECK(TotalAmountCents > 0),
                    PaidAmountCents INTEGER NOT NULL DEFAULT 0 CHECK(PaidAmountCents >= 0 AND PaidAmountCents <= TotalAmountCents),
                    Status INTEGER NOT NULL CHECK(Status IN (1, 2, 3, 4)),
                    CreatedAtUtc TEXT NOT NULL,
                    CHECK (TotalAmountCents = NominalPrincipalCents + NominalInterestCents),
                    UNIQUE (ContractId, InstallmentNumber),
                    FOREIGN KEY (ContractId) REFERENCES CreditContracts(Id) ON DELETE RESTRICT
                );

                CREATE INDEX IF NOT EXISTS IX_CreditInstallments_ContractId ON CreditInstallments(ContractId);
                CREATE INDEX IF NOT EXISTS IX_CreditInstallments_DueDate ON CreditInstallments(DueDate);
                CREATE INDEX IF NOT EXISTS IX_CreditInstallments_Status ON CreditInstallments(Status);

                -- 4. Ciclos de Giro
                CREATE TABLE IF NOT EXISTS CreditCycles (
                    Id TEXT PRIMARY KEY NOT NULL,
                    ContractId TEXT NOT NULL,
                    CycleNumber INTEGER NOT NULL CHECK(CycleNumber > 0),
                    StartDate TEXT NOT NULL,
                    EndDate TEXT NOT NULL,
                    DueDate TEXT NOT NULL,
                    AccruedInterestCents INTEGER NOT NULL CHECK(AccruedInterestCents >= 0),
                    PaidInterestCents INTEGER NOT NULL DEFAULT 0 CHECK(PaidInterestCents >= 0),
                    MinimumAmountCents INTEGER NOT NULL CHECK(MinimumAmountCents >= 0),
                    PaidAmountCents INTEGER NOT NULL DEFAULT 0 CHECK(PaidAmountCents >= 0),
                    Status INTEGER NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UNIQUE (ContractId, CycleNumber),
                    FOREIGN KEY (ContractId) REFERENCES CreditContracts(Id) ON DELETE RESTRICT
                );

                -- 5. Pagamentos de Crédito
                CREATE TABLE IF NOT EXISTS CreditPayments (
                    Id TEXT PRIMARY KEY NOT NULL,
                    ContractId TEXT NOT NULL,
                    PaymentDate TEXT NOT NULL,
                    AmountCents INTEGER NOT NULL CHECK(AmountCents > 0),
                    Status INTEGER NOT NULL CHECK(Status IN (1, 2, 3)),
                    CreatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (ContractId) REFERENCES CreditContracts(Id) ON DELETE RESTRICT
                );

                CREATE INDEX IF NOT EXISTS IX_CreditPayments_ContractId ON CreditPayments(ContractId);
                CREATE INDEX IF NOT EXISTS IX_CreditPayments_PaymentDate ON CreditPayments(PaymentDate);

                -- 6. Alocações de Pagamento em Parcelas (Regra A)
                CREATE TABLE IF NOT EXISTS CreditPaymentInstallmentAllocs (
                    Id TEXT PRIMARY KEY NOT NULL,
                    PaymentId TEXT NOT NULL,
                    InstallmentId TEXT NOT NULL,
                    AllocatedAmount INTEGER NOT NULL CHECK(AllocatedAmount > 0),
                    InterestAllocated INTEGER NOT NULL CHECK(InterestAllocated >= 0),
                    PrincipalAllocated INTEGER NOT NULL CHECK(PrincipalAllocated >= 0),
                    CreatedAtUtc TEXT NOT NULL,
                    CHECK (AllocatedAmount = InterestAllocated + PrincipalAllocated),
                    FOREIGN KEY (PaymentId) REFERENCES CreditPayments(Id) ON DELETE RESTRICT,
                    FOREIGN KEY (InstallmentId) REFERENCES CreditInstallments(Id) ON DELETE RESTRICT
                );

                CREATE INDEX IF NOT EXISTS IX_CreditPaymentInstallmentAllocs_PaymentId ON CreditPaymentInstallmentAllocs(PaymentId);
                CREATE INDEX IF NOT EXISTS IX_CreditPaymentInstallmentAllocs_InstallmentId ON CreditPaymentInstallmentAllocs(InstallmentId);

                -- 7. Bolsas de Excedente
                CREATE TABLE IF NOT EXISTS CreditSurplusBalances (
                    Id TEXT PRIMARY KEY NOT NULL,
                    ContractId TEXT NOT NULL,
                    SourcePaymentId TEXT NOT NULL,
                    OriginalSurplusCents INTEGER NOT NULL CHECK(OriginalSurplusCents > 0),
                    RemainingSurplusCents INTEGER NOT NULL CHECK(RemainingSurplusCents >= 0 AND RemainingSurplusCents <= OriginalSurplusCents),
                    Status INTEGER NOT NULL CHECK(Status IN (1, 2, 3)),
                    CreatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (ContractId) REFERENCES CreditContracts(Id) ON DELETE RESTRICT,
                    FOREIGN KEY (SourcePaymentId) REFERENCES CreditPayments(Id) ON DELETE RESTRICT
                );

                CREATE INDEX IF NOT EXISTS IX_CreditSurplusBalances_ContractId ON CreditSurplusBalances(ContractId);

                -- 8. Consumos de Excedente (Regra A)
                CREATE TABLE IF NOT EXISTS CreditSurplusConsumptions (
                    Id TEXT PRIMARY KEY NOT NULL,
                    SurplusBalanceId TEXT NOT NULL,
                    TargetInstallmentId TEXT NULL,
                    TargetCycleId TEXT NULL,
                    ConsumedAmount INTEGER NOT NULL CHECK(ConsumedAmount > 0),
                    InterestConsumed INTEGER NOT NULL CHECK(InterestConsumed >= 0),
                    PrincipalConsumed INTEGER NOT NULL CHECK(PrincipalConsumed >= 0),
                    IsReversed INTEGER NOT NULL DEFAULT 0 CHECK(IsReversed IN (0, 1)),
                    CreatedAtUtc TEXT NOT NULL,
                    CHECK (ConsumedAmount = InterestConsumed + PrincipalConsumed),
                    CHECK ((TargetInstallmentId IS NOT NULL AND TargetCycleId IS NULL) OR (TargetInstallmentId IS NULL AND TargetCycleId IS NOT NULL)),
                    FOREIGN KEY (SurplusBalanceId) REFERENCES CreditSurplusBalances(Id) ON DELETE RESTRICT,
                    FOREIGN KEY (TargetInstallmentId) REFERENCES CreditInstallments(Id) ON DELETE RESTRICT,
                    FOREIGN KEY (TargetCycleId) REFERENCES CreditCycles(Id) ON DELETE RESTRICT
                );

                CREATE INDEX IF NOT EXISTS IX_CreditSurplusConsumptions_SurplusBalanceId ON CreditSurplusConsumptions(SurplusBalanceId);
                CREATE INDEX IF NOT EXISTS IX_CreditSurplusConsumptions_TargetInstallmentId ON CreditSurplusConsumptions(TargetInstallmentId);

                -- 9. Reversões de Consumo (1:1)
                CREATE TABLE IF NOT EXISTS CreditSurplusConsumptionReversals (
                    Id TEXT PRIMARY KEY NOT NULL,
                    CreditSurplusConsumptionId TEXT NOT NULL UNIQUE,
                    ReversedAmountCents INTEGER NOT NULL CHECK(ReversedAmountCents > 0),
                    ReversedInterestCents INTEGER NOT NULL CHECK(ReversedInterestCents >= 0),
                    ReversedPrincipalCents INTEGER NOT NULL CHECK(ReversedPrincipalCents >= 0),
                    ReversalDateUtc TEXT NOT NULL,
                    CHECK (ReversedAmountCents = ReversedInterestCents + ReversedPrincipalCents),
                    FOREIGN KEY (CreditSurplusConsumptionId) REFERENCES CreditSurplusConsumptions(Id) ON DELETE RESTRICT
                );

                -- 10. Estornos de Pagamento (Dois Passos)
                CREATE TABLE IF NOT EXISTS CreditPaymentRefunds (
                    Id TEXT PRIMARY KEY NOT NULL,
                    PaymentId TEXT NOT NULL UNIQUE,
                    AuthorizedAmountCents INTEGER NOT NULL CHECK(AuthorizedAmountCents > 0),
                    Status INTEGER NOT NULL CHECK(Status IN (1, 2, 3)),
                    Reason TEXT NOT NULL,
                    AuthorizedAtUtc TEXT NOT NULL,
                    SettledAtUtc TEXT NULL,
                    CancelledAtUtc TEXT NULL,
                    FOREIGN KEY (PaymentId) REFERENCES CreditPayments(Id) ON DELETE RESTRICT
                );

                -- 11. Registros Persistentes de Idempotência
                CREATE TABLE IF NOT EXISTS CreditIdempotencyRecords (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Scope TEXT NOT NULL,
                    IdempotencyKey TEXT NOT NULL,
                    RequestPayloadHash TEXT NOT NULL,
                    ResponsePayloadJson TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UNIQUE(Scope, IdempotencyKey)
                );

                CREATE INDEX IF NOT EXISTS IX_CreditIdempotencyRecords_Lookup ON CreditIdempotencyRecords(Scope, IdempotencyKey);

                -- 12. Intenções de Pagamento (Fase 1 Two-Phase)
                CREATE TABLE IF NOT EXISTS CreditPaymentIntents (
                    Id TEXT PRIMARY KEY NOT NULL,
                    IdempotencyKey TEXT NOT NULL UNIQUE,
                    ContractId TEXT NOT NULL,
                    AmountCents INTEGER NOT NULL CHECK(AmountCents > 0),
                    RequestPayloadHash TEXT NOT NULL,
                    Status INTEGER NOT NULL CHECK(Status IN (1, 2, 3)),
                    CreatedAtUtc TEXT NOT NULL,
                    CompletedAtUtc TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_CreditPaymentIntents_IdempotencyKey ON CreditPaymentIntents(IdempotencyKey);

                -- 13. Auditoria Transacional
                CREATE TABLE IF NOT EXISTS CreditTransactionAudits (
                    Id TEXT PRIMARY KEY NOT NULL,
                    ContractId TEXT NULL,
                    OperationType TEXT NOT NULL,
                    DetailsJson TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_CreditTransactionAudits_ContractId ON CreditTransactionAudits(ContractId);

                -- Gravação do versionamento
                INSERT INTO CreditSchemaVersions (Version, AppliedAtUtc, Description)
                VALUES (1, strftime('%Y-%m-%dT%H:%M:%SZ', 'now'), 'Initial schema for Credito & Giro module');
                ";

                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private static async Task ApplyVersion2Async(SqliteConnection connection)
        {
            using var tx = connection.BeginTransaction();
            try
            {
                var sql = @"
                -- 14. Devoluções de Caixa
                CREATE TABLE IF NOT EXISTS CreditDevolutions (
                    Id TEXT PRIMARY KEY NOT NULL,
                    RefundId TEXT NULL,
                    PaymentId TEXT NOT NULL,
                    ContractId TEXT NOT NULL,
                    AuthorizedAmountCents INTEGER NOT NULL CHECK(AuthorizedAmountCents > 0),
                    Status INTEGER NOT NULL CHECK(Status IN (1, 2, 3)),
                    Reason TEXT NOT NULL,
                    AuthorizedAtUtc TEXT NOT NULL,
                    SettledAtUtc TEXT NULL,
                    CancelledAtUtc TEXT NULL,
                    FOREIGN KEY (RefundId) REFERENCES CreditPaymentRefunds(Id) ON DELETE RESTRICT,
                    FOREIGN KEY (PaymentId) REFERENCES CreditPayments(Id) ON DELETE RESTRICT,
                    FOREIGN KEY (ContractId) REFERENCES CreditContracts(Id) ON DELETE RESTRICT
                );

                CREATE INDEX IF NOT EXISTS IX_CreditDevolutions_PaymentId ON CreditDevolutions(PaymentId);
                CREATE INDEX IF NOT EXISTS IX_CreditDevolutions_ContractId ON CreditDevolutions(ContractId);
                CREATE INDEX IF NOT EXISTS IX_CreditDevolutions_RefundId ON CreditDevolutions(RefundId);

                -- 15. Saídas Financeiras de Caixa (Liquidação única)
                CREATE TABLE IF NOT EXISTS CreditCashOutflows (
                    Id TEXT PRIMARY KEY NOT NULL,
                    DevolutionId TEXT NOT NULL UNIQUE,
                    PaymentId TEXT NOT NULL,
                    ContractId TEXT NOT NULL,
                    AmountCents INTEGER NOT NULL CHECK(AmountCents > 0),
                    OutflowDate TEXT NOT NULL,
                    PaymentMethod INTEGER NOT NULL,
                    Description TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (DevolutionId) REFERENCES CreditDevolutions(Id) ON DELETE RESTRICT,
                    FOREIGN KEY (PaymentId) REFERENCES CreditPayments(Id) ON DELETE RESTRICT,
                    FOREIGN KEY (ContractId) REFERENCES CreditContracts(Id) ON DELETE RESTRICT
                );

                CREATE INDEX IF NOT EXISTS IX_CreditCashOutflows_DevolutionId ON CreditCashOutflows(DevolutionId);
                CREATE INDEX IF NOT EXISTS IX_CreditCashOutflows_ContractId ON CreditCashOutflows(ContractId);

                -- Gravação do versionamento da etapa 3
                INSERT INTO CreditSchemaVersions (Version, AppliedAtUtc, Description)
                VALUES (2, strftime('%Y-%m-%dT%H:%M:%SZ', 'now'), 'Add CreditDevolutions and CreditCashOutflows for cash devolution lifecycle');
                ";

                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private static async Task ApplyVersion3Async(SqliteConnection connection)
        {
            using var tx = connection.BeginTransaction();
            try
            {
                // Inspecionar colunas existentes na tabela CreditContracts via PRAGMA table_info
                var existingCols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmdCols = connection.CreateCommand())
                {
                    cmdCols.Transaction = tx;
                    cmdCols.CommandText = "PRAGMA table_info(CreditContracts);";
                    using var reader = await cmdCols.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        existingCols.Add(reader.GetString(1)); // coluna 'name'
                    }
                }

                if (!existingCols.Contains("Periodicity"))
                {
                    using var cmdAddPeriodicity = connection.CreateCommand();
                    cmdAddPeriodicity.Transaction = tx;
                    cmdAddPeriodicity.CommandText = "ALTER TABLE CreditContracts ADD COLUMN Periodicity INTEGER NOT NULL DEFAULT 4 CHECK(Periodicity IN (1, 2, 3, 4, 5));";
                    await cmdAddPeriodicity.ExecuteNonQueryAsync();
                }

                if (!existingCols.Contains("CustomIntervalDays"))
                {
                    using var cmdAddCustomDays = connection.CreateCommand();
                    cmdAddCustomDays.Transaction = tx;
                    cmdAddCustomDays.CommandText = "ALTER TABLE CreditContracts ADD COLUMN CustomIntervalDays INTEGER NULL;";
                    await cmdAddCustomDays.ExecuteNonQueryAsync();
                }

                // Gravação do versionamento da etapa de periodicidade
                using (var cmdVer = connection.CreateCommand())
                {
                    cmdVer.Transaction = tx;
                    cmdVer.CommandText = @"
                        INSERT INTO CreditSchemaVersions (Version, AppliedAtUtc, Description)
                        VALUES (3, strftime('%Y-%m-%dT%H:%M:%SZ', 'now'), 'Add configurable periodicity (Daily, Weekly, Biweekly, Monthly, CustomDays) and CustomIntervalDays to CreditContracts');";
                    await cmdVer.ExecuteNonQueryAsync();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
}
