using Free.Shared.AppServices;

namespace Free.Shared.Shell.Wpf;

public sealed record SisterBackstagePaneTextSpec(
    string RecentEmptyText,
    string TemplateHeading,
    string TemplateTileCaption,
    string TemplateFooterText,
    string OptionsDescription,
    string? OptionsEditText = null)
{
    public static SisterBackstagePaneTextSpec FreeW { get; } = new(
        RecentEmptyText: "No recent documents.",
        TemplateHeading: "New",
        TemplateTileCaption: "Blank document",
        TemplateFooterText: "More templates are not available in this build.",
        OptionsDescription: "FreeW application settings. These persist between sessions and apply immediately.",
        OptionsEditText: "Edit options\u2026");

    public static SisterBackstagePaneTextSpec FreeP { get; } = new(
        RecentEmptyText: "No recent presentations.",
        TemplateHeading: "New",
        TemplateTileCaption: "Blank presentation",
        TemplateFooterText: "More templates are not available in this build.",
        OptionsDescription: "FreeP application settings. These persist between sessions.");
}

/// <summary>
/// Builds common Backstage pane specs for WPF sister apps from app-specific text presets and host callbacks.
/// </summary>
public sealed class SisterBackstagePaneSpecPlanner
{
    private readonly SisterBackstagePaneTextSpec _text;

    public SisterBackstagePaneSpecPlanner(SisterBackstagePaneTextSpec text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    public BackstageRecentPaneSpec BuildRecentPaneSpec(
        IEnumerable<string> recentPaths,
        Action<string> openPath)
    {
        ArgumentNullException.ThrowIfNull(recentPaths);
        ArgumentNullException.ThrowIfNull(openPath);

        return new BackstageRecentPaneSpec(
            recentPaths.ToArray(),
            _text.RecentEmptyText,
            openPath);
    }

    public BackstageTemplatePaneSpec BuildNewPaneSpec(Action create)
    {
        ArgumentNullException.ThrowIfNull(create);

        return new BackstageTemplatePaneSpec(
            _text.TemplateHeading,
            _text.TemplateTileCaption,
            _text.TemplateFooterText,
            create);
    }

    public BackstageOptionsPaneSpec BuildOptionsPaneSpec(
        IApplicationOptionsSummarySource options,
        string dataFolder,
        Action? edit = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataFolder);

        return BackstageApplicationOptionsPanePlanner.Build(
            _text.OptionsDescription,
            options,
            dataFolder,
            _text.OptionsEditText,
            edit);
    }
}
