using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Resumo consolidado de contadores do módulo de lembretes.
    /// </summary>
    public record ReminderSummary(int TotalPending, int OverdueCount, int TodayCount, int CompletedCount);

    /// <summary>
    /// Contrato de serviço para gerenciamento completo do ciclo de vida de Lembretes.
    /// </summary>
    public interface IReminderService
    {
        Task<List<Reminder>> GetAllRemindersAsync(
            string? search = null,
            ReminderStatus? status = null,
            ReminderPriority? priority = null,
            string? category = null);

        Task<Reminder?> GetReminderByIdAsync(int id);
        Task<Reminder> CreateReminderAsync(Reminder reminder);
        Task<Reminder> UpdateReminderAsync(Reminder reminder);
        Task<bool> DeleteReminderAsync(int id);
        Task<Reminder> CompleteReminderAsync(int id);
        Task<Reminder> CancelReminderAsync(int id);
        Task<Reminder> SnoozeReminderAsync(int id, TimeSpan delay, DateTime? referenceTime = null);
        Task<Reminder> RescheduleReminderAsync(int id, DateTime newDueDate);
        Task<ReminderSummary> GetSummaryAsync();
        Task<List<Reminder>> GetDueRemindersForNotificationAsync(DateTime referenceTime);
        DateTime? CalculateNextOccurrence(Reminder reminder, DateTime fromDate);
    }
}
