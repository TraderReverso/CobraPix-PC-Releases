using System.Collections.Generic;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    public interface IClientService
    {
        Task<Client> CreateClientAsync(string name, string? phone = null, string? notes = null);
        Task<Client> UpdateClientAsync(int id, string name, string? phone = null, string? notes = null);
        Task<bool> ArchiveClientAsync(int id);
        Task<bool> ReactivateClientAsync(int id);
        Task<Client?> GetClientByIdAsync(int id);
        Task<List<Client>> GetAllClientsAsync();
        Task<List<Client>> GetActiveClientsAsync();
        Task<List<Client>> GetArchivedClientsAsync();
    }
}
