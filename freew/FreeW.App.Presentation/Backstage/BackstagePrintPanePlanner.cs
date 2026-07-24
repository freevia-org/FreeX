using System.Globalization;
using Free.Shared.Shell;
using FreeW.App.Presentation.DocumentView;
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
        "backstage-print-preview-fidelity",
        "page-composition-print-layout",
        "f2-hf-basic",
        "f2-footnotes",
        "f2-section-landscape",
    ];

    private static readonly string[] PdfExportFixtureScenarioIds =
    [
        "backstage-pdf-export-fidelity",
        "page-composition-print-layout",
        "f2-hf-basic",
        "f2-footnotes",
        "f2-section-landscape",
    ];

    public static BackstagePrintPanePlan Build(
        string displayName,
        PageSettings page,
        BackstageDirectPrintCapability? directPrintCapability = null,
        FreeWVisualEvidenceNormalizedSummary? visualEvidenceSummary = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        var directPrint = directPrintCapability ?? BackstageDirectPrintCapability.Deferred();
        var printPreviewReadiness = BuildEvidenceReadiness(
            BackstagePrintEvidenceKind.PrintPreviewFidelity,
            visualEvidenceSummary);
        var pdfExportReadiness = BuildEvidenceReadiness(
            BackstagePrintEvidenceKind.PdfExportFidelity,
            visualEvidenceSummary);

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
        BackstagePrintEvidenceKind kind,
        FreeWVisualEvidenceNormalizedSummary? visualEvidenceSummary)
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

        if (visualEvidenceSummary is null)
        {
            return new BackstagePrintEvidenceReadiness(
                BackstagePrintEvidenceStatus.FixtureReady,
                fixtureDescription,
                []);
        }

        var failures = new List<string>();
        foreach (var requirement in requirements)
        {
            var scenario = visualEvidenceSummary.Scenarios.SingleOrDefault(candidate =>
                string.Equals(candidate.HostId, requirement.HostId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.ScenarioId, requirement.ScenarioId, StringComparison.OrdinalIgnoreCase));

            if (scenario is null)
            {
                failures.Add(
                    $"{requirement.HostId}/{requirement.ScenarioId}: missing normalized scenario row");
                continue;
            }

            if (scenario.TrustedOutputs < requirement.MinimumExpectedOutputs)
            {
                failures.Add(
                    $"{requirement.HostId}/{requirement.ScenarioId}: expected at least {requirement.MinimumExpectedOutputs.ToString(CultureInfo.InvariantCulture)} trusted output(s), found {scenario.TrustedOutputs.ToString(CultureInfo.InvariantCulture)}");
            }

            failures.AddRange(scenario.Trust.Failures.Select(failure =>
                $"{requirement.HostId}/{requirement.ScenarioId}: {failure}"));
        }

        var scenarioIds = requirements
            .Select(requirement => requirement.ScenarioId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        failures.AddRange(visualEvidenceSummary.Trust.Failures
            .Where(failure => scenarioIds.Any(scenarioId =>
                failure.Contains(scenarioId, StringComparison.OrdinalIgnoreCase)))
            .Select(failure => "summary: " + failure));

        if (failures.Count == 0)
        {
            return new BackstagePrintEvidenceReadiness(
                BackstagePrintEvidenceStatus.HostBacked,
                "Real WPF and Avalonia captures satisfy the visual summary contract for " +
                string.Join(", ", scenarioIds) + ".",
                []);
        }

        return new BackstagePrintEvidenceReadiness(
            BackstagePrintEvidenceStatus.Deferred,
            "Real WPF/Avalonia captures are not ready: " + string.Join("; ", failures.Distinct(StringComparer.OrdinalIgnoreCase)),
            failures.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static IReadOnlyList<BackstagePrintEvidenceRequirement> BuildEvidenceRequirements(
        BackstagePrintEvidenceKind kind)
    {
        IReadOnlyList<string> scenarioIds = kind switch
        {
            BackstagePrintEvidenceKind.PrintPreviewFidelity => [PrintPreviewFixtureScenarioIds[0]],
            BackstagePrintEvidenceKind.PdfExportFidelity => [PdfExportFixtureScenarioIds[0]],
            _ => []
        };

        if (scenarioIds.Count == 0)
            return [];

        return FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios
            .Where(expected => scenarioIds.Contains(expected.ScenarioId, StringComparer.OrdinalIgnoreCase))
            .OrderBy(expected => expected.HostId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(expected => expected.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .Select(expected => new BackstagePrintEvidenceRequirement(
                expected.HostId,
                expected.ScenarioId,
                expected.MinimumExpectedOutputs))
            .ToArray();
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
