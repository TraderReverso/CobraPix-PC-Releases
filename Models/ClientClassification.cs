namespace CobraPix_PC.Models
{
    /// <summary>
    /// Classificação de clientes do CobraPix PC por comportamento financeiro e pontualidade.
    /// Sistema não-competitivo por letras: S, A, B, C, D, E.
    /// </summary>
    public enum ClientRank
    {
        S = 0, // Pagador Ouro (Dourado)
        A = 1, // Top (Verde)
        B = 2, // Bom (Azul)
        C = 3, // Médio (Amarelo)
        D = 4, // Mais ou menos (Laranja)
        E = 5  // Ruim (Vermelho)
    }

    /// <summary>
    /// Relatório consolidado e objetivo da classificação de um cliente.
    /// </summary>
    public class ClientClassification
    {
        public ClientRank Rank { get; set; } = ClientRank.C;
        public string RankLetter => Rank.ToString();

        /// <summary>
        /// Descrição amigável da classificação (ex: Pagador Ouro, Top, Bom, Médio, Mais ou menos, Ruim).
        /// </summary>
        public string Description { get; set; } = "Médio";

        /// <summary>
        /// Cor hexadecimal oficial correspondente para badges e chips.
        /// </summary>
        public string ColorHex { get; set; } = "#EAB308";

        /// <summary>
        /// Texto completo do badge formatado: [Letra] Descrição.
        /// </summary>
        public string BadgeText => $"[{RankLetter}] {Description}";

        /// <summary>
        /// Motivo detalhado e transparente da atribuição da nota.
        /// </summary>
        public string DetailReason { get; set; } = string.Empty;

        /// <summary>
        /// Percentual de pontualidade (obrigações pagas em dia sobre o total de obrigações avaliadas).
        /// </summary>
        public decimal OnTimePercentage { get; set; }

        /// <summary>
        /// Quantidade de pagamentos realizados no histórico.
        /// </summary>
        public int TotalPaidCount { get; set; }

        /// <summary>
        /// Quantidade de obrigações pagas no prazo de vencimento.
        /// </summary>
        public int OnTimePaidCount { get; set; }

        /// <summary>
        /// Quantidade de obrigações pagas com atraso.
        /// </summary>
        public int LatePaidCount { get; set; }

        /// <summary>
        /// Quantidade de parcelas/mensalidades atualmente em atraso.
        /// </summary>
        public int CurrentOverdueCount { get; set; }

        /// <summary>
        /// Valor financeiro total atualmente em atraso.
        /// </summary>
        public decimal CurrentOverdueAmount { get; set; }

        /// <summary>
        /// Indica se o cliente possui histórico de obrigações para avaliação.
        /// Clientes sem histórico recebem uma nota neutra sem dados fictícios.
        /// </summary>
        public bool HasSufficientHistory { get; set; }
    }
}
