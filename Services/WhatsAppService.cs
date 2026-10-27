using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CobraPix_PC.Data;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Serviço de integração com WhatsApp.
    /// Valida números, normaliza para formato internacional brasileiro sem duplicar DDI,
    /// constrói link seguro para WhatsApp, dispara a abertura de conversa no Windows,
    /// gera saudações dinâmicas pelo horário local, insere chave Pix configurável automaticamente
    /// e suporta pacote de duas mensagens (mensagem principal + código Pix Copia e Cola isolado).
    /// </summary>
    public class WhatsAppService : IWhatsAppService
    {
        private static readonly CultureInfo PtBr = new("pt-BR");
        private readonly CobraPixDbContext? _dbContext;
        private readonly Func<string, bool>? _urlLauncher;
        private string? _pixKey;
        private bool _pixKeyLoaded;

        public const string SettingKeyPixKey = "App.PixKey";
        public const string SettingKeyReceiverName = "App.ReceiverName";
        public const string SettingKeyCity = "App.City";

        public string? PixKey
        {
            get
            {
                if (!_pixKeyLoaded)
                {
                    _pixKey = GetConfiguredPixKey();
                    _pixKeyLoaded = true;
                }
                return _pixKey;
            }
            set
            {
                _pixKey = value;
                _pixKeyLoaded = true;
            }
        }

        public WhatsAppService(Func<string, bool>? urlLauncher = null)
        {
            _urlLauncher = urlLauncher;
        }

        public WhatsAppService(CobraPixDbContext dbContext, Func<string, bool>? urlLauncher = null)
        {
            _dbContext = dbContext;
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

            return $"https://api.whatsapp.com/send?phone={normalized}&text={encoded}";
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

        public bool OpenWhatsAppWithPixCopy(string? phone, string message, string? isolatedPixToCopy = null)
        {
            if (!string.IsNullOrWhiteSpace(isolatedPixToCopy))
            {
                CopyToClipboardSafe(isolatedPixToCopy.Trim());
            }

            return OpenWhatsApp(phone, message);
        }

        public static void CopyToClipboardSafe(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                if (System.Threading.Thread.CurrentThread.GetApartmentState() == System.Threading.ApartmentState.STA)
                {
                    System.Windows.Clipboard.SetText(text);
                }
                else
                {
                    var thread = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            System.Windows.Clipboard.SetText(text);
                        }
                        catch { }
                    });
                    thread.SetApartmentState(System.Threading.ApartmentState.STA);
                    thread.Start();
                    thread.Join(1000);
                }
            }
            catch
            {
                // Tratamento seguro em caso de ambiente sem área de transferência (testes/servidor)
            }
        }

        public OverdueMessageBundle BuildOverdueMessageBundle(
            string clientName,
            string reference,
            DateTime dueDate,
            decimal amount,
            decimal? totalPaid = null,
            decimal? remainingBalance = null,
            decimal? globalRemainingBalance = null,
            string? pixKey = null,
            string? receiverName = null,
            string? city = null,
            decimal? overdueInterestAmount = null,
            DateTime? referenceTime = null)
        {
            string effectivePixKey = (!string.IsNullOrWhiteSpace(pixKey) ? pixKey : PixKey)?.Trim() ?? string.Empty;
            string primaryMessage = GenerateParcelaAtrasadaMessage(
                clientName,
                reference,
                dueDate,
                amount,
                totalPaid,
                remainingBalance,
                effectivePixKey,
                referenceTime,
                globalRemainingBalance,
                overdueInterestAmount);

            decimal payableAmount = (remainingBalance ?? amount) + (overdueInterestAmount ?? 0m);

            string? pixPayload = null;
            if (!string.IsNullOrWhiteSpace(effectivePixKey) &&
                !string.IsNullOrWhiteSpace(receiverName) &&
                !string.IsNullOrWhiteSpace(city) &&
                payableAmount > 0m)
            {
                try
                {
                    pixPayload = PixGenerator.GeneratePayload(effectivePixKey, payableAmount, receiverName, city);
                }
                catch
                {
                    pixPayload = null;
                }
            }

            string isolatedPixKey = !string.IsNullOrWhiteSpace(pixPayload) ? pixPayload : effectivePixKey;

            return new OverdueMessageBundle(
                primaryMessage,
                isolatedPixKey,
                pixPayload,
                !string.IsNullOrWhiteSpace(isolatedPixKey),
                payableAmount);
        }

        /// <summary>
        /// Gera a saudação dinâmica baseada no horário local do computador:
        /// 05:00 até 11:59: "Bom dia"
        /// 12:00 até 17:59: "Boa tarde"
        /// 18:00 até 04:59: "Boa noite"
        /// </summary>
        public string GetGreeting(DateTime? dateTime = null)
        {
            var dt = dateTime ?? DateTime.Now;
            int hour = dt.Hour;

            if (hour >= 5 && hour < 12)
                return "Bom dia";
            if (hour >= 12 && hour < 18)
                return "Boa tarde";
            return "Boa noite";
        }

        public const string EmojiSorriso = "\uD83D\uDE0A";     // 😊
        public const string EmojiDinheiro = "\uD83D\uDCB0";    // 💰
        public const string EmojiAmpulheta = "\u23F3";          // ⏳
        public const string EmojiCheck = "\u2705";              // ✅

        private static string FormatParcelaReference(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return "em aberto";
            string trimmed = reference.Trim();
            var match = Regex.Match(trimmed, @"\b(\d+/\d+)\b");
            if (match.Success) return match.Groups[1].Value;

            if (trimmed.StartsWith("Parc. ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(6).Trim();
            else if (trimmed.StartsWith("Parcela ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(8).Trim();

            return trimmed;
        }

        private static string FormatDueCondition(DateTime dueDate, DateTime referenceDate)
        {
            string dataStr = dueDate.ToString("dd/MM/yyyy");
            if (dueDate.Date == referenceDate.Date)
            {
                return $"com vencimento para hoje ({dataStr})";
            }
            if (dueDate.Date < referenceDate.Date)
            {
                return $"que venceu em {dataStr} e encontra-se em atraso";
            }
            return $"com vencimento para {dataStr}";
        }

        private static string FormatFinancialBlock(
            decimal totalPaid,
            decimal remainingBalance,
            decimal? globalRemainingBalance = null,
            decimal? overdueInterest = null)
        {
            var sb = new StringBuilder();
            sb.Append($"{EmojiDinheiro} Total já pago: {totalPaid.ToString("C2", PtBr)}\n{EmojiAmpulheta} Falta pagar: {remainingBalance.ToString("C2", PtBr)}");

            if (overdueInterest.HasValue && overdueInterest.Value > 0m)
            {
                sb.Append($"\n{EmojiAmpulheta} Encargos de mora (atraso): {overdueInterest.Value.ToString("C2", PtBr)}");
            }

            if (globalRemainingBalance.HasValue && globalRemainingBalance.Value > 0m && globalRemainingBalance.Value != remainingBalance)
            {
                sb.Append($"\n{EmojiDinheiro} Saldo devedor global restante: {globalRemainingBalance.Value.ToString("C2", PtBr)}");
            }

            return sb.ToString();
        }

        private string FormatPixBlock(string? pixKey)
        {
            string effectivePixKey = (!string.IsNullOrWhiteSpace(pixKey) ? pixKey : PixKey)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(effectivePixKey))
                return string.Empty;

            return $"\n\nPara sua comodidade, você pode pagar via Pix ou QR Code.\nChave Pix para pagamento:\n{effectivePixKey}";
        }

        public string GenerateParcelaAtrasadaMessage(
            string clientName,
            string reference,
            DateTime dueDate,
            decimal amount,
            decimal? totalPaid = null,
            decimal? remainingBalance = null,
            string? pixKey = null,
            DateTime? referenceTime = null,
            decimal? globalRemainingBalance = null,
            decimal? overdueInterest = null)
        {
            DateTime now = referenceTime ?? DateTime.Now;
            string saudacao = GetGreeting(now);
            string nome = FormatClientFirstNameOrFullName(clientName);
            string refParcela = FormatParcelaReference(reference);
            string valor = amount.ToString("C2", PtBr);
            string dataStr = dueDate.ToString("dd/MM/yyyy");

            decimal pago = totalPaid ?? 0.00m;
            decimal saldo = remainingBalance ?? Math.Max(0m, amount - pago);

            string financialBlock = FormatFinancialBlock(pago, saldo, globalRemainingBalance, overdueInterest);
            string pixBlock = FormatPixBlock(pixKey);

            return $"{saudacao}, {nome}! Tudo bem? {EmojiSorriso}\n\nPassando para lembrar da sua parcela {refParcela}, no valor de {valor}, que venceu em {dataStr} e encontra-se em atraso.\n\n{financialBlock}{pixBlock}\n\nAssim que realizar o pagamento, por favor, envie o comprovante. Obrigado!";
        }

        public string GenerateMensalidadeAtrasadaMessage(
            string clientName,
            string competence,
            DateTime dueDate,
            decimal amount,
            decimal? totalPaid = null,
            decimal? remainingBalance = null,
            string? pixKey = null,
            DateTime? referenceTime = null,
            decimal? globalRemainingBalance = null,
            decimal? overdueInterest = null)
        {
            DateTime now = referenceTime ?? DateTime.Now;
            string saudacao = GetGreeting(now);
            string nome = FormatClientFirstNameOrFullName(clientName);
            string valor = amount.ToString("C2", PtBr);
            string dataStr = dueDate.ToString("dd/MM/yyyy");

            decimal pago = totalPaid ?? 0.00m;
            decimal saldo = remainingBalance ?? Math.Max(0m, amount - pago);

            string financialBlock = FormatFinancialBlock(pago, saldo, globalRemainingBalance, overdueInterest);
            string pixBlock = FormatPixBlock(pixKey);

            return $"{saudacao}, {nome}! Tudo bem? {EmojiSorriso}\n\nPassando para lembrar da sua mensalidade referente a {competence}, no valor de {valor}, que venceu em {dataStr} e encontra-se em atraso.\n\n{financialBlock}{pixBlock}\n\nAssim que realizar o pagamento, por favor, envie o comprovante. Obrigado!";
        }

        public string GenerateVencimentoProximoMessage(
            string clientName,
            string reference,
            DateTime dueDate,
            decimal amount,
            decimal? totalPaid = null,
            decimal? remainingBalance = null,
            string? pixKey = null,
            DateTime? referenceTime = null,
            decimal? globalRemainingBalance = null)
        {
            DateTime now = referenceTime ?? DateTime.Now;
            string saudacao = GetGreeting(now);
            string nome = FormatClientFirstNameOrFullName(clientName);
            string refParcela = FormatParcelaReference(reference);
            string valor = amount.ToString("C2", PtBr);
            string condicaoVencimento = FormatDueCondition(dueDate, now);

            decimal pago = totalPaid ?? 0.00m;
            decimal saldo = remainingBalance ?? Math.Max(0m, amount - pago);

            string financialBlock = FormatFinancialBlock(pago, saldo, globalRemainingBalance, null);
            string pixBlock = FormatPixBlock(pixKey);

            string aberturaCorpo = dueDate.Date < now.Date
                ? $"Passando para lembrar da sua parcela {refParcela}, no valor de {valor}, {condicaoVencimento}."
                : $"Passando para lembrar do vencimento da sua parcela {refParcela}, no valor de {valor}, {condicaoVencimento}.";

            return $"{saudacao}, {nome}! Tudo bem? {EmojiSorriso}\n\n{aberturaCorpo}\n\n{financialBlock}{pixBlock}\n\nAssim que realizar o pagamento, por favor, envie o comprovante. Obrigado!";
        }

        public string GeneratePagamentoConfirmadoMessage(
            string clientName,
            string reference,
            decimal amountPaid,
            DateTime paymentDate,
            decimal? totalPaid = null,
            decimal? remainingBalance = null,
            string? paymentMethod = null,
            DateTime? referenceTime = null,
            decimal? globalRemainingBalance = null)
        {
            DateTime now = referenceTime ?? DateTime.Now;
            string saudacao = GetGreeting(now);
            string nome = FormatClientFirstNameOrFullName(clientName);
            string refParcela = FormatParcelaReference(reference);
            string valor = amountPaid.ToString("C2", PtBr);
            string dataHora = paymentDate.ToString("dd/MM/yyyy HH:mm");

            var sb = new StringBuilder();
            sb.Append($"{saudacao}, {nome}! {EmojiSorriso}\n\n");
            sb.Append($"Seu pagamento da parcela {refParcela}, no valor de {valor}, foi confirmado com sucesso em {dataHora}.\n");

            if (!string.IsNullOrWhiteSpace(paymentMethod))
            {
                sb.Append($"Forma de pagamento: {paymentMethod}\n");
            }

            if (totalPaid.HasValue || remainingBalance.HasValue || globalRemainingBalance.HasValue)
            {
                decimal pago = totalPaid ?? amountPaid;
                decimal saldo = remainingBalance ?? 0m;
                sb.Append($"\n{FormatFinancialBlock(pago, saldo, globalRemainingBalance)}\n");
            }
            else
            {
                sb.Append("\n");
            }

            sb.Append($"{EmojiCheck} Pagamento realizado com sucesso!\n\nObrigado pelo pagamento!");
            return sb.ToString();
        }

        public string GeneratePagamentoMensalidadeConfirmadoMessage(
            string clientName,
            string competence,
            decimal amountPaid,
            DateTime paymentDate,
            decimal? totalPaid = null,
            decimal? remainingBalance = null,
            string? paymentMethod = null,
            DateTime? referenceTime = null,
            decimal? globalRemainingBalance = null)
        {
            DateTime now = referenceTime ?? DateTime.Now;
            string saudacao = GetGreeting(now);
            string nome = FormatClientFirstNameOrFullName(clientName);
            string valor = amountPaid.ToString("C2", PtBr);
            string dataHora = paymentDate.ToString("dd/MM/yyyy HH:mm");

            var sb = new StringBuilder();
            sb.Append($"{saudacao}, {nome}! {EmojiSorriso}\n\n");
            sb.Append($"Seu pagamento da mensalidade referente a {competence}, no valor de {valor}, foi confirmado com sucesso em {dataHora}.\n");

            if (!string.IsNullOrWhiteSpace(paymentMethod))
            {
                sb.Append($"Forma de pagamento: {paymentMethod}\n");
            }

            if (totalPaid.HasValue || remainingBalance.HasValue || globalRemainingBalance.HasValue)
            {
                decimal pago = totalPaid ?? amountPaid;
                decimal saldo = remainingBalance ?? 0m;
                sb.Append($"\n{FormatFinancialBlock(pago, saldo, globalRemainingBalance)}\n");
            }
            else
            {
                sb.Append("\n");
            }

            sb.Append($"{EmojiCheck} Pagamento realizado com sucesso!\n\nObrigado pelo pagamento!");
            return sb.ToString();
        }

        public string GenerateSimpleContactMessage(string clientName, DateTime? referenceTime = null)
        {
            string nome = FormatClientFirstNameOrFullName(clientName);
            string saudacao = GetGreeting(referenceTime);
            return $"Olá, {nome}! {saudacao}! Tudo bem? {EmojiSorriso}";
        }

        private static string FormatClientFirstNameOrFullName(string clientName)
        {
            if (string.IsNullOrWhiteSpace(clientName))
                return "Cliente";
            return clientName.Trim();
        }

        private string? GetConfiguredPixKey()
        {
            if (_dbContext != null)
            {
                try
                {
                    var setting = _dbContext.AppSettings.AsNoTracking().FirstOrDefault(s => s.Key == SettingKeyPixKey);
                    return setting?.Value;
                }
                catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
                {
                    throw;
                }
                catch
                {
                }
                return null;
            }

            try
            {
                using var scope = App.Services?.CreateScope();
                var ctx = scope?.ServiceProvider.GetService<CobraPixDbContext>();
                if (ctx != null)
                {
                    var setting = ctx.AppSettings.AsNoTracking().FirstOrDefault(s => s.Key == SettingKeyPixKey);
                    return setting?.Value;
                }
            }
            catch (InvalidOperationException secEx) when (secEx.Message.Contains("[SEGURANÇA DE EXECUÇÃO]", StringComparison.Ordinal))
            {
                throw;
            }
            catch
            {
            }
            return null;
        }
    }
}
