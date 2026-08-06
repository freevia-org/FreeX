using Free.Shared.AppServices;
using Free.Shared.Localization;

namespace Free.Shared.Shell;

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

    public static SisterBackstageExportPaneTextSpec FromDescriptor(
        SisterBackstageExportPaneTextDescriptor descriptor,
        Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new SisterBackstageExportPaneTextSpec(
            Resolve(descriptor.Heading, getText),
            Resolve(descriptor.Description, getText),
            Resolve(descriptor.FixedLayoutGroupHeading, getText),
            Resolve(descriptor.PdfActionLabel, getText),
            Resolve(descriptor.PdfActionDescription, getText),
            descriptor.XpsActionLabel is null ? null : Resolve(descriptor.XpsActionLabel, getText),
            descriptor.XpsActionDescription is null ? null : Resolve(descriptor.XpsActionDescription, getText));
    }

    private static string Resolve(ResourceTextDescriptor descriptor, Func<string, string?>? getText)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return LocalizedFallbackTextResolver.Resolve(
            descriptor.ResourceKey,
            descriptor.FallbackText,
            getText);
    }
}

public sealed record SisterBackstagePaneTextSpec(
    string RecentEmptyText,
    string TemplateHeading,
    string TemplateTileCaption,
    string TemplateFooterText,
    string OptionsDescription,
    string? OptionsEditText = null)
{
    public SisterBackstageAccountPaneTextSpec Account { get; init; } =
        SisterBackstageAccountPaneTextSpec.NeutralEnglish;

    public SisterBackstageExportPaneTextSpec Export { get; init; } =
        SisterBackstageExportPaneTextSpec.NeutralEnglish;

    public static SisterBackstagePaneTextSpec FromDescriptor(
        SisterBackstagePaneTextDescriptor descriptor,
        Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new SisterBackstagePaneTextSpec(
            Resolve(descriptor.RecentEmptyText, getText),
            Resolve(descriptor.TemplateHeading, getText),
            Resolve(descriptor.TemplateTileCaption, getText),
            Resolve(descriptor.TemplateFooterText, getText),
            Resolve(descriptor.OptionsDescription, getText),
            descriptor.OptionsEditText is null ? null : Resolve(descriptor.OptionsEditText, getText))
        {
            Export = SisterBackstageExportPaneTextSpec.FromDescriptor(descriptor.Export, getText)
        };
    }

    private static string Resolve(ResourceTextDescriptor descriptor, Func<string, string?>? getText)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return LocalizedFallbackTextResolver.Resolve(
            descriptor.ResourceKey,
            descriptor.FallbackText,
            getText);
    }
}

/// <summary>
/// Builds renderer-neutral Backstage pane specs for sister apps from app text and live callbacks.
/// </summary>
public sealed class SisterBackstagePaneSpecPlanner
{
    private readonly SisterBackstagePaneTextSpec _text;

    public SisterBackstagePaneSpecPlanner(SisterBackstagePaneTextSpec text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    public SisterBackstagePaneTextSpec Text => _text;

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

public static class BackstageApplicationOptionsPanePlanner
{
    public static BackstageOptionsPaneSpec Build(
        string description,
        IApplicationOptionsSummarySource options,
        string dataFolder,
        string? editText = null,
        Action? edit = null)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataFolder);

        var summary = ApplicationOptionsSummaryPlanner.Build(options, dataFolder);
        return new BackstageOptionsPaneSpec(
            description,
            summary.Rows.Select(row => new BackstageFieldRow(row.Label, row.Value)).ToArray(),
            editText,
            edit);
    }
}
