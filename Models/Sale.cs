using System;
using System.Collections.Generic;
using System.Globalization;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa uma venda associada a um cliente.
    /// Inclui campos de paridade funcional com a versão Android (juros contratuais e periodicidade),
    /// com suporte a persistência não invasiva e retrocompatibilidade total com bancos legados.
    /// </summary>
    public class Sale
    {
        public int Id { get; set; }

        public int ClientId { get; set; }

        public Client Client { get; set; } = null!;

        public decimal TotalAmount { get; set; }

        public int InstallmentCount { get; set; }

        public DateTime SaleDate { get; set; } = DateTime.Today;

        public string? Description { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Campos de Paridade Funcional Android (Juros e Regras de Negócio)
        private decimal? _originalPrincipal;
        public decimal OriginalPrincipal
        {
            get => _originalPrincipal ?? ParseMetadataDecimal("P") ?? TotalAmount;
            set => _originalPrincipal = value;
        }

        private double? _contractInterestRate;
        public double ContractInterestRate
        {
            get => _contractInterestRate ?? ParseMetadataDouble("R") ?? 0.0;
            set => _contractInterestRate = value;
        }

        private decimal? _contractInterestAmount;
        public decimal ContractInterestAmount
        {
            get => _contractInterestAmount ?? ParseMetadataDecimal("I") ?? 0m;
            set => _contractInterestAmount = value;
        }

        private bool? _useFifthBusinessDay;
        public bool UseFifthBusinessDay
        {
            get => _useFifthBusinessDay ?? ParseMetadataBool("F") ?? false;
            set => _useFifthBusinessDay = value;
        }

        private int? _periodicity;
        public int Periodicity
        {
            get => _periodicity ?? ParseMetadataInt("Per") ?? 0;
            set => _periodicity = value;
        }

        // Relacionamentos de navegação
        public ICollection<Installment> Installments { get; set; } = new List<Installment>();

        public void EmbedParityMetadata(decimal principal, double rate, decimal interest, bool fifthDay, int periodicity)
        {
            _originalPrincipal = principal;
            _contractInterestRate = rate;
            _contractInterestAmount = interest;
            _useFifthBusinessDay = fifthDay;
            _periodicity = periodicity;

            if (rate <= 0.0 && interest <= 0m && !fifthDay && periodicity == 0)
            {
                return;
            }

            string metaTag = $"[PARITY:P={principal.ToString(CultureInfo.InvariantCulture)};R={rate.ToString(CultureInfo.InvariantCulture)};I={interest.ToString(CultureInfo.InvariantCulture)};F={(fifthDay ? 1 : 0)};Per={periodicity}]";
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

        private int? ParseMetadataInt(string key)
        {
            var val = ExtractTagValue(key);
            if (val != null && int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
                return i;
            return null;
        }

        private bool? ParseMetadataBool(string key)
        {
            var val = ExtractTagValue(key);
            if (val == "1" || val?.Equals("true", StringComparison.OrdinalIgnoreCase) == true) return true;
            if (val == "0" || val?.Equals("false", StringComparison.OrdinalIgnoreCase) == true) return false;
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
