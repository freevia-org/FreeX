using Free.Shared.AppServices;
using FreeP.Core.IO;

namespace FreeP.App.Host;

/// <summary>
/// FreeP's persisted application settings. App-specific by design; only the <em>persistence</em> is shared,
/// via the neutral <see cref="JsonSettingsStore{T}"/>. Kept deliberately small for the scaffold — enough real
/// settings to prove the mechanism end-to-end (a read site, a write site, a round-trip). Mirrors FreeWOptions.
///
/// <para>All properties carry sensible defaults and the type is JSON round-trippable with a parameterless
/// constructor, so a missing or corrupt settings file degrades to <c>new FreePOptions()</c>.</para>
/// </summary>
public sealed class FreePOptions : INormalizableApplicationOptions, IApplicationOptionsSummarySource
{
    public const int DefaultRecentFilesCap = ApplicationOptionsNormalizer.DefaultRecentFilesCap;
    public const int MinRecentFilesCap = ApplicationOptionsNormalizer.MinRecentFilesCap;
    public const int MaxRecentFilesCap = ApplicationOptionsNormalizer.MaxRecentFilesCap;
    public const string FxpDefaultFormat = FxpFormat.Extension;
    public const string SystemDefaultLanguage = ApplicationOptionsNormalizer.SystemDefaultLanguage;

    /// <summary>How many recent files FreeP retains. Clamped to [0, <see cref="MaxRecentFilesCap"/>].</summary>
    public int RecentFilesCap { get; set; } = DefaultRecentFilesCap;

    /// <summary>Default save format extension (FreeP ships a single <c>.fxp</c> format today).</summary>
    public string DefaultSaveFormat { get; set; } = FxpDefaultFormat;

    /// <summary>UI language placeholder (empty = follow the system culture). Reserved for a future picker.</summary>
    public string UiLanguage { get; set; } = SystemDefaultLanguage;

    /// <summary>Normalizes loaded values to their valid ranges (called after a load).</summary>
    public void Normalize()
    {
        RecentFilesCap = ApplicationOptionsNormalizer.NormalizeRecentFilesCap(RecentFilesCap);
        DefaultSaveFormat = ApplicationOptionsNormalizer.NormalizeDefaultSaveFormat(DefaultSaveFormat, FxpDefaultFormat);
        UiLanguage = ApplicationOptionsNormalizer.NormalizeUiLanguage(UiLanguage);
    }
}
