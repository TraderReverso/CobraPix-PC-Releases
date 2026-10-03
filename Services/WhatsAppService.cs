using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Serviço de integração com WhatsApp.
    /// Valida números, normaliza para formato internacional brasileiro sem duplicar DDI,
    /// constrói link seguro para WhatsApp e dispara a abertura de conversa no Windows.
    /// </summary>
    public class WhatsAppService : IWhatsAppService
    {
        private static readonly CultureInfo PtBr = new("pt-BR");
        private readonly Func<string, bool>? _urlLauncher;

        public WhatsAppService(Func<string, bool>? urlLauncher = null)
        {
            _urlLauncher = urlLauncher;
        }

        public bool IsValidPhoneNumber(string? phone)
        {
            return NormalizePhoneNumber(phone) != null;
        }

        public string? NormalizePhoneNumber(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return null;

            // Remove todos os caracteres não numéricos
            string digits = Regex.Replace(phone, @"[^\d]", "");

            if (string.IsNullOrEmpty(digits))
                return null;

            // Remove zero à esquerda caso o usuário tenha digitado ex: 011988887777
            if (digits.StartsWith("0") && digits.Length > 10)
            {
                digits = digits.TrimStart('0');
            }

            // Se já começa com DDI 55 e tem comprimento de DDD + número (12 ou 13 dígitos)
            // Ex: 5511999991111 (13 dígitos) ou 551133334444 (12 dígitos)
            if (digits.StartsWith("55") && (digits.Length == 12 || digits.Length == 13))
            {
                // Verifica se não são todos dígitos repetidos inválidos (ex: 00000000000)
                if (IsAllSameDigits(digits.Substring(2))) return null;
                return digits;
            }

            // Se tem 10 dígitos (DDD + 8 dígitos) ou 11 dígitos (DDD + 9 dígitos)
            if (digits.Length == 10 || digits.Length == 11)
            {
                if (IsAllSameDigits(digits)) return null;
                return "55" + digits;
            }

            // Telefones com outros comprimentos são considerados inválidos
            return null;
        }

        private static bool IsAllSameDigits(string str)
        {
            if (string.IsNullOrEmpty(str)) return true;
            char first = str[0];
            for (int i = 1; i < str.Length; i++)
            {
                if (str[i] != first) return false;
            }
            return true;
        }

        public string? BuildWhatsAppUrl(string? phone, string message)
        {
            string? normalized = NormalizePhoneNumber(phone);
            if (string.IsNullOrEmpty(normalized))
                return null;

            string safeMessage = message ?? string.Empty;
            string encoded = Uri.EscapeDataString(safeMessage);

            return $"https://wa.me/{normalized}?text={encoded}";
        }

        public bool OpenWhatsApp(string? phone, string message)
        {
            string? url = BuildWhatsAppUrl(phone, message);
            if (string.IsNullOrEmpty(url))
                return false;

            if (_urlLauncher != null)
            {
                try
                {
                    return _urlLauncher(url);
                }
                catch
                {
                    return false;
                }
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = url,
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

        public string GenerateParcelaAtrasadaMessage(string clientName, string reference, DateTime dueDate, decimal remainingBalance)
        {
            string nome = FormatClientFirstNameOrFullName(clientName);
            string data = dueDate.ToString("dd/MM/yyyy");
            string saldo = remainingBalance.ToString("C2", PtBr);

            return $"Olá, {nome}! Tudo bem?\n\nPassando para lembrar sobre a parcela {reference}, com vencimento em {data}.\n\nO valor pendente é de {saldo}.\n\nSe precisar de alguma informação, estou à disposição.";
        }

        public string GenerateMensalidadeAtrasadaMessage(string clientName, string competence, DateTime dueDate, decimal amount)
        {
            string nome = FormatClientFirstNameOrFullName(clientName);
            string data = dueDate.ToString("dd/MM/yyyy");
            string valor = amount.ToString("C2", PtBr);

            return $"Olá, {nome}! Tudo bem?\n\nPassando para lembrar sobre a mensalidade referente a {competence}, com vencimento em {data}.\n\nO valor pendente é de {valor}.\n\nSe precisar de alguma informação, estou à disposição.";
        }

        public string GenerateVencimentoProximoMessage(string clientName, string reference, DateTime dueDate, decimal amount)
        {
            string nome = FormatClientFirstNameOrFullName(clientName);
            string data = dueDate.ToString("dd/MM/yyyy");
            string valor = amount.ToString("C2", PtBr);

            return $"Olá, {nome}! Tudo bem?\n\nPassando para lembrar que {reference} vence em {data}.\n\nO valor é de {valor}.\n\nSe precisar de alguma informação, estou à disposição.";
        }

        public string GenerateSimpleContactMessage(string clientName)
        {
            string nome = FormatClientFirstNameOrFullName(clientName);
            return $"Olá, {nome}! Tudo bem?";
        }

        private static string FormatClientFirstNameOrFullName(string clientName)
        {
            if (string.IsNullOrWhiteSpace(clientName))
                return "Cliente";
            return clientName.Trim();
        }
    }
}
