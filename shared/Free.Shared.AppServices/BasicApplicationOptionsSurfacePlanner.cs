using System.Globalization;

namespace Free.Shared.AppServices;

/// <summary>
/// One selectable entry in a "Default save format" picker: the human label the picker shows and the
/// extension the accepted <see cref="IBasicApplicationOptions.DefaultSaveFormat"/> is set to. The label is
/// product text (and may be localized by the owning app); the record itself is pure data, which is why the
/// sister apps can share it. <see cref="ToString"/> is overridden because both shells bind the choice list
/// straight into a native combo box that renders items through <c>ToString</c>.
/// </summary>
public sealed record ApplicationOptionsFormatChoice(string Label, string Extension)
{
    public override string ToString() => Label;
}

/// <summary>Which of the shared basic-options fields a <see cref="BasicApplicationOptionsFieldSpec"/> describes.</summary>
public enum BasicApplicationOptionsFieldKind
{
    RecentFilesCap,
    DefaultSaveFormat,
    UiLanguage,
}

/// <summary>A single labelled row on the basic options surface, optionally with a hint line beneath it.</summary>
public sealed record BasicApplicationOptionsFieldSpec(
    BasicApplicationOptionsFieldKind Kind,
    string Label,
    string? Hint = null);

/// <summary>
/// The neutral schema of the "General" options surface the sister apps share: the recent-files cap, the
/// default save format picker, and the UI-language override — in that order, with the language hint on the
/// language row. Apps supply their own (possibly localized) label text and format choices; the field set,
/// the ordering, and the hint placement are the shared decision.
/// </summary>
public sealed record BasicApplicationOptionsGeneralSpec(
    string RecentFilesLabel,
    string DefaultSaveFormatLabel,
    string UiLanguageLabel,
    string UiLanguageHint,
    IReadOnlyList<ApplicationOptionsFormatChoice> FormatChoices)
{
    public IReadOnlyList<BasicApplicationOptionsFieldSpec> Fields { get; } =
    [
        new(BasicApplicationOptionsFieldKind.RecentFilesCap, RecentFilesLabel),
        new(BasicApplicationOptionsFieldKind.DefaultSaveFormat, DefaultSaveFormatLabel),
        new(BasicApplicationOptionsFieldKind.UiLanguage, UiLanguageLabel, UiLanguageHint),
    ];
}

/// <summary>
/// Builds the neutral parts of the sister apps' basic Options surface. This owns the two decisions that
/// were the same in FreeW and FreeP — which fields the General surface has and in what order, and how the
/// UI-language hint switches between its "no detected culture" and "currently &lt;culture&gt;" forms — while
/// the apps keep their own product text, localization mechanism, extra tabs, and extra settings.
/// </summary>
public static class BasicApplicationOptionsSurfacePlanner
{
    /// <summary>
    /// Picks the UI-language hint text. A blank <paramref name="systemLanguageLabel"/> (the invariant /
    /// unnamed culture) yields <paramref name="systemOnlyHint"/>; otherwise the label is formatted into
    /// <paramref name="currentLanguageHintFormat"/> (a single <c>{0}</c> placeholder).
    /// </summary>
    public static string BuildUiLanguageHint(
        string? systemLanguageLabel,
        string systemOnlyHint,
        string currentLanguageHintFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemOnlyHint);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentLanguageHintFormat);

        return string.IsNullOrWhiteSpace(systemLanguageLabel)
            ? systemOnlyHint
            : string.Format(CultureInfo.CurrentCulture, currentLanguageHintFormat, systemLanguageLabel);
    }

    /// <summary>
    /// Assembles the shared General surface from app-supplied labels, the resolved language hint, and the
    /// app's default-save-format choices.
    /// </summary>
    public static BasicApplicationOptionsGeneralSpec BuildGeneral(
        string recentFilesLabel,
        string defaultSaveFormatLabel,
        string uiLanguageLabel,
        string uiLanguageHint,
        IReadOnlyList<ApplicationOptionsFormatChoice> formatChoices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recentFilesLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultSaveFormatLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(uiLanguageLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(uiLanguageHint);
        ArgumentNullException.ThrowIfNull(formatChoices);

        return new BasicApplicationOptionsGeneralSpec(
            recentFilesLabel,
            defaultSaveFormatLabel,
            uiLanguageLabel,
            uiLanguageHint,
            formatChoices);
    }

    /// <summary>
    /// Builds the General surface in one step: resolves the language hint, then assembles the spec.
    /// </summary>
    public static BasicApplicationOptionsGeneralSpec BuildGeneral(
        string recentFilesLabel,
        string defaultSaveFormatLabel,
        string uiLanguageLabel,
        string? systemLanguageLabel,
        string systemOnlyHint,
        string currentLanguageHintFormat,
        IReadOnlyList<ApplicationOptionsFormatChoice> formatChoices) =>
        BuildGeneral(
            recentFilesLabel,
            defaultSaveFormatLabel,
            uiLanguageLabel,
            BuildUiLanguageHint(systemLanguageLabel, systemOnlyHint, currentLanguageHintFormat),
            formatChoices);
}
