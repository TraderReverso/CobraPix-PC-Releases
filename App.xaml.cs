using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services;
using CobraPix_PC.Services.Interfaces;
using CobraPix_PC.Services.Testing;
using CobraPix_PC.Services.Credit;
using CobraPix_PC.Services.Credit.Interfaces;
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

        /// <summary>
        /// Mutex global para garantir instância única do CobraPix PC.
        /// Reutilizado tanto na abertura manual quanto na inicialização automática com o Windows.
        /// </summary>
        private static Mutex? _singleInstanceMutex;

        /// <summary>
        /// Indica que o app foi iniciado automaticamente pelo Windows (via --minimized).
        /// Quando verdadeiro, a MainWindow inicia minimizada em vez de abrir normalmente.
        /// </summary>
        public static bool StartMinimized { get; private set; }

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

            // Serviços do Módulo Crédito & Giro (Exclusivo PRO)
            services.AddScoped<ICreditTransactionExecutor, CreditTransactionExecutor>();
            services.AddScoped<ICreditContractService, CreditContractService>();
            services.AddScoped<ICreditPaymentService, CreditPaymentService>();
            services.AddScoped<ICreditRefundService, CreditRefundService>();

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
            services.AddTransient<CobraPix_PC.ViewModels.Modules.CreditoGiroViewModel>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            // ──────────────────────────────────────────────────────────
            // INSTÂNCIA ÚNICA: garante que apenas um processo CobraPix
            // esteja em execução, independentemente de como foi aberto
            // (manual ou automático pelo Windows).
            // ──────────────────────────────────────────────────────────
            const string mutexName = "Global\\CobraPixPC_SingleInstance";
            bool isNewInstance;
            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out isNewInstance);

            if (!isNewInstance)
            {
                // Já existe uma instância em execução; encerra silenciosamente
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Environment.Exit(0);
                return;
            }

            // ──────────────────────────────────────────────────────────
            // INICIALIZAÇÃO AUTOMÁTICA: quando iniciado pelo Windows
            // (via registro Run) com o argumento --minimized, a janela
            // principal será aberta no estado minimizado.
            // ──────────────────────────────────────────────────────────
            if (e.Args.Contains("--minimized"))
            {
                StartMinimized = true;
            }

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

            if (e.Args.Length > 0 && e.Args[0] == "--test-enable-start-boot")
            {
                try
                {
                    var configVm = Services.GetRequiredService<ConfiguracoesViewModel>();
                    await configVm.SetStartWithWindowsAsync(true);
                    Console.WriteLine("START_BOOT_ENABLED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("ERROR: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-disable-start-boot")
            {
                try
                {
                    var configVm = Services.GetRequiredService<ConfiguracoesViewModel>();
                    await configVm.SetStartWithWindowsAsync(false);
                    Console.WriteLine("START_BOOT_DISABLED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("ERROR: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-theme-system")
            {
                try
                {
                    string tempDb = Path.Combine(Path.GetTempPath(), "cobrapix_theme_test_" + Guid.NewGuid() + ".db");
                    var optBuilder = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite($"Data Source={tempDb}");
                    using (var ctxInit = new CobraPixDbContext(optBuilder.Options))
                    {
                        ctxInit.Database.EnsureCreated();
                    }

                    using (var ctx = new CobraPixDbContext(optBuilder.Options))
                    {
                        var themeSvc = new ThemeService(ctx);

                        // 1. GetThemeAsync inicial
                        var initial = await themeSvc.GetThemeAsync();
                        if (initial != AppTheme.Dark) throw new Exception($"Erro: Tema inicial esperado Dark, obtido {initial}");
                        Console.WriteLine("TEST_1_INITIAL_THEME: PASS");

                        // 2. SetThemeAsync Light
                        await themeSvc.SetThemeAsync(AppTheme.Light);
                        var light = await themeSvc.GetThemeAsync();
                        if (light != AppTheme.Light) throw new Exception("Erro: SetThemeAsync(Light) falhou.");
                        Console.WriteLine("TEST_2_LIGHT_THEME: PASS");

                        // 3. SetThemeAsync GreenYellow
                        await themeSvc.SetThemeAsync(AppTheme.GreenYellow);
                        var gy = await themeSvc.GetThemeAsync();
                        if (gy != AppTheme.GreenYellow) throw new Exception("Erro: SetThemeAsync(GreenYellow) falhou.");
                        Console.WriteLine("TEST_3_GREENYELLOW_THEME: PASS");

                        // 4. Persistência em nova instância
                        using (var ctx2 = new CobraPixDbContext(optBuilder.Options))
                        {
                            var themeSvc2 = new ThemeService(ctx2);
                            var persistedGy = await themeSvc2.GetThemeAsync();
                            if (persistedGy != AppTheme.GreenYellow) throw new Exception("Erro: Persistência de GreenYellow falhou.");
                            Console.WriteLine("TEST_4_PERSISTENCE: PASS");
                        }

                        // 5. ApplyTheme em Application.Current
                        themeSvc.ApplyTheme(AppTheme.GreenYellow);
                        if (Current != null)
                        {
                            var merged = Current.Resources.MergedDictionaries;
                            bool hasGY = merged.Any(d => d.Source != null && d.Source.ToString().Contains("ColorsGreenYellow.xaml"));
                            bool hasDark = merged.Any(d => d.Source != null && d.Source.ToString().Contains("Colors.xaml") && !d.Source.ToString().Contains("ColorsGreenYellow.xaml") && !d.Source.ToString().Contains("ColorsLight.xaml"));
                            bool hasLight = merged.Any(d => d.Source != null && d.Source.ToString().Contains("ColorsLight.xaml"));
                            if (!hasGY || hasDark || hasLight) throw new Exception("Erro: ApplyTheme(GreenYellow) não configurou MergedDictionaries corretamente.");
                            bool isGyFlag = Current.Resources["IsGreenYellowTheme"] as bool? == true;
                            if (!isGyFlag) throw new Exception("Erro: IsGreenYellowTheme não está true no Application.Current.Resources.");
                            Console.WriteLine("TEST_5_APPLY_GREENYELLOW: PASS");
                        }

                        // 6. Teste de alternância dinâmica para Dark e Light via ApplyTheme
                        themeSvc.ApplyTheme(AppTheme.Dark);
                        if (Current != null)
                        {
                            var merged = Current.Resources.MergedDictionaries;
                            bool hasDark = merged.Any(d => d.Source != null && d.Source.ToString().Contains("Colors.xaml") && !d.Source.ToString().Contains("ColorsGreenYellow.xaml") && !d.Source.ToString().Contains("ColorsLight.xaml"));
                            if (!hasDark) throw new Exception("Erro: ApplyTheme(Dark) falhou.");
                            Console.WriteLine("TEST_6_APPLY_DARK: PASS");
                        }

                        themeSvc.ApplyTheme(AppTheme.Light);
                        if (Current != null)
                        {
                            var merged = Current.Resources.MergedDictionaries;
                            bool hasLight = merged.Any(d => d.Source != null && d.Source.ToString().Contains("ColorsLight.xaml"));
                            if (!hasLight) throw new Exception("Erro: ApplyTheme(Light) falhou.");
                            Console.WriteLine("TEST_7_APPLY_LIGHT: PASS");
                        }

                        // 7. ViewModel com comandos SetGreenYellowThemeCommand, SetDarkThemeCommand, SetLightThemeCommand
                        var paySvc = new PaymentService(ctx);
                        var monSvc = new MonthlyCustomerService(ctx);
                        var notifSvc = new NotificationService(ctx, paySvc, monSvc);
                        var backupSvc = new BackupService(ctx);
                        var proSvc = new ProService(ctx);
                        var configVm = new ConfiguracoesViewModel(notifSvc, backupSvc, proSvc, themeSvc);

                        configVm.SetGreenYellowThemeCommand.Execute(null);
                        await Task.Delay(50);
                        if (!configVm.IsGreenYellowTheme || configVm.IsDarkTheme || configVm.IsLightTheme || configVm.CurrentAppTheme != AppTheme.GreenYellow)
                            throw new Exception("Erro: SetGreenYellowThemeCommand no ViewModel falhou.");
                        Console.WriteLine("TEST_8_VM_GREENYELLOW_COMMAND: PASS");

                        configVm.SetDarkThemeCommand.Execute(null);
                        await Task.Delay(50);
                        if (!configVm.IsDarkTheme || configVm.IsGreenYellowTheme || configVm.IsLightTheme)
                            throw new Exception("Erro: SetDarkThemeCommand no ViewModel falhou.");
                        Console.WriteLine("TEST_9_VM_DARK_COMMAND: PASS");

                        configVm.SetLightThemeCommand.Execute(null);
                        await Task.Delay(50);
                        if (!configVm.IsLightTheme || configVm.IsGreenYellowTheme || configVm.IsDarkTheme)
                            throw new Exception("Erro: SetLightThemeCommand no ViewModel falhou.");
                        Console.WriteLine("TEST_10_VM_LIGHT_COMMAND: PASS");
                    }

                    try { File.Delete(tempDb); } catch { }
                    Console.WriteLine("ALL_THEME_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("THEME_TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-whatsapp-improvements")
            {
                try
                {
                    var whatsAppSvc = new WhatsAppService();

                    // Diagnóstico e Inspeção de Emojis e Unicode (Etapas 1 e 2)
                    string sampleMsg = whatsAppSvc.GenerateVencimentoProximoMessage("Vitor", "2/10", new DateTime(2026, 11, 6), 56.48m, 56.48m, 508.32m, "131.805.017-07", new DateTime(2026, 11, 6, 20, 0, 0));
                    Console.WriteLine("=== DIAGNOSTICO UNICODE ETAPA 1 E 2 ===");
                    Console.WriteLine("String Original Length: " + sampleMsg.Length);
                    Console.WriteLine("Contem U+FFFD (corrompido): " + sampleMsg.Contains("\uFFFD"));
                    Console.WriteLine("Sorriso code point: U+" + char.ConvertToUtf32(WhatsAppService.EmojiSorriso, 0).ToString("X4"));
                    Console.WriteLine("Dinheiro code point: U+" + char.ConvertToUtf32(WhatsAppService.EmojiDinheiro, 0).ToString("X4"));
                    Console.WriteLine("Ampulheta code point: U+" + ((int)WhatsAppService.EmojiAmpulheta[0]).ToString("X4"));
                    Console.WriteLine("Check code point: U+" + ((int)WhatsAppService.EmojiCheck[0]).ToString("X4"));
                    string urlDiag = whatsAppSvc.BuildWhatsAppUrl("11988887777", sampleMsg) ?? "";
                    Console.WriteLine("URL Final Completa: " + urlDiag);
                    Console.WriteLine("URL Contem %EF%BF%BD: " + urlDiag.Contains("%EF%BF%BD"));
                    Console.WriteLine("URL Contem %F0%9F%98%8A: " + urlDiag.Contains("%F0%9F%98%8A"));
                    Console.WriteLine("URL Contem %F0%9F%92%B0: " + urlDiag.Contains("%F0%9F%92%B0"));
                    Console.WriteLine("URL Contem %E2%8F%B3: " + urlDiag.Contains("%E2%8F%B3"));
                    Console.WriteLine("=== FIM DO DIAGNOSTICO ===");

                    // 1. Saudação manhã (05:00 até 11:59)
                    var morning1 = whatsAppSvc.GetGreeting(new DateTime(2026, 10, 8, 5, 0, 0));
                    var morning2 = whatsAppSvc.GetGreeting(new DateTime(2026, 10, 8, 11, 59, 59));
                    if (morning1 != "Bom dia" || morning2 != "Bom dia")
                        throw new Exception($"Falha saudação manhã: {morning1}, {morning2}");
                    Console.WriteLine("TEST_1_GREETING_MORNING: PASS");

                    // 2. Saudação tarde (12:00 até 17:59)
                    var afternoon1 = whatsAppSvc.GetGreeting(new DateTime(2026, 10, 8, 12, 0, 0));
                    var afternoon2 = whatsAppSvc.GetGreeting(new DateTime(2026, 10, 8, 17, 59, 59));
                    if (afternoon1 != "Boa tarde" || afternoon2 != "Boa tarde")
                        throw new Exception($"Falha saudação tarde: {afternoon1}, {afternoon2}");
                    Console.WriteLine("TEST_2_GREETING_AFTERNOON: PASS");

                    // 3. Saudação noite (18:00 até 04:59)
                    var night1 = whatsAppSvc.GetGreeting(new DateTime(2026, 10, 8, 18, 0, 0));
                    var night2 = whatsAppSvc.GetGreeting(new DateTime(2026, 10, 8, 23, 59, 59));
                    var night3 = whatsAppSvc.GetGreeting(new DateTime(2026, 10, 8, 0, 0, 0));
                    var night4 = whatsAppSvc.GetGreeting(new DateTime(2026, 10, 8, 4, 59, 59));
                    if (night1 != "Boa noite" || night2 != "Boa noite" || night3 != "Boa noite" || night4 != "Boa noite")
                        throw new Exception($"Falha saudação noite: {night1}, {night2}, {night3}, {night4}");
                    Console.WriteLine("TEST_3_GREETING_NIGHT: PASS");

                    // 4. Parcela sem Pix
                    whatsAppSvc.PixKey = null;
                    var tNoite = new DateTime(2026, 11, 6, 20, 0, 0);
                    var dHoje = new DateTime(2026, 11, 6);
                    var msgParcSemPix = whatsAppSvc.GenerateVencimentoProximoMessage("Vitor adv manutenção", "2/10", dHoje, 56.48m, 56.48m, 508.32m, referenceTime: tNoite);
                    if (!msgParcSemPix.StartsWith("Boa noite, Vitor adv manutenção! Tudo bem? 😊"))
                        throw new Exception("Falha: saudação com emoji não confere.");
                    if (!msgParcSemPix.Contains("Passando para lembrar do vencimento da sua parcela 2/10, no valor de R$ 56,48, com vencimento para hoje (06/11/2026)."))
                        throw new Exception("Falha: corpo do lembrete de vencimento hoje não confere.");
                    if (!msgParcSemPix.Contains("Assim que realizar o pagamento, por favor, envie o comprovante. Obrigado!"))
                        throw new Exception("Falha: fechamento de comprovante não confere.");
                    if (msgParcSemPix.Contains("chave Pix") || msgParcSemPix.Contains("Chave Pix"))
                        throw new Exception("Falha: mensagem sem Pix contém chave Pix.");
                    Console.WriteLine("TEST_4_PARCELA_SEM_PIX: PASS");

                    // 5. Parcela com Pix
                    whatsAppSvc.PixKey = "131.805.017-07";
                    var msgParcComPix = whatsAppSvc.GenerateVencimentoProximoMessage("Vitor adv manutenção", "2/10", dHoje, 56.48m, 56.48m, 508.32m, referenceTime: tNoite);
                    if (!msgParcComPix.Contains("Para sua comodidade, segue a chave Pix para pagamento:\n131.805.017-07"))
                        throw new Exception("Falha: bloco de chave Pix não confere.");
                    Console.WriteLine("TEST_5_PARCELA_COM_PIX: PASS");

                    // 6. Parcela com pagamento parcial
                    var msgParcParcial = whatsAppSvc.GenerateVencimentoProximoMessage("Carlos Silva", "1/5", dHoje.AddDays(2), 100.00m, 40.00m, 60.00m, referenceTime: tNoite);
                    if (!msgParcParcial.Contains("💰 Total já pago: R$ 40,00") || !msgParcParcial.Contains("⏳ Falta pagar: R$ 60,00"))
                        throw new Exception("Falha: valores de pagamento parcial não conferem.");
                    Console.WriteLine("TEST_6_PARCELA_PAGAMENTO_PARCIAL: PASS");

                    // 7. Conferência de 'Total já pago'
                    if (!msgParcComPix.Contains("💰 Total já pago: R$ 56,48"))
                        throw new Exception("Falha: 'Total já pago' de R$ 56,48 não encontrado.");
                    Console.WriteLine("TEST_7_TOTAL_JA_PAGO: PASS");

                    // 8. Conferência de 'Falta pagar'
                    if (!msgParcComPix.Contains("⏳ Falta pagar: R$ 508,32"))
                        throw new Exception("Falha: 'Falta pagar' de R$ 508,32 não encontrado.");
                    Console.WriteLine("TEST_8_FALTA_PAGAR: PASS");

                    // 9. Mensalista sem Pix
                    whatsAppSvc.PixKey = null;
                    var tTarde = new DateTime(2026, 11, 6, 14, 0, 0);
                    var msgMensalSemPix = whatsAppSvc.GenerateMensalidadeAtrasadaMessage("Academia Boa Forma", "Outubro/2026", new DateTime(2026, 10, 10), 250.00m, 0m, 250.00m, referenceTime: tTarde);
                    if (!msgMensalSemPix.StartsWith("Boa tarde, Academia Boa Forma! Tudo bem? 😊"))
                        throw new Exception("Falha: saudação mensalista não confere.");
                    if (!msgMensalSemPix.Contains("Passando para lembrar da sua mensalidade referente a Outubro/2026, no valor de R$ 250,00, que venceu em 10/10/2026 e encontra-se em atraso."))
                        throw new Exception("Falha: corpo da mensagem de mensalidade em atraso não confere.");
                    if (!msgMensalSemPix.Contains("💰 Total já pago: R$ 0,00") || !msgMensalSemPix.Contains("⏳ Falta pagar: R$ 250,00"))
                        throw new Exception("Falha: bloco financeiro da mensalidade não confere.");
                    if (msgMensalSemPix.Contains("chave Pix") || msgMensalSemPix.Contains("Chave Pix"))
                        throw new Exception("Falha: mensalista sem Pix contém bloco Pix.");
                    Console.WriteLine("TEST_9_MENSALISTA: PASS");

                    // 10. Mensalista com Pix
                    whatsAppSvc.PixKey = "contato@empresa.com.br";
                    var msgMensalComPix = whatsAppSvc.GenerateMensalidadeAtrasadaMessage("Academia Boa Forma", "Outubro/2026", new DateTime(2026, 10, 10), 250.00m, 0m, 250.00m, referenceTime: tTarde);
                    if (!msgMensalComPix.Contains("Para sua comodidade, segue a chave Pix para pagamento:\ncontato@empresa.com.br"))
                        throw new Exception("Falha: bloco Pix não encontrado na mensagem de mensalista.");
                    Console.WriteLine("TEST_10_MENSALISTA_COM_PIX: PASS");

                    // 11. Atrasado
                    whatsAppSvc.PixKey = null;
                    var tManha = new DateTime(2026, 11, 6, 9, 0, 0);
                    var msgAtrasado = whatsAppSvc.GenerateParcelaAtrasadaMessage("Lucas Silva", "3/10", new DateTime(2026, 10, 20), 120.00m, 60.00m, 60.00m, referenceTime: tManha);
                    if (!msgAtrasado.StartsWith("Bom dia, Lucas Silva! Tudo bem? 😊"))
                        throw new Exception("Falha: saudação atrasado não confere.");
                    if (!msgAtrasado.Contains("Passando para lembrar da sua parcela 3/10, no valor de R$ 120,00, que venceu em 20/10/2026 e encontra-se em atraso."))
                        throw new Exception("Falha: corpo atrasado não confere.");
                    if (!msgAtrasado.Contains("💰 Total já pago: R$ 60,00") || !msgAtrasado.Contains("⏳ Falta pagar: R$ 60,00"))
                        throw new Exception("Falha: bloco financeiro de atrasado não confere.");
                    Console.WriteLine("TEST_11_ATRASADO: PASS");

                    // 12. Atrasado com Pix
                    whatsAppSvc.PixKey = "11988887777";
                    var msgAtrasadoComPix = whatsAppSvc.GenerateParcelaAtrasadaMessage("Lucas Silva", "3/10", new DateTime(2026, 10, 20), 120.00m, 60.00m, 60.00m, referenceTime: tManha);
                    if (!msgAtrasadoComPix.Contains("Para sua comodidade, segue a chave Pix para pagamento:\n11988887777"))
                        throw new Exception("Falha: bloco Pix não encontrado no atrasado.");
                    Console.WriteLine("TEST_12_ATRASADO_COM_PIX: PASS");

                    // 13. Pagamento confirmado
                    var dataPagamentoReal = new DateTime(2026, 10, 6, 21, 13, 0);
                    var msgConfirmacao = whatsAppSvc.GeneratePagamentoConfirmadoMessage("Vitor adv manutenção", "2/10", 56.48m, dataPagamentoReal, referenceTime: tNoite);
                    string expectedConfirmacao = "Boa noite, Vitor adv manutenção! 😊\n\nSeu pagamento da parcela 2/10, no valor de R$ 56,48, foi confirmado com sucesso em 06/10/2026 21:13.\n\n✅ Pagamento realizado com sucesso!\n\nObrigado pelo pagamento!";
                    if (msgConfirmacao != expectedConfirmacao)
                        throw new Exception($"Falha: mensagem de confirmação de pagamento não confere com o padrão exato.\nObtido:\n{msgConfirmacao}\nEsperado:\n{expectedConfirmacao}");
                    Console.WriteLine("TEST_13_PAGAMENTO_CONFIRMADO: PASS");

                    // 14. Data/hora real do pagamento
                    if (!msgConfirmacao.Contains("06/10/2026 21:13"))
                        throw new Exception("Falha: data e hora real do pagamento não conferem.");
                    Console.WriteLine("TEST_14_DATA_HORA_REAL: PASS");

                    // 15. Chave Pix vazia
                    whatsAppSvc.PixKey = "";
                    var msgPixVazia = whatsAppSvc.GenerateVencimentoProximoMessage("Vitor", "2/10", dHoje, 56.48m, 0m, 56.48m, referenceTime: tNoite);
                    if (msgPixVazia.Contains("Para sua comodidade, segue a chave Pix para pagamento:") || msgPixVazia.Contains("chave Pix"))
                        throw new Exception("Falha: chave Pix vazia gerou resíduo de texto na mensagem.");
                    if (msgPixVazia.EndsWith("\n\n") || msgPixVazia.Contains("\n\n\n"))
                        throw new Exception("Falha: linhas em branco excessivas geradas com chave Pix vazia.");
                    Console.WriteLine("TEST_15_CHAVE_PIX_VAZIA: PASS");

                    // 16. Persistência da chave Pix via AppSettings
                    string tempDb = Path.Combine(Path.GetTempPath(), "cobrapix_pix_test_" + Guid.NewGuid() + ".db");
                    var optBuilder = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite($"Data Source={tempDb}");
                    using (var ctxInit = new CobraPixDbContext(optBuilder.Options))
                    {
                        ctxInit.Database.EnsureCreated();
                    }

                    using (var ctx = new CobraPixDbContext(optBuilder.Options))
                    {
                        var themeSvc = new ThemeService(ctx);
                        var paySvc = new PaymentService(ctx);
                        var monSvc = new MonthlyCustomerService(ctx);
                        var notifSvc = new NotificationService(ctx, paySvc, monSvc);
                        var backupSvc = new BackupService(ctx);
                        var proSvc = new ProService(ctx);
                        var configVm = new ConfiguracoesViewModel(notifSvc, backupSvc, proSvc, themeSvc);

                        // Inicialmente sem chave
                        var initialSetting = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == "App.PixKey");
                        if (initialSetting != null && !string.IsNullOrEmpty(initialSetting.Value))
                            throw new Exception("Erro: Chave Pix inicial deveria ser vazia.");

                        // Salva chave no banco
                        configVm.PixKey = "131.805.017-07";
                        var existing = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == "App.PixKey");
                        if (existing != null) existing.Value = configVm.PixKey.Trim();
                        else ctx.AppSettings.Add(new AppSetting { Key = "App.PixKey", Value = configVm.PixKey.Trim(), UpdatedAt = DateTime.Now });
                        await ctx.SaveChangesAsync();
                        whatsAppSvc.PixKey = configVm.PixKey.Trim();

                        // Verifica se persistiu em novo DbContext
                        using (var ctx2 = new CobraPixDbContext(optBuilder.Options))
                        {
                            var savedSetting = await ctx2.AppSettings.FirstOrDefaultAsync(s => s.Key == "App.PixKey");
                            if (savedSetting?.Value != "131.805.017-07")
                                throw new Exception("Erro: Chave Pix não foi persistida corretamente no AppSettings.");
                        }

                        // Remove a chave e verifica limpeza
                        var settingToClear = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == "App.PixKey");
                        if (settingToClear != null) { settingToClear.Value = ""; await ctx.SaveChangesAsync(); }
                        whatsAppSvc.PixKey = "";

                        using (var ctx3 = new CobraPixDbContext(optBuilder.Options))
                        {
                            var clearedSetting = await ctx3.AppSettings.FirstOrDefaultAsync(s => s.Key == "App.PixKey");
                            if (!string.IsNullOrEmpty(clearedSetting?.Value))
                                throw new Exception("Erro: Limpeza da chave Pix falhou.");
                        }
                    }

                    try { File.Delete(tempDb); } catch { }
                    Console.WriteLine("TEST_16_PIX_KEY_PERSISTENCE: PASS");

                    // 17. Validação literal do emoji 😊 (U+1F60A)
                    if (!msgParcSemPix.Contains("😊") || !msgConfirmacao.Contains("😊"))
                        throw new Exception("Falha: mensagem não contém literalmente o emoji 😊.");
                    Console.WriteLine("TEST_17_EMOJI_SMILE_LITERAL: PASS");

                    // 18. Validação literal do emoji 💰 (U+1F4B0)
                    if (!msgParcSemPix.Contains("💰") || !msgParcParcial.Contains("💰"))
                        throw new Exception("Falha: mensagem não contém literalmente o emoji 💰.");
                    Console.WriteLine("TEST_18_EMOJI_MONEY_LITERAL: PASS");

                    // 19. Validação literal do emoji ⏳ (U+23F3)
                    if (!msgParcSemPix.Contains("⏳") || !msgParcParcial.Contains("⏳"))
                        throw new Exception("Falha: mensagem não contém literalmente o emoji ⏳.");
                    Console.WriteLine("TEST_19_EMOJI_HOURGLASS_LITERAL: PASS");

                    // 20. Validação literal do emoji ✅ (U+2705)
                    if (!msgConfirmacao.Contains("✅"))
                        throw new Exception("Falha: mensagem de confirmação não contém literalmente o emoji ✅.");
                    Console.WriteLine("TEST_20_EMOJI_CHECK_LITERAL: PASS");

                    // 21. Ausência de caractere de substituição/corrupção (U+FFFD)
                    if (msgParcSemPix.Contains("\uFFFD") || msgParcComPix.Contains("\uFFFD") || msgConfirmacao.Contains("\uFFFD") || msgMensalComPix.Contains("\uFFFD"))
                        throw new Exception("Falha: caractere de substituição/corrupção (\uFFFD) detectado nas mensagens.");
                    Console.WriteLine("TEST_21_NO_REPLACEMENT_CHAR: PASS");

                    // 22. URL do WhatsApp preserva caracteres Unicode ao ser decodificada
                    string testPhone = "11988887777";
                    string? generatedUrl = whatsAppSvc.BuildWhatsAppUrl(testPhone, msgParcComPix);
                    if (string.IsNullOrEmpty(generatedUrl) || !generatedUrl.StartsWith("https://api.whatsapp.com/send?phone=5511988887777&text="))
                        throw new Exception("Falha: URL do WhatsApp inválida.");
                    if (!generatedUrl.Contains("%F0%9F%98%8A") || !generatedUrl.Contains("%F0%9F%92%B0") || !generatedUrl.Contains("%E2%8F%B3") || generatedUrl.Contains("%EF%BF%BD"))
                        throw new Exception("Falha: URL não contém os percent-encodings UTF-8 corretos ou contém %EF%BF%BD.");
                    string rawQueryText = generatedUrl.Substring(generatedUrl.IndexOf("text=") + 5);
                    string decodedText = Uri.UnescapeDataString(rawQueryText);
                    if (decodedText != msgParcComPix)
                        throw new Exception("Falha: decodificação da URL não é idêntica à mensagem original.");
                    if (!decodedText.Contains("😊") || !decodedText.Contains("💰") || !decodedText.Contains("⏳") || decodedText.Contains("\uFFFD"))
                        throw new Exception("Falha: caracteres Unicode foram corrompidos na URL do WhatsApp.");
                    Console.WriteLine("TEST_22_URL_UNICODE_PRESERVATION: PASS");

                    // 23. Validação de acentos essenciais
                    if (!msgParcComPix.Contains("vencimento") || !msgParcComPix.Contains("pagamento") || !msgParcComPix.Contains("já") || !msgParcComPix.Contains("comprovante"))
                        throw new Exception("Falha: acentuação ou palavras com caracteres latinos corrompidos.");
                    Console.WriteLine("TEST_23_ACCENTS_PRESERVED: PASS");

                    // 24. Chave Pix sem caracteres especiais (alfanumérica simples)
                    whatsAppSvc.PixKey = "13180501707";
                    var msgPixSimples = whatsAppSvc.GenerateVencimentoProximoMessage("Vitor", "1/1", dHoje, 100m, 0m, 100m, referenceTime: tNoite);
                    if (!msgPixSimples.Contains("Para sua comodidade, segue a chave Pix para pagamento:\n13180501707"))
                        throw new Exception("Falha: chave Pix simples sem formatação não inserida corretamente.");
                    Console.WriteLine("TEST_24_SIMPLE_PIX_KEY: PASS");

                    // 25. Mensagem completa com emojis + acentos + R$ + chave Pix
                    whatsAppSvc.PixKey = "131.805.017-07";
                    var msgFinalCompleta = whatsAppSvc.GenerateVencimentoProximoMessage("Vitor", "2/10", dHoje, 56.48m, 56.48m, 508.32m, referenceTime: tNoite);
                    if (!msgFinalCompleta.Contains("Boa noite, Vitor! Tudo bem? 😊") ||
                        !msgFinalCompleta.Contains("💰 Total já pago: R$ 56,48") ||
                        !msgFinalCompleta.Contains("⏳ Falta pagar: R$ 508,32") ||
                        !msgFinalCompleta.Contains("Para sua comodidade, segue a chave Pix para pagamento:\n131.805.017-07") ||
                        !msgFinalCompleta.Contains("Assim que realizar o pagamento, por favor, envie o comprovante. Obrigado!"))
                    {
                        throw new Exception("Falha na mensagem final completa.");
                    }
                    Console.WriteLine("TEST_25_FULL_MESSAGE_INTEGRITY: PASS");

                    Console.WriteLine("ALL_WHATSAPP_IMPROVEMENT_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("WHATSAPP_TEST_FAILED: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-financial-rules-pdf")
            {
                try
                {
                    string tempDb = Path.Combine(Path.GetTempPath(), "cobrapix_financial_test_" + Guid.NewGuid() + ".db");
                    var optBuilder = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite($"Data Source={tempDb}");
                    using (var ctxInit = new CobraPixDbContext(optBuilder.Options))
                    {
                        ctxInit.Database.EnsureCreated();
                    }

                    using (var ctx = new CobraPixDbContext(optBuilder.Options))
                    {
                        var paySvc = new PaymentService(ctx);
                        var histSvc = new FinancialHistoryService(ctx);
                        var whatsAppSvc = new WhatsAppService();
                        var pdfSvc = new ReceiptPdfService(ctx);

                        // Cria cliente e venda base para testes
                        var client = new Client
                        {
                            Name = "Cliente Teste Financeiro",
                            Phone = "11988887777",
                            CreatedAt = DateTime.Now
                        };
                        ctx.Clients.Add(client);
                        await ctx.SaveChangesAsync();

                        var sale = new Sale
                        {
                            ClientId = client.Id,
                            Description = "Venda Teste",
                            TotalAmount = 500m,
                            CreatedAt = DateTime.Now
                        };
                        ctx.Sales.Add(sale);
                        await ctx.SaveChangesAsync();

                        // 1. Parcela de 100 com pagamento de 40:
                        //    - saldo restante = 60;
                        //    - status coerente (não quitada);
                        //    - nenhum saldo negativo.
                        var inst1 = new Installment
                        {
                            SaleId = sale.Id,
                            InstallmentNumber = 1,
                            Amount = 100.00m,
                            DueDate = DateTime.Today.AddDays(5),
                            Status = InstallmentStatus.Pending
                        };
                        ctx.Installments.Add(inst1);
                        await ctx.SaveChangesAsync();

                        var pay1 = await paySvc.RegisterPaymentAsync(inst1.Id, 40.00m, DateTime.Now, PaymentMethod.Pix, "Pagamento parcial 1");
                        var balance1 = await paySvc.GetRemainingBalanceForInstallmentAsync(inst1.Id);
                        var inst1Reloaded = await ctx.Installments.FindAsync(inst1.Id);

                        if (balance1 != 60.00m)
                            throw new Exception($"Teste 1: Saldo esperado 60.00, obtido {balance1}");
                        if (inst1Reloaded?.Status == InstallmentStatus.Paid)
                            throw new Exception("Teste 1: Parcela não deveria estar quitada com pagamento parcial.");
                        if (balance1 < 0)
                            throw new Exception("Teste 1: Saldo negativo indevido.");
                        Console.WriteLine("TEST_1_PARCELA_100_PAGO_40: PASS");

                        // 2. Parcela de 100 com pagamento de 100:
                        //    - saldo restante = 0;
                        //    - status quitada.
                        var inst2 = new Installment
                        {
                            SaleId = sale.Id,
                            InstallmentNumber = 2,
                            Amount = 100.00m,
                            DueDate = DateTime.Today.AddDays(10),
                            Status = InstallmentStatus.Pending
                        };
                        ctx.Installments.Add(inst2);
                        await ctx.SaveChangesAsync();

                        var pay2 = await paySvc.RegisterPaymentAsync(inst2.Id, 100.00m, DateTime.Now, PaymentMethod.Cash, "Pagamento integral");
                        var balance2 = await paySvc.GetRemainingBalanceForInstallmentAsync(inst2.Id);
                        var inst2Reloaded = await ctx.Installments.FindAsync(inst2.Id);

                        if (balance2 != 0.00m)
                            throw new Exception($"Teste 2: Saldo esperado 0.00, obtido {balance2}");
                        if (inst2Reloaded?.Status != InstallmentStatus.Paid)
                            throw new Exception($"Teste 2: Parcela deveria estar Paid, mas está {inst2Reloaded?.Status}");
                        Console.WriteLine("TEST_2_PARCELA_100_PAGO_100_QUITADA: PASS");

                        // 3. Parcela de 100 sem nenhum pagamento:
                        //    - saldo restante = 100;
                        //    - total pago = 0.
                        var inst3 = new Installment
                        {
                            SaleId = sale.Id,
                            InstallmentNumber = 3,
                            Amount = 100.00m,
                            DueDate = DateTime.Today.AddDays(15),
                            Status = InstallmentStatus.Pending
                        };
                        ctx.Installments.Add(inst3);
                        await ctx.SaveChangesAsync();

                        var balance3 = await paySvc.GetRemainingBalanceForInstallmentAsync(inst3.Id);
                        var totalPaid3 = await ctx.InstallmentPayments.Where(p => p.InstallmentId == inst3.Id).SumAsync(p => (decimal?)p.AmountPaid) ?? 0m;

                        if (balance3 != 100.00m || totalPaid3 != 0.00m)
                            throw new Exception($"Teste 3: Esperado saldo 100 e pago 0, obtido saldo={balance3}, pago={totalPaid3}");
                        Console.WriteLine("TEST_3_PARCELA_100_SEM_PAGAMENTO: PASS");

                        // 4. Tentativa de simular pagamento não registrado:
                        //    - não pode reduzir saldo.
                        decimal simulatedBalance = await paySvc.GetRemainingBalanceForInstallmentAsync(inst3.Id);
                        if (simulatedBalance != 100.00m)
                            throw new Exception("Teste 4: Pagamento não registrado reduziu o saldo indevidamente.");
                        Console.WriteLine("TEST_4_PAGAMENTO_NAO_REGISTRADO_NAO_REDUZ_SALDO: PASS");

                        // 5. Dois pagamentos parciais para a mesma parcela (ex: 30 + 30):
                        //    - total pago = 60;
                        //    - saldo restante = 40.
                        var inst5 = new Installment
                        {
                            SaleId = sale.Id,
                            InstallmentNumber = 4,
                            Amount = 100.00m,
                            DueDate = DateTime.Today.AddDays(20),
                            Status = InstallmentStatus.Pending
                        };
                        ctx.Installments.Add(inst5);
                        await ctx.SaveChangesAsync();

                        await paySvc.RegisterPaymentAsync(inst5.Id, 30.00m, DateTime.Now, PaymentMethod.Pix, "Parcial 1");
                        await paySvc.RegisterPaymentAsync(inst5.Id, 30.00m, DateTime.Now, PaymentMethod.Card, "Parcial 2");

                        var balance5 = await paySvc.GetRemainingBalanceForInstallmentAsync(inst5.Id);
                        var totalPaid5 = await ctx.InstallmentPayments.Where(p => p.InstallmentId == inst5.Id).SumAsync(p => p.AmountPaid);

                        if (totalPaid5 != 60.00m || balance5 != 40.00m)
                            throw new Exception($"Teste 5: Esperado pago=60 e saldo=40, obtido pago={totalPaid5}, saldo={balance5}");
                        Console.WriteLine("TEST_5_DOIS_PAGAMENTOS_PARCIAIS_30_30: PASS");

                        // 6. Geração de PDF para parcela parcial:
                        //    - valor original = 100;
                        //    - valor pago = 40;
                        //    - total pago = 40;
                        //    - saldo restante = 60.
                        var transactions = await histSvc.GetTransactionsByDateRangeAsync(DateTime.Today.AddMonths(-1), DateTime.Today.AddMonths(1));
                        var transParcial = transactions.FirstOrDefault(t => t.Id == pay1.Id && t.TransactionType == FinancialTransactionType.Installment);
                        if (transParcial == null) throw new Exception("Teste 6: Transação do pagamento parcial não encontrada no histórico.");
                        if (transParcial.OriginalAmount != 100.00m || transParcial.Amount != 40.00m || transParcial.TotalPaid != 40.00m || transParcial.RemainingBalance != 60.00m)
                            throw new Exception($"Teste 6: Dados do histórico divergentes. Orig={transParcial.OriginalAmount}, Pago={transParcial.Amount}, TotalPago={transParcial.TotalPaid}, Saldo={transParcial.RemainingBalance}");

                        byte[] pdfParcialBytes = pdfSvc.GenerateReceiptBytes(transParcial);
                        if (pdfParcialBytes == null || pdfParcialBytes.Length < 1000)
                            throw new Exception("Teste 6: PDF gerado inválido ou vazio.");
                        string pdfHeader = System.Text.Encoding.ASCII.GetString(pdfParcialBytes, 0, 5);
                        if (!pdfHeader.StartsWith("%PDF")) throw new Exception("Teste 6: Cabeçalho do arquivo PDF corrompido.");
                        Console.WriteLine("TEST_6_PDF_PARCELA_PARCIAL: PASS");

                        // 7. Geração de PDF para parcela quitada:
                        //    - valor original = 100;
                        //    - valor pago = 60;
                        //    - total pago = 100;
                        //    - saldo restante = 0.
                        var pay1Final = await paySvc.RegisterPaymentAsync(inst1.Id, 60.00m, DateTime.Now, PaymentMethod.Pix, "Quitação inst1");
                        var transactionsQuitada = await histSvc.GetTransactionsByDateRangeAsync(DateTime.Today.AddMonths(-1), DateTime.Today.AddMonths(1));
                        var transQuitada = transactionsQuitada.FirstOrDefault(t => t.Id == pay1Final.Id && t.TransactionType == FinancialTransactionType.Installment);
                        if (transQuitada == null) throw new Exception("Teste 7: Transação de quitação não encontrada.");
                        if (transQuitada.OriginalAmount != 100.00m || transQuitada.Amount != 60.00m || transQuitada.TotalPaid != 100.00m || transQuitada.RemainingBalance != 0.00m)
                            throw new Exception($"Teste 7: Dados da quitação divergentes. Orig={transQuitada.OriginalAmount}, Pago={transQuitada.Amount}, TotalPago={transQuitada.TotalPaid}, Saldo={transQuitada.RemainingBalance}");

                        byte[] pdfQuitadaBytes = pdfSvc.GenerateReceiptBytes(transQuitada);
                        if (pdfQuitadaBytes == null || pdfQuitadaBytes.Length < 1000)
                            throw new Exception("Teste 7: PDF de quitação inválido.");
                        Console.WriteLine("TEST_7_PDF_PARCELA_QUITADA: PASS");

                        // 8. Geração de PDF para mensalista:
                        //    - valor da mensalidade;
                        //    - status de recebimento.
                        var clientMon = new Client
                        {
                            Name = "Academia Fit Teste",
                            Phone = "11977776666",
                            CreatedAt = DateTime.Now
                        };
                        ctx.Clients.Add(clientMon);
                        await ctx.SaveChangesAsync();

                        var monCust = new MonthlyCustomer
                        {
                            ClientId = clientMon.Id,
                            MonthlyAmount = 150.00m,
                            DueDay = 10,
                            IsActive = true,
                            CreatedAt = DateTime.Now
                        };
                        ctx.MonthlyCustomers.Add(monCust);
                        await ctx.SaveChangesAsync();

                        var monPay = new MonthlyPayment
                        {
                            MonthlyCustomerId = monCust.Id,
                            ReferenceMonth = 10,
                            ReferenceYear = 2026,
                            Amount = 150.00m,
                            PaymentDate = DateTime.Now,
                            PaymentMethod = PaymentMethod.Pix,
                            Notes = "Mensalidade paga integralmente"
                        };
                        ctx.MonthlyPayments.Add(monPay);
                        await ctx.SaveChangesAsync();

                        var transMonList = await histSvc.GetTransactionsByDateRangeAsync(DateTime.Today.AddMonths(-1), DateTime.Today.AddMonths(1));
                        var transMon = transMonList.FirstOrDefault(t => t.Id == monPay.Id && t.TransactionType == FinancialTransactionType.Monthly);
                        if (transMon == null) throw new Exception("Teste 8: Transação do mensalista não encontrada no histórico.");
                        if (transMon.Amount != 150.00m || transMon.TransactionType != FinancialTransactionType.Monthly)
                            throw new Exception("Teste 8: Dados do mensalista divergentes.");

                        byte[] pdfMonBytes = pdfSvc.GenerateReceiptBytes(transMon);
                        if (pdfMonBytes == null || pdfMonBytes.Length < 1000)
                            throw new Exception("Teste 8: PDF de mensalista inválido.");
                        Console.WriteLine("TEST_8_PDF_MENSALISTA: PASS");

                        // 9. Configuração de Nome da Empresa:
                        //    - salvar 'Gestão CPix';
                        //    - verificar se o nome aparece no PDF.
                        var themeSvc = new ThemeService(ctx);
                        var notifSvc = new NotificationService(ctx, paySvc, new MonthlyCustomerService(ctx));
                        var backupSvc = new BackupService(ctx);
                        var proSvc = new ProService(ctx);
                        var configVm = new ConfiguracoesViewModel(notifSvc, backupSvc, proSvc, themeSvc);

                        configVm.CompanyName = "Gestão CPix";
                        var existingSetting = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == "App.CompanyName");
                        if (existingSetting != null) existingSetting.Value = "Gestão CPix";
                        else ctx.AppSettings.Add(new AppSetting { Key = "App.CompanyName", Value = "Gestão CPix", UpdatedAt = DateTime.Now });
                        await ctx.SaveChangesAsync();
                        pdfSvc.CompanyName = "Gestão CPix";

                        // Verifica persistência em novo DbContext
                        using (var ctx2 = new CobraPixDbContext(optBuilder.Options))
                        {
                            var settingEmissor = await ctx2.AppSettings.FirstOrDefaultAsync(s => s.Key == "App.CompanyName");
                            if (settingEmissor?.Value != "Gestão CPix")
                                throw new Exception($"Teste 9: Nome da empresa não persistido. Valor: {settingEmissor?.Value}");
                        }

                        byte[] pdfCompanyBytes = pdfSvc.GenerateReceiptBytes(transQuitada);
                        if (pdfCompanyBytes == null || pdfCompanyBytes.Length < 1000)
                            throw new Exception("Teste 9: PDF com nome da empresa inválido.");
                        Console.WriteLine("TEST_9_NOME_EMPRESA_CONFIGURADO: PASS");

                        // 10. Comportamento do botão no Histórico Financeiro:
                        //     - se o arquivo já existir, abre;
                        //     - se não existir, gera e abre.
                        string testPdfDir = Path.Combine(Path.GetTempPath(), "cobrapix_receipts_test_" + Guid.NewGuid());
                        Directory.CreateDirectory(testPdfDir);
                        string customFile = Path.Combine(testPdfDir, "Comprovante_Teste_10.pdf");

                        if (File.Exists(customFile)) File.Delete(customFile);
                        pdfSvc.GenerateAndSaveReceipt(transQuitada, customFile);
                        if (!File.Exists(customFile))
                            throw new Exception("Teste 10: Arquivo PDF não foi gerado no destino.");

                        long initialLength = new FileInfo(customFile).Length;
                        if (initialLength == 0) throw new Exception("Teste 10: Arquivo PDF gerado tem 0 bytes.");
                        Console.WriteLine("TEST_10_BOTAO_HISTORICO_PDF: PASS");

                        // 11. Preservação de layout, temas e WhatsApp:
                        var themeSaved = await themeSvc.GetThemeAsync();
                        if (themeSaved != AppTheme.Dark && themeSaved != AppTheme.GreenYellow && themeSaved != AppTheme.Light)
                            throw new Exception("Teste 11: Tema inválido.");

                        string greetingTest = whatsAppSvc.GetGreeting(new DateTime(2026, 10, 8, 14, 0, 0));
                        if (greetingTest != "Boa tarde")
                            throw new Exception("Teste 11: WhatsApp greeting quebrou.");

                        string msgConfirmacao = whatsAppSvc.GeneratePagamentoConfirmadoMessage(
                            "Vitor adv manutenção",
                            "2/10",
                            56.48m,
                            new DateTime(2026, 10, 6, 21, 13, 0),
                            referenceTime: new DateTime(2026, 10, 6, 21, 13, 0));
                        if (!msgConfirmacao.Contains("✅ Pagamento realizado com sucesso!"))
                            throw new Exception("Teste 11: Confirmação do WhatsApp quebrou.");

                        Console.WriteLine("TEST_11_PRESERVACAO_GERAL: PASS");
                    }

                    try { File.Delete(tempDb); } catch { }
                    Console.WriteLine("ALL_FINANCIAL_RULES_AND_PDF_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("FINANCIAL_PDF_TEST_FAILED: " + ex.Message + "\n" + ex.StackTrace);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-auto-backup")
            {
                try
                {
                    string tempDb = Path.Combine(Path.GetTempPath(), "cobrapix_autobackup_test_" + Guid.NewGuid() + ".db");
                    string tempAutoDir = Path.Combine(Path.GetTempPath(), "cobrapix_autobackup_dir_" + Guid.NewGuid());
                    string tempManualDir = Path.Combine(Path.GetTempPath(), "cobrapix_manual_dir_" + Guid.NewGuid());
                    string tempRestoredDb = Path.Combine(Path.GetTempPath(), "cobrapix_restored_test_" + Guid.NewGuid() + ".db");

                    Directory.CreateDirectory(tempAutoDir);
                    Directory.CreateDirectory(tempManualDir);

                    var optBuilder = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite($"Data Source={tempDb}");
                    using (var ctxInit = new CobraPixDbContext(optBuilder.Options))
                    {
                        ctxInit.Database.EnsureCreated();
                        var cl = new Client { Name = "Cliente Teste AutoBackup", Phone = "11988887777", CreatedAt = DateTime.Now };
                        ctxInit.Clients.Add(cl);
                        ctxInit.SaveChanges();
                    }

                    using (var ctx = new CobraPixDbContext(optBuilder.Options))
                    {
                        var backupSvc = new BackupService(ctx);

                        // 1. Primeiro backup automático do dia
                        var res1 = await backupSvc.CheckAndPerformDailyAutoBackupAsync(customSourceDb: tempDb, customTargetDir: tempAutoDir);
                        if (res1 == null || !res1.Success)
                            throw new Exception("Teste 1: Primeiro backup automático do dia falhou.");
                        if (!File.Exists(res1.FilePath))
                            throw new Exception("Teste 1: Arquivo de backup não existe em disco.");
                        string todayStr = DateTime.Today.ToString("yyyy-MM-dd");
                        var settingDate = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == BackupService.SettingKeyLastAutoBackupDate);
                        if (settingDate?.Value != todayStr)
                            throw new Exception($"Teste 1: Data do backup não registrada corretamente: {settingDate?.Value}");
                        Console.WriteLine("TEST_1_PRIMEIRO_BACKUP_DO_DIA: PASS");

                        // 2. Impedir um segundo backup no mesmo dia
                        int fileCountBefore = Directory.GetFiles(tempAutoDir, "*.cpb").Length;
                        var res2 = await backupSvc.CheckAndPerformDailyAutoBackupAsync(customSourceDb: tempDb, customTargetDir: tempAutoDir);
                        if (res2 != null)
                            throw new Exception("Teste 2: Segundo backup no mesmo dia não deveria ter sido gerado.");
                        int fileCountAfter = Directory.GetFiles(tempAutoDir, "*.cpb").Length;
                        if (fileCountBefore != fileCountAfter)
                            throw new Exception("Teste 2: Novo arquivo gerado indevidamente no mesmo dia.");
                        Console.WriteLine("TEST_2_IMPEDIR_SEGUNDO_BACKUP_MESMO_DIA: PASS");

                        // 3. Criar backup na próxima inicialização quando o backup do dia estiver ausente
                        settingDate.Value = "2026-10-01";
                        await ctx.SaveChangesAsync();
                        var res3 = await backupSvc.CheckAndPerformDailyAutoBackupAsync(customSourceDb: tempDb, customTargetDir: tempAutoDir);
                        if (res3 == null || !res3.Success)
                            throw new Exception("Teste 3: Falha ao gerar backup quando a data de hoje estava pendente.");
                        var settingDate3 = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == BackupService.SettingKeyLastAutoBackupDate);
                        if (settingDate3?.Value != todayStr)
                            throw new Exception("Teste 3: Data de hoje não atualizada após execução.");
                        Console.WriteLine("TEST_3_BACKUP_PROXIMA_INICIALIZACAO_QUANDO_AUSENTE: PASS");

                        // 4. Criar backup após falha anterior
                        var existingErr = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == BackupService.SettingKeyLastAutoBackupError);
                        if (existingErr != null) existingErr.Value = "Simulação de erro prévio";
                        else ctx.AppSettings.Add(new AppSetting { Key = BackupService.SettingKeyLastAutoBackupError, Value = "Simulação de erro prévio", UpdatedAt = DateTime.Now });
                        settingDate.Value = "2026-09-01";
                        await ctx.SaveChangesAsync();

                        var res4 = await backupSvc.CheckAndPerformDailyAutoBackupAsync(customSourceDb: tempDb, customTargetDir: tempAutoDir);
                        if (res4 == null || !res4.Success)
                            throw new Exception("Teste 4: Recuperação após falha anterior falhou.");
                        var errSettingAfter = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == BackupService.SettingKeyLastAutoBackupError);
                        if (errSettingAfter != null)
                            throw new Exception("Teste 4: Erro anterior não foi limpo após sucesso.");
                        Console.WriteLine("TEST_4_BACKUP_APOS_FALHA_ANTERIOR: PASS");

                        // 5. Comportamento quando o recurso estiver desativado
                        await backupSvc.SetAutoBackupEnabledAsync(false);
                        bool isEnabled = await backupSvc.IsAutoBackupEnabledAsync();
                        if (isEnabled) throw new Exception("Teste 5: Desativação do backup automático falhou.");
                        settingDate.Value = "2026-09-01";
                        await ctx.SaveChangesAsync();

                        var res5 = await backupSvc.CheckAndPerformDailyAutoBackupAsync(customSourceDb: tempDb, customTargetDir: tempAutoDir);
                        if (res5 != null)
                            throw new Exception("Teste 5: Backup automático não deveria rodar quando desativado.");
                        // Reativa
                        await backupSvc.SetAutoBackupEnabledAsync(true);
                        Console.WriteLine("TEST_5_COMPORTAMENTO_QUANDO_DESATIVADO: PASS");

                        // 6. Integridade do backup e possibilidade de restauração em ambiente isolado
                        string lastAutoBackupFile = res4.FilePath ?? throw new Exception("Teste 6: Caminho do arquivo de backup não pode ser nulo.");
                        var validation = backupSvc.ValidateBackupFile(lastAutoBackupFile);
                        if (!validation.IsValid)
                            throw new Exception($"Teste 6: Validação do backup gerado falhou: {validation.ErrorMessage}");

                        var restoreResult = await backupSvc.RestoreBackupAsync(lastAutoBackupFile, tempRestoredDb);
                        if (!restoreResult.Success)
                            throw new Exception($"Teste 6: Restauração em ambiente isolado falhou: {restoreResult.Message}");
                        if (!File.Exists(tempRestoredDb))
                            throw new Exception("Teste 6: Banco restaurado não existe.");

                        var restoredOpt = new DbContextOptionsBuilder<CobraPixDbContext>().UseSqlite($"Data Source={tempRestoredDb}");
                        using (var ctxRestored = new CobraPixDbContext(restoredOpt.Options))
                        {
                            var clientRestored = await ctxRestored.Clients.FirstOrDefaultAsync(c => c.Name == "Cliente Teste AutoBackup");
                            if (clientRestored == null)
                                throw new Exception("Teste 6: Dados do cliente não encontrados no banco restaurado.");
                        }
                        Console.WriteLine("TEST_6_INTEGRIDADE_E_RESTAURACAO_ISOLADA: PASS");

                        // 7. Preservação dos backups manuais existentes
                        string manualFile = Path.Combine(tempManualDir, "CobraPix_Manual_Teste.cpb");
                        var manualResult = await backupSvc.CreateBackupAsync(manualFile, tempDb);
                        if (!manualResult.Success || !File.Exists(manualFile))
                            throw new Exception("Teste 7: Criação do backup manual de teste falhou.");
                        long manualLength = new FileInfo(manualFile).Length;

                        // Roda rotina automática forçada
                        await backupSvc.CheckAndPerformDailyAutoBackupAsync(customSourceDb: tempDb, customTargetDir: tempAutoDir, force: true);
                        if (!File.Exists(manualFile) || new FileInfo(manualFile).Length != manualLength)
                            throw new Exception("Teste 7: O backup manual foi violado ou modificado pela rotina automática.");
                        Console.WriteLine("TEST_7_PRESERVACAO_BACKUPS_MANUAIS: PASS");

                        // 8. Banco ativo intacto após os testes
                        using (var ctxCheck = new CobraPixDbContext(optBuilder.Options))
                        {
                            var clientCount = await ctxCheck.Clients.CountAsync();
                            if (clientCount != 1)
                                throw new Exception("Teste 8: Banco ativo foi corrompido ou teve dados alterados.");
                        }
                        Console.WriteLine("TEST_8_BANCO_ATIVO_INTACTO: PASS");

                        // 9. Tratamento de falha de gravação ou diretório indisponível
                        string invalidDir = "Z:\\caminho_inexistente_impossivel_" + Guid.NewGuid();
                        settingDate.Value = "2026-09-01";
                        await ctx.SaveChangesAsync();
                        var res9 = await backupSvc.CheckAndPerformDailyAutoBackupAsync(customSourceDb: tempDb, customTargetDir: invalidDir, force: true);
                        if (res9 == null || res9.Success)
                            throw new Exception("Teste 9: Esperada falha controlada ao gravar em diretório inválido.");
                        var errSetting9 = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == BackupService.SettingKeyLastAutoBackupError);
                        if (errSetting9 == null || string.IsNullOrWhiteSpace(errSetting9.Value))
                            throw new Exception("Teste 9: Erro de gravação não foi registrado.");
                        var dateSetting9 = await ctx.AppSettings.FirstOrDefaultAsync(s => s.Key == BackupService.SettingKeyLastAutoBackupDate);
                        if (dateSetting9?.Value == todayStr)
                            throw new Exception("Teste 9: Data de hoje não deveria ter sido marcada como sucesso em caso de falha.");
                        Console.WriteLine("TEST_9_TRATAMENTO_FALHA_GRAVACAO: PASS");

                        // 10. Não bloquear interface e concorrência protegida
                        var taskA = backupSvc.CheckAndPerformDailyAutoBackupAsync(customSourceDb: tempDb, customTargetDir: tempAutoDir, force: true);
                        var taskB = backupSvc.CheckAndPerformDailyAutoBackupAsync(customSourceDb: tempDb, customTargetDir: tempAutoDir, force: true);
                        await Task.WhenAll(taskA, taskB);
                        Console.WriteLine("TEST_10_NAO_BLOQUEAR_UI_E_CONCORRENCIA: PASS");
                    }

                    try { File.Delete(tempDb); } catch { }
                    try { File.Delete(tempRestoredDb); } catch { }
                    try { Directory.Delete(tempAutoDir, true); } catch { }
                    try { Directory.Delete(tempManualDir, true); } catch { }

                    Console.WriteLine("ALL_AUTO_BACKUP_TESTS_PASSED");
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("AUTO_BACKUP_TEST_FAILED: " + ex.Message + "\n" + ex.StackTrace);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            // Suporte a validação automatizada do Módulo Crédito & Giro em ambiente isolado
            if (e.Args.Length > 0 && e.Args[0] == "--test-credit-giro")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "credit_test_result.log");
                try
                {
                    var results = await CreditGiroTests.RunAllTestsAsync();
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("CREDIT_TEST_FAILED: " + ex.Message + "\n" + ex.StackTrace);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            // Suporte a homologação controlada com cópia da base legada
            if (e.Args.Length > 0 && e.Args[0] == "--homologate-legacy-copy")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "homologation_legacy_copy_result.log");
                try
                {
                    string? customCopy = e.Args.Length > 1 ? e.Args[1] : null;
                    var results = await LegacyCopyHomologationValidator.RunHomologationAsync(customCopy);
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("HOMOLOGATION_FAILED: " + ex.Message + "\n" + ex.StackTrace);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

            // Suporte a migração controlada de produção do Módulo Crédito & Giro
            if (e.Args.Length > 0 && e.Args[0] == "--migrate-production-credit")
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "production_credit_migration.log");
                try
                {
                    string? targetDb = null;
                    for (int i = 1; i < e.Args.Length; i++)
                    {
                        if (e.Args[i] == "--target-db" && i + 1 < e.Args.Length)
                        {
                            targetDb = e.Args[i + 1];
                            break;
                        }
                    }

                    var results = await ProductionCreditMigrationRunner.ExecuteMigrationAsync(targetDb);
                    File.WriteAllLines(logFile, results);
                    foreach (var r in results) Console.WriteLine(r);
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(logFile, $"ERRO: {ex.Message}\n{ex.StackTrace}");
                    Console.Error.WriteLine("MIGRATION_FAILED: " + ex.Message + "\n" + ex.StackTrace);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    Shutdown(Environment.ExitCode);
                }
                return;
            }

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
                    // Dispara checagem coordenada de backup automático diário fora da UI thread (descartada se concorrente ou se já realizado hoje)
                    TriggerCoordinatedAutoBackup();

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

            // Disparo inicial coordenado do backup automático diário após inicialização da interface
            if (!string.Equals(Environment.GetEnvironmentVariable("COBRAPIX_DISABLE_BACKGROUND_ROUTINES"), "1", StringComparison.OrdinalIgnoreCase))
            {
                _ = Task.Delay(3000).ContinueWith(_ => TriggerCoordinatedAutoBackup());
            }
        }

        /// <summary>
        /// Dispara o fluxo coordenado e não bloqueante de verificação do backup automático diário.
        /// Protegido internamente contra concorrência por semáforo e verificação de duplicidade.
        /// </summary>
        private static void TriggerCoordinatedAutoBackup()
        {
            if (string.Equals(Environment.GetEnvironmentVariable("COBRAPIX_DISABLE_BACKGROUND_ROUTINES"), "1", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = Services?.CreateScope();
                    var backupSvc = scope?.ServiceProvider.GetService<IBackupService>();
                    if (backupSvc != null)
                    {
                        await backupSvc.CheckAndPerformDailyAutoBackupAsync();
                    }
                }
                catch { }
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Libera o Mutex de instância única ao encerrar o aplicativo
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
            }
            catch { }
            base.OnExit(e);
        }
    }
}
