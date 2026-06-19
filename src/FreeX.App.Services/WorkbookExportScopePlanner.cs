using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// A single selectable export scope for the backstage Export pane (whether it can be chosen and why).
/// Labels are NOT localized here — <see cref="Scope"/> drives the rendering shell's localized caption.
/// </summary>
public sealed record WorkbookExportScopeOption(
    WorkbookExportPrintScope Scope,
    bool IsAvailable,
    bool IsDefault);

/// <summary>
/// The set of export scope options the user may pick from, plus the format choices the surface supports.
/// Framework-neutral so the Avalonia/macOS shell only has to render radios + localize.
/// </summary>
public sealed record WorkbookExportScopePlan(
    System.Collections.Generic.IReadOnlyList<WorkbookExportScopeOption> Scopes,
    WorkbookExportPrintScope DefaultScope,
    System.Collections.Generic.IReadOnlyList<WorkbookExportPrintOutputKind> SupportedOutputKinds,
    WorkbookExportPrintOutputKind DefaultOutputKind,
    bool CanExport);

/// <summary>
/// Decides which export scopes (selection / active sheet / whole visible workbook) and output formats
/// (PDF, plus XPS only where the surface supports it) are offered, given the live workbook and whether a
/// range is selected. Pure data shaping; the existing <see cref="WorkbookExportPrintPlanner"/> still does
/// the heavy page planning once a scope is chosen.
/// </summary>
public static class WorkbookExportScopePlanner
{
    public static WorkbookExportScopePlan Build(
        Workbook workbook,
        bool hasSelection,
        WorkbookExportPrintSurface? surface = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        surface ??= WorkbookExportPrintSurface.PortablePdf;

        var hasVisibleSheet = HasVisibleWorksheet(workbook);
        var supportedOutputKinds = surface.SupportedOutputKinds;
        var canExport = hasVisibleSheet && supportedOutputKinds.Count > 0;

        // Active sheet is the natural default whenever the workbook can be exported.
        var defaultScope = WorkbookExportPrintScope.ActiveSheet;

        var scopes = new[]
        {
            new WorkbookExportScopeOption(
                WorkbookExportPrintScope.SelectedRange,
                IsAvailable: canExport && hasSelection,
                IsDefault: false),
            new WorkbookExportScopeOption(
                WorkbookExportPrintScope.ActiveSheet,
                IsAvailable: canExport,
                IsDefault: canExport),
            new WorkbookExportScopeOption(
                WorkbookExportPrintScope.VisibleWorkbook,
                IsAvailable: canExport,
                IsDefault: false),
        };

        var defaultOutputKind = supportedOutputKinds.Count > 0
            ? supportedOutputKinds[0]
            : WorkbookExportPrintOutputKind.Pdf;

        return new WorkbookExportScopePlan(
            scopes,
            defaultScope,
            supportedOutputKinds,
            defaultOutputKind,
            canExport);
    }

    private static bool HasVisibleWorksheet(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (!sheet.IsHidden)
                return true;
        }

        return false;
    }
}
