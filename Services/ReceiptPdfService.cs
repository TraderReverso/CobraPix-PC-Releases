using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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

        public byte[] GenerateReceiptBytes(FinancialTransaction transaction)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            using var document = new PdfDocument();
            document.Info.Title = $"Comprovante - {transaction.ClientName}";
            document.Info.Author = "CobraPix PC";
            document.Info.Subject = "Comprovante de Pagamento";

            var page = document.AddPage();
            page.Size = PageSize.A4;
            page.Orientation = PageOrientation.Portrait;

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                DrawReceipt(gfx, transaction, page.Width.Point, page.Height.Point);
            }

            using var ms = new MemoryStream();
            document.Save(ms);
            return ms.ToArray();
        }

        public string GenerateAndSaveReceipt(FinancialTransaction transaction, string destinationPath)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Caminho de destino não pode ser vazio.", nameof(destinationPath));

            string? dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            byte[] bytes = GenerateReceiptBytes(transaction);
            File.WriteAllBytes(destinationPath, bytes);
            return Path.GetFullPath(destinationPath);
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

        private static void DrawReceipt(XGraphics gfx, FinancialTransaction transaction, double pageWidth, double pageHeight)
        {
            double marginLeft = 40;
            double marginRight = 40;
            double contentWidth = pageWidth - marginLeft - marginRight;

            // Paleta de Cores do Documento
            var brushTopBar = new XSolidBrush(XColor.FromArgb(124, 58, 237));     // #7C3AED CobraPix Violet
            var brushTitle = new XSolidBrush(XColor.FromArgb(15, 23, 42));       // #0F172A Dark Slate
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
            var fontSub = new XFont("Arial", 11, XFontStyleEx.Bold);
            var fontBadge = new XFont("Arial", 9, XFontStyleEx.Bold);
            var fontSection = new XFont("Arial", 10.5, XFontStyleEx.Bold);
            var fontLabel = new XFont("Arial", 8.5, XFontStyleEx.Bold);
            var fontValue = new XFont("Arial", 10.5, XFontStyleEx.Regular);
            var fontValueBold = new XFont("Arial", 11, XFontStyleEx.Bold);
            var fontAmountLabel = new XFont("Arial", 9, XFontStyleEx.Bold);
            var fontAmountValue = new XFont("Arial", 24, XFontStyleEx.Bold);
            var fontNotice = new XFont("Arial", 8.5, XFontStyleEx.Italic);
            var fontFooter = new XFont("Arial", 8.5, XFontStyleEx.Regular);

            // 1. Barra superior colorida CobraPix
            gfx.DrawRectangle(brushTopBar, marginLeft, 36, contentWidth, 5);

            // 2. Cabeçalho
            gfx.DrawString("COBRAPIX", fontTitle, brushTitle, new XPoint(marginLeft, 72));
            gfx.DrawString("COMPROVANTE DE PAGAMENTO", fontSub, brushSubtitle, new XPoint(marginLeft, 90));

            // Badge de Tipo de Pagamento (Direita)
            bool isMonthly = transaction.TransactionType == FinancialTransactionType.Monthly;
            string typeBadgeText = isMonthly ? "MENSALIDADE" : "PARCELA";
            double badgeWidth = 110;
            double badgeHeight = 24;
            double badgeX = marginLeft + contentWidth - badgeWidth;
            double badgeY = 56;

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
            gfx.DrawLine(penDivider, marginLeft, 108, marginLeft + contentWidth, 108);

            // 3. Card de Destaque: Valor Recebido
            double cardY = 120;
            double cardHeight = 74;
            gfx.DrawRoundedRectangle(penAmountBorder, brushAmountBg, marginLeft, cardY, contentWidth, cardHeight, 6, 6);

            gfx.DrawString("VALOR EFETIVAMENTE RECEBIDO", fontAmountLabel, new XSolidBrush(XColor.FromArgb(4, 120, 87)), new XPoint(marginLeft + 16, cardY + 24));
            string formattedAmount = transaction.Amount.ToString("C2", PtBr);
            gfx.DrawString(formattedAmount, fontAmountValue, brushAmountText, new XPoint(marginLeft + 16, cardY + 56));

            // Status no lado direito do Card
            double statusPillW = 120;
            double statusPillH = 24;
            double statusPillX = marginLeft + contentWidth - statusPillW - 16;
            double statusPillY = cardY + 18;
            var brushPillBg = new XSolidBrush(XColor.FromArgb(220, 252, 231));
            var penPillBorder = new XPen(XColor.FromArgb(134, 239, 172), 1);
            var brushPillText = new XSolidBrush(XColor.FromArgb(21, 128, 61));

            gfx.DrawRoundedRectangle(penPillBorder, brushPillBg, statusPillX, statusPillY, statusPillW, statusPillH, 4, 4);
            var statusText = "✓ RECEBIDO";
            var statusSize = gfx.MeasureString(statusText, fontBadge);
            gfx.DrawString(statusText, fontBadge, brushPillText,
                new XPoint(statusPillX + (statusPillW - statusSize.Width) / 2, statusPillY + 16));

            // 4. Seção Detalhes da Movimentação
            double detailsStartY = 216;
            gfx.DrawString("DETALHES DO RECEBIMENTO", fontSection, brushTitle, new XPoint(marginLeft, detailsStartY));
            gfx.DrawLine(penDivider, marginLeft, detailsStartY + 6, marginLeft + contentWidth, detailsStartY + 6);

            // Container de Detalhes
            double tableY = detailsStartY + 14;
            double tableHeight = 270;
            var brushTableBg = new XSolidBrush(XColor.FromArgb(248, 250, 252)); // #F8FAFC
            gfx.DrawRoundedRectangle(penCardBorder, brushTableBg, marginLeft, tableY, contentWidth, tableHeight, 6, 6);

            // Linhas internas com dados reais
            double currentY = tableY + 22;
            double col1LabelX = marginLeft + 16;
            double col1ValueX = marginLeft + 130;
            double rowSpacing = 28;

            // Cliente
            gfx.DrawString("CLIENTE:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            string clientDisplay = string.IsNullOrWhiteSpace(transaction.ClientPhone)
                ? transaction.ClientName.ToUpperInvariant()
                : $"{transaction.ClientName.ToUpperInvariant()} ({transaction.ClientPhone})";
            gfx.DrawString(clientDisplay, fontValueBold, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Tipo / Operação
            gfx.DrawString("OPERAÇÃO:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            string operacao = isMonthly ? "Mensalidade Recorrente" : "Pagamento de Parcela";
            gfx.DrawString(operacao, fontValue, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Referência
            gfx.DrawString("REFERÊNCIA:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            string refText = GetReferenceDisplay(transaction);
            gfx.DrawString(refText, fontValueBold, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Descritivo / Venda
            if (!string.IsNullOrWhiteSpace(transaction.Reference) && transaction.Reference != refText)
            {
                gfx.DrawString("CONTRATO / VENDA:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
                gfx.DrawString(transaction.Reference, fontValue, brushValue, new XPoint(col1ValueX, currentY));
                currentY += rowSpacing;
            }

            // Forma de Pagamento
            gfx.DrawString("PAGAMENTO VIA:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            string methodDisplay = transaction.PaymentMethod switch
            {
                PaymentMethod.Pix => "PIX (Transferência Instantânea)",
                PaymentMethod.Cash => "DINHEIRO (Espécie)",
                PaymentMethod.Card => "CARTÃO DE CRÉDITO / DÉBITO",
                PaymentMethod.Other => "OUTRO",
                _ => "NÃO INFORMADO"
            };
            gfx.DrawString(methodDisplay, fontValueBold, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Data e Hora do Pagamento (Reais)
            gfx.DrawString("DATA E HORA:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            string formattedDate = transaction.PaymentDate.ToString("dd/MM/yyyy 'às' HH:mm", PtBr);
            gfx.DrawString(formattedDate, fontValue, brushValue, new XPoint(col1ValueX, currentY));
            currentY += rowSpacing;

            // Observações (se houver)
            if (!string.IsNullOrWhiteSpace(transaction.Notes))
            {
                gfx.DrawString("OBSERVAÇÕES:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
                gfx.DrawString(transaction.Notes, fontValue, brushValue, new XPoint(col1ValueX, currentY));
                currentY += rowSpacing;
            }

            // ID do Registro
            gfx.DrawString("ID REGISTRO:", fontLabel, brushLabel, new XPoint(col1LabelX, currentY));
            string idTransacao = $"CPX-{(isMonthly ? "MTH" : "PAR")}-{transaction.Id:D6}";
            gfx.DrawString(idTransacao, fontValue, brushLabel, new XPoint(col1ValueX, currentY));

            // 5. Termo de Autenticidade e Quitação
            double noticeY = tableY + tableHeight + 20;
            double noticeH = 50;
            var brushNoticeBg = new XSolidBrush(XColor.FromArgb(241, 245, 249)); // #F1F5F9
            gfx.DrawRoundedRectangle(penCardBorder, brushNoticeBg, marginLeft, noticeY, contentWidth, noticeH, 6, 6);

            string notice1 = "Este comprovante comprova o recebimento do valor especificado acima no sistema CobraPix PC.";
            string notice2 = "Documento emitido eletronicamente para fins de comprovação e registro contábil entre as partes.";
            gfx.DrawString(notice1, fontNotice, brushLabel, new XPoint(marginLeft + 12, noticeY + 20));
            gfx.DrawString(notice2, fontNotice, brushLabel, new XPoint(marginLeft + 12, noticeY + 36));

            // 6. Rodapé
            double footerY = pageHeight - 40;
            gfx.DrawLine(penDivider, marginLeft, footerY - 14, marginLeft + contentWidth, footerY - 14);

            gfx.DrawString("CobraPix PC · Gestão Financeira e Cobranças", fontFooter, brushFooter, new XPoint(marginLeft, footerY));
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
                    return $"Parcela {transaction.InstallmentNumber.Value} de {transaction.TotalInstallments.Value}";
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
