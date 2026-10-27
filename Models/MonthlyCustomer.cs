using System;
using System.Collections.Generic;
using System.Globalization;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa a configuração de mensalidade de um cliente mensalista.
    /// Inclui campos de paridade funcional com a versão Android (juros contratuais de mensalistas),
    /// com suporte a persistência não invasiva e retrocompatibilidade total com bancos legados.
    /// </summary>
    public class MonthlyCustomer
    {
        public int Id { get; set; }

        public int ClientId { get; set; }

        public Client Client { get; set; } = null!;

        public decimal MonthlyAmount { get; set; }

        public int DueDay { get; set; } = 5;

        public bool IsActive { get; set; } = true;

        public DateTime StartDate { get; set; } = DateTime.Today;

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Campos de Paridade Funcional Android (Juros Contratuais para Mensalistas)
        private decimal? _originalMonthlyAmount;
        public decimal OriginalMonthlyAmount
        {
            get => _originalMonthlyAmount ?? ParseMetadataDecimal("MP") ?? MonthlyAmount;
            set => _originalMonthlyAmount = value;
        }

        private double? _contractInterestRate;
        public double ContractInterestRate
        {
            get => _contractInterestRate ?? ParseMetadataDouble("MR") ?? 0.0;
            set => _contractInterestRate = value;
        }

        private decimal? _contractInterestAmount;
        public decimal ContractInterestAmount
        {
            get => _contractInterestAmount ?? ParseMetadataDecimal("MI") ?? 0m;
            set => _contractInterestAmount = value;
        }

        // Relacionamentos de navegação
        public ICollection<MonthlyPayment> Payments { get; set; } = new List<MonthlyPayment>();

        public void EmbedParityMetadata(decimal principal, double rate, decimal interest)
        {
            _originalMonthlyAmount = principal;
            _contractInterestRate = rate;
            _contractInterestAmount = interest;

            if (rate <= 0.0 && interest <= 0m)
            {
                return;
            }

            string metaTag = $"[PARITY:MP={principal.ToString(CultureInfo.InvariantCulture)};MR={rate.ToString(CultureInfo.InvariantCulture)};MI={interest.ToString(CultureInfo.InvariantCulture)}]";
            if (string.IsNullOrWhiteSpace(Notes))
            {
                Notes = metaTag;
            }
            else if (!Notes.Contains("[PARITY:"))
            {
                Notes = Notes + " " + metaTag;
            }
        }

        private decimal? ParseMetadataDecimal(string key)
        {
            var val = ExtractTagValue(key);
            if (val != null && decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d;
            return null;
        }

        private double? ParseMetadataDouble(string key)
        {
            var val = ExtractTagValue(key);
            if (val != null && double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d;
            return null;
        }

        private string? ExtractTagValue(string key)
        {
            if (string.IsNullOrWhiteSpace(Notes)) return null;
            int start = Notes.IndexOf("[PARITY:", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return null;
            int end = Notes.IndexOf(']', start);
            if (end < 0) return null;
            string content = Notes.Substring(start + 8, end - (start + 8));
            var parts = content.Split(';');
            foreach (var p in parts)
            {
                var kv = p.Split('=');
                if (kv.Length == 2 && string.Equals(kv[0].Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return kv[1].Trim();
                }
            }
            return null;
        }
    }
}
