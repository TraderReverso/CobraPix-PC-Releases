namespace CobraPix_PC.Models
{
    /// <summary>
    /// Modalidades de licença do CobraPix PRO.
    /// </summary>
    public enum LicenseType : byte
    {
        /// <summary>
        /// Licença mensal com validade de 30 dias.
        /// </summary>
        Monthly = 1,

        /// <summary>
        /// Licença anual com validade de 365 dias.
        /// </summary>
        Annual = 2
    }
}
