using System;

using Avalonia.Platform;
using Avalonia.Styling;

namespace SourceGit.Models
{
    public static class AppTheme
    {
        public static readonly ThemeVariant CodexDark = new("Codex Dark", ThemeVariant.Dark);

        public static ThemeVariant FromKey(string key)
        {
            if (string.Equals(key, (string)CodexDark.Key, StringComparison.OrdinalIgnoreCase))
                return CodexDark;
            if (string.Equals(key, "Dark", StringComparison.OrdinalIgnoreCase))
                return ThemeVariant.Dark;
            if (string.Equals(key, "Light", StringComparison.OrdinalIgnoreCase))
                return ThemeVariant.Light;

            return ThemeVariant.Default;
        }

        public static bool IsDark(ThemeVariant variant) =>
            variant != null && (PlatformThemeVariant?)variant == PlatformThemeVariant.Dark;
    }
}
