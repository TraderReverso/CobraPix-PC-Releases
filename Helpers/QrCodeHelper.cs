using System;
using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace CobraPix_PC.Helpers
{
    /// <summary>
    /// Gerador local e offline de imagens QR Code em BitmapImage para o WPF utilizando QRCoder.
    /// Opera de forma 100% autônoma, sem chamadas externas nem bibliotecas legadas de desenho.
    /// </summary>
    public static class QrCodeHelper
    {
        public static BitmapImage GenerateQrCodeBitmap(string payload, int pixelsPerModule = 15)
        {
            if (string.IsNullOrWhiteSpace(payload))
                throw new ArgumentException("Payload não pode ser nulo ou vazio.", nameof(payload));

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeBytes = qrCode.GetGraphic(pixelsPerModule);

            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(qrCodeBytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();
            return bitmap;
        }
    }
}
