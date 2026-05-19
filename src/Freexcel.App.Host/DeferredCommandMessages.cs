using Freexcel.Core.IO;

namespace Freexcel.App.Host;

public static class DeferredCommandMessages
{
    public static DeferredCommandMessage WorkbookTheme(string commandName) =>
        new(
            commandName,
            $"{commandName} is deferred until Freexcel has a workbook theme model. It is tracked as a documented parity gap, not a silent partial implementation.");

    public static DeferredCommandMessage MultiWindow(string commandName) =>
        new(
            commandName,
            $"{commandName} is deferred until Freexcel has multi-window workbook hosting. It is tracked as a documented parity gap, not a silent partial implementation.");

    public static DeferredCommandMessage ShareExcluded() =>
        new(
            "Share Workbook",
            "Microsoft 365 Share and cloud co-authoring are excluded from Freexcel. Save the workbook and share the file through your normal file system or source-control workflow.");

    public static DeferredCommandMessage OnlineTemplatesExcluded() =>
        new(
            "Online Templates",
            "Online template discovery depends on an external Microsoft template service and is excluded from Freexcel. Create a blank workbook or open a local template file instead.");

    public static DeferredCommandMessage LocalAccountInfo() =>
        new(
            "Account",
            "Microsoft account integration is not implemented in Freexcel. Workbooks are local files; use Options for local app settings and your normal file-system workflow for identity, storage, and sharing.");

    public static DeferredCommandMessage PivotTableModelFirst() =>
        new(
            "PivotTable",
            "Freexcel loads and saves PivotTable and pivot caches metadata, creates and refreshes worksheet-range PivotTables from same-sheet or cross-sheet sources, supports Field List layout editing, GETPIVOTDATA, PivotChart field-button filtering and chart-type changes, Insert Slicer/Timeline authoring, slicer/timeline filtering, and preserves native PivotTable package parts where possible. Exact full-gallery PivotStyle theme semantics, full PivotChart Tools layout/design editing, and external/OLAP/data-model pivot cache behavior remain partial or excluded.");

    public static DeferredCommandMessage UnsupportedXlsxFeatureSaveWarning(XlsxFeatureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var featureList = string.Join(", ",
            FormatUnsupportedXlsxFeatureList(report));

        return new(
            "Unsupported XLSX Features",
            "This workbook contains features Freexcel does not preserve yet. " +
            $"Saving to .xlsx may remove: {featureList}.\n\nContinue saving?");
    }

    public static DeferredCommandMessage UnsupportedXlsxFeatureOpenWarning(XlsxFeatureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var featureList = string.Join(", ",
            FormatUnsupportedXlsxFeatureList(report));

        return new(
            "Unsupported XLSX Features Detected",
            "Freexcel opened this workbook, but it contains unsupported or excluded XLSX features: " +
            $"{featureList}. These features may be removed if you save the workbook from Freexcel.");
    }

    public static string FormatUnsupportedXlsxFeatureKind(XlsxUnsupportedFeatureKind kind) => kind switch
    {
        XlsxUnsupportedFeatureKind.Macros => "VBA macros (excluded)",
        XlsxUnsupportedFeatureKind.PivotTables => "PivotTables/pivot caches",
        XlsxUnsupportedFeatureKind.Charts => "XLSX chart package parts",
        XlsxUnsupportedFeatureKind.Slicers => "slicers",
        XlsxUnsupportedFeatureKind.Timelines => "timelines",
        XlsxUnsupportedFeatureKind.ExternalLinks => "external links",
        XlsxUnsupportedFeatureKind.EmbeddedObjects => "embedded objects",
        XlsxUnsupportedFeatureKind.CustomXmlParts => "custom XML parts",
        XlsxUnsupportedFeatureKind.ConditionalFormats => "unsupported conditional formatting",
        XlsxUnsupportedFeatureKind.DrawingObjects => "drawing objects",
        XlsxUnsupportedFeatureKind.Sparklines => "sparklines",
        XlsxUnsupportedFeatureKind.PowerQuery => "Power Query queries (excluded)",
        XlsxUnsupportedFeatureKind.DataModel => "Data Model / Power Pivot (excluded)",
        XlsxUnsupportedFeatureKind.LinkedDataTypes => "Microsoft linked data types (excluded)",
        XlsxUnsupportedFeatureKind.ThreadedComments => "threaded comments",
        XlsxUnsupportedFeatureKind.TrackChanges => "track changes / revision history",
        XlsxUnsupportedFeatureKind.FormControls => "form controls / ActiveX controls",
        XlsxUnsupportedFeatureKind.DigitalSignatures => "digital signatures",
        XlsxUnsupportedFeatureKind.CustomRibbonUi => "custom ribbon UI",
        XlsxUnsupportedFeatureKind.OfficeAddIns => "Office add-ins",
        XlsxUnsupportedFeatureKind.LiveWebQueries => "live web queries / web publishing",
        XlsxUnsupportedFeatureKind.SensitivityLabels => "sensitivity labels / IRM metadata",
        XlsxUnsupportedFeatureKind.SmartArtDiagrams => "SmartArt diagrams",
        XlsxUnsupportedFeatureKind.PrinterSettings => "printer settings",
        XlsxUnsupportedFeatureKind.StructuredTables => "structured Excel tables",
        XlsxUnsupportedFeatureKind.UnsupportedSheetTypes => "chart sheets / dialog sheets / macro sheets",
        _ => kind.ToString()
    };

    private static IEnumerable<string> FormatUnsupportedXlsxFeatureList(XlsxFeatureReport report) =>
        report.Features
            .Select(f => FormatUnsupportedXlsxFeatureKind(f.Kind))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);
}

public sealed record DeferredCommandMessage(string Title, string Body);
