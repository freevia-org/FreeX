using FreeX.Core.IO;

namespace FreeX.App.Presentation.Shell;

public sealed record DeferredCommandMessagePlan(
    DeferredCommandMessageTextPlan Title,
    DeferredCommandMessageTextPlan Body);

public sealed record DeferredCommandMessageTextPlan(
    string? ResourceKey,
    string? LiteralText,
    IReadOnlyList<DeferredCommandMessageArgument> Arguments)
{
    public static DeferredCommandMessageTextPlan Literal(string text) =>
        new(null, text, []);

    public static DeferredCommandMessageTextPlan Resource(
        string resourceKey,
        params DeferredCommandMessageArgument[] arguments) =>
        new(resourceKey, null, arguments);
}

public sealed record DeferredCommandMessageArgument(
    string? LiteralText,
    string? ResourceKey,
    IReadOnlyList<DeferredCommandMessageTextPlan> TextItems,
    bool SortResolvedText)
{
    public static DeferredCommandMessageArgument Literal(string text) =>
        new(text, null, [], SortResolvedText: false);

    public static DeferredCommandMessageArgument Resource(string resourceKey) =>
        new(null, resourceKey, [], SortResolvedText: false);

    public static DeferredCommandMessageArgument TextList(
        IEnumerable<DeferredCommandMessageTextPlan> textItems,
        bool sortResolvedText = false) =>
        new(null, null, textItems.ToList(), sortResolvedText);
}

/// <summary>
/// Shared message-plan catalog for deferred or intentionally unsupported command surfaces.
/// Hosts resolve resources and show dialogs; the selection of title/body keys and message
/// arguments stays renderer-neutral.
/// </summary>
public static class DeferredCommandMessagePlanner
{
    public static DeferredCommandMessagePlan WorkbookTheme(string commandName) =>
        ResourceBodyWithLiteralTitle(
            commandName,
            "DeferredCommand_WorkbookTheme_Body",
            DeferredCommandMessageArgument.Literal(commandName));

    public static DeferredCommandMessagePlan MultiWindow(string commandName) =>
        ResourceBodyWithLiteralTitle(
            commandName,
            "DeferredCommand_MultiWindow_Body",
            DeferredCommandMessageArgument.Literal(commandName));

    public static DeferredCommandMessagePlan OnlineTemplatesExcluded() =>
        ResourceMessage(
            "DeferredCommand_OnlineTemplates_Title",
            "DeferredCommand_OnlineTemplates_Body");

    public static DeferredCommandMessagePlan LocalAccountInfo() =>
        ResourceMessage(
            "DeferredCommand_LocalAccount_Title",
            "DeferredCommand_LocalAccount_Body");

    public static DeferredCommandMessagePlan PivotTableModelFirst() =>
        ResourceMessage(
            "DeferredCommand_PivotTable_Title",
            "DeferredCommand_PivotTable_Body");

    public static DeferredCommandMessagePlan AutoCorrectOptions() =>
        ResourceMessage(
            "DeferredCommand_AutoCorrectOptions_Title",
            "DeferredCommand_AutoCorrectOptions_Body");

    public static DeferredCommandMessagePlan EditingLanguages() =>
        ResourceMessage(
            "DeferredCommand_EditingLanguages_Title",
            "DeferredCommand_EditingLanguages_Body");

    public static DeferredCommandMessagePlan RibbonCustomizationImportExport() =>
        ResourceMessage(
            "DeferredCommand_RibbonCustomization_Title",
            "DeferredCommand_RibbonCustomization_Body");

    public static DeferredCommandMessagePlan OfficeAddIns() =>
        ResourceMessage(
            "DeferredCommand_OfficeAddIns_Title",
            "DeferredCommand_OfficeAddIns_Body");

    public static DeferredCommandMessagePlan TrustCenterSettings() =>
        ResourceMessage(
            "DeferredCommand_TrustCenter_Title",
            "DeferredCommand_TrustCenter_Body");

    public static DeferredCommandMessagePlan UnsupportedXlsxFeatureSaveWarning(XlsxFeatureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new DeferredCommandMessagePlan(
            DeferredCommandMessageTextPlan.Resource("DeferredCommand_UnsupportedXlsxFeatureSaveWarning_Title"),
            DeferredCommandMessageTextPlan.Resource(
                "DeferredCommand_UnsupportedXlsxFeatureSaveWarning_Body",
                UnsupportedFeatureList(report),
                DigitalSignatureWarning(report)));
    }

    public static DeferredCommandMessagePlan UnsupportedXlsxFeatureOpenWarning(XlsxFeatureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new DeferredCommandMessagePlan(
            DeferredCommandMessageTextPlan.Resource("DeferredCommand_UnsupportedXlsxFeatureOpenWarning_Title"),
            DeferredCommandMessageTextPlan.Resource(
                "DeferredCommand_UnsupportedXlsxFeatureOpenWarning_Body",
                UnsupportedFeatureList(report),
                DigitalSignatureWarning(report)));
    }

