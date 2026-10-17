using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Models;

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

        public CobraPixDbContext()
        {
        }

        public CobraPixDbContext(DbContextOptions<CobraPixDbContext> options)
            : base(options)
        {
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
            // Entidade: Reminder (Módulo Lembretes v1.0.5)
            // ==========================================
            modelBuilder.Entity<Reminder>(entity =>
            {
                entity.ToTable("Reminders");
                entity.HasKey(r => r.Id);
                entity.Property(r => r.Title).IsRequired().HasMaxLength(200);
                entity.Property(r => r.Description).HasMaxLength(2000);
                entity.Property(r => r.Category).IsRequired().HasMaxLength(50);
                entity.Property(r => r.Priority).HasDefaultValue(ReminderPriority.Normal);
                entity.Property(r => r.Status).HasDefaultValue(ReminderStatus.Pending);
                entity.Property(r => r.Recurrence).HasDefaultValue(ReminderRecurrence.None);
                entity.Property(r => r.CustomIntervalDays);
                entity.Property(r => r.SnoozeCount).HasDefaultValue(0);
                entity.Property(r => r.RescheduledAt);
                entity.Property(r => r.CreatedAt).IsRequired();
                entity.Property(r => r.CompletedAt);
                entity.Property(r => r.UpdatedAt);
                entity.Property(r => r.HistoryLog).HasMaxLength(4000);

                entity.HasIndex(r => r.DueDate);
                entity.HasIndex(r => r.Status);
                entity.HasIndex(r => r.Category);
                entity.HasIndex(r => r.Priority);
            });
        }
    }
}
