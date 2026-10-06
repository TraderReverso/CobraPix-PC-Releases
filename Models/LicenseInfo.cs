using System;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa os dados decodificados de uma licença assinada do CobraPix PRO.
    /// </summary>
    public class LicenseInfo
    {
        public string LicenseId { get; set; } = string.Empty;
        public string Product { get; set; } = "CobraPix PC";
        public PlanType PlanType { get; set; } = PlanType.Pro;
        public LicenseType LicenseType { get; set; } = LicenseType.Monthly;
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string? CustomerNotes { get; set; }
        public string RawKey { get; set; } = string.Empty;

        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
        public TimeSpan RemainingTime => ExpiresAt > DateTime.UtcNow ? ExpiresAt - DateTime.UtcNow : TimeSpan.Zero;
        public int RemainingDays => (int)Math.Ceiling(RemainingTime.TotalDays);

        public string FormattedExpirationDate => ExpiresAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        public string FormattedIssuedDate => IssuedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

        public string PlanDisplayName => LicenseType switch
        {
            LicenseType.Annual => "CobraPix PRO Anual",
            LicenseType.Monthly => "CobraPix PRO Mensal",
            _ => "CobraPix PRO"
        };
    }
}
