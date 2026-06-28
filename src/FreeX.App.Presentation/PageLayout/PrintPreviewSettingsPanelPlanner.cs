using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public sealed record PrintPreviewSettingsPanelPlan(
    int Copies,
    IReadOnlyList<PrintPreviewChoice<PrintWhat>> PrintWhatOptions,
    int PrintWhatSelectedIndex,
    IReadOnlyList<PrintPreviewChoice<PrintPreviewSidesMode>> SidesOptions,
    int SidesSelectedIndex,
    IReadOnlyList<PrintPreviewChoice<bool>> CollationOptions,
    int CollationSelectedIndex,
    IReadOnlyList<PrintPreviewChoice<WorksheetPageOrientation>> OrientationOptions,
    int OrientationSelectedIndex,
    IReadOnlyList<PrintPreviewChoice<WorksheetPaperSize>> PaperSizeOptions,
    int PaperSizeSelectedIndex,
    IReadOnlyList<PrintPreviewChoice<WorksheetPageMargins>> MarginOptions,
    int MarginsSelectedIndex,
    int CustomMarginsIndex,
    IReadOnlyList<PrintPreviewChoice<WorksheetScaleToFit>> ScalingOptions,
    int ScalingSelectedIndex,
    int CustomScalingIndex,
    bool IgnorePrintAreaChecked,
    bool IgnorePrintAreaEnabled,
    bool PrintGridlines,
    bool PrintHeadings);

public static class PrintPreviewSettingsPanelPlanner
{
    public const int CustomMarginsOptionIndex = 3;
    public const int CustomScalingOptionIndex = 4;

    public static PrintPreviewSettingsPanelPlan Build(
        Sheet? sheet,
        PrintPreviewSettings currentSettings,
        bool hasSelection,
        bool canUpdatePrintPreviewSettings,
        PrintSettingsTextResolver? textResolver = null)
    {
        var orientation = sheet?.PageOrientation ?? WorksheetPageOrientation.Portrait;
        var paperSize = sheet?.PaperSize ?? WorksheetPaperSize.A4;
        var margins = sheet?.PageMargins ?? WorksheetPageMargins.Narrow;
        var scaling = sheet?.ScaleToFit ?? WorksheetScaleToFit.Default;

        return new PrintPreviewSettingsPanelPlan(
            Copies: PrintSettingsPlanner.ClampCopies(currentSettings.Copies),
            PrintWhatOptions: CreatePrintWhatOptions(hasSelection, textResolver),
            PrintWhatSelectedIndex: PrintWhatToIndex(currentSettings.PrintWhat, hasSelection),
            SidesOptions: PrintPreviewToolbarStatePlanner.CreateSidesOptions(textResolver),
            SidesSelectedIndex: PrintPreviewToolbarStatePlanner.SidesModeToIndex(currentSettings.Sides),
            CollationOptions: PrintPreviewToolbarStatePlanner.CreateCollationOptions(textResolver),
            CollationSelectedIndex: currentSettings.Collated ? 0 : 1,
            OrientationOptions: CreateOrientationOptions(textResolver),
            OrientationSelectedIndex: OrientationToIndex(orientation),
            PaperSizeOptions: CreatePaperSizeOptions(textResolver),
            PaperSizeSelectedIndex: PaperSizeToIndex(paperSize),
            MarginOptions: CreateMarginOptions(textResolver),
            MarginsSelectedIndex: MarginsToIndex(margins),
            CustomMarginsIndex: CustomMarginsOptionIndex,
            ScalingOptions: CreateScalingOptions(textResolver),
            ScalingSelectedIndex: PrintSettingsPlanner.ScaleToFitToIndex(scaling),
            CustomScalingIndex: CustomScalingOptionIndex,
            IgnorePrintAreaChecked: currentSettings.IgnorePrintArea,
            IgnorePrintAreaEnabled: sheet?.PrintArea is not null && canUpdatePrintPreviewSettings,
            PrintGridlines: sheet?.PrintGridlines ?? false,
            PrintHeadings: sheet?.PrintHeadings ?? false);
    }

