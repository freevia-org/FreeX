using Free.Shared.Shell;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Backstage;

/// <summary>
/// Shared export-pane contract consumed by both FreeW hosts. The action sequence is part of the
/// WPF authority surface, so host renderers must not rebuild it independently.
/// </summary>
public static class BackstageExportPanePlanner
{
    public static BackstageExportPaneVisualMetrics VisualMetrics { get; } =
        new(
            PaneMaxWidth: 720,
            HeadingFontSize: 26,
            HeadingBottomMargin: new(0, 0, 0, 18),
            DescriptionFontSize: 12,
            DescriptionBottomMargin: new(0, 0, 0, 16),
            SectionHeaderFontSize: 15,
            SectionHeaderMargin: new(0, 16, 0, 6),
            ActionFontSize: 14,
            DescriptionTextFontSize: 11,
            ActionRowMargin: new(0, 0, 0, 10),
            ActionDescriptionMargin: new(0, 2, 0, 0));

    public static IReadOnlyList<BackstageActionRow> BuildFixedLayoutActions(
        IReadOnlyList<DocumentFormatCapabilityRow> capabilities,
        Action exportPdf,
        Action? exportXps,
        BackstageExportPaneSurfaceText text)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(exportPdf);
        ArgumentNullException.ThrowIfNull(text);

        var pdf = capabilities.Single(row =>
            string.Equals(row.PrimaryExtension, ".pdf", StringComparison.OrdinalIgnoreCase));
        var actions = new List<BackstageActionRow>
        {
            new(
                exportXps is null ? text.PdfOnlyActionLabel : text.PdfActionLabel,
                pdf.Description,
                exportPdf),
        };

        if (exportXps is not null &&
            !string.IsNullOrWhiteSpace(text.XpsActionLabel) &&
            !string.IsNullOrWhiteSpace(text.XpsActionDescription))
        {
            var xps = capabilities.Single(row =>
                string.Equals(row.PrimaryExtension, ".xps", StringComparison.OrdinalIgnoreCase));
            actions.Add(new BackstageActionRow(text.XpsActionLabel, xps.Description, exportXps));
        }

        return actions;
    }
}

public readonly record struct BackstageExportPaneVisualMetrics(
    double PaneMaxWidth,
    double HeadingFontSize,
    BackstageThickness HeadingBottomMargin,
    double DescriptionFontSize,
    BackstageThickness DescriptionBottomMargin,
    double SectionHeaderFontSize,
    BackstageThickness SectionHeaderMargin,
    double ActionFontSize,
    double DescriptionTextFontSize,
    BackstageThickness ActionRowMargin,
    BackstageThickness ActionDescriptionMargin);
