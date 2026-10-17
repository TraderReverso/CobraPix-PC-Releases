using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Serviço de gerenciamento completo do ciclo de vida de lembretes e tarefas.
    /// Suporta criação, alteração, filtros, recorrência automática, adiamento (snooze) e histórico.
    /// </summary>
    public class ReminderService : IReminderService
    {
        private readonly CobraPixDbContext _context;

        internal CobraPixDbContext DbContext => _context;

        public ReminderService(CobraPixDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<List<Reminder>> GetAllRemindersAsync(
            string? search = null,
            ReminderStatus? status = null,
            ReminderPriority? priority = null,
            string? category = null)
        {
            var query = _context.Reminders.AsNoTracking().AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(r => r.Status == status.Value);
            }

            if (priority.HasValue)
            {
                query = query.Where(r => r.Priority == priority.Value);
            }

            if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Todas", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => r.Category == category);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                string searchLower = search.Trim().ToLower();
                query = query.Where(r => r.Title.ToLower().Contains(searchLower) ||
                                         (r.Description != null && r.Description.ToLower().Contains(searchLower)) ||
                                         r.Category.ToLower().Contains(searchLower));
            }

            // Ordenação: Pendentes primeiro, depois ordenados por DueDate (atrasados/próximos), prioridade decrescente e criação
            return await query
                .OrderBy(r => r.Status == ReminderStatus.Completed ? 1 : 0)
                .ThenBy(r => r.DueDate == null ? 1 : 0)
                .ThenBy(r => r.DueDate)
                .ThenByDescending(r => r.Priority)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        public async Task<Reminder?> GetReminderByIdAsync(int id)
        {
            return await _context.Reminders
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<Reminder> CreateReminderAsync(Reminder reminder)
        {
            if (reminder == null) throw new ArgumentNullException(nameof(reminder));

            if (string.IsNullOrWhiteSpace(reminder.Title))
            {
                throw new BusinessRuleException("O título do lembrete é obrigatório.");
            }

            reminder.Title = reminder.Title.Trim();
            reminder.Description = string.IsNullOrWhiteSpace(reminder.Description) ? null : reminder.Description.Trim();
            reminder.Category = string.IsNullOrWhiteSpace(reminder.Category) ? "Geral" : reminder.Category.Trim();
            reminder.CreatedAt = DateTime.Now;
            reminder.Status = ReminderStatus.Pending;
            reminder.HistoryLog = $"[{DateTime.Now:dd/MM/yyyy HH:mm}] Lembrete criado.";

            _context.Reminders.Add(reminder);
            await _context.SaveChangesAsync();

            DiagnosticLogger.Instance.LogInformation("ReminderService", $"Lembrete #{reminder.Id} criado: {reminder.Title}");

            return reminder;
        }

        public async Task<Reminder> UpdateReminderAsync(Reminder reminder)
        {
            if (reminder == null) throw new ArgumentNullException(nameof(reminder));

            if (string.IsNullOrWhiteSpace(reminder.Title))
            {
                throw new BusinessRuleException("O título do lembrete é obrigatório.");
            }

            var existing = await _context.Reminders.FirstOrDefaultAsync(r => r.Id == reminder.Id);
            if (existing == null)
            {
                throw new BusinessRuleException($"Lembrete #{reminder.Id} não encontrado.");
            }

            existing.Title = reminder.Title.Trim();
            existing.Description = string.IsNullOrWhiteSpace(reminder.Description) ? null : reminder.Description.Trim();
            existing.DueDate = reminder.DueDate;
            existing.Priority = reminder.Priority;
            existing.Category = string.IsNullOrWhiteSpace(reminder.Category) ? "Geral" : reminder.Category.Trim();
            existing.Recurrence = reminder.Recurrence;
            existing.CustomIntervalDays = reminder.CustomIntervalDays;
            existing.Status = reminder.Status;
            existing.UpdatedAt = DateTime.Now;

            string logEntry = $"[{DateTime.Now:dd/MM/yyyy HH:mm}] Lembrete atualizado.";
            existing.HistoryLog = string.IsNullOrWhiteSpace(existing.HistoryLog)
                ? logEntry
                : $"{existing.HistoryLog}\n{logEntry}";

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteReminderAsync(int id)
        {
            var existing = await _context.Reminders.FirstOrDefaultAsync(r => r.Id == id);
            if (existing == null)
            {
                return false;
            }

            _context.Reminders.Remove(existing);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<Reminder> CompleteReminderAsync(int id)
        {
            var existing = await _context.Reminders.FirstOrDefaultAsync(r => r.Id == id);
            if (existing == null)
            {
                throw new BusinessRuleException($"Lembrete #{id} não encontrado.");
            }

            if (existing.Status == ReminderStatus.Completed)
            {
                return existing;
            }

            existing.Status = ReminderStatus.Completed;
            existing.CompletedAt = DateTime.Now;
            existing.UpdatedAt = DateTime.Now;

            string logEntry = $"[{DateTime.Now:dd/MM/yyyy HH:mm}] Concluído.";
            existing.HistoryLog = string.IsNullOrWhiteSpace(existing.HistoryLog)
                ? logEntry
                : $"{existing.HistoryLog}\n{logEntry}";

            // Recorrência automática
            if (existing.Recurrence != ReminderRecurrence.None)
            {
                DateTime baseDate = existing.DueDate ?? DateTime.Now;
                var nextDate = CalculateNextOccurrence(existing, baseDate);
                if (nextDate.HasValue)
                {
                    var recurringReminder = new Reminder
                    {
                        Title = existing.Title,
                        Description = existing.Description,
                        Priority = existing.Priority,
                        Category = existing.Category,
                        Recurrence = existing.Recurrence,
                        CustomIntervalDays = existing.CustomIntervalDays,
                        DueDate = nextDate.Value,
                        Status = ReminderStatus.Pending,
                        CreatedAt = DateTime.Now,
                        HistoryLog = $"[{DateTime.Now:dd/MM/yyyy HH:mm}] Gerado automaticamente pela recorrência ({existing.RecurrenceLabel}) do lembrete #{existing.Id}."
                    };

                    _context.Reminders.Add(recurringReminder);
                }
            }

            await _context.SaveChangesAsync();
            DiagnosticLogger.Instance.LogInformation("ReminderService", $"Lembrete #{existing.Id} concluído.");
            return existing;
        }

        public async Task<Reminder> CancelReminderAsync(int id)
        {
            var existing = await _context.Reminders.FirstOrDefaultAsync(r => r.Id == id);
            if (existing == null)
            {
                throw new BusinessRuleException($"Lembrete #{id} não encontrado.");
            }

            existing.Status = ReminderStatus.Cancelled;
            existing.UpdatedAt = DateTime.Now;

            string logEntry = $"[{DateTime.Now:dd/MM/yyyy HH:mm}] Cancelado.";
            existing.HistoryLog = string.IsNullOrWhiteSpace(existing.HistoryLog)
                ? logEntry
                : $"{existing.HistoryLog}\n{logEntry}";

            await _context.SaveChangesAsync();
            DiagnosticLogger.Instance.LogInformation("ReminderService", $"Lembrete #{existing.Id} cancelado.");
            return existing;
        }

        public async Task<Reminder> SnoozeReminderAsync(int id, TimeSpan delay, DateTime? referenceTime = null)
        {
            var existing = await _context.Reminders.FirstOrDefaultAsync(r => r.Id == id);
            if (existing == null)
            {
                throw new BusinessRuleException($"Lembrete #{id} não encontrado.");
            }

            DateTime now = referenceTime ?? DateTime.Now;
            DateTime baseDate = existing.DueDate.HasValue && existing.DueDate.Value > now
                ? existing.DueDate.Value
                : now;

            DateTime newDueDate = baseDate.Add(delay);

            existing.DueDate = newDueDate;
            existing.SnoozeCount++;
            existing.RescheduledAt = now;
            existing.UpdatedAt = now;
            existing.Status = ReminderStatus.Pending;

            string logEntry = $"[{now:dd/MM/yyyy HH:mm}] Adiado por {(int)delay.TotalMinutes} min. Novo prazo: {newDueDate:dd/MM/yyyy HH:mm}.";
            existing.HistoryLog = string.IsNullOrWhiteSpace(existing.HistoryLog)
                ? logEntry
                : $"{existing.HistoryLog}\n{logEntry}";

            await _context.SaveChangesAsync();
            DiagnosticLogger.Instance.LogInformation("ReminderService", $"Lembrete #{existing.Id} adiado para {newDueDate:dd/MM/yyyy HH:mm}.");
            return existing;
        }

        public async Task<Reminder> RescheduleReminderAsync(int id, DateTime newDueDate)
        {
            var existing = await _context.Reminders.FirstOrDefaultAsync(r => r.Id == id);
            if (existing == null)
            {
                throw new BusinessRuleException($"Lembrete #{id} não encontrado.");
            }

            DateTime now = DateTime.Now;
            existing.DueDate = newDueDate;
            existing.RescheduledAt = now;
            existing.UpdatedAt = now;
            existing.Status = ReminderStatus.Pending;

            string logEntry = $"[{now:dd/MM/yyyy HH:mm}] Reagendado para {newDueDate:dd/MM/yyyy HH:mm}.";
            existing.HistoryLog = string.IsNullOrWhiteSpace(existing.HistoryLog)
                ? logEntry
                : $"{existing.HistoryLog}\n{logEntry}";

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<ReminderSummary> GetSummaryAsync()
        {
            DateTime now = DateTime.Now;
            DateTime today = now.Date;

            var reminders = await _context.Reminders
                .AsNoTracking()
                .Select(r => new { r.Status, r.DueDate })
                .ToListAsync();

            int totalPending = reminders.Count(r => r.Status == ReminderStatus.Pending);
            int overdue = reminders.Count(r => r.Status == ReminderStatus.Pending && r.DueDate.HasValue && r.DueDate.Value < now);
            int todayCount = reminders.Count(r => r.Status == ReminderStatus.Pending && r.DueDate.HasValue && r.DueDate.Value.Date == today);
            int completed = reminders.Count(r => r.Status == ReminderStatus.Completed);

            return new ReminderSummary(totalPending, overdue, todayCount, completed);
        }

        public async Task<List<Reminder>> GetDueRemindersForNotificationAsync(DateTime referenceTime)
        {
            return await _context.Reminders
                .AsNoTracking()
                .Where(r => r.Status == ReminderStatus.Pending &&
                            r.DueDate.HasValue &&
                            r.DueDate.Value <= referenceTime)
                .OrderBy(r => r.DueDate)
                .ToListAsync();
        }

        public DateTime? CalculateNextOccurrence(Reminder reminder, DateTime fromDate)
        {
            return reminder.Recurrence switch
            {
                ReminderRecurrence.Daily => fromDate.AddDays(1),
                ReminderRecurrence.Weekly => fromDate.AddDays(7),
                ReminderRecurrence.Monthly => fromDate.AddMonths(1),
                ReminderRecurrence.CustomDays => fromDate.AddDays(Math.Max(1, reminder.CustomIntervalDays ?? 1)),
                _ => null
            };
        }
    }
}
