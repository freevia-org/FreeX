using System.Globalization;
using Free.Shared.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Backstage;

public sealed record BackstagePrintPanePlan(
    string Description,
    IReadOnlyList<BackstageFieldRow> Fields,
    IReadOnlyList<BackstagePrintActionGroup> Groups,
    IReadOnlyList<BackstagePrintEvidenceRow> Evidence);

public sealed record BackstagePrintActionGroup(
    string Heading,
    IReadOnlyList<BackstagePrintActionRow> Actions);

public sealed record BackstagePrintActionRow(
    BackstagePrintActionKind Kind,
    string Label,
    string Description);

public enum BackstagePrintActionKind
{
    Print,
    PrintPreview
}

public sealed record BackstagePrintEvidenceRow(
    BackstagePrintEvidenceKind Kind,
    BackstagePrintEvidenceStatus Status,
    string Description,
    IReadOnlyList<string> FixtureScenarioIds,
    IReadOnlyList<BackstagePrintEvidenceRequirement> Requirements);

public sealed record BackstagePrintEvidenceRequirement(
    string HostId,
    string ScenarioId,
    int MinimumExpectedOutputs);

public sealed record BackstagePrintEvidenceReadiness(
    BackstagePrintEvidenceStatus Status,
    string Description,
    IReadOnlyList<string> Failures);

public enum BackstagePrintEvidenceKind
{
    PrintPreviewFidelity,
    PdfExportFidelity,
    NativePrint
}

public enum BackstagePrintEvidenceStatus
{
    FixtureReady,
    HostBacked,
    Deferred
}