    public static IReadOnlyList<PrintPreviewChoice<PrintWhat>> CreatePrintWhatOptions(
        bool hasSelection,
        PrintSettingsTextResolver? textResolver = null) =>
        [
            new(Get(textResolver, "PrintPreview_PrintWhatActiveSheets", "Print Active Sheets"), PrintWhat.ActiveSheets),
            new(Get(textResolver, "PrintPreview_PrintWhatEntireWorkbook", "Print Entire Workbook"), PrintWhat.EntireWorkbook),
            new(Get(textResolver, "PrintPreview_PrintWhatSelection", "Print Selection"), PrintWhat.Selection, hasSelection)
        ];

    public static IReadOnlyList<PrintPreviewChoice<WorksheetPageOrientation>> CreateOrientationOptions(
        PrintSettingsTextResolver? textResolver = null) =>
        [
            new(Get(textResolver, "PageSetup_Portrait", "Portrait"), WorksheetPageOrientation.Portrait),
            new(Get(textResolver, "PageSetup_Landscape", "Landscape"), WorksheetPageOrientation.Landscape)
        ];

    public static IReadOnlyList<PrintPreviewChoice<WorksheetPaperSize>> CreatePaperSizeOptions(
        PrintSettingsTextResolver? textResolver = null) =>
        [
            new(Get(textResolver, "MainWindow_Header_A4", "A4"), WorksheetPaperSize.A4),
            new(Get(textResolver, "MainWindow_Header_Letter", "Letter"), WorksheetPaperSize.Letter),
            new(Get(textResolver, "MainWindow_Header_Legal", "Legal"), WorksheetPaperSize.Legal)
        ];

    public static IReadOnlyList<PrintPreviewChoice<WorksheetPageMargins>> CreateMarginOptions(
        PrintSettingsTextResolver? textResolver = null) =>
        [
            new(Get(textResolver, "MainWindow_Header_Narrow", "Narrow"), WorksheetPageMargins.Narrow),
            new(Get(textResolver, "MainWindow_Header_Normal", "Normal"), WorksheetPageMargins.Normal),
            new(Get(textResolver, "MainWindow_Header_Wide", "Wide"), WorksheetPageMargins.Wide),
            new(Get(textResolver, "PrintPreview_CustomMarginsOption", "Custom Margins..."), WorksheetPageMargins.Narrow, IsPlaceholder: true)
        ];

    public static IReadOnlyList<PrintPreviewChoice<WorksheetScaleToFit>> CreateScalingOptions(
        PrintSettingsTextResolver? textResolver = null) =>
        [
            new(Get(textResolver, "PrintPreview_ScaleNoScaling", "No Scaling"), WorksheetScaleToFit.Default),
            new(Get(textResolver, "PrintPreview_ScaleFitSheet", "Fit Sheet on One Page"), new WorksheetScaleToFit(null, 1, 1)),
            new(Get(textResolver, "PrintPreview_ScaleFitColumns", "Fit All Columns on One Page"), new WorksheetScaleToFit(null, 1, null)),
            new(Get(textResolver, "PrintPreview_ScaleFitRows", "Fit All Rows on One Page"), new WorksheetScaleToFit(null, null, 1)),
            new(Get(textResolver, "PrintPreview_ScaleCustomOptions", "Custom Scaling Options..."), WorksheetScaleToFit.Default, IsPlaceholder: true)
        ];

    public static int OrientationToIndex(WorksheetPageOrientation orientation) =>
        orientation == WorksheetPageOrientation.Landscape ? 1 : 0;

    public static int PaperSizeToIndex(WorksheetPaperSize paperSize) =>
        paperSize switch
        {
            WorksheetPaperSize.Letter => 1,
            WorksheetPaperSize.Legal => 2,
            _ => 0
        };

    public static int MarginsToIndex(WorksheetPageMargins margins) =>
        margins == WorksheetPageMargins.Normal
            ? 1
            : margins == WorksheetPageMargins.Wide
                ? 2
                : 0;

    private static int PrintWhatToIndex(PrintWhat printWhat, bool hasSelection) =>
        printWhat == PrintWhat.Selection && !hasSelection
            ? (int)PrintWhat.ActiveSheets
            : (int)printWhat;

    private static string Get(PrintSettingsTextResolver? textResolver, string key, string fallback) =>
        textResolver?.Get(key, fallback) ?? fallback;
}
