using System;
using CobraPix_PC.Models;

namespace CobraPix_PC.ViewModels
{
    /// <summary>
    /// Modelo de exibição para itens individuais de clientes na listagem e detalhes.
    /// </summary>
    public class ClientItemViewModel : ViewModelBase
    {
        private string _name = string.Empty;
        private string? _phone;
        private string? _notes;
        private bool _isArchived;
        private DateTime? _updatedAt;

        public int Id { get; }

        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                {
                    OnPropertyChanged(nameof(Initials));
                }
            }
        }

        public string? Phone
        {
            get => _phone;
            set
            {
                if (SetProperty(ref _phone, value))
                {
                    OnPropertyChanged(nameof(FormattedPhone));
                }
            }
        }

        public string? Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        public bool IsArchived
        {
            get => _isArchived;
            set
            {
                if (SetProperty(ref _isArchived, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public DateTime CreatedAt { get; }

        public DateTime? UpdatedAt
        {
            get => _updatedAt;
            set
            {
                if (SetProperty(ref _updatedAt, value))
                {
                    OnPropertyChanged(nameof(FormattedUpdatedAt));
                }
            }
        }

        public string StatusText => IsArchived ? "ARQUIVADO" : "ATIVO";

        public string FormattedPhone => string.IsNullOrWhiteSpace(Phone) ? "Não informado" : Phone;

        public string FormattedCreatedAt => CreatedAt.ToString("dd/MM/yyyy HH:mm");

        public string FormattedUpdatedAt => UpdatedAt.HasValue
            ? UpdatedAt.Value.ToString("dd/MM/yyyy HH:mm")
            : "Sem alterações";

        public string Initials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name)) return "CL";
                var parts = Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
                return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
            }
        }

        public ClientItemViewModel(Client client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            Id = client.Id;
            _name = client.Name;
            _phone = client.Phone;
            _notes = client.Notes;
            _isArchived = client.IsArchived;
            CreatedAt = client.CreatedAt;
            _updatedAt = client.UpdatedAt;
        }

        public void UpdateFrom(Client client)
        {
            Name = client.Name;
            Phone = client.Phone;
            Notes = client.Notes;
            IsArchived = client.IsArchived;
            UpdatedAt = client.UpdatedAt;
        }
    }
}
