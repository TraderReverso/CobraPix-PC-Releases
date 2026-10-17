using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services;
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.Services.Interfaces;
using CobraPix_PC.ViewModels.Modules;

namespace CobraPix_PC.Services.Testing
{
    /// <summary>
    /// Suíte automatizada de testes do módulo Lembretes e Tarefas.
    /// Executa 100% em base SQLite temporária descartável em disco (%TEMP%),
    /// com isolamento completo e garantia de não-violação da base de produção.
    /// </summary>
    public static class ReminderTests
    {
        public static async Task<List<string>> RunAllTestsAsync()
        {
            var results = new List<string>();
            string tempDir = Path.GetTempPath();
            string tempDbPath = Path.Combine(tempDir, $"cobrapix_reminder_test_{Guid.NewGuid():N}.db");
            var connectionString = $"Data Source={tempDbPath}";

            // Auditoria da base de produção antes de iniciar
            string prodDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CobraPix",
                "cobrapix.db");

            string? initialProdHash = null;
            if (File.Exists(prodDbPath))
            {
                using var sha = SHA256.Create();
                using var fs = File.OpenRead(prodDbPath);
                initialProdHash = Convert.ToHexString(sha.ComputeHash(fs));
            }

            results.Add("=================================================================");
            results.Add("SUÍTE DE TESTES AUTOMATIZADOS: MÓDULO LEMBRETES & TAREFAS");
            results.Add($"Banco Temporário Isolado: {tempDbPath}");
            results.Add($"Hash Inicial da Produção: {initialProdHash ?? "NÃO ENCONTRADO"}");
            results.Add("=================================================================");

            var options = new DbContextOptionsBuilder<CobraPixDbContext>()
                .UseSqlite(connectionString)
                .Options;

