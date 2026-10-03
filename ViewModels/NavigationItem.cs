using System;

namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// Representa um item clicável no menu lateral de navegação.
    /// </summary>
    public class NavigationItem : ViewModelBase
    {
        private bool _isSelected;
        private string? _badge;

        public string Title { get; }
        public string Icon { get; }
        public ModuleViewModelBase ViewModel { get; }
        public bool IsPro { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string? Badge
        {
            get => _badge;
            set
            {
                if (SetProperty(ref _badge, value))
                {
                    OnPropertyChanged(nameof(HasBadge));
                }
            }
        }

        public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

        public NavigationItem(string title, string icon, ModuleViewModelBase viewModel, string? badge = null, bool isPro = false)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Icon = icon ?? throw new ArgumentNullException(nameof(icon));
            ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Badge = badge;
            IsPro = isPro;
        }
    }
}
