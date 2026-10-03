using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Serviço de exportação profissional de dados para planilha Excel (.xlsx).
    /// Realiza leitura somente-leitura (AsNoTracking) de todas as entidades relacionais do sistema.
    /// Gera um arquivo local, offline, sem envio a servidores e sem alterar banco de dados, quotas ou configurações.
    /// </summary>
    public class ExcelExportService : IExcelExportService
    {
        private readonly CobraPixDbContext _context;

        public ExcelExportService(CobraPixDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<ExcelExportResult> ExportToExcelAsync(string targetFilePath, string? sourceDatabasePath = null)
        {
            if (string.IsNullOrWhiteSpace(targetFilePath))
            {
                return new ExcelExportResult
                {
                    Success = false,
                    Message = "Caminho do arquivo de destino não pode ser vazio."
                };
            }

            try
            {
                // Garante que o diretório de destino exista
                string? targetDir = Path.GetDirectoryName(targetFilePath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Define o contexto de leitura (injetado ou isolado via sourceDatabasePath para testes)
                if (!string.IsNullOrWhiteSpace(sourceDatabasePath) && File.Exists(sourceDatabasePath))
                {
                    var options = new DbContextOptionsBuilder<CobraPixDbContext>()
                        .UseSqlite($"Data Source={sourceDatabasePath}")
                        .Options;

                    using var testCtx = new CobraPixDbContext(options);
                    return await GenerateExcelWorkbookAsync(testCtx, targetFilePath);
                }
                else
                {
                    return await GenerateExcelWorkbookAsync(_context, targetFilePath);
                }
            }
            catch (Exception ex)
            {
                return new ExcelExportResult
                {
                    Success = false,
                    FilePath = targetFilePath,
                    Message = "Erro ao exportar dados para Excel: " + ex.Message
                };
            }
        }

        private static async Task<ExcelExportResult> GenerateExcelWorkbookAsync(CobraPixDbContext db, string targetFilePath)
        {
            // 1. Leitura somente-leitura das tabelas
            var clients = await db.Clients
                .AsNoTracking()
                .OrderBy(c => c.Id)
                .ToListAsync();

            var sales = await db.Sales
                .AsNoTracking()
                .Include(s => s.Client)
                .OrderBy(s => s.Id)
                .ToListAsync();

            var installments = await db.Installments
                .AsNoTracking()
                .Include(i => i.Sale)
                    .ThenInclude(s => s.Client)
                .OrderBy(i => i.SaleId)
                .ThenBy(i => i.InstallmentNumber)
                .ToListAsync();

            var payments = await db.InstallmentPayments
                .AsNoTracking()
                .Include(p => p.Installment)
                    .ThenInclude(i => i.Sale)
                        .ThenInclude(s => s.Client)
                .OrderByDescending(p => p.PaymentDate)
                .ThenByDescending(p => p.Id)
                .ToListAsync();

            var monthlyCustomers = await db.MonthlyCustomers
                .AsNoTracking()
                .Include(m => m.Client)
                .OrderBy(m => m.Id)
                .ToListAsync();

            var monthlyPayments = await db.MonthlyPayments
                .AsNoTracking()
                .Include(mp => mp.MonthlyCustomer)
                    .ThenInclude(m => m.Client)
                .OrderByDescending(mp => mp.PaymentDate)
                .ThenByDescending(mp => mp.Id)
                .ToListAsync();

            // Obtenção consolidada do Histórico Financeiro
            var financialHistoryService = new FinancialHistoryService(db);
            var financialTransactions = await financialHistoryService.GetAllTransactionsAsync();
            financialTransactions = financialTransactions
                .OrderByDescending(t => t.PaymentDate)
                .ThenByDescending(t => t.Id)
                .ToList();

            // 2. Construção da Planilha Excel (.xlsx) com ClosedXML
            using var workbook = new XLWorkbook();

            // Paleta de Cores Corporativa CobraPix
            var headerBgColor = XLColor.FromArgb(30, 41, 59); // Slate-800 (#1E293B)
            var alternateRowColor = XLColor.FromArgb(248, 250, 252); // Slate-50 (#F8FAFC)
            var borderColor = XLColor.FromArgb(226, 232, 240); // Slate-200 (#E2E8F0)

            // ==========================================
            // Aba 1: Clientes
            // ==========================================
            var wsClientes = workbook.Worksheets.Add("Clientes");
            string[] headersClientes = { "ID", "Nome do Cliente", "Telefone", "Status", "Data de Cadastro", "Última Atualização", "Observações" };
            SetupSheetHeader(wsClientes, headersClientes, headerBgColor);

            int rowIdx = 2;
            foreach (var c in clients)
            {
                wsClientes.Cell(rowIdx, 1).SetValue(c.Id);
                wsClientes.Cell(rowIdx, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsClientes.Cell(rowIdx, 2).SetValue(c.Name);

                wsClientes.Cell(rowIdx, 3).SetValue(c.Phone ?? string.Empty);

                string statusText = c.IsArchived ? "Arquivado" : "Ativo";
                wsClientes.Cell(rowIdx, 4).SetValue(statusText);
                wsClientes.Cell(rowIdx, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsClientes.Cell(rowIdx, 5).SetValue(c.CreatedAt);
                wsClientes.Cell(rowIdx, 5).Style.NumberFormat.Format = "dd/MM/yyyy HH:mm";
                wsClientes.Cell(rowIdx, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (c.UpdatedAt.HasValue)
                {
                    wsClientes.Cell(rowIdx, 6).SetValue(c.UpdatedAt.Value);
                    wsClientes.Cell(rowIdx, 6).Style.NumberFormat.Format = "dd/MM/yyyy HH:mm";
                    wsClientes.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                else
                {
                    wsClientes.Cell(rowIdx, 6).SetValue("-");
                    wsClientes.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                wsClientes.Cell(rowIdx, 7).SetValue(c.Notes ?? string.Empty);

                if (rowIdx % 2 == 1)
                {
                    wsClientes.Range(rowIdx, 1, rowIdx, headersClientes.Length).Style.Fill.BackgroundColor = alternateRowColor;
                }
                rowIdx++;
            }
            FinalizeSheet(wsClientes, rowIdx - 1, headersClientes.Length);

            // ==========================================
            // Aba 2: Vendas
            // ==========================================
            var wsVendas = workbook.Worksheets.Add("Vendas");
            string[] headersVendas = { "ID da Venda", "ID do Cliente", "Nome do Cliente", "Valor Total", "Qtd. Parcelas", "Data da Venda", "Descrição", "Observações", "Data de Cadastro" };
            SetupSheetHeader(wsVendas, headersVendas, headerBgColor);

            rowIdx = 2;
            foreach (var s in sales)
            {
                wsVendas.Cell(rowIdx, 1).SetValue(s.Id);
                wsVendas.Cell(rowIdx, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsVendas.Cell(rowIdx, 2).SetValue(s.ClientId);
                wsVendas.Cell(rowIdx, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsVendas.Cell(rowIdx, 3).SetValue(s.Client?.Name ?? $"Cliente #{s.ClientId}");

                wsVendas.Cell(rowIdx, 4).SetValue(s.TotalAmount);
                wsVendas.Cell(rowIdx, 4).Style.NumberFormat.Format = "\"R$\" #,##0.00";
                wsVendas.Cell(rowIdx, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                wsVendas.Cell(rowIdx, 5).SetValue(s.InstallmentCount);
                wsVendas.Cell(rowIdx, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsVendas.Cell(rowIdx, 6).SetValue(s.SaleDate);
                wsVendas.Cell(rowIdx, 6).Style.NumberFormat.Format = "dd/MM/yyyy";
                wsVendas.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsVendas.Cell(rowIdx, 7).SetValue(s.Description ?? string.Empty);
                wsVendas.Cell(rowIdx, 8).SetValue(s.Notes ?? string.Empty);

                wsVendas.Cell(rowIdx, 9).SetValue(s.CreatedAt);
                wsVendas.Cell(rowIdx, 9).Style.NumberFormat.Format = "dd/MM/yyyy HH:mm";
                wsVendas.Cell(rowIdx, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (rowIdx % 2 == 1)
                {
                    wsVendas.Range(rowIdx, 1, rowIdx, headersVendas.Length).Style.Fill.BackgroundColor = alternateRowColor;
                }
                rowIdx++;
            }
            FinalizeSheet(wsVendas, rowIdx - 1, headersVendas.Length);

            // ==========================================
            // Aba 3: Parcelas
            // ==========================================
            var wsParcelas = workbook.Worksheets.Add("Parcelas");
            string[] headersParcelas = { "ID da Parcela", "ID da Venda", "Nome do Cliente", "Nº Parcela", "Valor da Parcela", "Data de Vencimento", "Data de Pagamento", "Situação", "Observações" };
            SetupSheetHeader(wsParcelas, headersParcelas, headerBgColor);

            rowIdx = 2;
            foreach (var inst in installments)
            {
                wsParcelas.Cell(rowIdx, 1).SetValue(inst.Id);
                wsParcelas.Cell(rowIdx, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsParcelas.Cell(rowIdx, 2).SetValue(inst.SaleId);
                wsParcelas.Cell(rowIdx, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsParcelas.Cell(rowIdx, 3).SetValue(inst.Sale?.Client?.Name ?? $"Cliente #{inst.Sale?.ClientId}");

                wsParcelas.Cell(rowIdx, 4).SetValue(inst.InstallmentNumber);
                wsParcelas.Cell(rowIdx, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsParcelas.Cell(rowIdx, 5).SetValue(inst.Amount);
                wsParcelas.Cell(rowIdx, 5).Style.NumberFormat.Format = "\"R$\" #,##0.00";
                wsParcelas.Cell(rowIdx, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                wsParcelas.Cell(rowIdx, 6).SetValue(inst.DueDate);
                wsParcelas.Cell(rowIdx, 6).Style.NumberFormat.Format = "dd/MM/yyyy";
                wsParcelas.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (inst.PaidDate.HasValue)
                {
                    wsParcelas.Cell(rowIdx, 7).SetValue(inst.PaidDate.Value);
                    wsParcelas.Cell(rowIdx, 7).Style.NumberFormat.Format = "dd/MM/yyyy";
                    wsParcelas.Cell(rowIdx, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                else
                {
                    wsParcelas.Cell(rowIdx, 7).SetValue("-");
                    wsParcelas.Cell(rowIdx, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                wsParcelas.Cell(rowIdx, 8).SetValue(FormatInstallmentStatus(inst.Status));
                wsParcelas.Cell(rowIdx, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsParcelas.Cell(rowIdx, 9).SetValue(inst.Notes ?? string.Empty);

                if (rowIdx % 2 == 1)
                {
                    wsParcelas.Range(rowIdx, 1, rowIdx, headersParcelas.Length).Style.Fill.BackgroundColor = alternateRowColor;
                }
                rowIdx++;
            }
            FinalizeSheet(wsParcelas, rowIdx - 1, headersParcelas.Length);

            // ==========================================
            // Aba 4: Pagamentos
            // ==========================================
            var wsPagamentos = workbook.Worksheets.Add("Pagamentos");
            string[] headersPagamentos = { "ID Pagamento", "ID da Parcela", "Nº Parcela", "ID da Venda", "Nome do Cliente", "Valor Pago", "Data do Pagamento", "Forma de Pagamento", "Observações" };
            SetupSheetHeader(wsPagamentos, headersPagamentos, headerBgColor);

            rowIdx = 2;
            foreach (var p in payments)
            {
                wsPagamentos.Cell(rowIdx, 1).SetValue(p.Id);
                wsPagamentos.Cell(rowIdx, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsPagamentos.Cell(rowIdx, 2).SetValue(p.InstallmentId);
                wsPagamentos.Cell(rowIdx, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                int instNum = p.Installment?.InstallmentNumber ?? 0;
                wsPagamentos.Cell(rowIdx, 3).SetValue(instNum > 0 ? instNum.ToString() : "-");
                wsPagamentos.Cell(rowIdx, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                int saleId = p.Installment?.SaleId ?? 0;
                wsPagamentos.Cell(rowIdx, 4).SetValue(saleId > 0 ? saleId.ToString() : "-");
                wsPagamentos.Cell(rowIdx, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsPagamentos.Cell(rowIdx, 5).SetValue(p.Installment?.Sale?.Client?.Name ?? "Cliente");

                wsPagamentos.Cell(rowIdx, 6).SetValue(p.AmountPaid);
                wsPagamentos.Cell(rowIdx, 6).Style.NumberFormat.Format = "\"R$\" #,##0.00";
                wsPagamentos.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                wsPagamentos.Cell(rowIdx, 7).SetValue(p.PaymentDate);
                wsPagamentos.Cell(rowIdx, 7).Style.NumberFormat.Format = "dd/MM/yyyy HH:mm";
                wsPagamentos.Cell(rowIdx, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsPagamentos.Cell(rowIdx, 8).SetValue(FormatPaymentMethod(p.PaymentMethod));
                wsPagamentos.Cell(rowIdx, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsPagamentos.Cell(rowIdx, 9).SetValue(p.Notes ?? string.Empty);

                if (rowIdx % 2 == 1)
                {
                    wsPagamentos.Range(rowIdx, 1, rowIdx, headersPagamentos.Length).Style.Fill.BackgroundColor = alternateRowColor;
                }
                rowIdx++;
            }
            FinalizeSheet(wsPagamentos, rowIdx - 1, headersPagamentos.Length);

            // ==========================================
            // Aba 5: Mensalistas
            // ==========================================
            var wsMensalistas = workbook.Worksheets.Add("Mensalistas");
            string[] headersMensalistas = { "ID Mensalista", "ID do Cliente", "Nome do Cliente", "Telefone", "Valor Mensalidade", "Dia do Vencimento", "Status", "Data de Início", "Observações" };
            SetupSheetHeader(wsMensalistas, headersMensalistas, headerBgColor);

            rowIdx = 2;
            foreach (var m in monthlyCustomers)
            {
                wsMensalistas.Cell(rowIdx, 1).SetValue(m.Id);
                wsMensalistas.Cell(rowIdx, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsMensalistas.Cell(rowIdx, 2).SetValue(m.ClientId);
                wsMensalistas.Cell(rowIdx, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsMensalistas.Cell(rowIdx, 3).SetValue(m.Client?.Name ?? $"Cliente #{m.ClientId}");
                wsMensalistas.Cell(rowIdx, 4).SetValue(m.Client?.Phone ?? string.Empty);

                wsMensalistas.Cell(rowIdx, 5).SetValue(m.MonthlyAmount);
                wsMensalistas.Cell(rowIdx, 5).Style.NumberFormat.Format = "\"R$\" #,##0.00";
                wsMensalistas.Cell(rowIdx, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                wsMensalistas.Cell(rowIdx, 6).SetValue($"Dia {m.DueDay:D2}");
                wsMensalistas.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                string statusText = m.IsActive ? "Ativo" : "Inativo";
                wsMensalistas.Cell(rowIdx, 7).SetValue(statusText);
                wsMensalistas.Cell(rowIdx, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsMensalistas.Cell(rowIdx, 8).SetValue(m.StartDate);
                wsMensalistas.Cell(rowIdx, 8).Style.NumberFormat.Format = "dd/MM/yyyy";
                wsMensalistas.Cell(rowIdx, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsMensalistas.Cell(rowIdx, 9).SetValue(m.Notes ?? string.Empty);

                if (rowIdx % 2 == 1)
                {
                    wsMensalistas.Range(rowIdx, 1, rowIdx, headersMensalistas.Length).Style.Fill.BackgroundColor = alternateRowColor;
                }
                rowIdx++;
            }
            FinalizeSheet(wsMensalistas, rowIdx - 1, headersMensalistas.Length);

            // ==========================================
            // Aba 6: Pagamentos Mensais
            // ==========================================
            var wsPagamentosMensais = workbook.Worksheets.Add("Pagamentos Mensais");
            string[] headersPagMensais = { "ID Pagamento", "ID Mensalista", "Nome do Cliente", "Mês Ref.", "Ano Ref.", "Referência", "Valor Pago", "Data do Pagamento", "Forma de Pagamento", "Observações" };
            SetupSheetHeader(wsPagamentosMensais, headersPagMensais, headerBgColor);

            rowIdx = 2;
            foreach (var mp in monthlyPayments)
            {
                wsPagamentosMensais.Cell(rowIdx, 1).SetValue(mp.Id);
                wsPagamentosMensais.Cell(rowIdx, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsPagamentosMensais.Cell(rowIdx, 2).SetValue(mp.MonthlyCustomerId);
                wsPagamentosMensais.Cell(rowIdx, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsPagamentosMensais.Cell(rowIdx, 3).SetValue(mp.MonthlyCustomer?.Client?.Name ?? "Mensalista");

                wsPagamentosMensais.Cell(rowIdx, 4).SetValue(mp.ReferenceMonth);
                wsPagamentosMensais.Cell(rowIdx, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsPagamentosMensais.Cell(rowIdx, 5).SetValue(mp.ReferenceYear);
                wsPagamentosMensais.Cell(rowIdx, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsPagamentosMensais.Cell(rowIdx, 6).SetValue($"{mp.ReferenceMonth:D2}/{mp.ReferenceYear}");
                wsPagamentosMensais.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsPagamentosMensais.Cell(rowIdx, 7).SetValue(mp.Amount);
                wsPagamentosMensais.Cell(rowIdx, 7).Style.NumberFormat.Format = "\"R$\" #,##0.00";
                wsPagamentosMensais.Cell(rowIdx, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                wsPagamentosMensais.Cell(rowIdx, 8).SetValue(mp.PaymentDate);
                wsPagamentosMensais.Cell(rowIdx, 8).Style.NumberFormat.Format = "dd/MM/yyyy HH:mm";
                wsPagamentosMensais.Cell(rowIdx, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsPagamentosMensais.Cell(rowIdx, 9).SetValue(FormatPaymentMethod(mp.PaymentMethod));
                wsPagamentosMensais.Cell(rowIdx, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsPagamentosMensais.Cell(rowIdx, 10).SetValue(mp.Notes ?? string.Empty);

                if (rowIdx % 2 == 1)
                {
                    wsPagamentosMensais.Range(rowIdx, 1, rowIdx, headersPagMensais.Length).Style.Fill.BackgroundColor = alternateRowColor;
                }
                rowIdx++;
            }
            FinalizeSheet(wsPagamentosMensais, rowIdx - 1, headersPagMensais.Length);

            // ==========================================
            // Aba 7: Histórico Financeiro
            // ==========================================
            var wsHistorico = workbook.Worksheets.Add("Histórico Financeiro");
            string[] headersHistorico = { "ID Registro", "Tipo de Movimentação", "ID Cliente", "Nome do Cliente", "Telefone", "Valor Recebido", "Data do Pagamento", "Forma de Pagamento", "Descrição / Referência", "Observações" };
            SetupSheetHeader(wsHistorico, headersHistorico, headerBgColor);

            rowIdx = 2;
            foreach (var tx in financialTransactions)
            {
                wsHistorico.Cell(rowIdx, 1).SetValue(tx.Id);
                wsHistorico.Cell(rowIdx, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                string tipoStr = tx.TransactionType == FinancialTransactionType.Installment ? "Parcela" : "Mensalidade";
                wsHistorico.Cell(rowIdx, 2).SetValue(tipoStr);
                wsHistorico.Cell(rowIdx, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsHistorico.Cell(rowIdx, 3).SetValue(tx.ClientId);
                wsHistorico.Cell(rowIdx, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsHistorico.Cell(rowIdx, 4).SetValue(tx.ClientName);
                wsHistorico.Cell(rowIdx, 5).SetValue(tx.ClientPhone ?? string.Empty);

                wsHistorico.Cell(rowIdx, 6).SetValue(tx.Amount);
                wsHistorico.Cell(rowIdx, 6).Style.NumberFormat.Format = "\"R$\" #,##0.00";
                wsHistorico.Cell(rowIdx, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                wsHistorico.Cell(rowIdx, 7).SetValue(tx.PaymentDate);
                wsHistorico.Cell(rowIdx, 7).Style.NumberFormat.Format = "dd/MM/yyyy HH:mm";
                wsHistorico.Cell(rowIdx, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsHistorico.Cell(rowIdx, 8).SetValue(FormatPaymentMethod(tx.PaymentMethod));
                wsHistorico.Cell(rowIdx, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                wsHistorico.Cell(rowIdx, 9).SetValue(!string.IsNullOrEmpty(tx.Description) ? tx.Description : tx.Reference);
                wsHistorico.Cell(rowIdx, 10).SetValue(tx.Notes ?? string.Empty);

                if (rowIdx % 2 == 1)
                {
                    wsHistorico.Range(rowIdx, 1, rowIdx, headersHistorico.Length).Style.Fill.BackgroundColor = alternateRowColor;
                }
                rowIdx++;
            }
            FinalizeSheet(wsHistorico, rowIdx - 1, headersHistorico.Length);

            // 3. Salva a planilha gerada no arquivo local
            workbook.SaveAs(targetFilePath);

            var fileInfo = new FileInfo(targetFilePath);
            long fileSize = fileInfo.Exists ? fileInfo.Length : 0;

            return new ExcelExportResult
            {
                Success = true,
                FilePath = targetFilePath,
                FileSize = fileSize,
                ClientsCount = clients.Count,
                SalesCount = sales.Count,
                InstallmentsCount = installments.Count,
                PaymentsCount = payments.Count,
                MonthlyCustomersCount = monthlyCustomers.Count,
                MonthlyPaymentsCount = monthlyPayments.Count,
                FinancialHistoryCount = financialTransactions.Count,
                Message = $"Exportação para Excel concluída com sucesso! Arquivo gerado com 7 abas contendo {clients.Count} clientes, {sales.Count} vendas, {installments.Count} parcelas e {financialTransactions.Count} movimentações financeiras."
            };
        }

        private static void SetupSheetHeader(IXLWorksheet ws, string[] headers, XLColor bgColor)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.SetValue(headers[i]);
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 11;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = bgColor;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            ws.Row(1).Height = 26;
            ws.SheetView.FreezeRows(1);
        }

        private static void FinalizeSheet(IXLWorksheet ws, int lastRow, int colCount)
        {
            if (lastRow >= 1 && colCount >= 1)
            {
                ws.Range(1, 1, Math.Max(lastRow, 1), colCount).SetAutoFilter();
            }

            ws.Columns(1, colCount).AdjustToContents(10.0, 50.0);
        }

        private static string FormatPaymentMethod(PaymentMethod method) => method switch
        {
            PaymentMethod.Pix => "PIX",
            PaymentMethod.Cash => "Dinheiro",
            PaymentMethod.Card => "Cartão",
            PaymentMethod.Other => "Outro",
            _ => "Não Informado"
        };

        private static string FormatInstallmentStatus(InstallmentStatus status) => status switch
        {
            InstallmentStatus.Paid => "Paga",
            InstallmentStatus.Overdue => "Atrasada",
            InstallmentStatus.Pending => "Pendente",
            _ => status.ToString()
        };
    }
}
