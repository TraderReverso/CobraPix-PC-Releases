namespace CobraPix_PC.Models
{
    public enum LicenseStatus
    {
        Valid = 0,
        NotConfigured = 1,
        InvalidFormat = 2,
        InvalidSignature = 3,
        InvalidProduct = 4,
        InvalidPlan = 5,
        NotYetValid = 6,
        Expired = 7
    }

    /// <summary>
    /// Resultado da validação de uma chave de licença do CobraPix PRO.
    /// </summary>
    public class LicenseValidationResult
    {
        public bool IsValid { get; set; }
        public LicenseStatus Status { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public LicenseInfo? License { get; set; }

        public static LicenseValidationResult Success(LicenseInfo license)
        {
            return new LicenseValidationResult
            {
                IsValid = true,
                Status = LicenseStatus.Valid,
                License = license,
                ErrorMessage = string.Empty
            };
        }

        public static LicenseValidationResult Failure(LicenseStatus status, string message, LicenseInfo? license = null)
        {
            return new LicenseValidationResult
            {
                IsValid = false,
                Status = status,
                License = license,
                ErrorMessage = message
            };
        }
    }
}
