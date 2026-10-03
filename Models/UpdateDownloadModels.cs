using System;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Progresso detalhado do download do pacote de atualização.
    /// </summary>
    public class UpdateDownloadProgress
    {
        public long BytesReceived { get; set; }
        public long? TotalBytes { get; set; }
        public double Percentage { get; set; }
        public string ProgressText { get; set; } = string.Empty;
        public string PercentageText { get; set; } = string.Empty;

        public UpdateDownloadProgress(long bytesReceived, long? totalBytes, double percentage)
        {
            BytesReceived = bytesReceived;
            TotalBytes = totalBytes;
            Percentage = Math.Clamp(percentage, 0.0, 100.0);
            PercentageText = $"{Percentage:F0}%";

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                ProgressText = $"{FormatBytes(bytesReceived)} / {FormatBytes(totalBytes.Value)}";
            }
            else
            {
                ProgressText = FormatBytes(bytesReceived);
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB".Replace('.', ',');
            if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1} MB".Replace('.', ',');
            return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB".Replace('.', ',');
        }
    }

    /// <summary>
    /// Resultado consolidado da operação de download e validação criptográfica da atualização.
    /// </summary>
    public class UpdateDownloadResult
    {
        public bool Success { get; set; }
        public string? LocalFilePath { get; set; }
        public string? ComputedSha256 { get; set; }
        public string? ExpectedSha256 { get; set; }
        public bool IntegrityVerified { get; set; }
        public bool IsCancelled { get; set; }
        public string? ErrorMessage { get; set; }
        public string? FriendlyMessage { get; set; }

        public static UpdateDownloadResult CreateSuccess(string localFilePath, string computedSha256, string expectedSha256)
        {
            return new UpdateDownloadResult
            {
                Success = true,
                IntegrityVerified = true,
                LocalFilePath = localFilePath,
                ComputedSha256 = computedSha256,
                ExpectedSha256 = expectedSha256,
                FriendlyMessage = "Pacote de atualização baixado e validado. A instalação automática será implementada na próxima etapa."
            };
        }

        public static UpdateDownloadResult CreateCancelled()
        {
            return new UpdateDownloadResult
            {
                Success = false,
                IsCancelled = true,
                FriendlyMessage = "Download cancelado pelo usuário."
            };
        }

        public static UpdateDownloadResult CreateFailure(string errorMessage, string friendlyMessage, string? computedSha256 = null, string? expectedSha256 = null)
        {
            return new UpdateDownloadResult
            {
                Success = false,
                IntegrityVerified = false,
                ErrorMessage = errorMessage,
                FriendlyMessage = friendlyMessage,
                ComputedSha256 = computedSha256,
                ExpectedSha256 = expectedSha256
            };
        }
    }
}