public sealed record BackstageDirectPrintCapability(
    BackstagePrintEvidenceStatus EvidenceStatus,
    string FieldValue,
    string ActionDescription,
    string EvidenceDescription,
    string? DeferredNote)
{
    public bool IsAvailable => EvidenceStatus == BackstagePrintEvidenceStatus.HostBacked;

    public static BackstageDirectPrintCapability NativeDialogAvailable(
        string evidenceDescription = "Direct native printer selection is backed by this host.") =>
        new(
            BackstagePrintEvidenceStatus.HostBacked,
            "Available - operating-system printer dialog",
            "Choose a printer and send the document to print.",
            Normalize(evidenceDescription, "Direct native printer selection is backed by this host."),
            DeferredNote: null);

    public static BackstageDirectPrintCapability PlatformPrinterAvailable(
        string evidenceDescription = "Direct printer submission is backed by this host.") =>
        new(
            BackstagePrintEvidenceStatus.HostBacked,
            "Available - platform printer submission",
            "Choose a printer and submit the document to the platform printer service.",
            Normalize(evidenceDescription, "Direct printer submission is backed by this host."),
            DeferredNote: null);

    public static BackstageDirectPrintCapability Deferred(
        string reason = "Direct native printer selection remains host-specific; use Print Preview or Create PDF for OS printing.") =>
        CreateDeferred(reason, BackstageViewTextResources.DirectPrintDeferredNote);

    public static BackstageDirectPrintCapability Deferred(string reason, string deferredNote) =>
        CreateDeferred(reason, deferredNote);

    private static BackstageDirectPrintCapability CreateDeferred(string reason, string deferredNote)
    {
        var normalized = Normalize(
            reason,
            "Direct native printer selection remains host-specific; use Print Preview or Create PDF for OS printing.");
        return new(
            BackstagePrintEvidenceStatus.Deferred,
            "Deferred - " + normalized,
            normalized,
            normalized,
            Normalize(deferredNote, BackstageViewTextResources.DirectPrintDeferredNote));
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public static class BackstagePrintPanePlanner
{
    private static readonly string[] PrintPreviewFixtureScenarioIds =
    [
        BackstagePrintEvidenceRequirementCatalog.PrintPreviewScenarioId,
        "page-composition-print-layout",
        "f2-hf-basic",
        "f2-footnotes",
        "f2-section-landscape",
    ];

    private static readonly string[] PdfExportFixtureScenarioIds =
    [
        BackstagePrintEvidenceRequirementCatalog.PdfExportScenarioId,
        "page-composition-print-layout",
        "f2-hf-basic",
        "f2-footnotes",
        "f2-section-landscape",
    ];

    public static BackstagePrintPanePlan Build(
        string displayName,
        PageSettings page,
        BackstageDirectPrintCapability? directPrintCapability = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        var directPrint = directPrintCapability ?? BackstageDirectPrintCapability.Deferred();
        var printPreviewReadiness = BuildEvidenceReadiness(
            BackstagePrintEvidenceKind.PrintPreviewFidelity);
        var pdfExportReadiness = BuildEvidenceReadiness(
            BackstagePrintEvidenceKind.PdfExportFidelity);

        return new BackstagePrintPanePlan(
            "Print this document using the current page layout and printer settings.",
            [
                new("Document", Normalize(displayName, "Untitled")),
                new("Paper", FormatPaper(page)),
                new("Orientation", page.Landscape ? "Landscape" : "Portrait"),
                new("Margins", FormatMargins(page)),
                new("Columns", FormatColumns(page)),
                new("Direct print", directPrint.FieldValue),
            ],
            [
                new("Print",
                [
                    new(BackstagePrintActionKind.Print, "Print", directPrint.ActionDescription),
                    new(BackstagePrintActionKind.PrintPreview, "Print Preview", "Preview paginated pages before printing."),
                ]),
                new("Settings",
                [
                    new(BackstagePrintActionKind.PrintPreview, "Preview Current Layout", "Review pages with headers, footers, margins, columns, and page breaks applied."),
                ]),
            ],
            [
                new(
                    BackstagePrintEvidenceKind.PrintPreviewFidelity,
                    printPreviewReadiness.Status,
                    printPreviewReadiness.Description,
                    PrintPreviewFixtureScenarioIds,
                    BuildEvidenceRequirements(BackstagePrintEvidenceKind.PrintPreviewFidelity)),
                new(
                    BackstagePrintEvidenceKind.PdfExportFidelity,
                    pdfExportReadiness.Status,
                    pdfExportReadiness.Description,
                    PdfExportFixtureScenarioIds,
                    BuildEvidenceRequirements(BackstagePrintEvidenceKind.PdfExportFidelity)),
                new(
                    BackstagePrintEvidenceKind.NativePrint,
                    directPrint.EvidenceStatus,
                    directPrint.EvidenceDescription,
                    [],
                    []),
            ]);
    }

    public static BackstagePrintEvidenceReadiness BuildEvidenceReadiness(
        BackstagePrintEvidenceKind kind)
    {
        var fixtureDescription = kind switch
        {
            BackstagePrintEvidenceKind.PrintPreviewFidelity =>
                "Print Preview uses the paginated print-layout renderer; retained evidence must satisfy the host/scenario rows required by the visual summary contract.",
            BackstagePrintEvidenceKind.PdfExportFidelity =>
                "PDF export evidence is anchored by rasterized fixed-layout output scenarios; retained evidence must satisfy the host/scenario rows required by the visual summary contract.",
            _ => "No visual evidence readiness contract is required for this print action."
        };

        var requirements = BuildEvidenceRequirements(kind);
        if (requirements.Count == 0)
        {
            return new BackstagePrintEvidenceReadiness(
                BackstagePrintEvidenceStatus.Deferred,
                fixtureDescription,
                []);
        }

        return new BackstagePrintEvidenceReadiness(
            BackstagePrintEvidenceStatus.FixtureReady,
            fixtureDescription,
            []);
    }

    public static IReadOnlyList<BackstagePrintEvidenceRequirement> BuildEvidenceRequirements(
        BackstagePrintEvidenceKind kind)
    {
        return BackstagePrintEvidenceRequirementCatalog.Build(kind);
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string FormatPaper(PageSettings page) =>
        string.Create(CultureInfo.InvariantCulture, $"{Inches(page.WidthPt):0.##}\" x {Inches(page.HeightPt):0.##}\"");

    private static string FormatMargins(PageSettings page)
    {
        var suffix = page.MirrorMargins ? " (mirror margins)" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Top {Inches(page.MarginTopPt):0.##}\", Bottom {Inches(page.MarginBottomPt):0.##}\", Left {Inches(page.MarginLeftPt):0.##}\", Right {Inches(page.MarginRightPt):0.##}\"{suffix}");
    }

    private static string FormatColumns(PageSettings page) =>
        page.ColumnCount <= 1
            ? "One"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{page.ColumnCount} (spacing {Inches(page.ColumnSpacingPt):0.##}\")");

    private static double Inches(double points) => points / 72.0;
}
