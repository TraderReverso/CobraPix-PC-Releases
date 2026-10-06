using System;

namespace CobraPix_PC.Models
{
    /// <summary>
    /// Única fonte de verdade (Single Source of Truth) da versão oficial do CobraPix PC.
    /// Implementa regras SemVer (MAJOR.MINOR.PATCH) e utilitários de comparação segura.
    /// </summary>
    public static class AppVersionInfo
    {
        /// <summary>
        /// Versão oficial instalada do CobraPix PC.
        /// </summary>
        public const string CurrentVersion = "1.0.3";

        /// <summary>
        /// Valida se uma string segue estritamente o formato SemVer MAJOR.MINOR.PATCH (ex: 1.0.0).
        /// </summary>
        public static bool IsValidSemVer(string? versionStr)
        {
            return TryParseSemVer(versionStr, out _, out _, out _);
        }

        /// <summary>
        /// Tenta extrair Major, Minor e Patch de uma string de versão.
        /// </summary>
        public static bool TryParseSemVer(string? versionStr, out int major, out int minor, out int patch)
        {
            major = 0;
            minor = 0;
            patch = 0;

            if (string.IsNullOrWhiteSpace(versionStr))
                return false;

            // Remove prefixo 'v' ou 'V' se fornecido
            string clean = versionStr.Trim();
            if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(1).Trim();

            // Divide por pontos
            var parts = clean.Split('.');
            if (parts.Length != 3)
                return false;

            if (!int.TryParse(parts[0], out major) || major < 0)
                return false;

            if (!int.TryParse(parts[1], out minor) || minor < 0)
                return false;

            if (!int.TryParse(parts[2], out patch) || patch < 0)
                return false;

            return true;
        }

        /// <summary>
        /// Compara duas versões SemVer (MAJOR.MINOR.PATCH).
        /// Retorna:
        ///  &gt; 0 se v1 for maior que v2
        ///  0 se forem iguais
        ///  &lt; 0 se v1 for menor que v2
        /// </summary>
        public static int CompareVersions(string v1, string v2)
        {
            bool parsed1 = TryParseSemVer(v1, out int maj1, out int min1, out int pat1);
            bool parsed2 = TryParseSemVer(v2, out int maj2, out int min2, out int pat2);

            if (!parsed1 && !parsed2) return 0;
            if (!parsed1) return -1;
            if (!parsed2) return 1;

            if (maj1 != maj2)
                return maj1.CompareTo(maj2);

            if (min1 != min2)
                return min1.CompareTo(min2);

            return pat1.CompareTo(pat2);
        }

        /// <summary>
        /// Verifica se a versão remota é estritamente mais nova que a versão local.
        /// </summary>
        public static bool IsNewerVersion(string remoteVersion, string localVersion = CurrentVersion)
        {
            return CompareVersions(remoteVersion, localVersion) > 0;
        }
    }
}
