using System;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using CobraPix_PC.Models;
using CobraPix_PC.Models.Credit;

namespace CobraPix_PC.Data
{
    /// <summary>
    /// Contexto principal do Entity Framework Core para o CobraPix PC.
    /// Configura tabelas, tipos de dados, índices e relacionamentos financeiros com restrição de exclusão acidental.
    /// </summary>
    public class CobraPixDbContext : DbContext
    {
        public DbSet<Client> Clients => Set<Client>();
        public DbSet<Sale> Sales => Set<Sale>();
        public DbSet<Installment> Installments => Set<Installment>();
        public DbSet<InstallmentPayment> InstallmentPayments => Set<InstallmentPayment>();
        public DbSet<MonthlyCustomer> MonthlyCustomers => Set<MonthlyCustomer>();
        public DbSet<MonthlyPayment> MonthlyPayments => Set<MonthlyPayment>();
        public DbSet<AppSetting> AppSettings => Set<AppSetting>();
        public DbSet<Reminder> Reminders => Set<Reminder>();

        // Tabelas do Módulo Crédito & Giro (Exclusivo CobraPix PRO)
        public DbSet<CreditContract> CreditContracts => Set<CreditContract>();
        public DbSet<CreditDisbursement> CreditDisbursements => Set<CreditDisbursement>();
        public DbSet<CreditInstallment> CreditInstallments => Set<CreditInstallment>();
        public DbSet<CreditCycle> CreditCycles => Set<CreditCycle>();
        public DbSet<CreditPayment> CreditPayments => Set<CreditPayment>();
        public DbSet<CreditPaymentInstallmentAlloc> CreditPaymentInstallmentAllocs => Set<CreditPaymentInstallmentAlloc>();
        public DbSet<CreditSurplusBalance> CreditSurplusBalances => Set<CreditSurplusBalance>();
        public DbSet<CreditSurplusConsumption> CreditSurplusConsumptions => Set<CreditSurplusConsumption>();
        public DbSet<CreditSurplusConsumptionReversal> CreditSurplusConsumptionReversals => Set<CreditSurplusConsumptionReversal>();
        public DbSet<CreditPaymentRefund> CreditPaymentRefunds => Set<CreditPaymentRefund>();
        public DbSet<CreditDevolution> CreditDevolutions => Set<CreditDevolution>();
        public DbSet<CreditCashOutflow> CreditCashOutflows => Set<CreditCashOutflow>();
        public DbSet<CreditIdempotencyRecord> CreditIdempotencyRecords => Set<CreditIdempotencyRecord>();
        public DbSet<CreditPaymentIntent> CreditPaymentIntents => Set<CreditPaymentIntent>();
        public DbSet<CreditTransactionAudit> CreditTransactionAudits => Set<CreditTransactionAudit>();

        public CobraPixDbContext()
        {
            if (DatabasePath.IsTestExecutionMode())
            {
                // Bloqueia imediatamente (fail-closed) tentativa de instanciar contexto padrão sem base de teste válida
                _ = DatabasePath.GetDatabaseFilePath();
            }
        }

        public CobraPixDbContext(DbContextOptions<CobraPixDbContext> options)
            : base(options)
        {
            if (DatabasePath.IsTestExecutionMode())
            {
                var relationalExt = options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault();
                if (relationalExt == null || (string.IsNullOrWhiteSpace(relationalExt.ConnectionString) && relationalExt.Connection == null))
                {
                    // Contexto resolvido via DI (App.Services) sem ConnectionString explícita nem Conexão: valida imediatamente
                    _ = DatabasePath.GetDatabaseFilePath();
                }
                else if (relationalExt.Connection != null)
                {
                    ValidateExternalDbConnection(relationalExt.Connection);
                }
                else if (!string.IsNullOrWhiteSpace(relationalExt.ConnectionString))
                {
                    ValidateConnectionString(relationalExt.ConnectionString);
                }
            }
        }

        private static void ValidateConnectionString(string connectionString)
        {
            SqliteConnectionStringBuilder? builder = null;
            try
            {
                builder = new SqliteConnectionStringBuilder(connectionString);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[SEGURANÇA DE EXECUÇÃO] Inicialização de CobraPixDbContext abortada: falha ao interpretar ConnectionString: {ex.Message}", ex);
            }

            ValidateSqliteDataSource(builder, builder.DataSource, "ConnectionString");
        }

