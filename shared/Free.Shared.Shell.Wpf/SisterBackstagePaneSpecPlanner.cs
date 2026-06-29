using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

public sealed record SisterBackstageExportPaneTextSpec(
    string Heading,
    string Description,
    string FixedLayoutGroupHeading,
    string PdfActionLabel,
    string PdfActionDescription,
    string? XpsActionLabel = null,
    string? XpsActionDescription = null)
{
    public static SisterBackstageExportPaneTextSpec NeutralEnglish { get; } = new(
        Heading: "Export",
        Description: "Create a fixed-layout copy for sharing or presenting.",
        FixedLayoutGroupHeading: "Create PDF Copy",
        PdfActionLabel: "Export to PDF...",
        PdfActionDescription: "Publish a fixed-layout copy.");

    public static SisterBackstageExportPaneTextSpec FreeW { get; } = new(
        Heading: "Export",
        Description: "Create a fixed-layout copy or choose an editable document format.",
        FixedLayoutGroupHeading: "Create PDF/XPS Document",
        PdfActionLabel: "Create PDF or XPS",
        PdfActionDescription: "Publish a fixed-layout copy for sharing or printing.",
        XpsActionLabel: "Export to XPS",
        XpsActionDescription: "Publish an XPS document with selectable, searchable vector text.");

    public static SisterBackstageExportPaneTextSpec FreeP { get; } = new(
        Heading: "Export",
        Description: "Create a PDF copy of this presentation - one page per slide, with selectable text.",
        FixedLayoutGroupHeading: "Create PDF Copy",
        PdfActionLabel: "Export to PDF...",
        PdfActionDescription: "Publish a fixed-layout copy for sharing or presenting.");
}

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
        OptionsEditText: "Edit options\u2026")
    {
        Export = SisterBackstageExportPaneTextSpec.FreeW
    };

    public static SisterBackstagePaneTextSpec FreeP { get; } = new(
        RecentEmptyText: "No recent presentations.",
        TemplateHeading: "New",
        TemplateTileCaption: "Blank presentation",
        TemplateFooterText: "More templates are not available in this build.",
        OptionsDescription: "FreeP application settings. These persist between sessions.")
    {
        Export = SisterBackstageExportPaneTextSpec.FreeP
    };

    public SisterBackstageAccountPaneTextSpec Account { get; init; } =
        SisterBackstageAccountPaneTextSpec.NeutralEnglish;

    public SisterBackstageExportPaneTextSpec Export { get; init; } =
        SisterBackstageExportPaneTextSpec.NeutralEnglish;
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

    public BackstageAccountPaneSpec BuildAccountPaneSpec(
        SisterBackstageAccountPaneContext context,
        Action? openOptions)
    {
        var plan = SisterBackstageAccountPanePlanner.Build(context, _text.Account);

        return new BackstageAccountPaneSpec(
            plan.Heading,
            plan.Description,
            plan.Groups,
            plan.OptionsText,
            openOptions);
    }

    public BackstageActionPaneSpec BuildExportPaneSpec(
        Action exportPdf,
        Action? exportXps = null,
        IReadOnlyList<BackstageActionGroup>? additionalGroups = null)
    {
        ArgumentNullException.ThrowIfNull(exportPdf);

        var export = _text.Export;
        var fixedLayoutRows = new List<BackstageActionRow>
        {
            new(export.PdfActionLabel, export.PdfActionDescription, exportPdf),
        };

        if (exportXps is not null &&
            !string.IsNullOrWhiteSpace(export.XpsActionLabel) &&
            !string.IsNullOrWhiteSpace(export.XpsActionDescription))
        {
            fixedLayoutRows.Add(new BackstageActionRow(
                export.XpsActionLabel,
                export.XpsActionDescription,
                exportXps));
        }

        var groups = new List<BackstageActionGroup>
        {
            new(export.FixedLayoutGroupHeading, fixedLayoutRows),
        };

        if (additionalGroups is { Count: > 0 })
            groups.AddRange(additionalGroups);

        return new BackstageActionPaneSpec(
            export.Heading,
            export.Description,
            groups);
    }
}
