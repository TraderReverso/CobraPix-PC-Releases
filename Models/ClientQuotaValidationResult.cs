namespace CobraPix_PC.Models
{
    /// <summary>
    /// Resultado da validação centralizada de cota para operações de criação, reativação ou importação de clientes.
    /// Garante que o plano FREE e o plano PRO sejam auditados e respeitados com a regra oficial:
    /// activeCurrent + activeIncoming &lt;= 10 no plano FREE, e ilimitado no plano PRO.
    /// </summary>
    public class ClientQuotaValidationResult
    {
        /// <summary>
        /// Indica se a operação é permitida sem violar a quota comercial.
        /// </summary>
        public bool Allowed { get; set; }

        /// <summary>
        /// Quantidade de clientes ativos atualmente cadastrados no banco.
        /// </summary>
        public int CurrentActiveCount { get; set; }

        /// <summary>
        /// Quantidade de clientes ativos que seriam adicionados pela operação.
        /// </summary>
        public int IncomingActiveCount { get; set; }

        /// <summary>
        /// Limite máximo de clientes ativos para o plano (10 para Free, int.MaxValue para Pro).
        /// </summary>
        public int MaxAllowed { get; set; } = 10;

        /// <summary>
        /// Plano vigente no momento da validação (Free ou Pro).
        /// </summary>
        public PlanType Plan { get; set; } = PlanType.Free;

        /// <summary>
        /// Mensagem explicativa ou de bloqueio.
        /// No caso de excesso de quota no plano FREE durante importação:
        /// "Seu plano FREE permite até 10 clientes ativos.
        ///
        /// Esta importação adicionaria X clientes ativos e ultrapassaria o limite.
        ///
        /// Ative o PRO para importar sem limite ou arquive/revise os clientes antes de continuar."
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