        private static void ValidateExternalDbConnection(System.Data.Common.DbConnection connection)
        {
            if (connection == null)
            {
                throw new InvalidOperationException(
                    "[SEGURANÇA DE EXECUÇÃO] Inicialização de CobraPixDbContext abortada: DbConnection fornecida é nula.");
            }

            string connStr = connection.ConnectionString ?? string.Empty;
            string dataSource = connection.DataSource ?? string.Empty;

            SqliteConnectionStringBuilder? builder = null;
            if (!string.IsNullOrWhiteSpace(connStr))
            {
                try
                {
                    builder = new SqliteConnectionStringBuilder(connStr);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"[SEGURANÇA DE EXECUÇÃO] Inicialização de CobraPixDbContext abortada: falha ao interpretar ConnectionString da conexão DbConnection: {ex.Message}", ex);
                }
            }

            // Valida builder se presente
            ValidateSqliteDataSource(builder, dataSource, "DbConnection");

            // Se connection.DataSource for diferente de builder.DataSource e não vazio, valida também connection.DataSource
            if (builder != null &&
                !string.IsNullOrWhiteSpace(connection.DataSource) &&
                !string.Equals(connection.DataSource, builder.DataSource, StringComparison.OrdinalIgnoreCase))
            {
                ValidateSqliteDataSource(null, connection.DataSource, "DbConnection (DataSource)");
            }
        }

        private static void ValidateSqliteDataSource(SqliteConnectionStringBuilder? builder, string? rawDataSource, string sourceDescription)
        {
            string ds = (builder != null && !string.IsNullOrWhiteSpace(builder.DataSource))
                ? builder.DataSource
                : (rawDataSource ?? string.Empty);

            // Rejeição adversarial de caminhos que referenciem o banco ou diretório de produção, mesmo que Mode=Memory seja alegado
            if (!string.IsNullOrWhiteSpace(ds) && !string.Equals(ds, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                string prodPath = DatabasePath.GetDefaultProductionPath();
                string prodDir = DatabasePath.GetProductionDirectoryPath();
                if (ds.IndexOf(prodPath, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ds.IndexOf("cobrapix.db", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ds.IndexOf(prodDir, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException(
                        $"[SEGURANÇA DE EXECUÇÃO] Inicialização de CobraPixDbContext abortada: {sourceDescription} referencia a base ou diretório de produção.");
                }
            }

            bool isPureMemory = string.Equals(ds, ":memory:", StringComparison.OrdinalIgnoreCase) ||
                                (string.IsNullOrWhiteSpace(ds) && builder != null && builder.Mode == SqliteOpenMode.Memory) ||
                                (ds.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                                 (ds.Contains(":memory:") || ds.Contains("mode=memory", StringComparison.OrdinalIgnoreCase)));

            if (isPureMemory)
            {
                return; // Conexão em memória legítima permitida sem efeito colateral
            }

            if (string.IsNullOrWhiteSpace(ds))
            {
                throw new InvalidOperationException(
                    $"[SEGURANÇA DE EXECUÇÃO] Inicialização de CobraPixDbContext abortada: {sourceDescription} sem DataSource identificável.");
            }

            DatabasePath.AssertSafeTestFilePath(ds, $"Inicialização de CobraPixDbContext ({sourceDescription})");
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite(DatabasePath.GetConnectionString());
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ==========================================
            // Entidade: Client
            // ==========================================
            modelBuilder.Entity<Client>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.Property(c => c.Name).IsRequired().HasMaxLength(150);
                entity.Property(c => c.Phone).HasMaxLength(30);
                entity.Property(c => c.Notes).HasMaxLength(1000);
                entity.Property(c => c.IsArchived).HasDefaultValue(false);
                entity.Property(c => c.CreatedAt).IsRequired();

                // Índices para busca ágil e filtros
                entity.HasIndex(c => c.Name);
                entity.HasIndex(c => c.IsArchived);
            });

            // ==========================================
            // Entidade: Sale
            // ==========================================
            modelBuilder.Entity<Sale>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.TotalAmount).HasPrecision(18, 2).IsRequired();
                entity.Property(s => s.InstallmentCount).IsRequired();
                entity.Property(s => s.SaleDate).IsRequired();
                entity.Property(s => s.Description).HasMaxLength(250);
                entity.Property(s => s.Notes).HasMaxLength(1000);
                entity.Ignore(s => s.OriginalPrincipal);
                entity.Ignore(s => s.ContractInterestRate);
                entity.Ignore(s => s.ContractInterestAmount);
                entity.Ignore(s => s.UseFifthBusinessDay);
                entity.Ignore(s => s.Periodicity);

                // Relacionamento com Client (DeleteBehavior.Restrict para proteção de histórico financeiro)
                entity.HasOne(s => s.Client)
                      .WithMany(c => c.Sales)
                      .HasForeignKey(s => s.ClientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(s => s.ClientId);
                entity.HasIndex(s => s.SaleDate);
            });

            // ==========================================
            // Entidade: Installment
            // ==========================================
            modelBuilder.Entity<Installment>(entity =>
            {
                entity.HasKey(i => i.Id);
                entity.Property(i => i.InstallmentNumber).IsRequired();
                entity.Property(i => i.Amount).HasPrecision(18, 2).IsRequired();
                entity.Property(i => i.DueDate).IsRequired();
                entity.Property(i => i.Status).IsRequired();
                entity.Property(i => i.Notes).HasMaxLength(500);

                // Relacionamento com Sale (DeleteBehavior.Restrict)
                entity.HasOne(i => i.Sale)
                      .WithMany(s => s.Installments)
                      .HasForeignKey(i => i.SaleId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(i => i.SaleId);
                entity.HasIndex(i => i.DueDate);
                entity.HasIndex(i => i.Status);
            });

            // ==========================================
            // Entidade: InstallmentPayment
            // ==========================================
            modelBuilder.Entity<InstallmentPayment>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.AmountPaid).HasPrecision(18, 2).IsRequired();
                entity.Property(p => p.PaymentDate).IsRequired();
                entity.Property(p => p.PaymentMethod).IsRequired();
                entity.Property(p => p.Notes).HasMaxLength(500);

                // Relacionamento com Installment (DeleteBehavior.Restrict)
                entity.HasOne(p => p.Installment)
                      .WithMany(i => i.Payments)
                      .HasForeignKey(p => p.InstallmentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(p => p.InstallmentId);
                entity.HasIndex(p => p.PaymentDate);
            });