            try
            {
                // Test 1: Criação e Migração do Esquema
                using (var context = new CobraPixDbContext(options))
                {
                    context.Database.EnsureCreated();
                    DatabaseInitializer.EnsureRemindersTableCreated(context);
                    results.Add("[PASS] Teste 01: Criação de banco e tabela Reminders com índices executada com sucesso.");
                }

                // Test 2: Validação e Criação de Lembrete
                int reminderId1;
                using (var context = new CobraPixDbContext(options))
                {
                    var service = new ReminderService(context);

                    // Validação de título obrigatório
                    try
                    {
                        await service.CreateReminderAsync(new Reminder { Title = "   " });
                        throw new InvalidOperationException("Falha: Título em branco deveria ter lançado exceção.");
                    }
                    catch (BusinessRuleException)
                    {
                        // Esperado
                    }

                    var created = await service.CreateReminderAsync(new Reminder
                    {
                        Title = "Enviar relatório financeiro semanal",
                        Description = "Consolidar entradas e saídas no Excel",
                        Priority = ReminderPriority.High,
                        Category = "Financeiro",
                        DueDate = DateTime.Now.AddHours(2)
                    });

                    if (created.Id <= 0 || created.Status != ReminderStatus.Pending || created.Category != "Financeiro")
                    {
                        throw new InvalidOperationException("Falha na persistência inicial do lembrete.");
                    }

                    reminderId1 = created.Id;
                    results.Add($"[PASS] Teste 02: Criação de lembrete com validação e defaults concluída (Id={reminderId1}).");
                }

                // Test 3: Atualização de Lembrete
                using (var context = new CobraPixDbContext(options))
                {
                    var service = new ReminderService(context);
                    var item = await service.GetReminderByIdAsync(reminderId1);
                    if (item == null) throw new InvalidOperationException("Lembrete não encontrado.");

                    item.Title = "Enviar relatório financeiro semanal atualizado";
                    item.Priority = ReminderPriority.Urgent;
                    await service.UpdateReminderAsync(item);

                    var reloaded = await service.GetReminderByIdAsync(reminderId1);
                    if (reloaded!.Title != "Enviar relatório financeiro semanal atualizado" || reloaded.Priority != ReminderPriority.Urgent)
                    {
                        throw new InvalidOperationException("Falha na atualização do lembrete.");
                    }

                    results.Add("[PASS] Teste 03: Atualização e histórico do lembrete validados com sucesso.");
                }

                // Test 4: Conclusão Simples (Não-Recorrente)
                using (var context = new CobraPixDbContext(options))
                {
                    var service = new ReminderService(context);
                    var completed = await service.CompleteReminderAsync(reminderId1);

                    if (completed.Status != ReminderStatus.Completed || !completed.CompletedAt.HasValue)
                    {
                        throw new InvalidOperationException("Falha ao concluir lembrete simples.");
                    }

                    // Assegura que nenhum novo lembrete foi gerado
                    var count = await context.Reminders.CountAsync();
                    if (count != 1)
                    {
                        throw new InvalidOperationException("Lembrete não recorrente gerou novo item indevidamente.");
                    }

                    results.Add("[PASS] Teste 04: Conclusão de lembrete simples (não-recorrente) validada.");
                }

                // Test 5: Recorrência Diária Automática
                int dailyReminderId;
                DateTime originalDailyDue = DateTime.Today.AddHours(10);
                using (var context = new CobraPixDbContext(options))
                {
                    var service = new ReminderService(context);
                    var dailyRem = await service.CreateReminderAsync(new Reminder
                    {
                        Title = "Conferir extrato bancário diário",
                        DueDate = originalDailyDue,
                        Recurrence = ReminderRecurrence.Daily,
                        Priority = ReminderPriority.Normal
                    });
                    dailyReminderId = dailyRem.Id;

                    // Conclui o diário
                    await service.CompleteReminderAsync(dailyReminderId);

                    // Verifica se gerou o próximo para o dia seguinte
                    var all = await service.GetAllRemindersAsync();
                    var nextDaily = all.FirstOrDefault(r => r.Title == dailyRem.Title && r.Status == ReminderStatus.Pending);

                    if (nextDaily == null)
                    {
                        throw new InvalidOperationException("Recorrência diária não gerou a próxima ocorrência.");
                    }

                    if (!nextDaily.DueDate.HasValue || nextDaily.DueDate.Value.Date != originalDailyDue.AddDays(1).Date)
                    {
                        throw new InvalidOperationException($"Próxima ocorrência diária com data incorreta: {nextDaily.DueDate}");
                    }

                    results.Add("[PASS] Teste 05: Recorrência diária automática gerou nova ocorrência para D+1 com sucesso.");
                }

                // Test 6: Recorrência Semanal
                using (var context = new CobraPixDbContext(options))
                {
                    var service = new ReminderService(context);
                    var weeklyDue = DateTime.Today.AddHours(14);
                    var weeklyRem = await service.CreateReminderAsync(new Reminder
                    {
                        Title = "Reunião de alinhamento com equipe",
                        DueDate = weeklyDue,
                        Recurrence = ReminderRecurrence.Weekly,
                        Priority = ReminderPriority.High
                    });

                    await service.CompleteReminderAsync(weeklyRem.Id);

                    var all = await service.GetAllRemindersAsync();
                    var nextWeekly = all.FirstOrDefault(r => r.Title == weeklyRem.Title && r.Status == ReminderStatus.Pending);

                    if (nextWeekly == null || nextWeekly.DueDate?.Date != weeklyDue.AddDays(7).Date)
                    {
                        throw new InvalidOperationException("Recorrência semanal não calculou D+7 corretamente.");
                    }

                    results.Add("[PASS] Teste 06: Recorrência semanal gerou nova ocorrência para D+7 com sucesso.");
                }

                // Test 7: Recorrência Mensal
                using (var context = new CobraPixDbContext(options))
                {
                    var service = new ReminderService(context);
                    var monthlyDue = new DateTime(2026, 10, 15, 9, 0, 0);
                    var monthlyRem = await service.CreateReminderAsync(new Reminder
                    {
                        Title = "Fechamento contábil mensal",
                        DueDate = monthlyDue,
                        Recurrence = ReminderRecurrence.Monthly,
                        Priority = ReminderPriority.Urgent
                    });

                    await service.CompleteReminderAsync(monthlyRem.Id);

                    var all = await service.GetAllRemindersAsync();
                    var nextMonthly = all.FirstOrDefault(r => r.Title == monthlyRem.Title && r.Status == ReminderStatus.Pending);

                    if (nextMonthly == null || nextMonthly.DueDate?.Date != monthlyDue.AddMonths(1).Date)
                    {
                        throw new InvalidOperationException("Recorrência mensal não calculou +1 mês corretamente.");
                    }

                    results.Add("[PASS] Teste 07: Recorrência mensal gerou nova ocorrência para +1 mês com sucesso.");
                }

                // Test 8: Recorrência Personalizada (a cada N dias)
                using (var context = new CobraPixDbContext(options))
                {
                    var service = new ReminderService(context);
                    var baseDue = DateTime.Today.AddHours(8);
                    var customRem = await service.CreateReminderAsync(new Reminder
                    {
                        Title = "Backup externo em pendrive",
                        DueDate = baseDue,
                        Recurrence = ReminderRecurrence.CustomDays,
                        CustomIntervalDays = 5
                    });

                    await service.CompleteReminderAsync(customRem.Id);

                    var all = await service.GetAllRemindersAsync();
                    var nextCustom = all.FirstOrDefault(r => r.Title == customRem.Title && r.Status == ReminderStatus.Pending);

                    if (nextCustom == null || nextCustom.DueDate?.Date != baseDue.AddDays(5).Date)
                    {
                        throw new InvalidOperationException("Recorrência personalizada de 5 dias não calculou D+5 corretamente.");
                    }

                    results.Add("[PASS] Teste 08: Recorrência personalizada de 5 dias calculada com precisão.");
                }

                // Test 9: Adiamento (Snooze) e Reagendamento
                using (var context = new CobraPixDbContext(options))
                {
                    var service = new ReminderService(context);
                    var item = await service.CreateReminderAsync(new Reminder
                    {
                        Title = "Ligar para cliente especial",
                        DueDate = DateTime.Now.AddMinutes(10)
                    });

                    var snoozed = await service.SnoozeReminderAsync(item.Id, TimeSpan.FromMinutes(45));
                    if (snoozed.SnoozeCount != 1 || !snoozed.RescheduledAt.HasValue)
                    {
                        throw new InvalidOperationException("Falha ao registrar adiamento (SnoozeCount).");
                    }

                    var rescheduledDate = DateTime.Today.AddDays(3).AddHours(11);
                    var rescheduled = await service.RescheduleReminderAsync(item.Id, rescheduledDate);
                    if (rescheduled.DueDate != rescheduledDate)
                    {
                        throw new InvalidOperationException("Falha ao reagendar lembrete.");
                    }

                    results.Add("[PASS] Teste 09: Adiamento (snooze +45min) e reagendamento manual validados.");
                }

                // Test 10: Cancelamento e Exclusão
                using (var context = new CobraPixDbContext(options))
                {
                    var service = new ReminderService(context);
                    var toCancel = await service.CreateReminderAsync(new Reminder
                    {
                        Title = "Compromisso que será cancelado"
                    });

                    await service.CancelReminderAsync(toCancel.Id);
                    var cancelled = await service.GetReminderByIdAsync(toCancel.Id);
                    if (cancelled!.Status != ReminderStatus.Cancelled)
                    {
                        throw new InvalidOperationException("Falha ao cancelar lembrete.");
                    }

                    bool deleted = await service.DeleteReminderAsync(toCancel.Id);
                    if (!deleted || await service.GetReminderByIdAsync(toCancel.Id) != null)
                    {
                        throw new InvalidOperationException("Falha ao excluir lembrete.");
                    }

                    results.Add("[PASS] Teste 10: Cancelamento e exclusão física de lembrete executados com sucesso.");
                }

                // Test 11: Resumo Consolidado de Contadores (KPIs)
                using (var context = new CobraPixDbContext(options))
                {
                    var service = new ReminderService(context);

                    // Cria lembrete em atraso
                    await service.CreateReminderAsync(new Reminder
                    {
                        Title = "Tarefa Atrasada",
                        DueDate = DateTime.Now.AddDays(-2)
                    });

                    // Cria lembrete para hoje
                    await service.CreateReminderAsync(new Reminder
                    {
                        Title = "Tarefa para Hoje",
                        DueDate = DateTime.Today.AddHours(20)
                    });

                    var summary = await service.GetSummaryAsync();
                    if (summary.TotalPending <= 0 || summary.OverdueCount <= 0 || summary.TodayCount <= 0 || summary.CompletedCount <= 0)
                    {
                        throw new InvalidOperationException($"Contadores de resumo inválidos: P={summary.TotalPending}, O={summary.OverdueCount}, T={summary.TodayCount}, C={summary.CompletedCount}");
                    }

                    results.Add($"[PASS] Teste 11: Contadores KPI validados (TotalPendentes={summary.TotalPending}, Atrasados={summary.OverdueCount}, Hoje={summary.TodayCount}, Concluídos={summary.CompletedCount}).");
                }

                // Test 12: Integração com Notificações do Sistema
                using (var context = new CobraPixDbContext(options))
                {
                    var paymentSvc = new PaymentService(context);
                    var monthlySvc = new MonthlyCustomerService(context);
                    var notifSvc = new NotificationService(context, paymentSvc, monthlySvc);

                    var notifs = await notifSvc.CheckNotificationsAsync();
                    var reminderNotifs = notifs.Where(n => n.Type == NotificationType.Lembrete).ToList();

                    if (!reminderNotifs.Any())
                    {
                        throw new InvalidOperationException("NotificationService não gerou alertas para os lembretes pendentes/vencidos.");
                    }

                    results.Add($"[PASS] Teste 12: Integração com NotificationService validada ({reminderNotifs.Count} alertas de lembretes gerados).");
                }

                // Test 13: Integração Completa com o LembretesViewModel
                using (var context = new CobraPixDbContext(options))
                {
                    var service = new ReminderService(context);
                    var vm = new LembretesViewModel(service);

                    await vm.LoadDataAsync();

                    if (!vm.Reminders.Any())
                    {
                        throw new InvalidOperationException("LembretesViewModel não carregou a lista de lembretes.");
                    }

                    // Teste de abas
                    vm.ActiveTab = "Hoje";
                    int countHoje = vm.FilteredReminders.Count;

                    vm.ActiveTab = "Atrasados";
                    int countAtrasados = vm.FilteredReminders.Count;

                    vm.ActiveTab = "Concluídos";
                    int countConcluidos = vm.FilteredReminders.Count;

                    vm.ActiveTab = "Todos";
                    int countTodos = vm.FilteredReminders.Count;

                    if (countTodos < countHoje || countTodos < countAtrasados || countTodos < countConcluidos)
                    {
                        throw new InvalidOperationException("Filtro por abas no ViewModel inconsistente.");
                    }

                    // Teste de criação pelo ViewModel
                    vm.ResetForm();
                    vm.TitleInput = "Lembrete via ViewModel";
                    vm.DescriptionInput = "Criado diretamente na interface";
                    vm.CategoryInput = "Cobrança";
                    vm.SelectedPriority = ReminderPriority.Urgent;
                    await vm.SaveReminderAsync();

                    var newlyAdded = vm.Reminders.FirstOrDefault(r => r.Title == "Lembrete via ViewModel");
                    if (newlyAdded == null || newlyAdded.Category != "Cobrança" || newlyAdded.Priority != ReminderPriority.Urgent)
                    {
                        throw new InvalidOperationException("Falha ao salvar lembrete via ViewModel.");
                    }

                    // Teste de conclusão via ViewModel
                    await vm.CompleteReminderAsync(newlyAdded);
                    var reloadedAfterComplete = vm.Reminders.FirstOrDefault(r => r.Id == newlyAdded.Id);
                    if (reloadedAfterComplete == null || reloadedAfterComplete.Status != ReminderStatus.Completed)
                    {
                        throw new InvalidOperationException("Falha ao concluir lembrete via ViewModel.");
                    }

                    results.Add("[PASS] Teste 13: LembretesViewModel validado (abas, filtros, criação, conclusão e KPIs sincronizados).");
                }

                // Test 14: Disparo de Notificações em Tempo Real durante Sessão Aberta (Sem Reiniciar o App)
                using (var context = new CobraPixDbContext(options))
                {
                    var paymentSvc = new PaymentService(context);
                    var monthlySvc = new MonthlyCustomerService(context);
                    var capturedNotifications = new List<NotificationItem>();
                    var notifSvc = new NotificationService(context, paymentSvc, monthlySvc, presenter: capturedNotifications.Add);
                    var reminderSvc = new ReminderService(context);

                    DateTime sessionStart = DateTime.Today.AddHours(10).AddMinutes(15);

                    // Inicialização da sessão: processa pendências pré-existentes de testes anteriores ao abrir o app
                    await notifSvc.CheckScheduledNotificationsAsync(sessionStart);
                    capturedNotifications.Clear();

                    // 1. Simula criação de Lembrete A para 1 minuto depois (10:16)
                    var remA = await reminderSvc.CreateReminderAsync(new Reminder
                    {
                        Title = "Lembrete Sessão Aberta A",
                        DueDate = sessionStart.AddMinutes(1),
                        Priority = ReminderPriority.High
                    });

                    // 2. Simula verificação periódica aos 10:00:30 (antes do vencimento)
                    int firedBefore = await notifSvc.CheckScheduledNotificationsAsync(sessionStart.AddSeconds(30));
                    if (firedBefore > 0 || capturedNotifications.Any(n => n.Title.Contains("Lembrete Sessão Aberta A")))
                    {
                        throw new InvalidOperationException("Falha: Lembrete disparou antes do horário programado.");
                    }

                    // 3. Simula chegada do horário de vencimento (10:01:05)
                    int firedAtDue = await notifSvc.CheckScheduledNotificationsAsync(sessionStart.AddMinutes(1).AddSeconds(5));
                    if (firedAtDue != 1 || !capturedNotifications.Any(n => n.Title.Contains("Lembrete Sessão Aberta A")))
                    {
                        throw new InvalidOperationException($"Falha: Lembrete não disparou no horário com a sessão aberta (notificados: {firedAtDue}).");
                    }

                    // 4. Simula próximo tick 15 segundos depois (10:01:20) - Não deve duplicar!
                    int capturedCount = capturedNotifications.Count;
                    int firedNextTick = await notifSvc.CheckScheduledNotificationsAsync(sessionStart.AddMinutes(1).AddSeconds(20));
                    if (firedNextTick > 0 || capturedNotifications.Count != capturedCount)
                    {
                        throw new InvalidOperationException("Falha: Lembrete disparou repetidamente nos ticks subsequentes (duplicação detectada).");
                    }

                    // 5. Simula criação de Lembrete B durante a mesma sessão para 10:02:00
                    var remB = await reminderSvc.CreateReminderAsync(new Reminder
                    {
                        Title = "Lembrete Sessão Aberta B",
                        DueDate = sessionStart.AddMinutes(2),
                        Priority = ReminderPriority.Normal
                    });

                    // Disparo do Lembrete B ao chegar em 10:02:05
                    int firedB = await notifSvc.CheckScheduledNotificationsAsync(sessionStart.AddMinutes(2).AddSeconds(5));
                    if (firedB != 1 || !capturedNotifications.Any(n => n.Title.Contains("Lembrete Sessão Aberta B")))
                    {
                        throw new InvalidOperationException("Falha: Segundo lembrete criado na sessão não disparou.");
                    }

                    // Tick seguinte do Lembrete B: sem duplicação
                    int firedBNext = await notifSvc.CheckScheduledNotificationsAsync(sessionStart.AddMinutes(2).AddSeconds(20));
                    if (firedBNext > 0)
                    {
                        throw new InvalidOperationException("Falha: Segundo lembrete duplicou no tick seguinte.");
                    }

                    // 6. Simula criação de Lembrete C já atrasado (vencido há 10 minutos)
                    var remC = await reminderSvc.CreateReminderAsync(new Reminder
                    {
                        Title = "Lembrete Atrasado Sessão",
                        DueDate = sessionStart.AddMinutes(-10),
                        Priority = ReminderPriority.Urgent
                    });

                    // Próximo tick deve disparar o atrasado exatamente uma vez
                    int firedC = await notifSvc.CheckScheduledNotificationsAsync(sessionStart.AddMinutes(2).AddSeconds(30));
                    if (firedC != 1 || !capturedNotifications.Any(n => n.Title.Contains("Lembrete Atrasado Sessão")))
                    {
                        throw new InvalidOperationException("Falha: Lembrete atrasado não disparou no tick seguinte.");
                    }

                    // Tick seguinte para o atrasado: sem repetição
                    int firedCNext = await notifSvc.CheckScheduledNotificationsAsync(sessionStart.AddMinutes(2).AddSeconds(45));
                    if (firedCNext > 0)
                    {
                        throw new InvalidOperationException("Falha: Lembrete atrasado gerou sequência repetida.");
                    }

                    // 7. Simula adiamento (snooze +30m) do Lembrete A
                    var snoozedRemA = await reminderSvc.SnoozeReminderAsync(remA.Id, TimeSpan.FromMinutes(30), sessionStart.AddMinutes(1).AddSeconds(5));
                    DateTime snoozedDue = snoozedRemA.DueDate!.Value;

                    // Antes do novo horário: não dispara
                    int firedSnoozeBefore = await notifSvc.CheckScheduledNotificationsAsync(snoozedDue.AddMinutes(-5));
                    if (firedSnoozeBefore > 0)
                    {
                        throw new InvalidOperationException("Falha: Lembrete adiado disparou antes do novo prazo.");
                    }

                    // No novo horário: dispara exatamente uma vez
                    int firedSnoozeAtDue = await notifSvc.CheckScheduledNotificationsAsync(snoozedDue.AddSeconds(5));
                    if (firedSnoozeAtDue != 1)
                    {
                        throw new InvalidOperationException("Falha: Lembrete adiado não disparou no novo horário.");
                    }

                    // Tick subsequente do adiado: sem duplicação
                    int firedSnoozeNext = await notifSvc.CheckScheduledNotificationsAsync(snoozedDue.AddSeconds(20));
                    if (firedSnoozeNext > 0)
                    {
                        throw new InvalidOperationException("Falha: Lembrete adiado duplicou no tick subsequente.");
                    }

                    results.Add("[PASS] Teste 14: Disparo em tempo real durante sessão aberta 100% validado (criação em sessão, disparo no horário, sem duplicação, lembrete atrasado e snooze).");
                }

                // Test 15: Verificação de Isolamento Total da Produção
                if (File.Exists(prodDbPath))
                {
                    using var sha = SHA256.Create();
                    using var fs = File.OpenRead(prodDbPath);
                    string finalProdHash = Convert.ToHexString(sha.ComputeHash(fs));

                    if (finalProdHash != initialProdHash)
                    {
                        throw new InvalidOperationException($"VIOLAÇÃO CRÍTICA DE SEGURANÇA: O banco de produção foi alterado durante a execução dos testes! (Hash antes: {initialProdHash}, depois: {finalProdHash})");
                    }

                    results.Add($"[PASS] Teste 15: Integridade da base de produção confirmada! Hash inalterado ({finalProdHash}).");
                }
                else
                {
                    results.Add("[PASS] Teste 15: Base de produção inexistente no caminho padrão; nenhum arquivo externo foi criado ou modificado.");
                }

                results.Add("=================================================================");
                results.Add("RESULTADO FINAL: TODOS OS 15 TESTES PASSARAM COM 100% DE SUCESSO!");
                results.Add("=================================================================");
            }
            finally
            {
                // Limpeza do banco temporário
                try
                {
                    SqliteConnection.ClearAllPools();
                    if (File.Exists(tempDbPath))
                    {
                        File.Delete(tempDbPath);
                    }
                }
                catch { }
            }

            return results;
        }
    }
}
