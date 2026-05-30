using FreeX.Core.IO;

namespace FreeX.App.Host;

public static class DeferredCommandMessages
{
    public static DeferredCommandMessage WorkbookTheme(string commandName) =>
        new(
            commandName,
            UiText.Format("DeferredCommand_WorkbookTheme_Body", commandName));

    public static DeferredCommandMessage MultiWindow(string commandName) =>
        new(
            commandName,
            UiText.Format("DeferredCommand_MultiWindow_Body", commandName));

    public static DeferredCommandMessage OnlineTemplatesExcluded() =>
        new(
            UiText.Get("DeferredCommand_OnlineTemplates_Title"),
            UiText.Get("DeferredCommand_OnlineTemplates_Body"));

    public static DeferredCommandMessage LocalAccountInfo() =>
        new(
            UiText.Get("DeferredCommand_LocalAccount_Title"),
            UiText.Get("DeferredCommand_LocalAccount_Body"));

    public static DeferredCommandMessage PivotTableModelFirst() =>
        new(
            UiText.Get("DeferredCommand_PivotTable_Title"),
            UiText.Get("DeferredCommand_PivotTable_Body"));

    public static DeferredCommandMessage AutoCorrectOptions() =>
        new(
            UiText.Get("DeferredCommand_AutoCorrectOptions_Title"),
            UiText.Get("DeferredCommand_AutoCorrectOptions_Body"));

    public static DeferredCommandMessage EditingLanguages() =>
        new(
            UiText.Get("DeferredCommand_EditingLanguages_Title"),
            UiText.Get("DeferredCommand_EditingLanguages_Body"));

    public static DeferredCommandMessage RibbonCustomizationImportExport() =>
        new(
            UiText.Get("DeferredCommand_RibbonCustomization_Title"),
            UiText.Get("DeferredCommand_RibbonCustomization_Body"));

    public static DeferredCommandMessage QuickAccessToolbarReset() =>
        new(
            UiText.Get("DeferredCommand_QuickAccessToolbar_Title"),
            UiText.Get("DeferredCommand_QuickAccessToolbar_Body"));

    public static DeferredCommandMessage OfficeAddIns() =>
        new(
            UiText.Get("DeferredCommand_OfficeAddIns_Title"),
            UiText.Get("DeferredCommand_OfficeAddIns_Body"));

    public static DeferredCommandMessage TrustCenterSettings() =>
        new(
            UiText.Get("DeferredCommand_TrustCenter_Title"),
            UiText.Get("DeferredCommand_TrustCenter_Body"));

    public static DeferredCommandMessage UnsupportedXlsxFeatureSaveWarning(XlsxFeatureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var featureList = string.Join(", ",
            FormatUnsupportedXlsxFeatureList(report));

        return new(
            UiText.Get("DeferredCommand_UnsupportedXlsxFeatureSaveWarning_Title"),
            UiText.Format(
                "DeferredCommand_UnsupportedXlsxFeatureSaveWarning_Body",
                featureList,
                DigitalSignatureWarning(report)));
    }

    public static DeferredCommandMessage UnsupportedXlsxFeatureOpenWarning(XlsxFeatureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var featureList = string.Join(", ",
            FormatUnsupportedXlsxFeatureList(report));

        return new(
            UiText.Get("DeferredCommand_UnsupportedXlsxFeatureOpenWarning_Title"),
            UiText.Format(
                "DeferredCommand_UnsupportedXlsxFeatureOpenWarning_Body",
                featureList,
                DigitalSignatureWarning(report)));
    }

    public static string FormatUnsupportedXlsxFeatureKind(XlsxUnsupportedFeatureKind kind) => kind switch
    {
        XlsxUnsupportedFeatureKind.Macros => UiText.Get("UnsupportedXlsxFeatureKind_Macros"),
        XlsxUnsupportedFeatureKind.Charts => UiText.Get("UnsupportedXlsxFeatureKind_Charts"),
        XlsxUnsupportedFeatureKind.EmbeddedObjects => UiText.Get("UnsupportedXlsxFeatureKind_EmbeddedObjects"),
        XlsxUnsupportedFeatureKind.CustomXmlParts => UiText.Get("UnsupportedXlsxFeatureKind_CustomXmlParts"),
        XlsxUnsupportedFeatureKind.ConditionalFormats => UiText.Get("UnsupportedXlsxFeatureKind_ConditionalFormats"),
        XlsxUnsupportedFeatureKind.DrawingObjects => UiText.Get("UnsupportedXlsxFeatureKind_DrawingObjects"),
        XlsxUnsupportedFeatureKind.PowerQuery => UiText.Get("UnsupportedXlsxFeatureKind_PowerQuery"),
        XlsxUnsupportedFeatureKind.DataModel => UiText.Get("UnsupportedXlsxFeatureKind_DataModel"),
        XlsxUnsupportedFeatureKind.LinkedDataTypes => UiText.Get("UnsupportedXlsxFeatureKind_LinkedDataTypes"),
        XlsxUnsupportedFeatureKind.ThreadedComments => UiText.Get("UnsupportedXlsxFeatureKind_ThreadedComments"),
        XlsxUnsupportedFeatureKind.TrackChanges => UiText.Get("UnsupportedXlsxFeatureKind_TrackChanges"),
        XlsxUnsupportedFeatureKind.FormControls => UiText.Get("UnsupportedXlsxFeatureKind_FormControls"),
        XlsxUnsupportedFeatureKind.DigitalSignatures => UiText.Get("UnsupportedXlsxFeatureKind_DigitalSignatures"),
        XlsxUnsupportedFeatureKind.CustomRibbonUi => UiText.Get("UnsupportedXlsxFeatureKind_CustomRibbonUi"),
        XlsxUnsupportedFeatureKind.OfficeAddIns => UiText.Get("UnsupportedXlsxFeatureKind_OfficeAddIns"),
        XlsxUnsupportedFeatureKind.LiveWebQueries => UiText.Get("UnsupportedXlsxFeatureKind_LiveWebQueries"),
        XlsxUnsupportedFeatureKind.SensitivityLabels => UiText.Get("UnsupportedXlsxFeatureKind_SensitivityLabels"),
        XlsxUnsupportedFeatureKind.SmartArtDiagrams => UiText.Get("UnsupportedXlsxFeatureKind_SmartArtDiagrams"),
        XlsxUnsupportedFeatureKind.UnsupportedSheetTypes => UiText.Get("UnsupportedXlsxFeatureKind_UnsupportedSheetTypes"),
        _ => kind.ToString()
    };

    private static IEnumerable<string> FormatUnsupportedXlsxFeatureList(XlsxFeatureReport report) =>
        report.Features
            .Select(f => FormatUnsupportedXlsxFeatureKind(f.Kind))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

    private static string DigitalSignatureWarning(XlsxFeatureReport report) =>
        report.Features.Any(feature => feature.Kind == XlsxUnsupportedFeatureKind.DigitalSignatures)
            ? UiText.Get("DeferredCommand_UnsupportedXlsxFeature_DigitalSignatureWarningSuffix")
            : string.Empty;
}

public sealed record DeferredCommandMessage(string Title, string Body);