    public static DeferredCommandMessageTextPlan UnsupportedXlsxFeatureKindText(
        XlsxUnsupportedFeatureKind kind) => kind switch
    {
        XlsxUnsupportedFeatureKind.Macros => ResourceText("UnsupportedXlsxFeatureKind_Macros"),
        XlsxUnsupportedFeatureKind.Charts => ResourceText("UnsupportedXlsxFeatureKind_Charts"),
        XlsxUnsupportedFeatureKind.EmbeddedObjects => ResourceText("UnsupportedXlsxFeatureKind_EmbeddedObjects"),
        XlsxUnsupportedFeatureKind.CustomXmlParts => ResourceText("UnsupportedXlsxFeatureKind_CustomXmlParts"),
        XlsxUnsupportedFeatureKind.ConditionalFormats => ResourceText("UnsupportedXlsxFeatureKind_ConditionalFormats"),
        XlsxUnsupportedFeatureKind.DrawingObjects => ResourceText("UnsupportedXlsxFeatureKind_DrawingObjects"),
        XlsxUnsupportedFeatureKind.PowerQuery => ResourceText("UnsupportedXlsxFeatureKind_PowerQuery"),
        XlsxUnsupportedFeatureKind.DataModel => ResourceText("UnsupportedXlsxFeatureKind_DataModel"),
        XlsxUnsupportedFeatureKind.LinkedDataTypes => ResourceText("UnsupportedXlsxFeatureKind_LinkedDataTypes"),
        XlsxUnsupportedFeatureKind.ThreadedComments => ResourceText("UnsupportedXlsxFeatureKind_ThreadedComments"),
        XlsxUnsupportedFeatureKind.TrackChanges => ResourceText("UnsupportedXlsxFeatureKind_TrackChanges"),
        XlsxUnsupportedFeatureKind.FormControls => ResourceText("UnsupportedXlsxFeatureKind_FormControls"),
        XlsxUnsupportedFeatureKind.DigitalSignatures => ResourceText("UnsupportedXlsxFeatureKind_DigitalSignatures"),
        XlsxUnsupportedFeatureKind.CustomRibbonUi => ResourceText("UnsupportedXlsxFeatureKind_CustomRibbonUi"),
        XlsxUnsupportedFeatureKind.OfficeAddIns => ResourceText("UnsupportedXlsxFeatureKind_OfficeAddIns"),
        XlsxUnsupportedFeatureKind.LiveWebQueries => ResourceText("UnsupportedXlsxFeatureKind_LiveWebQueries"),
        XlsxUnsupportedFeatureKind.SensitivityLabels => ResourceText("UnsupportedXlsxFeatureKind_SensitivityLabels"),
        XlsxUnsupportedFeatureKind.SmartArtDiagrams => ResourceText("UnsupportedXlsxFeatureKind_SmartArtDiagrams"),
        XlsxUnsupportedFeatureKind.UnsupportedSheetTypes => ResourceText("UnsupportedXlsxFeatureKind_UnsupportedSheetTypes"),
        _ => DeferredCommandMessageTextPlan.Literal(kind.ToString())
    };

    private static DeferredCommandMessagePlan ResourceMessage(
        string titleResourceKey,
        string bodyResourceKey) =>
        new(
            DeferredCommandMessageTextPlan.Resource(titleResourceKey),
            DeferredCommandMessageTextPlan.Resource(bodyResourceKey));

    private static DeferredCommandMessagePlan ResourceBodyWithLiteralTitle(
        string title,
        string bodyResourceKey,
        params DeferredCommandMessageArgument[] arguments) =>
        new(
            DeferredCommandMessageTextPlan.Literal(title),
            DeferredCommandMessageTextPlan.Resource(bodyResourceKey, arguments));

    private static DeferredCommandMessageArgument UnsupportedFeatureList(XlsxFeatureReport report) =>
        DeferredCommandMessageArgument.TextList(
            report.Features
                .Select(feature => UnsupportedXlsxFeatureKindText(feature.Kind))
                .Distinct(),
            sortResolvedText: true);

    private static DeferredCommandMessageArgument DigitalSignatureWarning(XlsxFeatureReport report) =>
        report.Features.Any(feature => feature.Kind == XlsxUnsupportedFeatureKind.DigitalSignatures)
            ? DeferredCommandMessageArgument.Resource("DeferredCommand_UnsupportedXlsxFeature_DigitalSignatureWarningSuffix")
            : DeferredCommandMessageArgument.Literal(string.Empty);

    private static DeferredCommandMessageTextPlan ResourceText(string resourceKey) =>
        DeferredCommandMessageTextPlan.Resource(resourceKey);
}
