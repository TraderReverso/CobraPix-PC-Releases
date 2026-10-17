using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using CobraPix_PC.Services.Interfaces;
using CobraPix_PC.ViewModels;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Serviço de exportação de relatórios financeiros e livro caixa para CSV e Excel (.xlsx).
    /// Gera arquivos estruturados, com cabeçalhos profissionais e formatação regional brasileira (pt-BR).
    /// Opera de forma estritamente somente-leitura e offline.
    /// </summary>
    public class ReportExportService : IReportExportService
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        public async Task<ReportExportResult> ExportToCsvAsync(
            string filePath,
            IEnumerable<FinancialTransactionItemViewModel> transactions,
            string reportPeriodDescription,
            decimal totalReceived,
            decimal installmentsTotal,
            decimal monthlyTotal)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return new ReportExportResult { Success = false, Message = "Caminho do arquivo não informado." };
            }

            try
            {
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var txList = transactions?.ToList() ?? new List<FinancialTransactionItemViewModel>();
                var sb = new StringBuilder();

                // Cabeçalho institucional do relatório
                sb.AppendLine($"\"CobraPix PC - Relatório Financeiro de Recebimentos\"");
                sb.AppendLine($"\"Período: {EscapeCsv(reportPeriodDescription)}\";\"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\"");
                sb.AppendLine();

                // Colunas da tabela
                sb.AppendLine("Data;Hora;Cliente;Telefone;Tipo;Descrição;Referência;Método de Pagamento;Valor (R$)");

                foreach (var tx in txList)
                {
                    string dateStr = tx.PaymentDate.ToString("dd/MM/yyyy");
                    string timeStr = tx.PaymentDate.ToString("HH:mm:ss");
                    string clientStr = EscapeCsv(tx.ClientName);
                    string phoneStr = EscapeCsv(tx.FormattedPhone);
                    string typeStr = EscapeCsv(tx.TypeBadge);
                    string descStr = EscapeCsv(tx.Description);
                    string refStr = EscapeCsv(tx.Reference);
                    string methodStr = EscapeCsv(tx.PaymentMethodBadge);
                    string valStr = tx.Amount.ToString("N2", PtBr);

                    sb.AppendLine($"{dateStr};{timeStr};\"{clientStr}\";\"{phoneStr}\";\"{typeStr}\";\"{descStr}\";\"{refStr}\";\"{methodStr}\";{valStr}");
                }

                // Linhas de fechamento e totais
                sb.AppendLine();
                sb.AppendLine($"\"TOTAL REGISTROS\";\"{txList.Count}\";;\"TOTAL PARCELAS\";{installmentsTotal.ToString("N2", PtBr)};;\"TOTAL MENSALIDADES\";{monthlyTotal.ToString("N2", PtBr)};\"TOTAL RECEBIDO\";{totalReceived.ToString("N2", PtBr)}");

                // Grava com codificação UTF-8 com BOM para garantir abertura perfeita de acentos no Excel
                var encodingWithBom = new UTF8Encoding(true);
                await File.WriteAllTextAsync(filePath, sb.ToString(), encodingWithBom);

                long size = new FileInfo(filePath).Length;
                DiagnosticLogger.Instance.LogInformation("ReportExportService", $"Relatório CSV exportado com sucesso: {filePath} ({txList.Count} registros).");

                return new ReportExportResult
                {
                    Success = true,
                    FilePath = filePath,
                    FileSize = size,
                    RecordsCount = txList.Count,
                    Message = $"Relatório CSV exportado com sucesso com {txList.Count} registros."
                };
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Instance.LogError("ReportExportService.ExportToCsvAsync", ex);
                return new ReportExportResult
                {
                    Success = false,
                    FilePath = filePath,
                    Message = $"Erro ao exportar CSV: {ex.Message}"
                };
            }
        }

        public async Task<ReportExportResult> ExportToExcelAsync(
            string filePath,
            IEnumerable<FinancialTransactionItemViewModel> transactions,
            string reportPeriodDescription,
            decimal totalReceived,
            decimal installmentsTotal,
            decimal monthlyTotal)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return new ReportExportResult { Success = false, Message = "Caminho do arquivo não informado." };
            }

            try
            {
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var txList = transactions?.ToList() ?? new List<FinancialTransactionItemViewModel>();

                await Task.Run(() =>
                {
                    using var workbook = new XLWorkbook();
                    var ws = workbook.Worksheets.Add("Recebimentos");
                    ws.ShowGridLines = true;

                    // 1. Cabeçalho do Relatório
                    ws.Cell("A1").Value = "CobraPix PC - Relatório Financeiro de Recebimentos";
                    ws.Cell("A1").Style.Font.Bold = true;
                    ws.Cell("A1").Style.Font.FontSize = 16;
                    ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#0F172A");

                    ws.Cell("A2").Value = $"Período: {reportPeriodDescription} | Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
                    ws.Cell("A2").Style.Font.FontSize = 10;
                    ws.Cell("A2").Style.Font.FontColor = XLColor.FromHtml("#64748B");

                    // 2. Cartões de Resumo (Linha 4 e 5)
                    // Card 1: Total Recebido
                    ws.Range("A4:C4").Merge().Value = "TOTAL GERAL RECEBIDO";
                    ws.Range("A4:C4").Style.Font.Bold = true;
                    ws.Range("A4:C4").Style.Font.FontSize = 9;
                    ws.Range("A4:C4").Style.Fill.BackgroundColor = XLColor.FromHtml("#059669");
                    ws.Range("A4:C4").Style.Font.FontColor = XLColor.White;
                    ws.Range("A4:C4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Range("A5:C5").Merge().Value = (double)totalReceived;
                    ws.Range("A5:C5").Style.NumberFormat.Format = "R$ #,##0.00";
                    ws.Range("A5:C5").Style.Font.Bold = true;
                    ws.Range("A5:C5").Style.Font.FontSize = 14;
                    ws.Range("A5:C5").Style.Fill.BackgroundColor = XLColor.FromHtml("#ECFDF5");
                    ws.Range("A5:C5").Style.Font.FontColor = XLColor.FromHtml("#065F46");
                    ws.Range("A5:C5").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Card 2: Parcelas
                    ws.Range("D4:F4").Merge().Value = "TOTAL EM PARCELAS";
                    ws.Range("D4:F4").Style.Font.Bold = true;
                    ws.Range("D4:F4").Style.Font.FontSize = 9;
                    ws.Range("D4:F4").Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
                    ws.Range("D4:F4").Style.Font.FontColor = XLColor.White;
                    ws.Range("D4:F4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Range("D5:F5").Merge().Value = (double)installmentsTotal;
                    ws.Range("D5:F5").Style.NumberFormat.Format = "R$ #,##0.00";
                    ws.Range("D5:F5").Style.Font.Bold = true;
                    ws.Range("D5:F5").Style.Font.FontSize = 14;
                    ws.Range("D5:F5").Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF6FF");
                    ws.Range("D5:F5").Style.Font.FontColor = XLColor.FromHtml("#1E40AF");
                    ws.Range("D5:F5").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Card 3: Mensalidades
                    ws.Range("G4:I4").Merge().Value = "TOTAL EM MENSALIDADES";
                    ws.Range("G4:I4").Style.Font.Bold = true;
                    ws.Range("G4:I4").Style.Font.FontSize = 9;
                    ws.Range("G4:I4").Style.Fill.BackgroundColor = XLColor.FromHtml("#D97706");
                    ws.Range("G4:I4").Style.Font.FontColor = XLColor.White;
                    ws.Range("G4:I4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Range("G5:I5").Merge().Value = (double)monthlyTotal;
                    ws.Range("G5:I5").Style.NumberFormat.Format = "R$ #,##0.00";
                    ws.Range("G5:I5").Style.Font.Bold = true;
                    ws.Range("G5:I5").Style.Font.FontSize = 14;
                    ws.Range("G5:I5").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFBEB");
                    ws.Range("G5:I5").Style.Font.FontColor = XLColor.FromHtml("#92400E");
                    ws.Range("G5:I5").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // 3. Cabeçalho das Colunas (Linha 7)
                    string[] headers = { "Data", "Hora", "Cliente", "Telefone", "Tipo", "Descrição", "Referência", "Método", "Valor (R$)" };
                    int startRow = 7;
                    for (int c = 0; c < headers.Length; c++)
                    {
                        var cell = ws.Cell(startRow, c + 1);
                        cell.Value = headers[c];
                        cell.Style.Font.Bold = true;
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B");
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        if (headers[c] == "Valor (R$)")
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        else
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    }

                    // 4. Linhas de Dados
                    int currentRow = startRow + 1;
                    foreach (var tx in txList)
                    {
                        ws.Cell(currentRow, 1).Value = tx.PaymentDate.ToString("dd/MM/yyyy");
                        ws.Cell(currentRow, 2).Value = tx.PaymentDate.ToString("HH:mm:ss");
                        ws.Cell(currentRow, 3).Value = tx.ClientName;
                        ws.Cell(currentRow, 4).Value = tx.FormattedPhone;
                        ws.Cell(currentRow, 5).Value = tx.TypeBadge;
                        ws.Cell(currentRow, 6).Value = tx.Description;
                        ws.Cell(currentRow, 7).Value = tx.Reference;
                        ws.Cell(currentRow, 8).Value = tx.PaymentMethodBadge;

                        var valCell = ws.Cell(currentRow, 9);
                        valCell.Value = (double)tx.Amount;
                        valCell.Style.NumberFormat.Format = "R$ #,##0.00";
                        valCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        // Zebrado suave
                        if (currentRow % 2 == 0)
                        {
                            ws.Range(currentRow, 1, currentRow, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                        }

                        currentRow++;
                    }

                    // 5. Linha de Total Geral ao final da tabela
                    ws.Cell(currentRow, 1).Value = "TOTAL GERAL";
                    ws.Cell(currentRow, 1).Style.Font.Bold = true;
                    ws.Range(currentRow, 1, currentRow, 8).Merge();
                    ws.Range(currentRow, 1, currentRow, 8).Style.Font.Bold = true;
                    ws.Range(currentRow, 1, currentRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    var finalValCell = ws.Cell(currentRow, 9);
                    finalValCell.Value = (double)totalReceived;
                    finalValCell.Style.NumberFormat.Format = "R$ #,##0.00";
                    finalValCell.Style.Font.Bold = true;
                    finalValCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    ws.Range(currentRow, 1, currentRow, 9).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                    ws.Range(currentRow, 1, currentRow, 9).Style.Border.BottomBorder = XLBorderStyleValues.Double;

                    // Auto-ajustar largura das colunas
                    ws.Columns(1, 9).AdjustToContents();

                    workbook.SaveAs(filePath);
                });

                long size = new FileInfo(filePath).Length;
                DiagnosticLogger.Instance.LogInformation("ReportExportService", $"Relatório Excel (.xlsx) exportado com sucesso: {filePath} ({txList.Count} registros).");

                return new ReportExportResult
                {
                    Success = true,
                    FilePath = filePath,
                    FileSize = size,
                    RecordsCount = txList.Count,
                    Message = $"Relatório Excel (.xlsx) exportado com sucesso com {txList.Count} registros."
                };
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Instance.LogError("ReportExportService.ExportToExcelAsync", ex);
                return new ReportExportResult
                {
                    Success = false,
                    FilePath = filePath,
                    Message = $"Erro ao exportar Excel: {ex.Message}"
                };
            }
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\"", "\"\"").Replace("\r", "").Replace("\n", " ");
        }
    }
}
