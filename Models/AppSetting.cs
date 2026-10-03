using System;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa configurações gerais do sistema armazenadas em chave/valor.
    /// </summary>
    public class AppSetting
    {
        public int Id { get; set; }

        public string Key { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
