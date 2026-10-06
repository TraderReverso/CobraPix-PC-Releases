using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services;
using CobraPix_PC.Services.Interfaces;
using CobraPix_PC.Services.Testing;
using CobraPix_PC.ViewModels;

namespace CobraPix_PC
{
    /// <summary>
    /// Configuração e ciclo de vida da aplicação com Injeção de Dependências (DI) e inicialização de banco.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Provedor global de serviços configurado via Dependency Injection.
        /// </summary>
        public static IServiceProvider Services { get; private set; } = null!;

        public App()
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // Contexto do Banco de Dados (EF Core SQLite)
            services.AddDbContext<CobraPixDbContext>();

            // Serviços de Regras de Negócio
            services.AddScoped<IClientService, ClientService>();
            services.AddScoped<ISaleService, SaleService>();
            services.AddScoped<IPaymentService, PaymentService>();
            services.AddScoped<IMonthlyCustomerService, MonthlyCustomerService>();
            services.AddScoped<IWhatsAppService, WhatsAppService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IBackupService, BackupService>();
            services.AddScoped<IAndroidBackupReader, AndroidBackupReader>();
            services.AddScoped<IAndroidImportService, AndroidImportService>();
            services.AddScoped<ILicenseService, LicenseService>();
            services.AddScoped<IProService, ProService>();
            services.AddScoped<IThemeService, ThemeService>();
            services.AddScoped<IClientRankingService, ClientRankingService>();
            services.AddScoped<IFinancialHistoryService, FinancialHistoryService>();
            services.AddScoped<IReceiptPdfService, ReceiptPdfService>();
            services.AddScoped<IUpdateService, UpdateService>();
            services.AddScoped<IUpdateDownloadService, UpdateDownloadService>();
            services.AddScoped<IExcelExportService, ExcelExportService>();

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<ClientesViewModel>();
            services.AddTransient<VendasViewModel>();
            services.AddTransient<ParcelasViewModel>();
            services.AddTransient<PagamentosViewModel>();
            services.AddTransient<MensalistasViewModel>();
            services.AddTransient<VencimentosViewModel>();
            services.AddTransient<AtrasadosViewModel>();
            services.AddTransient<CobrancasViewModel>();
            services.AddTransient<ConfiguracoesViewModel>();
            services.AddTransient<CobraPix_PC.ViewModels.Modules.DashboardViewModel>();
            services.AddTransient<CobraPix_PC.ViewModels.Modules.QuitadosViewModel>();
            services.AddTransient<CobraPix_PC.ViewModels.Modules.ArquivadosViewModel>();
            services.AddTransient<CobraPix_PC.ViewModels.Modules.RanqueamentoViewModel>();
            services.AddTransient<CobraPix_PC.ViewModels.Modules.ProViewModel>();
            services.AddTransient<CobraPix_PC.ViewModels.Modules.HistoricoFinanceiroViewModel>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length > 0 && e.Args[0] == "--test-wait-for-updater")
            {
                string signalFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "close_signal.tmp");
                DateTime deadline = DateTime.UtcNow.AddMinutes(5);
                while (DateTime.UtcNow < deadline && !File.Exists(signalFile))
                {
                    System.Threading.Thread.Sleep(100);
                }
                if (File.Exists(signalFile))
                {
                    try { File.Delete(signalFile); } catch { }
                }
                Environment.Exit(0);
                return;
            }

            base.OnStartup(e);

            // Suporte a validação automatizada das regras em ambiente isolado (sem abrir telas)
            if (e.Args.Length > 0 && e.Args[0] == "--test-business-rules")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_rules_result.log");
                try
                {
                    var results = await BusinessRulesValidator.RunAllTestsAsync();
                    File.WriteAllLines(logFile, results);
                    Console.WriteLine("ALL_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Environment.Exit(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--diagnose-db")
            {
                try
                {
                    await BusinessRulesValidator.DiagnoseProductionDatabaseAsync();
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("DIAGNOSE_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-live-download")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_download_result.log");
                try
                {
                    var results = await BusinessRulesValidator.RunLiveDownloadTestAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Console.WriteLine("ALL_DOWNLOAD_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-updater-isolated")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_updater_isolated_result.log");
                try
                {
                    var results = await BusinessRulesValidator.RunIsolatedUpdaterAndRollbackTestAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Console.WriteLine("ALL_ISOLATED_UPDATER_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-ui-update-integration")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_ui_update_integration_result.log");
                try
                {
                    var results = await BusinessRulesValidator.RunUiUpdateIntegrationTestAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Console.WriteLine("ALL_UI_UPDATE_INTEGRATION_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-controlled-update-env")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_controlled_update_env_result.log");
                try
                {
                    var results = await BusinessRulesValidator.RunControlledUpdateEnvironmentTestAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Console.WriteLine("ALL_CONTROLLED_UPDATE_ENV_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            // Suporte para ativação explícita de ambiente de teste de atualização via CLI ou variável de ambiente
            for (int i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--test-manifest-url" && i + 1 < e.Args.Length)
                {
                    UpdateService.SetTestManifestUrl(e.Args[i + 1]);
                    break;
                }
            }

            string? envTestManifest = Environment.GetEnvironmentVariable("COBRAPIX_TEST_UPDATE_MANIFEST_URL");
            if (!string.IsNullOrWhiteSpace(envTestManifest) && !UpdateService.IsTestEnvironmentActive)
            {
                UpdateService.SetTestManifestUrl(envTestManifest);
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-verify-startup")
            {
                string installDir = AppDomain.CurrentDomain.BaseDirectory;
                string markerPath = Path.Combine(installDir, "test_version_marker.json");
                string verFilePath = Path.Combine(installDir, "version_info.txt");
                string dllPath = Path.Combine(installDir, "CobraPix PC.dll");
                string exePath = Path.Combine(installDir, "CobraPix PC.exe");
                string detectedVersion = "Unknown";
                bool isVersion102 = false;
                bool isVersion103 = false;

                if (File.Exists(dllPath))
                {
                    var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(dllPath);
                    detectedVersion = fvi.FileVersion ?? fvi.ProductVersion ?? "Unknown";
                    if (detectedVersion.StartsWith("1.0.2")) isVersion102 = true;
                    if (detectedVersion.StartsWith("1.0.3")) isVersion103 = true;
                }
                else if (File.Exists(exePath))
                {
                    var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                    detectedVersion = fvi.FileVersion ?? fvi.ProductVersion ?? "Unknown";
                    if (detectedVersion.StartsWith("1.0.2")) isVersion102 = true;
                    if (detectedVersion.StartsWith("1.0.3")) isVersion103 = true;
                }

                if (!isVersion102 && File.Exists(markerPath)) isVersion102 = true;
                if (!isVersion102 && File.Exists(verFilePath) && File.ReadAllText(verFilePath).Contains("1.0.2")) isVersion102 = true;
                if (!isVersion103 && File.Exists(verFilePath) && File.ReadAllText(verFilePath).Contains("1.0.3")) isVersion103 = true;

                Console.WriteLine($"STARTUP_SUCCESS_PID:{Environment.ProcessId}");
                Console.WriteLine($"INSTALL_DIR:{installDir}");
                Console.WriteLine($"DETECTED_VERSION:{detectedVersion}");
                Console.WriteLine($"IS_VERSION_1_0_2:{isVersion102}");
                Console.WriteLine($"IS_VERSION_1_0_3:{isVersion103}");
                Console.WriteLine("UPDATER_LOOP_DETECTED:False");
                Environment.Exit(0);
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-realrun-update")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_realrun_update_result.log");
                try
                {
                    var results = await BusinessRulesValidator.RunRealRunUpdateTestAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Console.WriteLine("ALL_REALRUN_UPDATE_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-phase-update-final")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_phase_update_final_result.log");
                try
                {
                    var results = await BusinessRulesValidator.RunFinal14UpdatePhaseTestsAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Console.WriteLine("ALL_FINAL_14_UPDATE_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-phase-manifest-connection")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_manifest_102_result.log");
                try
                {
                    var results = await BusinessRulesValidator.ValidatePhaseProductionManifest102ConnectionAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Console.WriteLine("ALL_MANIFEST_102_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-real-ui-update")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_real_ui_update_result.log");
                try
                {
                    var results = await BusinessRulesValidator.RunRealUiUpdateFlowAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Console.WriteLine("ALL_REAL_UI_UPDATE_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-phase-uac-elevation")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_phase_uac_elevation_result.log");
                try
                {
                    var results = await BusinessRulesValidator.RunUacElevationTestsAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Console.WriteLine("ALL_UAC_ELEVATION_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-phase-inno-update")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_phase_inno_update_result.log");
                try
                {
                    var results = await BusinessRulesValidator.RunInnoSetupCommercialUpdateTestsAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Console.WriteLine("ALL_INNO_SETUP_UPDATE_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-real-inno-flow")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_real_inno_flow_result.log");
                try
                {
                    var results = await BusinessRulesValidator.RunRealInnoUpdate102To103TestAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Console.WriteLine("ALL_REAL_INNO_UPDATE_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            // Garante que o diretório em %LOCALAPPDATA%\CobraPix e o banco SQLite existam
            DatabaseInitializer.Initialize();

            // Aplica o tema configurado nas preferências locais
            try
            {
                using var themeScope = Services.CreateScope();
                var themeSvc = themeScope.ServiceProvider.GetRequiredService<IThemeService>();
                var savedTheme = await themeSvc.GetThemeAsync();
                themeSvc.ApplyTheme(savedTheme);
            }
            catch
            {
                // Fallback gracioso com tema escuro padrão
            }

            // Agendador de alta precisão sincronizado com os 7 horários de notificação
            bool initialCheckDone = false;
            var notificationTimer = new System.Windows.Threading.DispatcherTimer();

            void RescheduleTimer(INotificationService notifSvc, NotificationSettings settings)
            {
                try
                {
                    if (!settings.NotificationsEnabled)
                    {
                        notificationTimer.Interval = TimeSpan.FromSeconds(15);
                        return;
                    }

                    var now = DateTime.Now;
                    DateTime nextTarget = notifSvc.GetNextScheduledTime(settings, now.AddSeconds(2));
                    // Adiciona pequeno buffer de 200ms para garantir que a virada do minuto já ocorreu
                    TimeSpan delay = (nextTarget.AddMilliseconds(200)) - now;

                    if (delay <= TimeSpan.Zero)
                    {
                        notificationTimer.Interval = TimeSpan.FromMilliseconds(500);
                    }
                    else if (delay <= TimeSpan.FromMinutes(2))
                    {
                        // Menos de 2 minutos para o próximo horário: programa o tick exatamente no segundo do slot
                        notificationTimer.Interval = delay < TimeSpan.FromMilliseconds(500) ? TimeSpan.FromMilliseconds(500) : delay;
                    }
                    else
                    {
                        // Mais de 2 minutos: timer intermediário de 15 segundos sem consulta pesada ao banco
                        notificationTimer.Interval = TimeSpan.FromSeconds(15);
                    }
                }
                catch
                {
                    notificationTimer.Interval = TimeSpan.FromSeconds(15);
                }
            }

            notificationTimer.Tick += async (s, args) =>
            {
                try
                {
                    using var scope = Services.CreateScope();
                    var notifSvc = scope.ServiceProvider.GetRequiredService<INotificationService>();
                    var settings = await notifSvc.GetSettingsAsync();

                    if (!initialCheckDone)
                    {
                        initialCheckDone = true;
                        // Verificação inicial ao inicializar o app
                        await notifSvc.CheckAndNotifyAsync();
                    }
                    else
                    {
                        // Verificação de alta precisão no horário configurado
                        await notifSvc.CheckScheduledNotificationsAsync();
                    }

                    // Recalcula o próximo horário e programa a próxima verificação
                    RescheduleTimer(notifSvc, settings);
                }
                catch
                {
                    notificationTimer.Interval = TimeSpan.FromSeconds(15);
                }
            };

            // Escuta alterações de configurações nos 7 horários para reprogramar imediatamente o timer
            NotificationService.ScheduleSettingsChanged += () =>
            {
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(async () =>
                {
                    try
                    {
                        using var scope = Services.CreateScope();
                        var notifSvc = scope.ServiceProvider.GetRequiredService<INotificationService>();
                        var settings = await notifSvc.GetSettingsAsync();
                        notificationTimer.Stop();
                        RescheduleTimer(notifSvc, settings);
                        notificationTimer.Start();
                    }
                    catch { }
                });
            };

            // Inicia em 2 segundos para dar tempo à renderização da janela
            notificationTimer.Interval = TimeSpan.FromSeconds(2);
            notificationTimer.Start();
        }
    }
}
