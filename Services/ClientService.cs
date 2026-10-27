using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Exceptions;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    public class ClientService : IClientService
    {
        private readonly CobraPixDbContext _context;
        private readonly IProService _proService;

        internal CobraPixDbContext DbContext => _context;

        public ClientService(CobraPixDbContext context, IProService? proService = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _proService = proService ?? new ProService(context);
        }

        public async Task<Client> CreateClientAsync(string name, string? phone = null, string? notes = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new BusinessRuleException("O nome do cliente é obrigatório.");
            }

            // Regra de Negócio: Limite do plano FREE (máximo de 10 clientes ativos)
            var limitStatus = await _proService.CheckClientLimitAsync();
            if (!limitStatus.CanAddClient)
            {
                throw new BusinessRuleException(limitStatus.LimitMessage);
            }

            var client = new Client
            {
                Name = name.Trim(),
                Phone = phone?.Trim(),
                Notes = notes?.Trim(),
                IsArchived = false,
                CreatedAt = DateTime.Now
            };

            _context.Clients.Add(client);
            await _context.SaveChangesAsync();
            return client;
        }

        public async Task<Client> UpdateClientAsync(int id, string name, string? phone = null, string? notes = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new BusinessRuleException("O nome do cliente é obrigatório.");
            }

            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
            if (client == null)
            {
                throw new BusinessRuleException($"Cliente com ID {id} não encontrado.");
            }

            client.Name = name.Trim();
            client.Phone = phone?.Trim();
            client.Notes = notes?.Trim();
            client.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return client;
        }

        public async Task<bool> ArchiveClientAsync(int id)
        {
            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
            if (client == null)
            {
                throw new BusinessRuleException($"Cliente com ID {id} não encontrado.");
            }

            // Arquivamento lógico: preserva integralmente o histórico financeiro
            client.IsArchived = true;
            client.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ReactivateClientAsync(int id)
        {
            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
            if (client == null)
            {
                throw new BusinessRuleException($"Cliente com ID {id} não encontrado.");
            }

            // Regra de Negócio: Limite do plano FREE na reativação
            var canReactivate = await _proService.CanReactivateClientAsync(id);
            if (!canReactivate)
            {
                throw new BusinessRuleException("Seu plano FREE permite até 10 clientes ativos. Arquive outro cliente ou assine o PRO para reativar este cliente.");
            }

            client.IsArchived = false;
            client.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<Client?> GetClientByIdAsync(int id)
        {
            return await _context.Clients
                .Include(c => c.Sales)
                    .ThenInclude(s => s.Installments)
                        .ThenInclude(i => i.Payments)
                .Include(c => c.MonthlyCustomer)
                    .ThenInclude(m => m!.Payments)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<List<Client>> GetAllClientsAsync()
        {
            return await _context.Clients
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<List<Client>> GetActiveClientsAsync()
        {
            return await _context.Clients
                .AsNoTracking()
                .Where(c => !c.IsArchived)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<List<Client>> GetArchivedClientsAsync()
        {
            return await _context.Clients
                .AsNoTracking()
                .Where(c => c.IsArchived)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }
    }
}
