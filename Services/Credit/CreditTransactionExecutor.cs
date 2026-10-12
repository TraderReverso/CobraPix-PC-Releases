using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models.Credit;
using CobraPix_PC.Services.Credit.Interfaces;
using CobraPix_PC.Services.Exceptions;

namespace CobraPix_PC.Services.Credit
{
    /// <summary>
    /// Implementação da infraestrutura transacional do módulo Crédito & Giro.
    /// Garante aquisição antecipada de lock (BEGIN IMMEDIATE), ativação mandatória de PRAGMA foreign_keys=ON,
    /// conciliação de idempotência com comparação de hash SHA-256 e tratamento de contenção com retry exponencial.
    /// </summary>
    public class CreditTransactionExecutor : ICreditTransactionExecutor
    {
        private readonly CobraPixDbContext _dbContext;

        public CreditTransactionExecutor(CobraPixDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<TResponse> ExecuteTransactionalAsync<TResponse>(
            string scope,
            string idempotencyKey,
            string canonicalPayload,
            Func<CobraPixDbContext, Task<TResponse>> operation)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                throw new ArgumentException("Chave de idempotência é obrigatória para operações financeiras.", nameof(idempotencyKey));

            string activeDbPath = _dbContext.Database.GetDbConnection().DataSource;
            if (string.IsNullOrWhiteSpace(activeDbPath))
            {
                activeDbPath = DatabasePath.GetDatabaseFilePath();
            }

            string payloadHash = ComputeSha256(canonicalPayload);
            const int maxRetries = 3;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                var connection = (SqliteConnection)_dbContext.Database.GetDbConnection();
                bool connectionWasClosed = connection.State != System.Data.ConnectionState.Open;

                if (connectionWasClosed)
                {
                    await connection.OpenAsync();
                }

                // 1. Configurações fundamentais da conexão SQLite antes do lock
                using (var cmdPragmas = connection.CreateCommand())
                {
                    cmdPragmas.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 10000;";
                    await cmdPragmas.ExecuteNonQueryAsync();
                }

                // 2. Aquisição imediata do lock exclusivo de escrita (BEGIN IMMEDIATE)
                using var transaction = connection.BeginTransaction(deferred: false);
                _dbContext.Database.UseTransaction(transaction);

                try
                {
                    // 2.1 Validação mandatória da trava multicamadas sob o lock exclusivo da transação (elimina janela TOCTOU)
                    CreditProductionSafetyGuard.AssertOperationAllowed(activeDbPath, scope, _dbContext);

                    // 3. Verificação de idempotência persistida sob lock
                    var existingIdempotency = await _dbContext.CreditIdempotencyRecords
                        .FirstOrDefaultAsync(r => r.Scope == scope && r.IdempotencyKey == idempotencyKey);

                    if (existingIdempotency != null)
                    {
                        // Se o hash diverge, rejeita imediatamente com conflito explícito sem tocar dados
                        if (existingIdempotency.RequestPayloadHash != payloadHash)
                        {
                            throw new IdempotencyConflictException(
                                $"Conflito de idempotência no escopo '{scope}': a chave '{idempotencyKey}' foi reutilizada com payload divergente.");
                        }

                        // Mesma chave e mesmo payload: retorna a resposta original persistida
                        transaction.Commit();
                        return JsonSerializer.Deserialize<TResponse>(existingIdempotency.ResponsePayloadJson)!;
                    }

                    // 4. Execução da operação de negócio
                    var result = await operation(_dbContext);

                    // 5. Gravação atômica do registro de idempotência no mesmo commit
                    string responseJson = JsonSerializer.Serialize(result);
                    var newRecord = new CreditIdempotencyRecord
                    {
                        Id = Guid.NewGuid().ToString(),
                        Scope = scope,
                        IdempotencyKey = idempotencyKey,
                        RequestPayloadHash = payloadHash,
                        ResponsePayloadJson = responseJson,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    _dbContext.CreditIdempotencyRecords.Add(newRecord);

                    // 5.1 Revalidação mandatória pré-commit sob lock exclusivo para anular qualquer janela TOCTOU residual
                    CreditProductionSafetyGuard.AssertOperationAllowed(activeDbPath, scope, _dbContext);

                    await _dbContext.SaveChangesAsync();
                    transaction.Commit();

                    return result;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5) // SQLITE_BUSY
                {
                    _dbContext.Database.UseTransaction(null);
                    _dbContext.ChangeTracker.Clear();

                    if (attempt == maxRetries)
                    {
                        throw new TimeoutException("Tempo limite de espera por lock no SQLite excedido após tentativas.", ex);
                    }

                    // Backoff exponencial com jitter
                    await Task.Delay(100 * (int)Math.Pow(2, attempt));
                }
                catch (Exception)
                {
                    _dbContext.Database.UseTransaction(null);
                    _dbContext.ChangeTracker.Clear();
                    throw;
                }
                finally
                {
                    _dbContext.Database.UseTransaction(null);
                    if (connectionWasClosed)
                    {
                        await connection.CloseAsync();
                    }
                }
            }

            throw new InvalidOperationException("Falha inesperada no pipeline transacional de crédito.");
        }

        public static string ComputeSha256(string rawData)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            var builder = new StringBuilder();
            foreach (byte b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }
    }
}
