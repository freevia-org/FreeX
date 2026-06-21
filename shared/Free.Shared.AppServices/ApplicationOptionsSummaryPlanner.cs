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

public static class ApplicationOptionsSummaryPlanner
{
    public const string RecentFilesKeptLabel = "Recent files kept";
    public const string DefaultSaveFormatLabel = "Default save format";
    public const string UiLanguageLabel = "UI language";
    public const string DataFolderLabel = "Data folder";
    public const string SystemDefaultLanguageLabel = "System default";

    public static ApplicationOptionsSummaryPlan Build(
        IApplicationOptionsSummarySource options,
        string dataFolder)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ApplicationOptionsSummaryPlan([
            new(RecentFilesKeptLabel, options.RecentFilesCap.ToString()),
            new(DefaultSaveFormatLabel, options.DefaultSaveFormat),
            new(UiLanguageLabel, FormatUiLanguage(options.UiLanguage)),
            new(DataFolderLabel, dataFolder),
        ]);
    }

    public static string FormatUiLanguage(string? uiLanguage) =>
        string.IsNullOrEmpty(uiLanguage) ? SystemDefaultLanguageLabel : uiLanguage;
}
