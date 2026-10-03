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
        /// Gera os bytes binários do PDF formatado para a transação financeira informada.
        /// </summary>
        byte[] GenerateReceiptBytes(FinancialTransaction transaction);

        /// <summary>
        /// Gera e grava o comprovante em PDF no caminho de destino especificado.
        /// Retorna o caminho absoluto do arquivo salvo.
        /// </summary>
        string GenerateAndSaveReceipt(FinancialTransaction transaction, string destinationPath);

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
