using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    public interface ISaleService
    {
        Task<Sale> CreateSaleAsync(
            int clientId,
            decimal totalAmount,
            int installmentCount,
            DateTime firstDueDate,
            string? description = null,
            string? notes = null);

        Task<Sale?> GetSaleByIdAsync(int saleId);
        Task<List<Sale>> GetAllSalesAsync();
        Task<List<Sale>> GetSalesByClientIdAsync(int clientId);
        Task<List<Installment>> GetInstallmentsBySaleIdAsync(int saleId);
        List<Installment> PreviewInstallments(decimal totalAmount, int installmentCount, DateTime firstDueDate);
    }
}
