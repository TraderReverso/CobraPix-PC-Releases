using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using CobraPix_PC.Data;
using CobraPix_PC.Models;
using CobraPix_PC.Services.Interfaces;

namespace CobraPix_PC.Services
{
    /// <summary>
    /// Implementação oficial do serviço de temas do CobraPix PC.
    /// Gerencia a alternância entre os temas Escuro (estética Solo Leveling) e Claro comercial,
    /// com persistência local na tabela AppSettings e atualização dinâmica dos recursos da aplicação.
    /// </summary>
    public class ThemeService : IThemeService
    {
        public const string SettingKeyTheme = "App.Theme";

        private readonly CobraPixDbContext _context;
        private AppTheme _currentTheme = AppTheme.Dark;

        internal CobraPixDbContext DbContext => _context;

        public event EventHandler<AppTheme>? ThemeChanged;

        public AppTheme CurrentTheme => _currentTheme;

        public ThemeService(CobraPixDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<AppTheme> GetThemeAsync()
        {
            try
            {
                var setting = await _context.AppSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == SettingKeyTheme);

                if (setting != null && Enum.TryParse<AppTheme>(setting.Value, true, out var theme))
                {
                    _currentTheme = theme;
                    return theme;
                }
            }
            catch
            {
                // Fallback para o tema padrão escuro em caso de erro de I/O
            }

            _currentTheme = AppTheme.Dark;
            return AppTheme.Dark;
        }

        public async Task SetThemeAsync(AppTheme theme)
        {
            _currentTheme = theme;

            try
            {
                var existing = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingKeyTheme);
                if (existing != null)
                {
                    existing.Value = theme.ToString();
                    existing.UpdatedAt = DateTime.Now;
                }
                else
                {
                    _context.AppSettings.Add(new AppSetting
                    {
                        Key = SettingKeyTheme,
                        Value = theme.ToString(),
                        UpdatedAt = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();
            }
            catch
            {
                // Tratamento seguro em cenários de teste sem persistência
            }

            ApplyTheme(theme);
            ThemeChanged?.Invoke(this, theme);
        }

        public void ApplyTheme(AppTheme theme)
        {
            _currentTheme = theme;

            try
            {
                if (Application.Current != null)
                {
                    var action = new Action(() =>
                    {
                        string fileName = theme switch
                        {
                            AppTheme.Light => "ColorsLight.xaml",
                            AppTheme.GreenYellow => "ColorsGreenYellow.xaml",
                            _ => "Colors.xaml"
                        };

                        ResourceDictionary themeDict;
                        try
                        {
                            var packUri = new Uri($"pack://application:,,,/Resources/{fileName}", UriKind.Absolute);
                            themeDict = new ResourceDictionary { Source = packUri };
                        }
                        catch
                        {
                            var relUri = new Uri($"/Resources/{fileName}", UriKind.Relative);
                            themeDict = new ResourceDictionary { Source = relUri };
                        }

                        // 1. Substitui todos os dicionários de cores anteriores na lista MergedDictionaries
                        var mergedDicts = Application.Current.Resources.MergedDictionaries;
                        var toRemove = new System.Collections.Generic.List<ResourceDictionary>();
                        int insertIndex = -1;

                        for (int i = 0; i < mergedDicts.Count; i++)
                        {
                            var d = mergedDicts[i];
                            if (d.Source != null && (d.Source.OriginalString.Contains("Colors.xaml", StringComparison.OrdinalIgnoreCase) ||
                                                     d.Source.OriginalString.Contains("ColorsLight.xaml", StringComparison.OrdinalIgnoreCase) ||
                                                     d.Source.OriginalString.Contains("ColorsGreenYellow.xaml", StringComparison.OrdinalIgnoreCase)))
                            {
                                if (insertIndex == -1) insertIndex = i;
                                toRemove.Add(d);
                            }
                        }

                        foreach (var d in toRemove)
                        {
                            mergedDicts.Remove(d);
                        }

                        if (insertIndex < 0 || insertIndex > mergedDicts.Count)
                        {
                            insertIndex = 0;
                        }

                        mergedDicts.Insert(insertIndex, themeDict);

                        // 2. Atualiza as chaves diretamente na coleção global de recursos
                        foreach (var key in themeDict.Keys)
                        {
                            Application.Current.Resources[key] = themeDict[key];
                        }

                        Application.Current.Resources["IsDarkTheme"] = theme == AppTheme.Dark;
                        Application.Current.Resources["IsLightTheme"] = theme == AppTheme.Light;
                        Application.Current.Resources["IsGreenYellowTheme"] = theme == AppTheme.GreenYellow;

                        // 3. Força invalidação visual em todas as janelas ativas
                        foreach (Window window in Application.Current.Windows)
                        {
                            window.InvalidateVisual();
                        }
                    });

                    if (Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                    {
                        Application.Current.Dispatcher.Invoke(action);
                    }
                    else
                    {
                        action();
                    }
                }
            }
            catch
            {
                // Tratamento defensivo em ambientes headless de teste
            }
        }
    }
}