            // ==========================================
            // Entidade: MonthlyCustomer
            // ==========================================
            modelBuilder.Entity<MonthlyCustomer>(entity =>
            {
                entity.HasKey(m => m.Id);
                entity.Property(m => m.MonthlyAmount).HasPrecision(18, 2).IsRequired();
                entity.Property(m => m.DueDay).IsRequired();
                entity.Property(m => m.IsActive).HasDefaultValue(true);
                entity.Property(m => m.StartDate).IsRequired();
                entity.Property(m => m.Notes).HasMaxLength(1000);
                entity.Ignore(m => m.OriginalMonthlyAmount);
                entity.Ignore(m => m.ContractInterestRate);
                entity.Ignore(m => m.ContractInterestAmount);

                // Relacionamento 1-para-1 com Client (DeleteBehavior.Restrict)
                entity.HasOne(m => m.Client)
                      .WithOne(c => c.MonthlyCustomer)
                      .HasForeignKey<MonthlyCustomer>(m => m.ClientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(m => m.ClientId).IsUnique();
                entity.HasIndex(m => m.IsActive);
            });

            // ==========================================
            // Entidade: MonthlyPayment
            // ==========================================
            modelBuilder.Entity<MonthlyPayment>(entity =>
            {
                entity.HasKey(mp => mp.Id);
                entity.Property(mp => mp.Amount).HasPrecision(18, 2).IsRequired();
                entity.Property(mp => mp.PaymentDate).IsRequired();
                entity.Property(mp => mp.ReferenceMonth).IsRequired();
                entity.Property(mp => mp.ReferenceYear).IsRequired();
                entity.Property(mp => mp.PaymentMethod).IsRequired();
                entity.Property(mp => mp.Notes).HasMaxLength(500);

                // Relacionamento com MonthlyCustomer (DeleteBehavior.Restrict)
                entity.HasOne(mp => mp.MonthlyCustomer)
                      .WithMany(m => m.Payments)
                      .HasForeignKey(mp => mp.MonthlyCustomerId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(mp => mp.MonthlyCustomerId);
                entity.HasIndex(mp => mp.PaymentDate);
            });

            // ==========================================
            // Entidade: AppSetting
            // ==========================================
            modelBuilder.Entity<AppSetting>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Key).IsRequired().HasMaxLength(100);
                entity.Property(s => s.Value).IsRequired().HasMaxLength(2000);
                entity.HasIndex(s => s.Key).IsUnique();
            });

            // ==========================================
            // Entidade: Reminder (Módulo Lembretes)
            // ==========================================
            modelBuilder.Entity<Reminder>(entity =>
            {
                entity.ToTable("Reminders");
                entity.HasKey(r => r.Id);
                entity.Property(r => r.Title).IsRequired().HasMaxLength(200);
                entity.Property(r => r.Description).HasMaxLength(2000);
                entity.Property(r => r.Category).IsRequired().HasMaxLength(50).HasDefaultValue("Geral");
                entity.Property(r => r.Priority).HasDefaultValue(ReminderPriority.Normal);
                entity.Property(r => r.Status).HasDefaultValue(ReminderStatus.Pending);
                entity.Property(r => r.Recurrence).HasDefaultValue(ReminderRecurrence.None);
                entity.Property(r => r.HistoryLog).HasMaxLength(4000);

                entity.HasIndex(r => r.DueDate);
                entity.HasIndex(r => r.Status);
                entity.HasIndex(r => r.Category);
                entity.HasIndex(r => r.Priority);
            });

            // ==========================================
            // Entidades do Módulo Crédito & Giro
            // ==========================================

            modelBuilder.Entity<CreditContract>(entity =>
            {
                entity.ToTable("CreditContracts");
                entity.HasKey(c => c.Id);
                entity.Property(c => c.ContractNumber).IsRequired().HasMaxLength(50);
                entity.Property(c => c.Periodicity).IsRequired().HasDefaultValue(CreditPeriodicity.Monthly);
                entity.Property(c => c.CustomIntervalDays).IsRequired(false);
                entity.Property(c => c.Notes).HasMaxLength(1000);
                entity.Property(c => c.CancellationReason).HasMaxLength(500);

                entity.HasIndex(c => c.ContractNumber).IsUnique();
                entity.HasIndex(c => c.ClientId);
                entity.HasIndex(c => c.Status);
                entity.HasIndex(c => c.ContractDate);

                entity.HasOne(c => c.Client)
                      .WithMany()
                      .HasForeignKey(c => c.ClientId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreditDisbursement>(entity =>
            {
                entity.ToTable("CreditDisbursements");
                entity.HasKey(d => d.Id);
                entity.Property(d => d.Notes).HasMaxLength(500);

                entity.HasIndex(d => d.ContractId).IsUnique();

                entity.HasOne(d => d.Contract)
                      .WithOne(c => c.Disbursement)
                      .HasForeignKey<CreditDisbursement>(d => d.ContractId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreditInstallment>(entity =>
            {
                entity.ToTable("CreditInstallments");
                entity.HasKey(i => i.Id);

                entity.HasIndex(i => new { i.ContractId, i.InstallmentNumber }).IsUnique();
                entity.HasIndex(i => i.ContractId);
                entity.HasIndex(i => i.DueDate);
                entity.HasIndex(i => i.Status);

                entity.HasOne(i => i.Contract)
                      .WithMany(c => c.Installments)
                      .HasForeignKey(i => i.ContractId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreditCycle>(entity =>
            {
                entity.ToTable("CreditCycles");
                entity.HasKey(cy => cy.Id);

                entity.HasIndex(cy => new { cy.ContractId, cy.CycleNumber }).IsUnique();
                entity.HasIndex(cy => cy.ContractId);

                entity.HasOne(cy => cy.Contract)
                      .WithMany(c => c.Cycles)
                      .HasForeignKey(cy => cy.ContractId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreditPayment>(entity =>
            {
                entity.ToTable("CreditPayments");
                entity.HasKey(p => p.Id);

                entity.HasIndex(p => p.ContractId);
                entity.HasIndex(p => p.PaymentDate);

                entity.HasOne(p => p.Contract)
                      .WithMany(c => c.Payments)
                      .HasForeignKey(p => p.ContractId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreditPaymentInstallmentAlloc>(entity =>
            {
                entity.ToTable("CreditPaymentInstallmentAllocs");
                entity.HasKey(a => a.Id);

                entity.HasIndex(a => a.PaymentId);
                entity.HasIndex(a => a.InstallmentId);

                entity.HasOne(a => a.Payment)
                      .WithMany(p => p.Allocations)
                      .HasForeignKey(a => a.PaymentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(a => a.Installment)
                      .WithMany(i => i.Allocations)
                      .HasForeignKey(a => a.InstallmentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreditSurplusBalance>(entity =>
            {
                entity.ToTable("CreditSurplusBalances");
                entity.HasKey(s => s.Id);

                entity.HasIndex(s => s.ContractId);

                entity.HasOne(s => s.Contract)
                      .WithMany(c => c.SurplusBalances)
                      .HasForeignKey(s => s.ContractId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(s => s.SourcePayment)
                      .WithMany(p => p.SurplusBalances)
                      .HasForeignKey(s => s.SourcePaymentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreditSurplusConsumption>(entity =>
            {
                entity.ToTable("CreditSurplusConsumptions");
                entity.HasKey(sc => sc.Id);

                entity.HasIndex(sc => sc.SurplusBalanceId);
                entity.HasIndex(sc => sc.TargetInstallmentId);

                entity.HasOne(sc => sc.SurplusBalance)
                      .WithMany(s => s.Consumptions)
                      .HasForeignKey(sc => sc.SurplusBalanceId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(sc => sc.TargetInstallment)
                      .WithMany(i => i.SurplusConsumptions)
                      .HasForeignKey(sc => sc.TargetInstallmentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(sc => sc.TargetCycle)
                      .WithMany(c => c.SurplusConsumptions)
                      .HasForeignKey(sc => sc.TargetCycleId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreditSurplusConsumptionReversal>(entity =>
            {
                entity.ToTable("CreditSurplusConsumptionReversals");
                entity.HasKey(r => r.Id);

                entity.HasIndex(r => r.CreditSurplusConsumptionId).IsUnique();

                entity.HasOne(r => r.Consumption)
                      .WithOne(sc => sc.Reversal)
                      .HasForeignKey<CreditSurplusConsumptionReversal>(r => r.CreditSurplusConsumptionId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreditPaymentRefund>(entity =>
            {
                entity.ToTable("CreditPaymentRefunds");
                entity.HasKey(rf => rf.Id);
                entity.Property(rf => rf.Reason).IsRequired().HasMaxLength(500);

                entity.HasIndex(rf => rf.PaymentId).IsUnique();

                entity.HasOne(rf => rf.Payment)
                      .WithOne(p => p.Refund)
                      .HasForeignKey<CreditPaymentRefund>(rf => rf.PaymentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreditIdempotencyRecord>(entity =>
            {
                entity.ToTable("CreditIdempotencyRecords");
                entity.HasKey(r => r.Id);
                entity.Property(r => r.Scope).IsRequired().HasMaxLength(50);
                entity.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(150);
                entity.Property(r => r.RequestPayloadHash).IsRequired().HasMaxLength(64);

                entity.HasIndex(r => new { r.Scope, r.IdempotencyKey }).IsUnique();
            });

            modelBuilder.Entity<CreditPaymentIntent>(entity =>
            {
                entity.ToTable("CreditPaymentIntents");
                entity.HasKey(pi => pi.Id);
                entity.Property(pi => pi.IdempotencyKey).IsRequired().HasMaxLength(150);
                entity.Property(pi => pi.RequestPayloadHash).IsRequired().HasMaxLength(64);

                entity.HasIndex(pi => pi.IdempotencyKey).IsUnique();
            });

            modelBuilder.Entity<CreditTransactionAudit>(entity =>
            {
                entity.ToTable("CreditTransactionAudits");
                entity.HasKey(a => a.Id);
                entity.Property(a => a.OperationType).IsRequired().HasMaxLength(100);

                entity.HasIndex(a => a.ContractId);
            });

            modelBuilder.Entity<CreditDevolution>(entity =>
            {
                entity.ToTable("CreditDevolutions");
                entity.HasKey(d => d.Id);
                entity.Property(d => d.Reason).IsRequired().HasMaxLength(500);

                entity.HasOne(d => d.Payment)
                      .WithMany(p => p.Devolutions)
                      .HasForeignKey(d => d.PaymentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.Contract)
                      .WithMany()
                      .HasForeignKey(d => d.ContractId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.Refund)
                      .WithOne(rf => rf.Devolution)
                      .HasForeignKey<CreditDevolution>(d => d.RefundId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CreditCashOutflow>(entity =>
            {
                entity.ToTable("CreditCashOutflows");
                entity.HasKey(o => o.Id);
                entity.Property(o => o.Description).IsRequired().HasMaxLength(500);

                entity.HasIndex(o => o.DevolutionId).IsUnique();

                entity.HasOne(o => o.Devolution)
                      .WithOne(d => d.CashOutflow)
                      .HasForeignKey<CreditCashOutflow>(o => o.DevolutionId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(o => o.Payment)
                      .WithMany(p => p.CashOutflows)
                      .HasForeignKey(o => o.PaymentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(o => o.Contract)
                      .WithMany()
                      .HasForeignKey(o => o.ContractId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
