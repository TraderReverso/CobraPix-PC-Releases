using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato de serviço para geração e exportação de comprovantes de pagamento em formato PDF.
    /// Utiliza exclusivamente dados reais de recebimentos do CobraPix (InstallmentPayment e MonthlyPayment).
    /// </summary>
    public interface IReceiptPdfService
    {
        /// <summary>
        /// Nome da empresa ou emissor configurado para inclusão nos recibos.
        /// </summary>
        string? CompanyName { get; set; }

        /// <summary>
        /// Gera os bytes binários do PDF formatado para a transação financeira informada.
        /// </summary>
        byte[] GenerateReceiptBytes(FinancialTransaction transaction, string? companyName = null);

        /// <summary>
        /// Gera e grava o comprovante em PDF no caminho de destino especificado.
        /// Retorna o caminho absoluto do arquivo salvo.
        /// </summary>
        string GenerateAndSaveReceipt(FinancialTransaction transaction, string destinationPath, string? companyName = null);

        /// <summary>
        /// Retorna o caminho padrão absoluto do recibo na pasta de recibos do CobraPix (%LOCALAPPDATA%\CobraPix\receipts).
        /// </summary>
        string GetDefaultReceiptPath(FinancialTransaction transaction);

        /// <summary>
        /// Visualiza o recibo PDF. Se o arquivo já existir no caminho padrão, abre no visualizador do Windows.
        /// Caso não exista ainda, gera o documento e depois abre. Retorna o caminho absoluto do PDF.
        /// </summary>
        string ViewOrGenerateReceipt(FinancialTransaction transaction, string? companyName = null);

        /// <summary>
        /// Retorna o nome de arquivo sugerido para o comprovante, sanitizado e padronizado:
        /// Comprovante_{Cliente}_{Data}.pdf
        /// </summary>
        string GetSuggestedFileName(FinancialTransaction transaction);

        /// <summary>
        /// Abre o arquivo PDF no visualizador padrão do Windows (ShellExecute).
        /// </summary>
        bool OpenPdf(string filePath);
    }
}
