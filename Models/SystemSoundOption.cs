namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa uma opção selecionável de som nativo do sistema Windows para alertas e notificações.
    /// </summary>
    public class SystemSoundOption
    {
        public string Type { get; }
        public string Name { get; }
        public string DisplayName => Name;
        public string Description { get; }

        public SystemSoundOption(string type, string name, string description)
        {
            Type = type;
            Name = name;
            Description = description;
        }

        public override string ToString() => Name;
    }
}
