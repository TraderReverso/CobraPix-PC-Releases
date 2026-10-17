using System;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Tipos suportados de notificações e lembretes locais do sistema.
    /// </summary>
    public enum NotificationType
    {
        ParcelaProxima,
        ParcelaAtrasada,
        MensalidadeProxima,
        MensalidadeAtrasada,
        QuintoDiaUtil,
        ResumoAtrasados,
        ResumoMensalidades,
        Info,
        Lembrete
    }

    /// <summary>
    /// Representa um alerta ou lembrete gerado localmente pelo CobraPix PC.
    /// </summary>
    public class NotificationItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? ClientName { get; set; }
        public int? InstallmentId { get; set; }
        public int? MonthlyCustomerId { get; set; }
        public decimal? Amount { get; set; }
        public string DeduplicationKey { get; set; } = string.Empty;

        public string Icon => Type switch
        {
            NotificationType.ParcelaAtrasada => "⚠️",
            NotificationType.MensalidadeAtrasada => "⚠️",
            NotificationType.ResumoAtrasados => "⚠️",
            NotificationType.ResumoMensalidades => "⚠️",
            NotificationType.ParcelaProxima => "⏰",
            NotificationType.MensalidadeProxima => "⭐",
            NotificationType.QuintoDiaUtil => "💼",
            NotificationType.Lembrete => "📝",
            _ => "🔔"
        };

        public string TypeBadge => Type switch
        {
            NotificationType.ParcelaAtrasada => "Atrasado",
            NotificationType.MensalidadeAtrasada => "Mensalidade Atrasada",
            NotificationType.ResumoAtrasados => "Resumo Atrasados",
            NotificationType.ResumoMensalidades => "Resumo Mensalidades",
            NotificationType.ParcelaProxima => "Vencendo",
            NotificationType.MensalidadeProxima => "Mensalidade Próxima",
            NotificationType.QuintoDiaUtil => "5º Dia Útil",
            NotificationType.Lembrete => "Lembrete",
            _ => "Aviso"
        };
    }
}
