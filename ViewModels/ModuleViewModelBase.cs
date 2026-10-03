namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// Classe base para ViewModels dos módulos de conteúdo do CobraPix PC.
    /// </summary>
    public abstract class ModuleViewModelBase : ViewModelBase
    {
        public abstract string Title { get; }
        public abstract string Description { get; }
        public abstract string Icon { get; }
        public abstract string Category { get; }
    }
}
