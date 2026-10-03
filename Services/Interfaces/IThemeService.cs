using System;
using System.Threading.Tasks;
using CobraPix_PC.Models;

namespace CobraPix_PC.Services.Interfaces
{
    /// <summary>
    /// Contrato de serviço para gerenciamento e persistência do tema visual (Escuro / Claro).
    /// </summary>
    public interface IThemeService
    {
        /// <summary>
        /// Tema atualmente ativo na aplicação.
        /// </summary>
        AppTheme CurrentTheme { get; }

        /// <summary>
        /// Obtém o tema persistido no banco de dados local (AppSettings).
        /// </summary>
        Task<AppTheme> GetThemeAsync();

        /// <summary>
        /// Define e persiste o tema, aplicando-o imediatamente aos recursos visuais da aplicação.
        /// </summary>
        Task SetThemeAsync(AppTheme theme);

        /// <summary>
        /// Aplica os recursos do tema informado ao Application.Current.Resources.
        /// </summary>
        void ApplyTheme(AppTheme theme);

        /// <summary>
        /// Evento disparado quando o tema visual é alterado.
        /// </summary>
        event EventHandler<AppTheme>? ThemeChanged;
    }
}
