using System;
using System.Threading.Tasks;
using CobraPix_PC.Data;

namespace CobraPix_PC.Services.Credit.Interfaces
{
    /// <summary>
    /// Gerenciador de execução transacional imediata no SQLite com EF Core e idempotência persistente.
    /// </summary>
    public interface ICreditTransactionExecutor
    {
        /// <summary>
        /// Executa uma operação transacional de escrita imediata (BEGIN IMMEDIATE) com garantia de idempotência e retry.
        /// </summary>
        /// <typeparam name="TResponse">Tipo do DTO de resposta retornado pela operação.</typeparam>
        /// <param name="scope">Escopo funcional da operação (ex: "ContractCreation", "Disbursement", "Payment").</param>
        /// <param name="idempotencyKey">Chave de idempotência enviada pelo cliente.</param>
        /// <param name="canonicalPayload">Payload canônico serializado para geração do hash SHA-256.</param>
        /// <param name="operation">Função de negócio executada sob lock exclusivo de escrita.</param>
        /// <returns>Resultado da operação (ou resposta original em caso de repetição idempotente).</returns>
        Task<TResponse> ExecuteTransactionalAsync<TResponse>(
            string scope,
            string idempotencyKey,
            string canonicalPayload,
            Func<CobraPixDbContext, Task<TResponse>> operation);
    }
}
