using System;
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
    /// Gera documentos A4 profissionais com identidade visual CobraPix, independentemente do tema do app.
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

                // Contexto explícito injetado: nunca faz fallback implícito para App.Services
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

            string? effectiveCompany = ResolveCompanyName(companyName);

            using var document = new PdfDocument();
            document.Info.Title = $"Comprovante - {transaction.ClientName}";
            document.Info.Author = !string.IsNullOrWhiteSpace(effectiveCompany) ? effectiveCompany : "CobraPix PC";
            document.Info.Subject = "Comprovante de Pagamento";

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

            string receiptsDir = GetReceiptsDirectory();
            string fileName = GetSuggestedFileName(transaction);
            string fullReceiptPath = Path.Combine(receiptsDir, fileName);

            if (DatabasePath.IsTestExecutionMode())
            {
                fullReceiptPath = DatabasePath.AssertSafeTestFilePath(fullReceiptPath, "Resolução de caminho padrão de recibo PDF");
            }

            if (!Directory.Exists(receiptsDir))
            {
                Directory.CreateDirectory(receiptsDir);
            }

            return fullReceiptPath;
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

            return $"Comprovante_{cleanClient}_{datePart}.pdf";
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

        private static void DrawReceipt(XGraphics gfx, FinancialTransaction transaction, double pageWidth, double pageHeight, string? companyName)
        {
            double marginLeft = 40;
            double marginRight = 40;
            double contentWidth = pageWidth - marginLeft - marginRight;

            bool hasCompany = !string.IsNullOrWhiteSpace(companyName);

            // Paleta de Cores do Documento
            var brushTopBar = new XSolidBrush(XColor.FromArgb(124, 58, 237));     // #7C3AED CobraPix Violet
            var brushTitle = new XSolidBrush(XColor.FromArgb(15, 23, 42));       // #0F172A Dark Slate
            var brushCompany = new XSolidBrush(XColor.FromArgb(71, 85, 105));    // #475569 Slate
            var brushSubtitle = new XSolidBrush(XColor.FromArgb(99, 102, 241));   // #6366F1 Indigo
            var brushLabel = new XSolidBrush(XColor.FromArgb(100, 116, 139));    // #64748B Slate Muted
            var brushValue = new XSolidBrush(XColor.FromArgb(15, 23, 42));       // #0F172A Value Dark
            var brushFooter = new XSolidBrush(XColor.FromArgb(148, 163, 184));   // #94A3B8 Gray

            var penDivider = new XPen(XColor.FromArgb(226, 232, 240), 1.0);     // #E2E8F0
            var penCardBorder = new XPen(XColor.FromArgb(226, 232, 240), 1.0);
            var penAmountBorder = new XPen(XColor.FromArgb(16, 185, 129), 1.5); // #10B981 Emerald
            var brushAmountBg = new XSolidBrush(XColor.FromArgb(240, 253, 244)); // #F0FDF4 Emerald light
            var brushAmountText = new XSolidBrush(XColor.FromArgb(6, 95, 70));   // #065F46 Emerald dark

            // Fontes
            var fontTitle = new XFont("Arial", 22, XFontStyleEx.Bold);
            var fontCompanyHeader = new XFont("Arial", 12, XFontStyleEx.Bold);
            var fontSub = new XFont("Arial", 11, XFontStyleEx.Bold);
            var fontBadge = new XFont("Arial", 9, XFontStyleEx.Bold);
            var fontSection = new XFont("Arial", 10.5, XFontStyleEx.Bold);
            var fontLabel = new XFont("Arial", 8.5, XFontStyleEx.Bold);
            var fontValue = new XFont("Arial", 10, XFontStyleEx.Regular);
            var fontValueBold = new XFont("Arial", 10.5, XFontStyleEx.Bold);
            var fontAmountLabel = new XFont("Arial", 9, XFontStyleEx.Bold);
            var fontAmountValue = new XFont("Arial", 24, XFontStyleEx.Bold);
            var fontSuccessTitle = new XFont("Arial", 11, XFontStyleEx.Bold);
            var fontSuccessSub = new XFont("Arial", 9.5, XFontStyleEx.Regular);
            var fontNotice = new XFont("Arial", 8, XFontStyleEx.Italic);
            var fontFooter = new XFont("Arial", 8.5, XFontStyleEx.Regular);

            // 1. Barra superior colorida CobraPix
            gfx.DrawRectangle(brushTopBar, marginLeft, 32, contentWidth, 5);

            // 2. Cabeçalho
            double headerDividerY;
            if (hasCompany)
            {
                gfx.DrawString("COBRAPIX", fontTitle, brushTitle, new XPoint(marginLeft, 64));
                gfx.DrawString(companyName!, fontCompanyHeader, brushCompany, new XPoint(marginLeft, 82));
                gfx.DrawString("RECIBO DE PAGAMENTO", fontSub, brushSubtitle, new XPoint(marginLeft, 98));
                headerDividerY = 110;
            }
            else
            {
                gfx.DrawString("COBRAPIX", fontTitle, brushTitle, new XPoint(marginLeft, 68));
                gfx.DrawString("RECIBO DE PAGAMENTO", fontSub, brushSubtitle, new XPoint(marginLeft, 88));
                headerDividerY = 102;
            }

            // Badge de Tipo de Pagamento (Direita)
            bool isMonthly = transaction.TransactionType == FinancialTransactionType.Monthly;
            string typeBadgeText = isMonthly ? "MENSALIDADE" : "PARCELA";
            double badgeWidth = 110;
            double badgeHeight = 24;
            double badgeX = marginLeft + contentWidth - badgeWidth;
            double badgeY = 52;

            var brushBadgeBg = isMonthly
                ? new XSolidBrush(XColor.FromArgb(254, 243, 199)) // Amarelo
                : new XSolidBrush(XColor.FromArgb(237, 233, 254)); // Roxo
            var penBadgeBorder = isMonthly
                ? new XPen(XColor.FromArgb(245, 158, 11), 1)
                : new XPen(XColor.FromArgb(196, 181, 253), 1);
            var brushBadgeText = isMonthly
                ? new XSolidBrush(XColor.FromArgb(180, 83, 9))
                : new XSolidBrush(XColor.FromArgb(109, 40, 217));

            gfx.DrawRoundedRectangle(penBadgeBorder, brushBadgeBg, badgeX, badgeY, badgeWidth, badgeHeight, 5, 5);
            var badgeTextSize = gfx.MeasureString(typeBadgeText, fontBadge);
            gfx.DrawString(typeBadgeText, fontBadge, brushBadgeText,
                new XPoint(badgeX + (badgeWidth - badgeTextSize.Width) / 2, badgeY + 16));

            // Linha divisória após cabeçalho
            gfx.DrawLine(penDivider, marginLeft, headerDividerY, marginLeft + contentWidth, headerDividerY);

            // 3. Card de Destaque: Valor Recebido / Valor Pago
            double cardY = headerDividerY + 12;
            double cardHeight = 68;
            gfx.DrawRoundedRectangle(penAmountBorder, brushAmountBg, marginLeft, cardY, contentWidth, cardHeight, 6, 6);

            gfx.DrawString("VALOR EFETIVAMENTE PAGO", fontAmountLabel, new XSolidBrush(XColor.FromArgb(4, 120, 87)), new XPoint(marginLeft + 16, cardY + 22));
            string formattedAmount = transaction.Amount.ToString("C2", PtBr);
            gfx.DrawString(formattedAmount, fontAmountValue, brushAmountText, new XPoint(marginLeft + 16, cardY + 52));

            // Status no lado direito do Card
            double statusPillW = 120;
            double statusPillH = 24;
            double statusPillX = marginLeft + contentWidth - statusPillW - 16;
            double statusPillY = cardY + 16;
            var brushPillBg = new XSolidBrush(XColor.FromArgb(220, 252, 231));
            var penPillBorder = new XPen(XColor.FromArgb(134, 239, 172), 1);
            var brushPillText = new XSolidBrush(XColor.FromArgb(21, 128, 61));

            gfx.DrawRoundedRectangle(penPillBorder, brushPillBg, statusPillX, statusPillY, statusPillW, statusPillH, 4, 4);
            var statusText = "✓ RECEBIDO";
            var statusSize = gfx.MeasureString(statusText, fontBadge);
            gfx.DrawString(statusText, fontBadge, brushPillText,
                new XPoint(statusPillX + (statusPillW - statusSize.Width) / 2, statusPillY + 16));

            // 4. Seção Detalhes da Movimentação
            double detailsStartY = cardY + cardHeight + 14;
            gfx.DrawString("DETALHES DO PAGAMENTO", fontSection, brushTitle, new XPoint(marginLeft, detailsStartY));
            gfx.DrawLine(penDivider, marginLeft, detailsStartY + 5, marginLeft + contentWidth, detailsStartY + 5);

            // Container de Detalhes
            double tableY = detailsStartY + 12;
            double tableHeight = 310;
            var brushTableBg = new XSolidBrush(XColor.FromArgb(248, 250, 252)); // #F8FAFC
            gfx.DrawRoundedRectangle(penCardBorder, brushTableBg, marginLeft, tableY, contentWidth, tableHeight, 6, 6);

            // Linhas internas com dados reais
            double currentY = tableY + 20;
            double col1LabelX = marginLeft + 16;
            double col1ValueX = marginLeft + 140;
            double rowSpacing = 24;

            // Cliente
            gfx.DrawString("CLIENTE:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            string clientDisplay = string.IsNullOrWhiteSpace(transaction.ClientPhone)
                ? transaction.ClientName.ToUpperInvariant()
                : $"{transaction.ClientName.ToUpperInvariant()} ({transaction.ClientPhone})";
            gfx.DrawString(clientDisplay, fontValueBold, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Operação
            gfx.DrawString("OPERAÇÃO:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            string operacao = isMonthly ? "Mensalidade Recorrente" : "Pagamento de Parcela";
            gfx.DrawString(operacao, fontValue, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Referência
            gfx.DrawString("REFERÊNCIA:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            string refText = GetReferenceDisplay(transaction);
            gfx.DrawString(refText, fontValueBold, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Contrato / Descritivo de Venda (se aplicável)
            if (!string.IsNullOrWhiteSpace(transaction.Reference) && transaction.Reference != refText)
            {
                gfx.DrawString("CONTRATO / VENDA:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
                gfx.DrawString(transaction.Reference, fontValue, brushValue, new XPoint(col1ValueX, currentY));
                currentY += rowSpacing;
            }

            // Valor Original (quando aplicável)
            if (transaction.OriginalAmount.HasValue && transaction.OriginalAmount.Value > 0)
            {
                gfx.DrawString("VALOR ORIGINAL:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
                gfx.DrawString(transaction.OriginalAmount.Value.ToString("C2", PtBr), fontValue, brushValue, new XPoint(col1ValueX, currentY));
                currentY += rowSpacing;
            }

            // Valor Pago
            gfx.DrawString("VALOR PAGO:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            gfx.DrawString(transaction.Amount.ToString("C2", PtBr), fontValueBold, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Total Já Pago
            if (transaction.TotalPaid.HasValue)
            {
                gfx.DrawString("TOTAL JÁ PAGO:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
                gfx.DrawString(transaction.TotalPaid.Value.ToString("C2", PtBr), fontValue, brushValue, new XPoint(col1ValueX, currentY));
                currentY += rowSpacing;
            }

            // Saldo Restante
            if (transaction.RemainingBalance.HasValue)
            {
                gfx.DrawString("SALDO RESTANTE:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
                var balanceBrush = transaction.RemainingBalance.Value == 0
                    ? new XSolidBrush(XColor.FromArgb(21, 128, 61))
                    : brushValue;
                gfx.DrawString(transaction.RemainingBalance.Value.ToString("C2", PtBr), fontValueBold, balanceBrush, new XPoint(col1ValueX, currentY));
                currentY += rowSpacing;
            }

            // Forma de Pagamento
            gfx.DrawString("FORMA DE PAGAMENTO:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            string methodDisplay = transaction.PaymentMethod switch
            {
                PaymentMethod.Pix => "Pix (Transferência Instantânea)",
                PaymentMethod.Cash => "Dinheiro (Espécie)",
                PaymentMethod.Card => "Cartão de Crédito / Débito",
                PaymentMethod.Other => "Outro",
                _ => "Não informado"
            };
            gfx.DrawString(methodDisplay, fontValueBold, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Data do Pagamento
            gfx.DrawString("DATA DO PAGAMENTO:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            gfx.DrawString(transaction.PaymentDate.ToString("dd/MM/yyyy", PtBr), fontValue, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Hora do Pagamento
            gfx.DrawString("HORA DO PAGAMENTO:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            gfx.DrawString(transaction.PaymentDate.ToString("HH:mm", PtBr), fontValue, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Observações (se houver)
            if (!string.IsNullOrWhiteSpace(transaction.Notes))
            {
                gfx.DrawString("OBSERVAÇÕES:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
                gfx.DrawString(transaction.Notes, fontValue, brushValue, new XPoint(col1ValueX, currentY));
                currentY += rowSpacing;
            }

            // ID do Documento / Registro
            gfx.DrawString("IDENTIFICAÇÃO:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            string idTransacao = $"CPX-{(isMonthly ? "MTH" : "PAR")}-{transaction.Id:D6}";
            gfx.DrawString(idTransacao, fontValue, brushLabel, new XPoint(col1ValueX, currentY));

            // 5. Termo de Confirmação e Quitação
            double noticeY = tableY + tableHeight + 14;
            double noticeH = 68;
            var brushNoticeBg = new XSolidBrush(XColor.FromArgb(240, 253, 244)); // Emerald light
            var penNoticeBorder = new XPen(XColor.FromArgb(187, 247, 208), 1);   // Emerald light border
            gfx.DrawRoundedRectangle(penNoticeBorder, brushNoticeBg, marginLeft, noticeY, contentWidth, noticeH, 6, 6);

            string confirmTitle = "PAGAMENTO EFETUADO COM SUCESSO!";
            string confirmSub = "Obrigado pelo pagamento.";
            string noticeText = "Este comprovante comprova o recebimento do valor especificado acima no sistema CobraPix PC para fins contábeis.";

            var brushSuccessDark = new XSolidBrush(XColor.FromArgb(6, 95, 70));
            gfx.DrawString(confirmTitle, fontSuccessTitle, brushSuccessDark, new XPoint(marginLeft + 16, noticeY + 20));
            gfx.DrawString(confirmSub, fontSuccessSub, brushTitle, new XPoint(marginLeft + 16, noticeY + 38));
            gfx.DrawString(noticeText, fontNotice, brushLabel, new XPoint(marginLeft + 16, noticeY + 54));

            // 6. Rodapé
            double footerY = pageHeight - 36;
            gfx.DrawLine(penDivider, marginLeft, footerY - 12, marginLeft + contentWidth, footerY - 12);

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
