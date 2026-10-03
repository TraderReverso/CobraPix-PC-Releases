using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato para o serviço de notificações, lembretes locais e reprodução de som do CobraPix PC.
    /// Responsável por identificar eventos financeiros que demandam alerta, evitar duplicidade no mesmo dia
    /// e apresentar avisos locais ao usuário respeitando suas configurações.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Obtém as configurações atuais de notificações do usuário, carregando do banco ou usando valores padrão.
        /// </summary>
        Task<NotificationSettings> GetSettingsAsync();

        /// <summary>
        /// Persiste as preferências de notificações no banco local de forma não destrutiva (tabela AppSettings).
        /// </summary>
        Task SaveSettingsAsync(NotificationSettings settings);

        /// <summary>
        /// Analisa parcelas, mensalidades e a regra do 5º dia útil e retorna os alertas elegíveis para a data informada.
        /// </summary>
        Task<List<NotificationItem>> CheckNotificationsAsync(DateTime? referenceDate = null);

        /// <summary>
        /// Instante da última verificação de notificações executada (agendada ou manual).
        /// </summary>
        DateTime? LastVerificationTime { get; }

        /// <summary>
        /// Executa o ciclo completo: verifica alertas elegíveis, apresenta-os na interface,
        /// toca som nativo (se ativado) e registra histórico.
        /// Se force for true, apresenta todos os alertas pendentes mesmo que já tenham sido notificados anteriormente hoje.
        /// </summary>
        /// <returns>Quantidade de novos alertas notificados.</returns>
        Task<int> CheckAndNotifyAsync(DateTime? referenceDate = null, bool force = false);

        /// <summary>
        /// Verifica se um evento específico já foi notificado no dia informado.
        /// </summary>
        Task<bool> HasBeenNotifiedTodayAsync(string deduplicationKey, DateTime today);

        /// <summary>
        /// Registra no controle local que o evento foi apresentado para evitar repetição no mesmo dia.
        /// </summary>
        Task MarkAsNotifiedAsync(string deduplicationKey, DateTime today);

        /// <summary>
        /// Emite o sinal sonoro nativo do Windows de forma segura (sem loop e sem bloqueio).
        /// </summary>
        void PlayNotificationSound();

        /// <summary>
        /// Emite o sinal sonoro nativo do Windows ou arquivo personalizado (.wav) de forma segura para teste ou notificação.
        /// </summary>
        void PlaySound(string? soundType = null, string? customPath = null);

        /// <summary>
        /// Apresenta o item de notificação na interface do usuário (ex: janela toast ou log em testes).
        /// </summary>
        void Notify(NotificationItem item);

        /// <summary>
        /// Obtém o próximo instante agendado para verificação a partir do horário de referência,
        /// respeitando a ordem cronológica dos 7 horários configurados.
        /// </summary>
        DateTime GetNextScheduledTime(NotificationSettings settings, DateTime? fromTime = null);

        /// <summary>
        /// Verifica se o instante atual ou de referência coincide com algum dos 7 horários programados.
        /// Se coincidir e ainda não tiver sido disparado nesse minuto/dia, executa CheckAndNotifyAsync.
        /// Retorna a quantidade de novas notificações geradas (ou 0 se não disparou / nada pendente).
        /// </summary>
        Task<int> CheckScheduledNotificationsAsync(DateTime? referenceDateTime = null);
    }
}
