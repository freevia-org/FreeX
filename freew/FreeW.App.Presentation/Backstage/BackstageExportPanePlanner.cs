using Free.Shared.Shell;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Backstage;

/// <summary>
/// Shared export-pane contract consumed by both FreeW hosts. The action sequence is part of the
/// WPF authority surface, so host renderers must not rebuild it independently.
/// </summary>
public static class BackstageExportPanePlanner
{
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
