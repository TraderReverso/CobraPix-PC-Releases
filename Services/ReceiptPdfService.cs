using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Resolvedor de fontes TrueType nativo do Windows para o PDFsharp 6.x.
    /// Carrega as fontes padrão (Arial) diretamente da pasta C:\Windows\Fonts.
    /// </summary>
    public class WindowsFontResolver : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            string key = "Arial";
            if (isBold && isItalic) key = "Arial-BoldItalic";
            else if (isBold) key = "Arial-Bold";
            else if (isItalic) key = "Arial-Italic";

            return new FontResolverInfo(key);
        }

        public byte[]? GetFont(string faceName)
        {
            try
            {
                string fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                string fileName = faceName switch
                {
                    "Arial-Bold" => "arialbd.ttf",
                    "Arial-Italic" => "ariali.ttf",
                    "Arial-BoldItalic" => "arialbi.ttf",
                    _ => "arial.ttf"
                };

                string fullPath = Path.Combine(fontsDir, fileName);
                if (File.Exists(fullPath))
                {
                    return File.ReadAllBytes(fullPath);
                }

                // Fallback para arial.ttf se a variante não existir
                string defaultArial = Path.Combine(fontsDir, "arial.ttf");
                if (File.Exists(defaultArial))
                {
                    return File.ReadAllBytes(defaultArial);
                }
            }
            catch
            {
                // Silencioso em caso de restrição de I/O
            }

            return null;
        }
    }

    /// <summary>
    /// Serviço de geração e exportação de comprovantes de pagamento em PDF para o CobraPix PC.
    /// Gera documentos A4 profissionais com identidade visual CobraPix, layout dinâmico sem sobreposição
    /// e detalhamento completo de dívida global, amortização, juros e saldos restantes.
    /// </summary>
    public class ReceiptPdfService : IReceiptPdfService
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        public string? CompanyName { get; set; }

        static ReceiptPdfService()
        {
            try
            {
                if (GlobalFontSettings.FontResolver == null)
                {
                    GlobalFontSettings.FontResolver = new WindowsFontResolver();
                }
            }
            catch
            {
                // Já inicializado anteriormente no AppDomain
            }
        }

        public const string ReceiptsDirectoryEnvVar = "COBRAPIX_RECEIPTS_DIRECTORY";

        private readonly CobraPixDbContext? _dbContext;

        public ReceiptPdfService()
        {
        }

        public ReceiptPdfService(CobraPixDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public ReceiptPdfService(string? companyName)
        {
            CompanyName = companyName;
        }

        private string? ResolveCompanyName(string? explicitName)
        {
            if (!string.IsNullOrWhiteSpace(explicitName))
                return explicitName.Trim();

            if (!string.IsNullOrWhiteSpace(CompanyName))
                return CompanyName.Trim();

            if (_dbContext != null)
            {
                try
                {
                    var setting = _dbContext.AppSettings.AsNoTracking()
                        .FirstOrDefault(s => s.Key == "App.CompanyName" || s.Key == "App.IssuerName");
                    if (!string.IsNullOrWhiteSpace(setting?.Value))
                        return setting.Value.Trim();
                }
                catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
                {
                    throw;
                }
                catch { }

                return null;
            }

            try
            {
                using var scope = App.Services?.CreateScope();
                var ctx = scope?.ServiceProvider.GetService<CobraPixDbContext>();
                if (ctx != null)
                {
                    var setting = ctx.AppSettings.AsNoTracking()
                        .FirstOrDefault(s => s.Key == "App.CompanyName" || s.Key == "App.IssuerName");
                    if (!string.IsNullOrWhiteSpace(setting?.Value))
                        return setting.Value.Trim();
                }
            }
            catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
            {
                throw;
            }
            catch
            {
            }

            return null;
        }

        public byte[] GenerateReceiptBytes(FinancialTransaction transaction) => GenerateReceiptBytes(transaction, null);

        public byte[] GenerateReceiptBytes(FinancialTransaction transaction, string? companyName = null)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            // Garante que a dívida global real esteja preenchida caso o objeto não a tenha
            if (!transaction.GlobalInitialDebt.HasValue && transaction.ClientId > 0)
            {
                try
                {
                    CobraPixDbContext? ctx = _dbContext;
                    IServiceScope? scope = null;
                    if (ctx == null && App.Services != null)
                    {
                        scope = App.Services.CreateScope();
                        ctx = scope.ServiceProvider.GetService<CobraPixDbContext>();
                    }

                    if (ctx != null)
                    {
                        var clientSales = ctx.Sales.Where(s => s.ClientId == transaction.ClientId).Select(s => s.Id).ToList();
                        var installments = ctx.Installments
                            .Where(i => clientSales.Contains(i.SaleId))
                            .Include(i => i.Payments)
                            .ToList();

                        if (installments.Count > 0)
                        {
                            var stmt = PaymentLogic.CalculateClientGlobalStatement(
                                installments.Select(i => (
                                    value: i.Amount,
                                    paidAmount: i.Payments.Sum(p => p.AmountPaid),
                                    status: i.Status,
                                    id: i.Id
                                ))
                            );

                            transaction.GlobalInitialDebt = stmt.TotalInitialDebt;
                            transaction.GlobalTotalPaid = stmt.TotalPaid;
                            transaction.GlobalRemainingBalance = stmt.RemainingBalance;
                        }
                    }

                    scope?.Dispose();
                }
                catch
                {
                }
            }

            string? effectiveCompany = ResolveCompanyName(companyName);

            using var document = new PdfDocument();
            document.Info.Title = transaction.IsRefund
                ? $"Comprovante de Estorno - {transaction.ClientName}"
                : $"Comprovante - {transaction.ClientName}";
            document.Info.Author = !string.IsNullOrWhiteSpace(effectiveCompany) ? effectiveCompany : "CobraPix PC";
            document.Info.Subject = transaction.IsRefund
                ? "Comprovante de Estorno de Pagamento"
                : "Comprovante de Pagamento";

            var page = document.AddPage();
            page.Size = PageSize.A4;
            page.Orientation = PageOrientation.Portrait;

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                DrawReceipt(gfx, transaction, page.Width.Point, page.Height.Point, effectiveCompany);
            }

            using var ms = new MemoryStream();
            document.Save(ms);
            return ms.ToArray();
        }

        public string GenerateAndSaveReceipt(FinancialTransaction transaction, string destinationPath) =>
            GenerateAndSaveReceipt(transaction, destinationPath, null);

        public string GenerateAndSaveReceipt(FinancialTransaction transaction, string destinationPath, string? companyName = null)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Caminho de destino não pode ser vazio.", nameof(destinationPath));

            string validatedPath = DatabasePath.IsTestExecutionMode()
                ? DatabasePath.AssertSafeTestFilePath(destinationPath, "Gravação de recibo PDF")
                : Path.GetFullPath(destinationPath.Trim());

            string? dir = Path.GetDirectoryName(validatedPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                if (DatabasePath.IsTestExecutionMode())
                {
                    DatabasePath.AssertSafeTestDirectoryPath(dir, "Diretório de recibos PDF");
                }

                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            byte[] bytes = GenerateReceiptBytes(transaction, companyName);
            File.WriteAllBytes(validatedPath, bytes);
            return validatedPath;
        }

        public string GetReceiptsDirectory()
        {
            if (DatabasePath.IsTestExecutionMode())
            {
                string? envReceiptsDir = Environment.GetEnvironmentVariable(ReceiptsDirectoryEnvVar);
                string targetDir;

                if (!string.IsNullOrWhiteSpace(envReceiptsDir))
                {
                    targetDir = envReceiptsDir.Trim();
                }
                else
                {
                    string validatedDbPath = DatabasePath.GetDatabaseFilePath();
                    string dbDir = Path.GetDirectoryName(validatedDbPath)
                        ?? throw new InvalidOperationException("[SEGURANÇA DE EXECUÇÃO] Não foi possível determinar o diretório da base de teste para recibos.");
                    targetDir = Path.Combine(dbDir, "receipts");
                }

                return DatabasePath.AssertSafeTestDirectoryPath(targetDir, "Diretório de recibos PDF");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CobraPix",
                "receipts");
        }

        public string GetDefaultReceiptPath(FinancialTransaction transaction)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            string directory = GetReceiptsDirectory();
            string fileName = GetSuggestedFileName(transaction);
            return Path.Combine(directory, fileName);
        }

        public string ViewOrGenerateReceipt(FinancialTransaction transaction, string? companyName = null)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            string defaultPath = GetDefaultReceiptPath(transaction);
            if (!File.Exists(defaultPath))
            {
                GenerateAndSaveReceipt(transaction, defaultPath, companyName);
            }

            OpenPdf(defaultPath);
            return defaultPath;
        }

        public string GetSuggestedFileName(FinancialTransaction transaction)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            string rawClient = string.IsNullOrWhiteSpace(transaction.ClientName) ? "Cliente" : transaction.ClientName;
            string cleanClient = SanitizeFileName(rawClient);
            string datePart = transaction.PaymentDate.ToString("yyyy-MM-dd");
            string prefix = transaction.IsRefund ? "Comprovante_Estorno" : "Comprovante";

            return $"{prefix}_{cleanClient}_{datePart}.pdf";
        }

        public bool OpenPdf(string filePath)
        {
            if (DatabasePath.IsTestExecutionMode())
            {
                string validated = DatabasePath.AssertSafeTestFilePath(filePath, "Abertura de recibo PDF");
                return File.Exists(validated);
            }

            try
            {
                if (!File.Exists(filePath)) return false;

                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public class ReceiptRowItem
        {
            public string Label { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public bool IsBold { get; set; }
            public bool IsHighlight { get; set; }
            public bool IsSectionHeader { get; set; }

            public ReceiptRowItem(string label, string value, bool isBold = false, bool isHighlight = false, bool isSectionHeader = false)
            {
                Label = label;
                Value = value;
                IsBold = isBold;
                IsHighlight = isHighlight;
                IsSectionHeader = isSectionHeader;
            }
        }

        public static List<ReceiptRowItem> BuildReceiptRows(FinancialTransaction transaction)
        {
            var rows = new List<ReceiptRowItem>();
            bool isMonthly = transaction.TransactionType == FinancialTransactionType.Monthly;

            // 1. DADOS DO CLIENTE E CONTRATO
            rows.Add(new ReceiptRowItem("DADOS DO CLIENTE E CONTRATO", "", isSectionHeader: true));
            rows.Add(new ReceiptRowItem("Cliente:", transaction.ClientName.ToUpperInvariant(), isBold: true));
            if (!string.IsNullOrWhiteSpace(transaction.ClientPhone))
            {
                rows.Add(new ReceiptRowItem("Telefone / WhatsApp:", transaction.ClientPhone));
            }
            string refText = GetReferenceDisplay(transaction);
            rows.Add(new ReceiptRowItem("Identificação da Parcela:", refText));
            rows.Add(new ReceiptRowItem("Escopo do demonstrativo:", "Todas as obrigações e contratos do cliente"));

            // 2. RESUMO DA DÍVIDA GLOBAL DO CLIENTE
            decimal globalInitial = transaction.GlobalInitialDebt ?? transaction.OriginalAmount ?? transaction.Amount;
            decimal globalPaid = transaction.GlobalTotalPaid ?? transaction.TotalPaid ?? transaction.Amount;
            decimal globalRemaining = transaction.GlobalRemainingBalance ?? transaction.RemainingBalance ?? 0m;
            decimal parcelaValor = transaction.OriginalAmount ?? transaction.Amount;

            rows.Add(new ReceiptRowItem("RESUMO DA DÍVIDA GLOBAL DO CLIENTE", "", isSectionHeader: true));
            rows.Add(new ReceiptRowItem("Valor total inicial da dívida global:", globalInitial.ToString("C2", PtBr), isBold: true));
            rows.Add(new ReceiptRowItem("Valor da parcela atual:", parcelaValor.ToString("C2", PtBr), isBold: true));
            rows.Add(new ReceiptRowItem("Total acumulado pago:", globalPaid.ToString("C2", PtBr)));
            rows.Add(new ReceiptRowItem("Saldo devedor global atualizado:", globalRemaining.ToString("C2", PtBr), isBold: true, isHighlight: true));
            rows.Add(new ReceiptRowItem("Saldo devedor global restante:", globalRemaining.ToString("C2", PtBr), isBold: true));

            // 3. VALORES CONTRATADOS DA PARCELA
            rows.Add(new ReceiptRowItem("VALORES CONTRATADOS DA PARCELA", "", isSectionHeader: true));
            if (transaction.OriginalPrincipal.HasValue && transaction.ContractInterest.HasValue && transaction.ContractInterest.Value > 0m)
            {
                rows.Add(new ReceiptRowItem("Principal original da parcela:", transaction.OriginalPrincipal.Value.ToString("C2", PtBr)));
                rows.Add(new ReceiptRowItem("Juros contratuais da parcela:", transaction.ContractInterest.Value.ToString("C2", PtBr)));
                decimal contratado = transaction.OriginalAmount ?? (transaction.OriginalPrincipal.Value + transaction.ContractInterest.Value);
                rows.Add(new ReceiptRowItem("Valor total contratado da parcela:", contratado.ToString("C2", PtBr), isBold: true));
            }
            else if (transaction.OriginalAmount.HasValue && transaction.OriginalAmount.Value > 0m)
            {
                rows.Add(new ReceiptRowItem("Valor total contratado da parcela:", transaction.OriginalAmount.Value.ToString("C2", PtBr), isBold: true));
            }
            else
            {
                rows.Add(new ReceiptRowItem("Valor total contratado da parcela:", transaction.Amount.ToString("C2", PtBr), isBold: true));
            }

            // 4. DETALHAMENTO DA OPERAÇÃO
            rows.Add(new ReceiptRowItem("DETALHAMENTO DA OPERAÇÃO", "", isSectionHeader: true));
            if (transaction.IsRefund)
            {
                rows.Add(new ReceiptRowItem("Valor estornado nesta transação:", transaction.Amount.ToString("C2", PtBr), isBold: true, isHighlight: true));
            }
            else
            {
                rows.Add(new ReceiptRowItem("Valor pago nesta transação:", transaction.Amount.ToString("C2", PtBr), isBold: true, isHighlight: true));
            }
            decimal totalPaidParcela = transaction.TotalPaid ?? transaction.Amount;
            rows.Add(new ReceiptRowItem("Total pago acumulado na parcela:", totalPaidParcela.ToString("C2", PtBr), isBold: true));

            // 5. SALDO DA PARCELA ATUAL
            rows.Add(new ReceiptRowItem("SALDO DA PARCELA ATUAL", "", isSectionHeader: true));
            if (transaction.UnpaidPrincipal.HasValue && transaction.ContractInterest.HasValue && transaction.ContractInterest.Value > 0m)
            {
                rows.Add(new ReceiptRowItem("Principal não amortizado:", transaction.UnpaidPrincipal.Value.ToString("C2", PtBr)));
            }

            decimal remainingParcela = transaction.RemainingBalance ?? 0m;
            rows.Add(new ReceiptRowItem("Saldo da parcela atual:", remainingParcela.ToString("C2", PtBr)));

            if (transaction.OverdueInterest.HasValue && transaction.OverdueInterest.Value > 0m)
            {
                rows.Add(new ReceiptRowItem("Encargos de mora (atraso):", transaction.OverdueInterest.Value.ToString("C2", PtBr)));
                rows.Add(new ReceiptRowItem("Saldo devedor total atualizado:", (remainingParcela + transaction.OverdueInterest.Value).ToString("C2", PtBr), isBold: true, isHighlight: true));
            }
            else
            {
                rows.Add(new ReceiptRowItem("Saldo devedor restante:", remainingParcela.ToString("C2", PtBr), isBold: true));
            }

            string situacao;
            if (transaction.IsRefund)
            {
                situacao = "ESTORNO CONFIRMADO";
            }
            else
            {
                situacao = remainingParcela <= 0m ? "PARCELA QUITADA" : "PAGAMENTO PARCIAL";
            }
            rows.Add(new ReceiptRowItem("Situação da parcela:", situacao, isBold: true, isHighlight: true));

            // 6. INFORMAÇÕES DA TRANSAÇÃO
            rows.Add(new ReceiptRowItem("INFORMAÇÕES DA TRANSAÇÃO", "", isSectionHeader: true));
            string methodDisplay = transaction.PaymentMethod switch
            {
                PaymentMethod.Pix => "PIX (Transferência Instantânea)",
                PaymentMethod.Cash => "Dinheiro (Espécie)",
                PaymentMethod.Card => "Cartão de Crédito / Débito",
                PaymentMethod.Other => "Outro",
                _ => "Não especificado"
            };
            rows.Add(new ReceiptRowItem("Forma de pagamento:", methodDisplay));
            string dateLabel = transaction.IsRefund ? "Data do estorno:" : "Data do pagamento:";
            rows.Add(new ReceiptRowItem(dateLabel, transaction.PaymentDate.ToString("dd/MM/yyyy", PtBr)));
            rows.Add(new ReceiptRowItem("Hora da operação:", transaction.PaymentDate.ToString("HH:mm", PtBr)));
            string idTransacao = $"CPX-{(isMonthly ? "MTH" : "PAR")}-{transaction.Id:D6}";
            rows.Add(new ReceiptRowItem("Identificação da transação:", idTransacao));

            // 7. DADOS DO RECEBEDOR (se presentes)
            if (!string.IsNullOrWhiteSpace(transaction.ReceiverName) || !string.IsNullOrWhiteSpace(transaction.PixKey))
            {
                rows.Add(new ReceiptRowItem("DADOS DO RECEBEDOR", "", isSectionHeader: true));
                if (!string.IsNullOrWhiteSpace(transaction.ReceiverName))
                    rows.Add(new ReceiptRowItem("Recebedor:", transaction.ReceiverName));
                if (!string.IsNullOrWhiteSpace(transaction.PixKey))
                    rows.Add(new ReceiptRowItem("Chave PIX:", transaction.PixKey));
            }

            return rows;
        }

        private static void DrawReceipt(XGraphics gfx, FinancialTransaction transaction, double pageWidth, double pageHeight, string? companyName)
        {
            double marginLeft = 40;
            double marginRight = 40;
            double contentWidth = pageWidth - marginLeft - marginRight;

            bool hasCompany = !string.IsNullOrWhiteSpace(companyName);

            // Paleta de Cores do Documento
            var brushTopBar = new XSolidBrush(XColor.FromArgb(13, 71, 161));      // Azul institucional CobraPix (#0D47A1)
            var brushTitle = new XSolidBrush(XColor.FromArgb(13, 71, 161));
            var brushCompany = new XSolidBrush(XColor.FromArgb(71, 85, 105));
            var brushSubtitle = new XSolidBrush(XColor.FromArgb(55, 71, 79));
            var brushLabel = new XSolidBrush(XColor.FromArgb(100, 116, 139));
            var brushValue = new XSolidBrush(XColor.FromArgb(17, 24, 39));
            var brushFooter = new XSolidBrush(XColor.FromArgb(117, 117, 117));

            var penDivider = new XPen(XColor.FromArgb(224, 224, 224), 1.0);
            var penCardBorder = new XPen(XColor.FromArgb(224, 224, 224), 1.0);

            // Fontes
            var fontTitle = new XFont("Arial", 20, XFontStyleEx.Bold);
            var fontCompanyHeader = new XFont("Arial", 11, XFontStyleEx.Bold);
            var fontSub = new XFont("Arial", 11, XFontStyleEx.Bold);
            var fontBadge = new XFont("Arial", 8.5, XFontStyleEx.Bold);
            var fontSection = new XFont("Arial", 9.5, XFontStyleEx.Bold);
            var fontLabel = new XFont("Arial", 8.5, XFontStyleEx.Regular);
            var fontLabelBold = new XFont("Arial", 8.5, XFontStyleEx.Bold);
            var fontValue = new XFont("Arial", 9.5, XFontStyleEx.Regular);
            var fontValueBold = new XFont("Arial", 9.5, XFontStyleEx.Bold);
            var fontAmountLabel = new XFont("Arial", 8.5, XFontStyleEx.Bold);
            var fontAmountValue = new XFont("Arial", 22, XFontStyleEx.Bold);
            var fontSuccessTitle = new XFont("Arial", 10.5, XFontStyleEx.Bold);
            var fontSuccessSub = new XFont("Arial", 9, XFontStyleEx.Regular);
            var fontNotice = new XFont("Arial", 8, XFontStyleEx.Italic);
            var fontFooter = new XFont("Arial", 8, XFontStyleEx.Regular);

            // 1. Barra superior colorida CobraPix
            gfx.DrawRectangle(brushTopBar, marginLeft, 28, contentWidth, 4);

            // 2. Cabeçalho
            string headerSubtitle = transaction.IsRefund
                ? "COMPROVANTE DE ESTORNO DE PAGAMENTO"
                : "COMPROVANTE DE PAGAMENTO";

            double headerDividerY;
            if (hasCompany)
            {
                gfx.DrawString("COBRAPIX", fontTitle, brushTitle, new XPoint(marginLeft, 56));
                gfx.DrawString(companyName!, fontCompanyHeader, brushCompany, new XPoint(marginLeft, 72));
                gfx.DrawString(headerSubtitle, fontSub, brushSubtitle, new XPoint(marginLeft, 88));
                headerDividerY = 98;
            }
            else
            {
                gfx.DrawString("COBRAPIX", fontTitle, brushTitle, new XPoint(marginLeft, 60));
                gfx.DrawString(headerSubtitle, fontSub, brushSubtitle, new XPoint(marginLeft, 78));
                headerDividerY = 90;
            }

            // Badge de Tipo de Pagamento (Direita)
            bool isMonthly = transaction.TransactionType == FinancialTransactionType.Monthly;
            string typeBadgeText;
            XSolidBrush brushBadgeBg;
            XPen penBadgeBorder;
            XSolidBrush brushBadgeText;

            if (transaction.IsRefund)
            {
                typeBadgeText = "ESTORNO";
                brushBadgeBg = new XSolidBrush(XColor.FromArgb(254, 226, 226));
                penBadgeBorder = new XPen(XColor.FromArgb(239, 68, 68), 1);
                brushBadgeText = new XSolidBrush(XColor.FromArgb(185, 28, 28));
            }
            else if (isMonthly)
            {
                typeBadgeText = "MENSALIDADE";
                brushBadgeBg = new XSolidBrush(XColor.FromArgb(254, 243, 199));
                penBadgeBorder = new XPen(XColor.FromArgb(245, 158, 11), 1);
                brushBadgeText = new XSolidBrush(XColor.FromArgb(180, 83, 9));
            }
            else
            {
                typeBadgeText = "PARCELA";
                brushBadgeBg = new XSolidBrush(XColor.FromArgb(224, 231, 255));
                penBadgeBorder = new XPen(XColor.FromArgb(99, 102, 241), 1);
                brushBadgeText = new XSolidBrush(XColor.FromArgb(67, 56, 202));
            }

            double badgeWidth = 100;
            double badgeHeight = 22;
            double badgeX = marginLeft + contentWidth - badgeWidth;
            double badgeY = 46;

            gfx.DrawRoundedRectangle(penBadgeBorder, brushBadgeBg, badgeX, badgeY, badgeWidth, badgeHeight, 4, 4);
            var badgeTextSize = gfx.MeasureString(typeBadgeText, fontBadge);
            gfx.DrawString(typeBadgeText, fontBadge, brushBadgeText,
                new XPoint(badgeX + (badgeWidth - badgeTextSize.Width) / 2, badgeY + 15));

            // Linha divisória após cabeçalho
            gfx.DrawLine(penDivider, marginLeft, headerDividerY, marginLeft + contentWidth, headerDividerY);

            // 3. Card de Destaque: Valor Recebido / Valor Estornado
            double cardY = headerDividerY + 10;
            double cardHeight = 58;

            XPen penAmountBorder;
            XSolidBrush brushAmountBg;
            XSolidBrush brushAmountText;
            string amountCardLabel;
            string statusText;
            XSolidBrush brushPillBg;
            XPen penPillBorder;
            XSolidBrush brushPillText;

            if (transaction.IsRefund)
            {
                amountCardLabel = "VALOR ESTORNADO NESTA TRANSAÇÃO";
                penAmountBorder = new XPen(XColor.FromArgb(220, 38, 38), 1.5);
                brushAmountBg = new XSolidBrush(XColor.FromArgb(254, 242, 242));
                brushAmountText = new XSolidBrush(XColor.FromArgb(185, 28, 28));
                statusText = "↩ ESTORNADO";
                brushPillBg = new XSolidBrush(XColor.FromArgb(254, 226, 226));
                penPillBorder = new XPen(XColor.FromArgb(252, 165, 165), 1);
                brushPillText = new XSolidBrush(XColor.FromArgb(185, 28, 28));
            }
            else
            {
                amountCardLabel = "VALOR RECEBIDO NESTA TRANSAÇÃO";
                penAmountBorder = new XPen(XColor.FromArgb(46, 125, 50), 1.5);
                brushAmountBg = new XSolidBrush(XColor.FromArgb(240, 253, 244));
                brushAmountText = new XSolidBrush(XColor.FromArgb(46, 125, 50));
                statusText = "✓ RECEBIDO";
                brushPillBg = new XSolidBrush(XColor.FromArgb(220, 252, 231));
                penPillBorder = new XPen(XColor.FromArgb(134, 239, 172), 1);
                brushPillText = new XSolidBrush(XColor.FromArgb(46, 125, 50));
            }

            gfx.DrawRoundedRectangle(penAmountBorder, brushAmountBg, marginLeft, cardY, contentWidth, cardHeight, 6, 6);

            gfx.DrawString(amountCardLabel, fontAmountLabel, brushAmountText, new XPoint(marginLeft + 16, cardY + 20));
            string formattedAmount = transaction.Amount.ToString("C2", PtBr);
            gfx.DrawString(formattedAmount, fontAmountValue, brushAmountText, new XPoint(marginLeft + 16, cardY + 46));

            // Status no lado direito do Card
            double statusPillW = 120;
            double statusPillH = 22;
            double statusPillX = marginLeft + contentWidth - statusPillW - 16;
            double statusPillY = cardY + 18;

            gfx.DrawRoundedRectangle(penPillBorder, brushPillBg, statusPillX, statusPillY, statusPillW, statusPillH, 4, 4);
            var statusSize = gfx.MeasureString(statusText, fontBadge);
            gfx.DrawString(statusText, fontBadge, brushPillText,
                new XPoint(statusPillX + (statusPillW - statusSize.Width) / 2, statusPillY + 15));
            gfx.DrawString(statusText, fontBadge, brushPillText,
                new XPoint(statusPillX + (statusPillW - statusSize.Width) / 2, statusPillY + 15));

            // 4. Detalhamento Dinâmico em Linhas Estruturadas
            var rows = BuildReceiptRows(transaction);

            double detailsStartY = cardY + cardHeight + 10;
            double rowSpacing = 16.5; // espaçamento seguro entre linhas
            double sectionSpacing = 22.0;

            // Calcula a altura exata do container para NUNCA truncar nem sobrepor
            double calculatedTableHeight = 20.0;
            foreach (var r in rows)
            {
                calculatedTableHeight += r.IsSectionHeader ? sectionSpacing : rowSpacing;
            }
            calculatedTableHeight += 10.0;

            double tableY = detailsStartY;
            var brushTableBg = new XSolidBrush(XColor.FromArgb(248, 250, 252));
            gfx.DrawRoundedRectangle(penCardBorder, brushTableBg, marginLeft, tableY, contentWidth, calculatedTableHeight, 6, 6);

            double currentY = tableY + 16;
            double labelX = marginLeft + 14;
            double valueRightX = marginLeft + contentWidth - 14;

            foreach (var r in rows)
            {
                if (r.IsSectionHeader)
                {
                    currentY += 6;
                    gfx.DrawLine(penDivider, labelX, currentY - 8, valueRightX, currentY - 8);
                    gfx.DrawString(r.Label, fontSection, new XSolidBrush(XColor.FromArgb(21, 101, 192)), new XPoint(labelX, currentY));
                    currentY += 14;
                    continue;
                }

                var currentLabelFont = r.IsBold ? fontLabelBold : fontLabel;
                var currentValueFont = r.IsBold ? fontValueBold : fontValue;

                var valueBrush = r.IsHighlight
                    ? new XSolidBrush(XColor.FromArgb(46, 125, 50))
                    : (r.IsBold ? brushValue : brushCompany);

                // Label à esquerda
                gfx.DrawString(r.Label, currentLabelFont, brushLabel, new XPoint(labelX, currentY));

                // Valor alinhado à direita
                var valueSize = gfx.MeasureString(r.Value, currentValueFont);
                gfx.DrawString(r.Value, currentValueFont, valueBrush, new XPoint(valueRightX - valueSize.Width, currentY));

                currentY += rowSpacing;
            }

            // 5. Termo de Confirmação e Quitação (Posicionado rigorosamente abaixo do container dinâmico)
            double noticeY = tableY + calculatedTableHeight + 10;
            double noticeH = 54;

            string confirmTitle;
            string confirmSub;
            string noticeText;
            XSolidBrush brushNoticeBg;
            XPen penNoticeBorder;
            XSolidBrush brushConfirmTitle;

            if (transaction.IsRefund)
            {
                confirmTitle = "ESTORNO PROCESSADO E REGISTRADO NO HISTÓRICO";
                confirmSub = "Estorno financeiro registrado com sucesso. Os saldos e parcelas foram recalculados.";
                noticeText = "Documento emitido via aplicativo CobraPix PC para controle financeiro e prestação de contas de estornos.";
                brushNoticeBg = new XSolidBrush(XColor.FromArgb(254, 242, 242));
                penNoticeBorder = new XPen(XColor.FromArgb(254, 202, 202), 1);
                brushConfirmTitle = new XSolidBrush(XColor.FromArgb(185, 28, 28));
            }
            else
            {
                confirmTitle = "PAGAMENTO CONFIRMADO E REGISTRADO";
                confirmSub = "Obrigado pelo pagamento. Autenticação válida para quitação e abatimento.";
                noticeText = "Documento emitido via aplicativo CobraPix PC para controle financeiro e prestação de contas.";
                brushNoticeBg = new XSolidBrush(XColor.FromArgb(240, 253, 244));
                penNoticeBorder = new XPen(XColor.FromArgb(187, 247, 208), 1);
                brushConfirmTitle = new XSolidBrush(XColor.FromArgb(46, 125, 50));
            }

            gfx.DrawRoundedRectangle(penNoticeBorder, brushNoticeBg, marginLeft, noticeY, contentWidth, noticeH, 6, 6);

            gfx.DrawString(confirmTitle, fontSuccessTitle, brushConfirmTitle, new XPoint(marginLeft + 14, noticeY + 18));
            gfx.DrawString(confirmSub, fontSuccessSub, brushTitle, new XPoint(marginLeft + 14, noticeY + 32));
            gfx.DrawString(noticeText, fontNotice, brushLabel, new XPoint(marginLeft + 14, noticeY + 45));

            // 6. Rodapé
            double footerY = pageHeight - 28;
            gfx.DrawLine(penDivider, marginLeft, footerY - 10, marginLeft + contentWidth, footerY - 10);

            string footerBrand = hasCompany ? $"{companyName} · CobraPix PC" : "CobraPix PC · Gestão Financeira e Cobranças";
            gfx.DrawString(footerBrand, fontFooter, brushFooter, new XPoint(marginLeft, footerY));

            string emissionTime = $"Emitido em {DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", PtBr)}";
            var emissionSize = gfx.MeasureString(emissionTime, fontFooter);
            gfx.DrawString(emissionTime, fontFooter, brushFooter,
                new XPoint(marginLeft + contentWidth - emissionSize.Width, footerY));
        }

        private static string GetReferenceDisplay(FinancialTransaction transaction)
        {
            if (transaction.TransactionType == FinancialTransactionType.Monthly)
            {
                if (transaction.ReferenceMonth.HasValue && transaction.ReferenceYear.HasValue)
                {
                    return $"Mensalidade {transaction.ReferenceMonth.Value:D2}/{transaction.ReferenceYear.Value}";
                }
                return string.IsNullOrWhiteSpace(transaction.Description) ? "Mensalidade" : transaction.Description;
            }
            else
            {
                if (transaction.InstallmentNumber.HasValue && transaction.TotalInstallments.HasValue)
                {
                    return $"Parcela {transaction.InstallmentNumber.Value}/{transaction.TotalInstallments.Value}";
                }
                if (transaction.InstallmentNumber.HasValue)
                {
                    return $"Parcela {transaction.InstallmentNumber.Value}";
                }
                return string.IsNullOrWhiteSpace(transaction.Description) ? "Parcela" : transaction.Description;
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (invalid.Contains(c) || c == ' ' || c == '.' || c == ',' || c == '/' || c == '\\')
                {
                    if (sb.Length > 0 && sb[^1] != '_')
                        sb.Append('_');
                }
                else if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Trim('_');
        }
    }
}
