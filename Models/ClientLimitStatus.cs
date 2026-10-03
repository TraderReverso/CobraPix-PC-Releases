namespace CobraPix_PC.Models
{
    /// <summary>
    /// Representa o estado consolidado da cota e limites de clientes conforme o plano comercial ativo.
    /// </summary>
    public class ClientLimitStatus
    {
        public PlanType Plan { get; set; } = PlanType.Free;

        public bool IsPro => Plan == PlanType.Pro;

        /// <summary>
        /// Quantidade de clientes ativos (que não estão arquivados e não estão quitados).
        /// </summary>
        public int ActiveClientsCount { get; set; }

        /// <summary>
        /// Quantidade total de clientes cadastrados no banco.
        /// </summary>
        public int TotalClientsCount { get; set; }

        /// <summary>
        /// Quantidade de clientes arquivados (não consomem o limite).
        /// </summary>
        public int ArchivedClientsCount { get; set; }

        /// <summary>
        /// Quantidade de clientes quitados com vendas 100% pagas (não consomem o limite).
        /// </summary>
        public int QuitadosClientsCount { get; set; }

        /// <summary>
        /// Quantidade de clientes que consomem a capacidade comercial no plano FREE (ativos + arquivados não-quitados).
        /// </summary>
        public int CapacityUsedClientsCount { get; set; }

        /// <summary>
        /// Limite máximo de clientes ativos para o plano atual (10 para Free, int.MaxValue para Pro).
        /// </summary>
        public int MaxActiveClients { get; set; } = 10;

        /// <summary>
        /// Indica se ainda é possível cadastrar um novo cliente.
        /// </summary>
        public bool CanAddClient { get; set; }

        /// <summary>
        /// Mensagem explicativa amigável sobre o status do limite.
        /// </summary>
        public string LimitMessage { get; set; } = string.Empty;
    }
}
