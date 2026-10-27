using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Helpers;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;
using CobraPix_PC.Views;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Serviço responsável pelo gerenciamento de notificações, alertas de vencimento, controle de duplicidade diária
    /// e reprodução de som do CobraPix PC.
    /// </summary>
    public class NotificationService : INotificationService
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        private readonly CobraPixDbContext _context;
        private readonly IPaymentService _paymentService;
        private readonly IMonthlyCustomerService _monthlyService;
        private readonly Action? _soundPlayer;
        private readonly Action<NotificationItem>? _presenter;

        internal CobraPixDbContext DbContext => _context;

        // Chaves para persistência na tabela AppSettings
        private const string SettingKeyNotificationsEnabled = "Notification.Enabled";
        private const string SettingKeySoundEnabled = "Notification.SoundEnabled";
        private const string SettingKeyAdvanceDays = "Notification.AdvanceDays";
        private const string SettingKeyOverdueAlertsEnabled = "Notification.OverdueAlertsEnabled";
        private const string SettingKeyMonthlyAlertsEnabled = "Notification.MonthlyAlertsEnabled";
        private const string SettingKeyFifthBusinessDayAlertEnabled = "Notification.FifthBusinessDayAlertEnabled";
        private const string SettingKeySoundType = "Notification.SoundType";
        public const string SettingKeyCustomSoundPath = "Notification.CustomSoundPath";
        public const string SettingKeyHourSlot1 = "Notification.HourSlot1";
        public const string SettingKeyHourSlot2 = "Notification.HourSlot2";
        public const string SettingKeyHourSlot3 = "Notification.HourSlot3";
        public const string SettingKeyHourSlot4 = "Notification.HourSlot4";
        public const string SettingKeyHourSlot5 = "Notification.HourSlot5";
        public const string SettingKeyHourSlot6 = "Notification.HourSlot6";
        public const string SettingKeyHourSlot7 = "Notification.HourSlot7";

        private static NotificationSettings? _cachedSettings;
        public static event Action? ScheduleSettingsChanged;

        public static void ClearSettingsCache()
        {
            _cachedSettings = null;
        }

        public NotificationService(
            CobraPixDbContext context,
            IPaymentService paymentService,
            IMonthlyCustomerService monthlyService,
            Action? soundPlayer = null,
            Action<NotificationItem>? presenter = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _monthlyService = monthlyService ?? throw new ArgumentNullException(nameof(monthlyService));
            _soundPlayer = soundPlayer;
            _presenter = presenter;
        }

        public async Task<NotificationSettings> GetSettingsAsync()
        {
            var settings = new NotificationSettings();

            try
            {
                var appSettings = await _context.AppSettings
                    .AsNoTracking()
                    .Where(s => s.Key.StartsWith("Notification."))
                    .ToListAsync();

                var map = appSettings.ToDictionary(s => s.Key, s => s.Value);

                if (map.TryGetValue(SettingKeyNotificationsEnabled, out var notifStr) && bool.TryParse(notifStr, out var notifVal))
                    settings.NotificationsEnabled = notifVal;

                if (map.TryGetValue(SettingKeySoundEnabled, out var soundStr) && bool.TryParse(soundStr, out var soundVal))
                    settings.SoundEnabled = soundVal;

                if (map.TryGetValue(SettingKeyAdvanceDays, out var advStr) && int.TryParse(advStr, out var advVal))
                    settings.AdvanceDays = Math.Max(0, advVal);

                if (map.TryGetValue(SettingKeyOverdueAlertsEnabled, out var overStr) && bool.TryParse(overStr, out var overVal))
                    settings.OverdueAlertsEnabled = overVal;

                if (map.TryGetValue(SettingKeyMonthlyAlertsEnabled, out var monStr) && bool.TryParse(monStr, out var monVal))
                    settings.MonthlyAlertsEnabled = monVal;

                if (map.TryGetValue(SettingKeyFifthBusinessDayAlertEnabled, out var fifthStr) && bool.TryParse(fifthStr, out var fifthVal))
                    settings.FifthBusinessDayAlertEnabled = fifthVal;

                if (map.TryGetValue(SettingKeySoundType, out var soundTypeStr) && !string.IsNullOrWhiteSpace(soundTypeStr))
                    settings.SoundType = soundTypeStr;

                if (map.TryGetValue(SettingKeyCustomSoundPath, out var customSoundStr) && !string.IsNullOrWhiteSpace(customSoundStr))
                    settings.CustomSoundPath = customSoundStr;

                if (map.TryGetValue(SettingKeyHourSlot1, out var h1) && NotificationSettings.IsValidTime(h1))
                    settings.HourSlot1 = h1;

                if (map.TryGetValue(SettingKeyHourSlot2, out var h2) && NotificationSettings.IsValidTime(h2))
                    settings.HourSlot2 = h2;

                if (map.TryGetValue(SettingKeyHourSlot3, out var h3) && NotificationSettings.IsValidTime(h3))
                    settings.HourSlot3 = h3;

                if (map.TryGetValue(SettingKeyHourSlot4, out var h4) && NotificationSettings.IsValidTime(h4))
                    settings.HourSlot4 = h4;

                if (map.TryGetValue(SettingKeyHourSlot5, out var h5) && NotificationSettings.IsValidTime(h5))
                    settings.HourSlot5 = h5;

                if (map.TryGetValue(SettingKeyHourSlot6, out var h6) && NotificationSettings.IsValidTime(h6))
                    settings.HourSlot6 = h6;

                if (map.TryGetValue(SettingKeyHourSlot7, out var h7) && NotificationSettings.IsValidTime(h7))
                    settings.HourSlot7 = h7;

                _cachedSettings = settings.Clone();
            }
            catch
            {
                // Se falhar a leitura no banco, preserva os valores padrão sensatos
                if (_cachedSettings != null)
                    return _cachedSettings.Clone();
            }

            return settings;
        }

        public async Task SaveSettingsAsync(NotificationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            await UpsertSettingAsync(SettingKeyNotificationsEnabled, settings.NotificationsEnabled.ToString().ToLowerInvariant());
            await UpsertSettingAsync(SettingKeySoundEnabled, settings.SoundEnabled.ToString().ToLowerInvariant());
            await UpsertSettingAsync(SettingKeyAdvanceDays, settings.AdvanceDays.ToString());
            await UpsertSettingAsync(SettingKeyOverdueAlertsEnabled, settings.OverdueAlertsEnabled.ToString().ToLowerInvariant());
            await UpsertSettingAsync(SettingKeyMonthlyAlertsEnabled, settings.MonthlyAlertsEnabled.ToString().ToLowerInvariant());
            await UpsertSettingAsync(SettingKeyFifthBusinessDayAlertEnabled, settings.FifthBusinessDayAlertEnabled.ToString().ToLowerInvariant());
            await UpsertSettingAsync(SettingKeySoundType, settings.SoundType);
            await UpsertSettingAsync(SettingKeyCustomSoundPath, settings.CustomSoundPath ?? string.Empty);
            await UpsertSettingAsync(SettingKeyHourSlot1, NotificationSettings.IsValidTime(settings.HourSlot1) ? settings.HourSlot1 : "08:00");
            await UpsertSettingAsync(SettingKeyHourSlot2, NotificationSettings.IsValidTime(settings.HourSlot2) ? settings.HourSlot2 : "10:00");
            await UpsertSettingAsync(SettingKeyHourSlot3, NotificationSettings.IsValidTime(settings.HourSlot3) ? settings.HourSlot3 : "12:00");
            await UpsertSettingAsync(SettingKeyHourSlot4, NotificationSettings.IsValidTime(settings.HourSlot4) ? settings.HourSlot4 : "14:00");
            await UpsertSettingAsync(SettingKeyHourSlot5, NotificationSettings.IsValidTime(settings.HourSlot5) ? settings.HourSlot5 : "16:00");
            await UpsertSettingAsync(SettingKeyHourSlot6, NotificationSettings.IsValidTime(settings.HourSlot6) ? settings.HourSlot6 : "18:00");
            await UpsertSettingAsync(SettingKeyHourSlot7, NotificationSettings.IsValidTime(settings.HourSlot7) ? settings.HourSlot7 : "20:00");

            await _context.SaveChangesAsync();

            _cachedSettings = settings.Clone();
            ScheduleSettingsChanged?.Invoke();
        }

        private async Task UpsertSettingAsync(string key, string value)
        {
            var existing = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (existing != null)
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.Now;
            }
            else
            {
                _context.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = value,
                    UpdatedAt = DateTime.Now
                });
            }
        }

        public async Task<List<NotificationItem>> CheckNotificationsAsync(DateTime? referenceDate = null)
        {
            DateTime now = referenceDate ?? DateTime.Now;
            DateTime today = now.Date;
            var settings = await GetSettingsAsync();

            var result = new List<NotificationItem>();

            // Se notificações estiverem desativadas globalmente pelo usuário, não gera nada
            if (!settings.NotificationsEnabled)
            {
                return result;
            }

            // Sincroniza o status de parcelas vencidas
            await _paymentService.UpdateOverdueInstallmentsAsync(today);

            // ================================================================
            // 1. ANÁLISE DE PARCELAS (Vencimentos Próximos e Atrasadas)
            // ================================================================
            var installments = await _paymentService.GetAllInstallmentsAsync();

            var candidateOverdueInstallments = new List<(Installment item, string clientName, decimal balance)>();
            var candidateUpcomingInstallments = new List<(Installment item, string clientName, decimal balance)>();

            foreach (var inst in installments)
            {
                // Ignora parcelas de clientes arquivados ou sem venda
                if (inst.Sale?.Client != null && inst.Sale.Client.IsArchived)
                    continue;

                decimal remainingBalance = await _paymentService.GetRemainingBalanceForInstallmentAsync(inst.Id);
                // Regra absoluta: Não notificar o que já foi pago. Saldo devedor deve ser > 0.
                if (remainingBalance <= 0)
                    continue;

                string clientName = inst.Sale?.Client?.Name ?? "Cliente";

                if (inst.DueDate.Date < today)
                {
                    if (settings.OverdueAlertsEnabled)
                    {
                        candidateOverdueInstallments.Add((inst, clientName, remainingBalance));
                    }
                }
                else
                {
                    int daysUntilDue = (inst.DueDate.Date - today).Days;
                    if (daysUntilDue <= settings.AdvanceDays)
                    {
                        candidateUpcomingInstallments.Add((inst, clientName, remainingBalance));
                    }
                }
            }

            // Agrupamento para muitas parcelas atrasadas (> 3 itens)
            if (candidateOverdueInstallments.Count > 3)
            {
                decimal totalSaldo = candidateOverdueInstallments.Sum(x => x.balance);
                result.Add(new NotificationItem
                {
                    Title = "Parcelas em atraso",
                    Message = $"Existem {candidateOverdueInstallments.Count} parcelas em atraso. Total em aberto: {totalSaldo.ToString("C2", PtBr)}.",
                    Type = NotificationType.ResumoAtrasados,
                    Amount = totalSaldo,
                    DeduplicationKey = $"ResumoParcelasAtrasadas:{today:yyyy-MM-dd}:{candidateOverdueInstallments.Count}"
                });
            }
            else
            {
                foreach (var (inst, clientName, balance) in candidateOverdueInstallments)
                {
                    result.Add(new NotificationItem
                    {
                        Title = "Parcela em atraso",
                        Message = $"{clientName} possui {balance.ToString("C2", PtBr)} em aberto.",
                        Type = NotificationType.ParcelaAtrasada,
                        ClientName = clientName,
                        InstallmentId = inst.Id,
                        Amount = balance,
                        DeduplicationKey = $"ParcelaAtrasada:{inst.Id}:{today:yyyy-MM-dd}"
                    });
                }
            }

            // Parcelas próximas do vencimento
            if (candidateUpcomingInstallments.Count > 3)
            {
                decimal totalPrevisto = candidateUpcomingInstallments.Sum(x => x.balance);
                result.Add(new NotificationItem
                {
                    Title = "Parcelas próximas",
                    Message = $"Existem {candidateUpcomingInstallments.Count} parcelas próximas do vencimento. Total: {totalPrevisto.ToString("C2", PtBr)}.",
                    Type = NotificationType.ParcelaProxima,
                    Amount = totalPrevisto,
                    DeduplicationKey = $"ResumoParcelasProximas:{today:yyyy-MM-dd}:{candidateUpcomingInstallments.Count}"
                });
            }
            else
            {
                foreach (var (inst, clientName, balance) in candidateUpcomingInstallments)
                {
                    int diff = (inst.DueDate.Date - today).Days;
                    string title = diff switch
                    {
                        0 => "Vencimento hoje",
                        1 => "Vencimento amanhã",
                        _ => "Vencimento próximo"
                    };

                    string msg = diff switch
                    {
                        0 => $"{clientName} possui uma parcela de {balance.ToString("C2", PtBr)} com vencimento hoje.",
                        1 => $"{clientName} possui uma parcela de {balance.ToString("C2", PtBr)} com vencimento amanhã.",
                        _ => $"{clientName} possui uma parcela de {balance.ToString("C2", PtBr)} com vencimento em {inst.DueDate:dd/MM/yyyy}."
                    };

                    result.Add(new NotificationItem
                    {
                        Title = title,
                        Message = msg,
                        Type = NotificationType.ParcelaProxima,
                        ClientName = clientName,
                        InstallmentId = inst.Id,
                        Amount = balance,
                        DeduplicationKey = $"ParcelaProxima:{inst.Id}:{inst.DueDate:yyyy-MM-dd}"
                    });
                }
            }

            // ================================================================
            // 2. ANÁLISE DE MENSALISTAS (Próximas e Atrasadas)
            // ================================================================
            if (settings.MonthlyAlertsEnabled)
            {
                // Regra: Apenas mensalistas ATIVOS geram novos alertas
                var activeMonthly = await _monthlyService.GetActiveMonthlyCustomersAsync();

                var candidateOverdueMonthly = new List<(MonthlyCustomer item, string clientName, DateTime dueDate)>();
                var candidateUpcomingMonthly = new List<(MonthlyCustomer item, string clientName, DateTime dueDate)>();

                foreach (var customer in activeMonthly)
                {
                    if (customer.Client != null && customer.Client.IsArchived)
                        continue;

                    string clientName = customer.Client?.Name ?? "Cliente";

                    // Vencimento no mês de referência
                    int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
                    int dueDayClamped = Math.Min(customer.DueDay, daysInMonth);
                    var currentMonthDueDate = new DateTime(today.Year, today.Month, dueDayClamped);

                    // Verifica se a competência do mês corrente já foi quitada
                    var payments = await _monthlyService.GetPaymentsByMonthlyCustomerAsync(customer.Id);
                    bool isCurrentMonthPaid = payments.Any(p => p.ReferenceMonth == today.Month && p.ReferenceYear == today.Year);

                    if (isCurrentMonthPaid)
                    {
                        // Regra absoluta: Se estiver quitado, não notificar como pendente!
                        continue;
                    }

                    if (currentMonthDueDate.Date < today)
                    {
                        if (settings.OverdueAlertsEnabled)
                        {
                            candidateOverdueMonthly.Add((customer, clientName, currentMonthDueDate));
                        }
                    }
                    else
                    {
                        int diff = (currentMonthDueDate.Date - today).Days;
                        if (diff <= settings.AdvanceDays)
                        {
                            candidateUpcomingMonthly.Add((customer, clientName, currentMonthDueDate));
                        }
                    }
                }

                // Agrupamento para mensalidades atrasadas (> 3 itens)
                if (candidateOverdueMonthly.Count > 3)
                {
                    decimal totalMonthly = candidateOverdueMonthly.Sum(x => x.item.MonthlyAmount);
                    result.Add(new NotificationItem
                    {
                        Title = "Mensalidades em atraso",
                        Message = $"Existem {candidateOverdueMonthly.Count} mensalidades em atraso. Total em aberto: {totalMonthly.ToString("C2", PtBr)}.",
                        Type = NotificationType.ResumoMensalidades,
                        Amount = totalMonthly,
                        DeduplicationKey = $"ResumoMensalidadesAtrasadas:{today:yyyy-MM-dd}:{candidateOverdueMonthly.Count}"
                    });
                }
                else
                {
                    foreach (var (customer, clientName, dueDate) in candidateOverdueMonthly)
                    {
                        result.Add(new NotificationItem
                        {
                            Title = "Mensalidade em atraso",
                            Message = $"{clientName} possui mensalidade de {customer.MonthlyAmount.ToString("C2", PtBr)} em atraso.",
                            Type = NotificationType.MensalidadeAtrasada,
                            ClientName = clientName,
                            MonthlyCustomerId = customer.Id,
                            Amount = customer.MonthlyAmount,
                            DeduplicationKey = $"MensalidadeAtrasada:{customer.Id}:{today.Year}-{today.Month}:{today:yyyy-MM-dd}"
                        });
                    }
                }

                // Mensalidades próximas do vencimento
                if (candidateUpcomingMonthly.Count > 3)
                {
                    decimal totalMonthly = candidateUpcomingMonthly.Sum(x => x.item.MonthlyAmount);
                    result.Add(new NotificationItem
                    {
                        Title = "Mensalidades próximas",
                        Message = $"Existem {candidateUpcomingMonthly.Count} mensalidades com vencimento próximo. Total: {totalMonthly.ToString("C2", PtBr)}.",
                        Type = NotificationType.MensalidadeProxima,
                        Amount = totalMonthly,
                        DeduplicationKey = $"ResumoMensalidadesProximas:{today:yyyy-MM-dd}:{candidateUpcomingMonthly.Count}"
                    });
                }
                else
                {
                    foreach (var (customer, clientName, dueDate) in candidateUpcomingMonthly)
                    {
                        int diff = (dueDate.Date - today).Days;
                        string title = diff switch
                        {
                            0 => "Mensalidade hoje",
                            1 => "Mensalidade amanhã",
                            _ => "Mensalidade próxima"
                        };

                        string msg = diff switch
                        {
                            0 => $"{clientName} possui mensalidade de {customer.MonthlyAmount.ToString("C2", PtBr)} com vencimento hoje.",
                            1 => $"{clientName} possui mensalidade de {customer.MonthlyAmount.ToString("C2", PtBr)} com vencimento amanhã.",
                            _ => $"{clientName} possui mensalidade de {customer.MonthlyAmount.ToString("C2", PtBr)} com vencimento em {dueDate:dd/MM/yyyy}."
                        };

                        result.Add(new NotificationItem
                        {
                            Title = title,
                            Message = msg,
                            Type = NotificationType.MensalidadeProxima,
                            ClientName = clientName,
                            MonthlyCustomerId = customer.Id,
                            Amount = customer.MonthlyAmount,
                            DeduplicationKey = $"MensalidadeProxima:{customer.Id}:{today.Year}-{today.Month}:{dueDate:yyyy-MM-dd}"
                        });
                    }
                }
            }

            // ================================================================
            // 3. ANÁLISE DO 5º DIA ÚTIL (Reutilizando BusinessDayHelper)
            // ================================================================
            if (settings.FifthBusinessDayAlertEnabled)
            {
                DateTime fifthBusinessDay = BusinessDayHelper.GetFifthBusinessDay(today.Year, today.Month);

                if (today == fifthBusinessDay.Date)
                {
                    result.Add(new NotificationItem
                    {
                        Title = "5º dia útil",
                        Message = $"Hoje é o 5º dia útil do mês ({fifthBusinessDay:dd/MM/yyyy}).",
                        Type = NotificationType.QuintoDiaUtil,
                        DeduplicationKey = $"QuintoDiaUtil:{fifthBusinessDay.Year}-{fifthBusinessDay.Month}:{today:yyyy-MM-dd}"
                    });
                }
                else if (today < fifthBusinessDay.Date)
                {
                    int diff = (fifthBusinessDay.Date - today).Days;
                    if (diff <= settings.AdvanceDays)
                    {
                        string msg = diff == 1
                            ? $"O 5º dia útil deste mês é amanhã ({fifthBusinessDay:dd/MM/yyyy})."
                            : $"O 5º dia útil deste mês é em {fifthBusinessDay:dd/MM/yyyy}.";

                        result.Add(new NotificationItem
                        {
                            Title = "5º dia útil",
                            Message = msg,
                            Type = NotificationType.QuintoDiaUtil,
                            DeduplicationKey = $"QuintoDiaUtilProximo:{fifthBusinessDay.Year}-{fifthBusinessDay.Month}:{today:yyyy-MM-dd}"
                        });
                    }
                }
            }

            // ================================================================
            // 4. ANÁLISE DE LEMBRETES E TAREFAS PENDENTES
            // ================================================================
            try
            {
                var dueReminders = await _context.Reminders
                    .AsNoTracking()
                    .Where(r => r.Status == ReminderStatus.Pending && r.DueDate.HasValue && r.DueDate.Value <= now)
                    .ToListAsync();

                foreach (var rem in dueReminders)
                {
                    var remDueDate = rem.DueDate!.Value;
                    bool isOverdue = (now - remDueDate).TotalMinutes > 2;
                    string prefix = isOverdue ? "Lembrete atrasado" : "Lembrete";
                    string timeStr = remDueDate.ToString("dd/MM HH:mm");

                    result.Add(new NotificationItem
                    {
                        Title = $"{prefix}: {rem.Title}",
                        Message = string.IsNullOrWhiteSpace(rem.Description)
                            ? $"Vencimento: {timeStr} | Prioridade: {rem.PriorityLabel}"
                            : $"{rem.Description} (Venc: {timeStr})",
                        Type = NotificationType.Lembrete,
                        ClientName = rem.Category,
                        Amount = 0,
                        DeduplicationKey = $"Lembrete:{rem.Id}:{remDueDate:yyyyMMdd_HHmm}:{rem.SnoozeCount}"
                    });
                }
            }
            catch
            {
                // Resiliente caso a tabela esteja sendo inicializada
            }

            return result;
        }

        public static DateTime? LastVerificationTime { get; set; }
        DateTime? INotificationService.LastVerificationTime => LastVerificationTime;

        public async Task<int> CheckAndNotifyAsync(DateTime? referenceDate = null, bool force = false)
        {
            DateTime today = (referenceDate ?? DateTime.Today).Date;
            var settings = await GetSettingsAsync();

            if (!settings.NotificationsEnabled)
            {
                return 0;
            }

            LastVerificationTime = DateTime.Now;

            var candidates = await CheckNotificationsAsync(today);
            var toNotify = new List<NotificationItem>();

            foreach (var item in candidates)
            {
                bool alreadyNotified = (!force || item.Type == NotificationType.Lembrete)
                    && await HasBeenNotifiedTodayAsync(item.DeduplicationKey, today);
                if (!alreadyNotified)
                {
                    toNotify.Add(item);
                }
            }

            if (toNotify.Count == 0)
            {
                return 0;
            }

            // Apresenta notificações e registra histórico
            foreach (var item in toNotify)
            {
                await MarkAsNotifiedAsync(item.DeduplicationKey, today);
                Notify(item);
            }

            // Reproduz som nativo uma única vez por lote de notificações se ativado
            if (settings.SoundEnabled)
            {
                PlayNotificationSound();
            }

            return toNotify.Count;
        }

        public Task<int> CheckAndNotifyAsync(DateTime? referenceDate = null) => CheckAndNotifyAsync(referenceDate, false);

        public async Task<bool> HasBeenNotifiedTodayAsync(string deduplicationKey, DateTime today)
        {
            if (string.IsNullOrWhiteSpace(deduplicationKey)) return false;

            string key = "NotificationLog:" + deduplicationKey;
            string dateStr = today.ToString("yyyy-MM-dd");

            try
            {
                var log = await _context.AppSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == key);

                return log != null && log.Value == dateStr;
            }
            catch
            {
                return false;
            }
        }

        public async Task MarkAsNotifiedAsync(string deduplicationKey, DateTime today)
        {
            if (string.IsNullOrWhiteSpace(deduplicationKey)) return;

            string key = "NotificationLog:" + deduplicationKey;
            string dateStr = today.ToString("yyyy-MM-dd");

            try
            {
                var existing = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
                if (existing != null)
                {
                    existing.Value = dateStr;
                    existing.UpdatedAt = DateTime.Now;
                }
                else
                {
                    _context.AppSettings.Add(new AppSetting
                    {
                        Key = key,
                        Value = dateStr,
                        UpdatedAt = DateTime.Now
                    });
                }
                await _context.SaveChangesAsync();
            }
            catch
            {
                // Silencioso em caso de falha de I/O
            }
        }

        public void PlayNotificationSound()
        {
            try
            {
                var settings = GetSettingsAsync().GetAwaiter().GetResult();
                if (!settings.SoundEnabled) return;
                PlaySound(settings.SoundType, settings.CustomSoundPath);
            }
            catch
            {
                PlaySound("Asterisk");
            }
        }

        public void PlaySound(string? soundType = null, string? customPath = null)
        {
            if (_soundPlayer != null)
            {
                _soundPlayer();
                return;
            }

            // 1. Prioriza arquivo personalizado se configurado e existente
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                try
                {
                    if (System.IO.File.Exists(customPath))
                    {
                        using var player = new System.Media.SoundPlayer(customPath);
                        player.Play();
                        return;
                    }
                }
                catch
                {
                    // Fallback para som nativo se arquivo personalizado falhar
                }
            }

            // 2. Tenta reproduzir arquivo nativo correspondente em C:\Windows\Media via SoundPlayer
            // (funciona de forma confiável mesmo se o esquema do Windows estiver sem som)
            try
            {
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string mediaDir = System.IO.Path.Combine(winDir, "Media");
                string target = soundType ?? "Asterisk";

                string? wavFile = target.ToLowerInvariant() switch
                {
                    "beep" => FindFirstExistingWav(mediaDir, "ding.wav", "Windows Ding.wav"),
                    "exclamation" => FindFirstExistingWav(mediaDir, "Windows Exclamation.wav", "chord.wav"),
                    "hand" => FindFirstExistingWav(mediaDir, "Windows Critical Stop.wav", "chord.wav"),
                    "question" => FindFirstExistingWav(mediaDir, "chimes.wav", "Windows Notify.wav"),
                    _ => FindFirstExistingWav(mediaDir, "Windows Notify.wav", "Windows Ding.wav", "ding.wav")
                };

                if (wavFile != null && System.IO.File.Exists(wavFile))
                {
                    using var player = new System.Media.SoundPlayer(wavFile);
                    player.Play();
                    return;
                }
            }
            catch
            {
                // Fallback para SystemSounds
            }

            // 3. Fallback clássico para SystemSounds nativos
            try
            {
                string target = soundType ?? "Asterisk";
                switch (target.ToLowerInvariant())
                {
                    case "beep":
                        System.Media.SystemSounds.Beep.Play();
                        break;
                    case "exclamation":
                        System.Media.SystemSounds.Exclamation.Play();
                        break;
                    case "hand":
                        System.Media.SystemSounds.Hand.Play();
                        break;
                    case "question":
                        System.Media.SystemSounds.Question.Play();
                        break;
                    case "asterisk":
                    default:
                        System.Media.SystemSounds.Asterisk.Play();
                        break;
                }
            }
            catch
            {
                // Tratamento seguro caso dispositivo de áudio não esteja disponível no Windows
            }
        }

        private static string? FindFirstExistingWav(string dir, params string[] fileNames)
        {
            if (!System.IO.Directory.Exists(dir)) return null;

            foreach (var name in fileNames)
            {
                string fullPath = System.IO.Path.Combine(dir, name);
                if (System.IO.File.Exists(fullPath))
                    return fullPath;
            }
            return null;
        }

        public void Notify(NotificationItem item)
        {
            if (item == null) return;

            if (_presenter != null)
            {
                _presenter(item);
                return;
            }

            try
            {
                if (Application.Current != null && Application.Current.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        NotificationToastWindow.ShowNotification(item);
                    });
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Instance.LogError("NotificationService", ex, "Falha ao apresentar notificação visual toast");
            }
        }

        public DateTime GetNextScheduledTime(NotificationSettings settings, DateTime? fromTime = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var now = fromTime ?? DateTime.Now;
            var validSlots = settings.GetValidTimeSpans(); // Já ordenados cronologicamente
            if (validSlots.Count == 0)
            {
                return now.Date.AddDays(1).AddHours(8);
            }

            var currentTimeOfDay = now.TimeOfDay;
            foreach (var slot in validSlots)
            {
                if (slot > currentTimeOfDay)
                {
                    return now.Date.Add(slot);
                }
            }

            // Se todos os horários de hoje já passaram, o próximo é o primeiro horário de amanhã
            return now.Date.AddDays(1).Add(validSlots[0]);
        }

        public async Task<int> CheckScheduledNotificationsAsync(DateTime? referenceDateTime = null)
        {
            DateTime now = referenceDateTime ?? DateTime.Now;
            var settings = _cachedSettings ?? await GetSettingsAsync();

            if (!settings.NotificationsEnabled)
            {
                return 0;
            }

            int totalNotified = 0;

            // 1. Verificação contínua em tempo real de Lembretes & Tarefas pendentes
            totalNotified += await CheckDueRemindersOnlyAsync(now, settings);

            // 2. Verificação de horários programados para parcelas e mensalidades (lotes diários)
            var validSlots = settings.GetValidTimeSpans();
            if (validSlots.Count > 0)
            {
                bool matchesAnySlot = false;
                TimeSpan matchedSlot = TimeSpan.Zero;
                foreach (var slot in validSlots)
                {
                    if (slot.Hours == now.Hour && slot.Minutes == now.Minute)
                    {
                        matchesAnySlot = true;
                        matchedSlot = slot;
                        break;
                    }
                }

                if (matchesAnySlot)
                {
                    // Deduplicação por horário para não disparar múltiplas vezes dentro do mesmo minuto
                    string slotDeduplicationKey = $"ScheduledSlotFired:{now:yyyy-MM-dd}_{matchedSlot.Hours:D2}:{matchedSlot.Minutes:D2}";
                    bool alreadyFiredForSlot = await HasBeenNotifiedTodayAsync(slotDeduplicationKey, now.Date);
                    if (!alreadyFiredForSlot)
                    {
                        await MarkAsNotifiedAsync(slotDeduplicationKey, now.Date);
                        LastVerificationTime = now;
                        totalNotified += await CheckAndNotifyAsync(now.Date, force: true);
                    }
                }
            }

            return totalNotified;
        }

        public async Task<int> CheckDueRemindersOnlyAsync(DateTime now, NotificationSettings? settings = null)
        {
            try
            {
                var s = settings ?? _cachedSettings ?? await GetSettingsAsync();
                if (!s.NotificationsEnabled) return 0;

                var dueReminders = await _context.Reminders
                    .AsNoTracking()
                    .Where(r => r.Status == ReminderStatus.Pending && r.DueDate.HasValue && r.DueDate.Value <= now)
                    .ToListAsync();

                if (dueReminders.Count == 0) return 0;

                var toNotify = new List<NotificationItem>();
                foreach (var rem in dueReminders)
                {
                    var remDueDate = rem.DueDate!.Value;
                    string key = $"Lembrete:{rem.Id}:{remDueDate:yyyyMMdd_HHmm}:{rem.SnoozeCount}";
                    bool alreadyNotified = await HasBeenNotifiedTodayAsync(key, now.Date);
                    if (!alreadyNotified)
                    {
                        bool isOverdue = (now - remDueDate).TotalMinutes > 2;
                        string prefix = isOverdue ? "Lembrete atrasado" : "Lembrete";
                        string timeStr = remDueDate.ToString("dd/MM HH:mm");

                        toNotify.Add(new NotificationItem
                        {
                            Title = $"{prefix}: {rem.Title}",
                            Message = string.IsNullOrWhiteSpace(rem.Description)
                                ? $"Vencimento: {timeStr} | Prioridade: {rem.PriorityLabel}"
                                : $"{rem.Description} (Venc: {timeStr})",
                            Type = NotificationType.Lembrete,
                            ClientName = rem.Category,
                            Amount = 0,
                            DeduplicationKey = key
                        });
                    }
                }

                if (toNotify.Count == 0) return 0;

                foreach (var item in toNotify)
                {
                    await MarkAsNotifiedAsync(item.DeduplicationKey, now.Date);
                    Notify(item);
                }

                if (s.SoundEnabled)
                {
                    PlayNotificationSound();
                }

                LastVerificationTime = now;
                return toNotify.Count;
            }
            catch
            {
                return 0;
            }
        }
    }
}
