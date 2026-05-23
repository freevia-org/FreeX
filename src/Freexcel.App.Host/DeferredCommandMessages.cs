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
            "Freexcel loads and saves PivotTable and pivot caches metadata, including external/OLAP cache source metadata, creates and refreshes worksheet-range PivotTables from same-sheet or cross-sheet sources, supports Field List layout editing, GETPIVOTDATA, PivotChart field-button filtering and chart-type changes, Insert Slicer/Timeline authoring, slicer/timeline filtering, and preserves native PivotTable package parts where possible. Exact full-gallery PivotStyle theme semantics, full PivotChart Tools layout/design editing, and external/OLAP/data-model refresh or execution remain partial or excluded.");

    public static DeferredCommandMessage AutoCorrectOptions() =>
        new(
            "AutoCorrect Options",
            "AutoCorrect replacement dictionaries are not implemented in Freexcel. Proofing options shown here are informational until a local spelling and replacement dictionary model exists.");

    public static DeferredCommandMessage EditingLanguages() =>
        new(
            "Editing Languages",
            "Editing language installation and Office language packs are not implemented in Freexcel. Freexcel uses the local app culture and workbook content as-is.");

    public static DeferredCommandMessage RibbonCustomizationImportExport() =>
        new(
            "Ribbon Customization",
            "Custom ribbon import/export is not implemented in Freexcel. Custom Ribbon UI package parts are retained as unsupported XLSX metadata where safe, but Freexcel does not run or edit them.");

    public static DeferredCommandMessage QuickAccessToolbarReset() =>
        new(
            "Quick Access Toolbar",
            "Quick Access Toolbar customization is not persisted in Freexcel yet, so there is no custom toolbar state to reset.");

    public static DeferredCommandMessage OfficeAddIns() =>
        new(
            "Office Add-ins",
            "Office add-ins are excluded from Freexcel. Add-in package metadata may be detected and retained, but add-ins are not installed, loaded, or executed.");

    public static DeferredCommandMessage TrustCenterSettings() =>
        new(
            "Trust Center",
            "Trust Center policy settings are informational in Freexcel because Freexcel does not execute VBA macros, Office add-ins, ActiveX controls, Power Query, or external data-model refresh.");

    public static DeferredCommandMessage UnsupportedXlsxFeatureSaveWarning(XlsxFeatureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var featureList = string.Join(", ",
            FormatUnsupportedXlsxFeatureList(report));

        return new(
            "Unsupported XLSX Features",
            "This workbook contains features Freexcel retains as opaque package parts, but does not run, render, author, or deeply edit: " +
            $"{featureList}.{DigitalSignatureWarning(report)}\n\nContinue saving?");
    }

    public static DeferredCommandMessage UnsupportedXlsxFeatureOpenWarning(XlsxFeatureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var featureList = string.Join(", ",
            FormatUnsupportedXlsxFeatureList(report));

        return new(
            "Unsupported XLSX Features Detected",
            "Freexcel opened this workbook, but it contains unsupported or excluded XLSX features: " +
            $"{featureList}. These features are retained as opaque package parts where safe, but will not be executed, refreshed, rendered, or edited by Freexcel." +
            DigitalSignatureWarning(report));
    }

    public static string FormatUnsupportedXlsxFeatureKind(XlsxUnsupportedFeatureKind kind) => kind switch
    {
        XlsxUnsupportedFeatureKind.Macros => "VBA macros (excluded)",
        XlsxUnsupportedFeatureKind.Charts => "XLSX chart package parts",
        XlsxUnsupportedFeatureKind.EmbeddedObjects => "embedded objects",
        XlsxUnsupportedFeatureKind.CustomXmlParts => "custom XML parts",
        XlsxUnsupportedFeatureKind.ConditionalFormats => "unsupported conditional formatting",
        XlsxUnsupportedFeatureKind.DrawingObjects => "drawing objects",
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
        XlsxUnsupportedFeatureKind.UnsupportedSheetTypes => "chart sheets / dialog sheets / macro sheets",
        _ => kind.ToString()
    };

    private static IEnumerable<string> FormatUnsupportedXlsxFeatureList(XlsxFeatureReport report) =>
        report.Features
            .Select(f => FormatUnsupportedXlsxFeatureKind(f.Kind))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

    private static string DigitalSignatureWarning(XlsxFeatureReport report) =>
        report.Features.Any(feature => feature.Kind == XlsxUnsupportedFeatureKind.DigitalSignatures)
            ? " Digital signatures may no longer validate after workbook edits."
            : string.Empty;
}

public sealed record DeferredCommandMessage(string Title, string Body);
