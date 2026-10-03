using System;
using System.Globalization;
using CobraPix_PC.Models;

namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// Item de apresentação para cada movimentação financeira de recebimento no histórico/livro caixa.
    /// </summary>
    public class FinancialTransactionItemViewModel : ViewModelBase
    {
        private static readonly CultureInfo PtBr = new("pt-BR");

        public int Id { get; }
        public FinancialTransactionType TransactionType { get; }
        public bool IsInstallment => TransactionType == FinancialTransactionType.Installment;
        public bool IsMonthly => TransactionType == FinancialTransactionType.Monthly;

        public string TypeBadge => IsMonthly ? "MENSALIDADE" : "PARCELA";

        public int ClientId { get; }
        public string ClientName { get; }
        public string ClientNameUpper => ClientName.ToUpperInvariant();
        public string? ClientPhone { get; }
        public string FormattedPhone => string.IsNullOrWhiteSpace(ClientPhone) ? "Sem telefone" : ClientPhone;

        public decimal Amount { get; }
        public string FormattedAmount => $"+ {Amount.ToString("C2", PtBr)}";

        public DateTime PaymentDate { get; }
        public string FormattedDate => PaymentDate.ToString("dd/MM/yyyy HH:mm");
        public string FormattedShortDate => PaymentDate.ToString("dd/MM/yyyy");

        public PaymentMethod PaymentMethod { get; }
        public string PaymentMethodBadge => PaymentMethod switch
        {
            PaymentMethod.Pix => "PIX",
            PaymentMethod.Cash => "DINHEIRO",
            PaymentMethod.Card => "CARTÃO",
            PaymentMethod.Other => "OUTRO",
            _ => "NENHUM"
        };

        public string Description { get; }
        public string? Notes { get; }
        public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

        public int? SaleId { get; }
        public int? InstallmentNumber { get; }
        public int? TotalInstallments { get; }
        public int? ReferenceMonth { get; }
        public int? ReferenceYear { get; }
        public string Reference { get; }

        public string StatusText => "RECEBIDO";

        public FinancialTransaction Transaction { get; }

        public FinancialTransactionItemViewModel(FinancialTransaction tx)
        {
            Transaction = tx ?? throw new ArgumentNullException(nameof(tx));

            Id = tx.Id;
            TransactionType = tx.TransactionType;
            ClientId = tx.ClientId;
            ClientName = tx.ClientName;
            ClientPhone = tx.ClientPhone;
            Amount = tx.Amount;
            PaymentDate = tx.PaymentDate;
            PaymentMethod = tx.PaymentMethod;
            Description = tx.Description;
            Notes = tx.Notes;
            SaleId = tx.SaleId;
            InstallmentNumber = tx.InstallmentNumber;
            TotalInstallments = tx.TotalInstallments;
            ReferenceMonth = tx.ReferenceMonth;
            ReferenceYear = tx.ReferenceYear;
            Reference = tx.Reference;
        }
    }
}
