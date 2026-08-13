using Free.Shared.AppServices;
using FreeW.App.Localization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Options;

/// <summary>
/// FreeW's persisted application settings. App-specific by design (the spreadsheet vs. word-processor
/// option sets are genuinely different); only the <em>persistence</em> is shared, via the neutral
/// <see cref="JsonSettingsStore{T}"/> in <c>Free.Shared.AppServices</c>. Kept deliberately small for now
/// - enough real settings to prove the mechanism end-to-end (a read site, a write site, a round-trip).
///
/// <para>
/// All properties carry sensible defaults and the type is JSON round-trippable with a parameterless
/// constructor, so a missing or corrupt settings file degrades to <c>new FreeWOptions()</c>.
/// </para>
/// </summary>
public sealed class FreeWOptions : IBasicApplicationOptions, IApplicationOptionsSummarySource
{
    public const int DefaultRecentFilesCap = ApplicationOptionsNormalizer.DefaultRecentFilesCap;
    public const int MinRecentFilesCap = ApplicationOptionsNormalizer.MinRecentFilesCap;
    public const int MaxRecentFilesCap = ApplicationOptionsNormalizer.MaxRecentFilesCap;
    public const string DocxDefaultFormat = ".docx";
    public const string SystemDefaultLanguage = ApplicationOptionsNormalizer.SystemDefaultLanguage;

    /// <summary>How many recent files FreeW retains. Clamped to [0, <see cref="MaxRecentFilesCap"/>].</summary>
    public int RecentFilesCap { get; set; } = DefaultRecentFilesCap;

    /// <summary>Default save format extension (FreeW ships a single <c>.docx</c> format today).</summary>
    public string DefaultSaveFormat { get; set; } = DocxDefaultFormat;

    /// <summary>UI language placeholder (empty = follow the system culture). Reserved for a future picker.</summary>
    public string UiLanguage { get; set; } = SystemDefaultLanguage;

    /// <summary>
    /// Master switch for as-you-type smart typing (Word's "AutoCorrect"). When off the editor performs no
    /// AutoCorrect / AutoFormat transforms at all, regardless of <see cref="AutoFormat"/>.
    /// </summary>
    public bool AutoCorrectEnabled { get; set; } = true;

    /// <summary>
    /// The per-rule "AutoFormat As You Type" toggles. A JSON-round-trippable, never-null sub-object; a
    /// missing value degrades to <see cref="AutoFormatOptions.Default"/> (every rule on).
    /// </summary>
    public AutoFormatOptions AutoFormat { get; set; } = AutoFormatOptions.Default;

    /// <summary>
    /// The Word "AutoCorrect" tab settings - the two-initial-capitals fix, day-name capitalization, and the
    /// user-editable replace-text table. A JSON-round-trippable, never-null sub-object; a missing value
    /// degrades to <see cref="AutoCorrectOptions.Default"/> (every rule on, default replace table).
    /// </summary>
    public AutoCorrectOptions AutoCorrect { get; set; } = AutoCorrectOptions.Default;

    /// <summary>Normalizes loaded values to their valid ranges (called after a load).</summary>
    public void Normalize()
    {
        RecentFilesCap = ApplicationOptionsNormalizer.NormalizeRecentFilesCap(RecentFilesCap);
        DefaultSaveFormat = ApplicationOptionsNormalizer.NormalizeDefaultSaveFormat(DefaultSaveFormat, DocxDefaultFormat);
        UiLanguage = AppLanguageCatalog.NormalizeCultureName(UiLanguage);
        AutoFormat ??= AutoFormatOptions.Default;
        AutoCorrect ??= AutoCorrectOptions.Default;
        AutoCorrect.Normalize();
    }
}
