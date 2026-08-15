namespace Free.Shared.AppServices;

/// <summary>
/// Minimal common shape for the sister apps' shared options summary rows.
/// The full option models remain app-owned.
/// </summary>
public interface IApplicationOptionsSummarySource
{
    int RecentFilesCap { get; }

    string DefaultSaveFormat { get; }

    string UiLanguage { get; }
}

public sealed record ApplicationOptionsSummaryRow(string Label, string Value);

public sealed record ApplicationOptionsSummaryPlan(IReadOnlyList<ApplicationOptionsSummaryRow> Rows);

public sealed record ApplicationOptionsSummaryTextSpec(
    string RecentFilesKeptLabel,
    string DefaultSaveFormatLabel,
    string UiLanguageLabel,
    string DataFolderLabel,
    string SystemDefaultLanguageLabel)
{
    public static ApplicationOptionsSummaryTextSpec NeutralEnglish { get; } = new(
        "Recent files kept",
        "Default save format",
        "UI language",
        "Data folder",
        "System default");

    public static ApplicationOptionsSummaryTextSpec FromDescriptor(
        ApplicationOptionsSummaryTextDescriptor descriptor,
        Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new ApplicationOptionsSummaryTextSpec(
            descriptor.RecentFilesKeptLabel.Resolve(getText),
            descriptor.DefaultSaveFormatLabel.Resolve(getText),
            descriptor.UiLanguageLabel.Resolve(getText),
            descriptor.DataFolderLabel.Resolve(getText),
            descriptor.SystemDefaultLanguageLabel.Resolve(getText));
    }
}

public static class ApplicationOptionsSummaryPlanner
{
    public const string RecentFilesKeptLabel = "Recent files kept";
    public const string DefaultSaveFormatLabel = "Default save format";
    public const string UiLanguageLabel = "UI language";
    public const string DataFolderLabel = "Data folder";
    public const string SystemDefaultLanguageLabel = "System default";

    public static ApplicationOptionsSummaryPlan Build(
        IApplicationOptionsSummarySource options,
        string dataFolder,
        ApplicationOptionsSummaryTextSpec? text = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        text ??= ApplicationOptionsSummaryTextSpec.NeutralEnglish;

        return new ApplicationOptionsSummaryPlan([
            new(text.RecentFilesKeptLabel, options.RecentFilesCap.ToString()),
            new(text.DefaultSaveFormatLabel, options.DefaultSaveFormat),
            new(text.UiLanguageLabel, FormatUiLanguage(options.UiLanguage, text)),
            new(text.DataFolderLabel, dataFolder),
        ]);
    }

    public static string FormatUiLanguage(string? uiLanguage) =>
        FormatUiLanguage(uiLanguage, ApplicationOptionsSummaryTextSpec.NeutralEnglish);

    public static string FormatUiLanguage(
        string? uiLanguage,
        ApplicationOptionsSummaryTextSpec text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.IsNullOrEmpty(uiLanguage) ? text.SystemDefaultLanguageLabel : uiLanguage;
    }
}
