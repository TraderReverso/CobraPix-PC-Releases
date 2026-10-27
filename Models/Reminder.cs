using System;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Nível de prioridade de um lembrete ou tarefa.
    /// </summary>
    public enum ReminderPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Urgent = 3
    }

    /// <summary>
    /// Estado atual do ciclo de vida do lembrete.
    /// </summary>
    public enum ReminderStatus
    {
        Pending = 0,
        Completed = 1,
        Cancelled = 2
    }

    /// <summary>
    /// Configuração de repetição automática ao concluir o lembrete.
    /// </summary>
    public enum ReminderRecurrence
    {
        None = 0,
        Daily = 1,
        Weekly = 2,
        Monthly = 3,
        CustomDays = 4
    }

    /// <summary>
    /// Entidade que representa um lembrete ou tarefa no CobraPix PC.
    /// </summary>
    public class Reminder
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime? DueDate { get; set; }
        public ReminderPriority Priority { get; set; } = ReminderPriority.Normal;
        public string Category { get; set; } = "Geral";
        public ReminderStatus Status { get; set; } = ReminderStatus.Pending;
        public ReminderRecurrence Recurrence { get; set; } = ReminderRecurrence.None;
        public int? CustomIntervalDays { get; set; }
        public int SnoozeCount { get; set; } = 0;
        public DateTime? RescheduledAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? HistoryLog { get; set; }

        // Propriedades auxiliares para apresentação visual
        public bool IsCompleted => Status == ReminderStatus.Completed;
        public bool IsPending => Status == ReminderStatus.Pending;
        public bool IsCancelled => Status == ReminderStatus.Cancelled;

        public bool IsOverdue => Status == ReminderStatus.Pending && DueDate.HasValue && DueDate.Value < DateTime.Now;
        public bool IsToday => Status == ReminderStatus.Pending && DueDate.HasValue && DueDate.Value.Date == DateTime.Today;

        public string FormattedDueDate => DueDate.HasValue
            ? DueDate.Value.ToString("dd/MM/yyyy HH:mm")
            : "Sem prazo";

        public string PriorityLabel => Priority switch
        {
            ReminderPriority.Low => "Baixa",
            ReminderPriority.Normal => "Normal",
            ReminderPriority.High => "Alta",
            ReminderPriority.Urgent => "Urgente",
            _ => "Normal"
        };

        public string PriorityBadgeColor => Priority switch
        {
            ReminderPriority.Low => "#10B981",    // Verde
            ReminderPriority.Normal => "#3B82F6", // Azul
            ReminderPriority.High => "#F59E0B",   // Âmbar
            ReminderPriority.Urgent => "#EF4444", // Vermelho
            _ => "#3B82F6"
        };

        public string StatusBadgeColor => Status switch
        {
            ReminderStatus.Pending => IsOverdue ? "#EF4444" : (IsToday ? "#F59E0B" : "#3B82F6"),
            ReminderStatus.Completed => "#10B981",
            ReminderStatus.Cancelled => "#6B7280",
            _ => "#6B7280"
        };

        public string StatusLabel => Status switch
        {
            ReminderStatus.Pending => IsOverdue ? "Atrasado" : (IsToday ? "Para Hoje" : "Pendente"),
            ReminderStatus.Completed => "Concluído",
            ReminderStatus.Cancelled => "Cancelado",
            _ => "Pendente"
        };

        public string RecurrenceLabel => Recurrence switch
        {
            ReminderRecurrence.None => "Não repete",
            ReminderRecurrence.Daily => "Diário",
            ReminderRecurrence.Weekly => "Semanal",
            ReminderRecurrence.Monthly => "Mensal",
            ReminderRecurrence.CustomDays => $"A cada {CustomIntervalDays ?? 1} dias",
            _ => "Não repete"
        };

        public string DueDateBadgeColor => IsOverdue
            ? "#EF4444"
            : (IsToday ? "#F59E0B" : "#9CA3AF");
    }
}
