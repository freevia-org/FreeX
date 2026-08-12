using Free.Shared.AppServices;
using FreeP.App.Localization;
using FreeP.Core.IO;

namespace FreeP.App.Compositor;

/// <summary>
/// Cross-host FreeP application settings. Both WPF and Avalonia consume the same normalized model so
/// Backstage state and recent-file policy cannot diverge by UI framework.
/// </summary>
public class FreePOptions : IBasicApplicationOptions, IApplicationOptionsSummarySource
{
    public const int DefaultRecentFilesCap = ApplicationOptionsNormalizer.DefaultRecentFilesCap;
    public const int MinRecentFilesCap = ApplicationOptionsNormalizer.MinRecentFilesCap;
    public const int MaxRecentFilesCap = ApplicationOptionsNormalizer.MaxRecentFilesCap;
    public const string FxpDefaultFormat = FxpFormat.Extension;
    public const string SystemDefaultLanguage = ApplicationOptionsNormalizer.SystemDefaultLanguage;

    public int RecentFilesCap { get; set; } = DefaultRecentFilesCap;

    public string DefaultSaveFormat { get; set; } = FxpDefaultFormat;

    public string UiLanguage { get; set; } = SystemDefaultLanguage;

    public void Normalize()
    {
        RecentFilesCap = ApplicationOptionsNormalizer.NormalizeRecentFilesCap(RecentFilesCap);
        DefaultSaveFormat = ApplicationOptionsNormalizer.NormalizeDefaultSaveFormat(DefaultSaveFormat, FxpDefaultFormat);
        UiLanguage = AppLanguageCatalog.NormalizeCultureName(UiLanguage);
    }
}
