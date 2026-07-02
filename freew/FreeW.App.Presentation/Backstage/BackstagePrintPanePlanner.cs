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
    IReadOnlyList<string> FixtureScenarioIds);

public enum BackstagePrintEvidenceKind
{
    PrintPreviewFidelity,
    PdfExportFidelity,
    NativePrint
}

public enum BackstagePrintEvidenceStatus
{
    FixtureReady,
    Deferred
}

public static class BackstagePrintPanePlanner
{
    public static BackstagePrintPanePlan Build(string displayName, PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new BackstagePrintPanePlan(
            "Print this document using the current page layout and printer settings.",
            [
                new("Document", Normalize(displayName, "Untitled")),
                new("Paper", FormatPaper(page)),
                new("Orientation", page.Landscape ? "Landscape" : "Portrait"),
                new("Margins", FormatMargins(page)),
                new("Columns", FormatColumns(page)),
            ],
            [
                new("Print",
                [
                    new(BackstagePrintActionKind.Print, "Print", "Choose a printer and send the document to print."),
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
                    BackstagePrintEvidenceStatus.FixtureReady,
                    "Print Preview uses the paginated print-layout renderer; retained evidence must include paired WPF/Avalonia visual summary rows for the named scenarios.",
                    [
                        "backstage-print-preview-fidelity",
                        "page-composition-print-layout",
                        "f2-hf-basic",
                        "f2-footnotes",
                        "f2-section-landscape",
                    ]),
                new(
                    BackstagePrintEvidenceKind.PdfExportFidelity,
                    BackstagePrintEvidenceStatus.FixtureReady,
                    "PDF export evidence is anchored by rasterized fixed-layout output scenarios; missing paired WPF/Avalonia rows fail the visual summary contract.",
                    [
                        "backstage-pdf-export-fidelity",
                        "page-composition-print-layout",
                        "f2-hf-basic",
                        "f2-footnotes",
                        "f2-section-landscape",
                    ]),
                new(
                    BackstagePrintEvidenceKind.NativePrint,
                    BackstagePrintEvidenceStatus.Deferred,
                    "Direct native printer selection remains host-specific; Avalonia keeps Print Preview and PDF export active while disabling direct print.",
                    []),
            ]);
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
