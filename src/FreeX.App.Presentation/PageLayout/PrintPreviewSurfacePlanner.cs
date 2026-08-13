using System.Globalization;
using Free.Shared.AppServices;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public sealed record PrintPreviewTopToolbarPlan(
    string PrintButtonText,
    string PrinterLabelText,
    string PrinterName,
    double PrinterComboWidth,
    string CopiesLabelText,
    string CopiesText,
    double CopiesBoxWidth,
    string CollatedText,
    IReadOnlyList<PrintPreviewChoice<PrintPreviewSidesMode>> SidesOptions,
    int SidesSelectedIndex,
    double SidesComboWidth,
    string SidesLabelText,
    string StatusText,
    string PageRangeText,
    double PageRangeComboWidth,
    string CloseButtonText);

public sealed record PrintPreviewNavigationGlyphPlan(
    PrintPreviewToolbarCommand Command,
    string Text,
    string AutomationId);

public sealed record PrintPreviewDocumentToolbarPlan(
    IReadOnlyList<PrintPreviewNavigationGlyphPlan> NavigationButtons,
    string PageLabelText,
    string PageNumberText,
    string PageStatusText,
    string ZoomLabelText,
    double ZoomComboWidth,
    IReadOnlyList<PrintPreviewZoomOption> ZoomOptions,
    int ZoomSelectedIndex,
    string MarginsButtonText,
    string PageSetupButtonText);

public sealed record PrintPreviewDocumentToolbarChromePlan(
    double Height,
    double ButtonWidth,
    double ButtonHeight,
    double IconSize,
    double LeftPadding,
    double ButtonSpacing,
    double SeparatorHeight);

public sealed record PrintPreviewFindBarPlan(
    string PlaceholderText,
    string PreviousButtonText,
    string NextButtonText);

public sealed record PrintPreviewPageRangeFieldsPlan(
    string FromPageText,
    string ToSeparatorText,
    string ToPageText,
    double PageBoxWidth);

public sealed record PrintPreviewSettingsRailPlan(
    PrintPreviewSettingsPanelPlan Settings,
    string CopiesSectionText,
    string CopiesText,
    double CopiesBoxWidth,
    string PrinterSectionText,
    string PrinterName,
    double PrinterComboWidth,
    string PrinterPropertiesButtonText,
    string PrintWhatLabelText,
    string PagesLabelText,
    PrintPreviewPageRangeFieldsPlan PageRange,
    string SidesSectionText,
    string CollationSectionText,
    string OrientationLabelText,
    string PaperSizeLabelText,
    string MarginsLabelText,
    string ScalingLabelText,
    string IgnorePrintAreaText,
    string PrintOptionsSectionText,
    string PrintGridlinesText,
    string PrintHeadingsText,
    string PageSetupLinkText,
    double ChoiceComboWidth);

public static class PrintPreviewSurfacePlanner
{
    public const string ParityPrinterName = "FreeX Parity Printer";
    // The WPF capture is a 1120x700 outer window whose content surface is 1106x663 after the
    // desktop frame is accounted for. Keep the Avalonia parity surface on the same client geometry
    // so the evidence compares the dialog rather than a different decoration model.
    public const double ParityClientWidth = 1106;
    public const double ParityClientHeight = 663;
    public const double SettingsRailWidth = 220;
    public const double TopToolbarHeight = 38;
    public const double TopToolbarPrintButtonWidth = 68;
    public const double PreviewPageLeftPadding = 86;
    public const double PrinterComboWidth = 190;
    public const double ToolbarCopiesBoxWidth = 44;
    public const double ToolbarSidesComboWidth = 178;
    public const double ToolbarPageRangeComboWidth = 96;
    public const double SettingsChoiceComboWidth = 183;
    public const double SettingsCopiesBoxWidth = 60;
    public const double SettingsPageRangeBoxWidth = 44;
    public const double DocumentZoomComboWidth = 82;
    public const double SettingsTextBoxHeight = 20;
    public const double SettingsButtonHeight = 22;
    public const double SettingsRailTopMargin = 16;
    public const double SettingsRailSpacing = 7;

    // WPF's DocumentViewer contributes a compact icon-only toolbar between the print controls and
    // the paper. Keep these metrics in the shared surface planner so Avalonia's replacement chrome
    // remains tied to the same cross-shell contract rather than becoming a capture-only adjustment.
    public static PrintPreviewDocumentToolbarChromePlan DocumentToolbarChrome { get; } =
        new(
            Height: 34,
            ButtonWidth: 28,
            ButtonHeight: 26,
            IconSize: 16,
            LeftPadding: 6,
            ButtonSpacing: 4,
            SeparatorHeight: 18);

    public static PrintPreviewTopToolbarPlan CreateTopToolbarPlan(
        int totalPages,
        string printerName,
        PrintSettingsTextResolver? textResolver = null)
    {
        var rangePlan = PrintPreviewToolbarStatePlanner.CreatePageRangeToolbarPlan(totalPages, textResolver);

        return new PrintPreviewTopToolbarPlan(
            PrintButtonText: Text(textResolver, "PrintPreview_PrintButton", "Print..."),
            PrinterLabelText: Text(textResolver, "PrintPreview_PrinterLabel", "Printer:"),
            PrinterName: NormalizePrinterName(printerName),
            PrinterComboWidth: PrinterComboWidth,
            CopiesLabelText: Text(textResolver, "PrintPreview_CopiesLabel", "Copies:"),
            CopiesText: "1",
            CopiesBoxWidth: ToolbarCopiesBoxWidth,
            CollatedText: PrintPreviewToolbarStatePlanner.CreateToolbarCollatedText(textResolver),
            SidesOptions: PrintPreviewToolbarStatePlanner.CreateSidesOptions(textResolver),
            SidesSelectedIndex: PrintPreviewToolbarStatePlanner.SidesModeToIndex(PrintPreviewSidesMode.OneSided),
            SidesComboWidth: ToolbarSidesComboWidth,
            SidesLabelText: Text(textResolver, "PrintPreview_SidesLabel", "Sides:"),
            StatusText: PrintPreviewToolbarStatePlanner.CreateStatusText(printerName, 1, totalPages),
            PageRangeText: rangePlan.Choices[0].Text,
            PageRangeComboWidth: ToolbarPageRangeComboWidth,
            CloseButtonText: Text(textResolver, "PrintPreview_CloseButton", "Close"));
    }

    public static PrintPreviewDocumentToolbarPlan CreateDocumentToolbarPlan(
        int totalPages,
        PrintSettingsTextResolver? textResolver = null) =>
        new(
            NavigationButtons:
            [
                new(
                    PrintPreviewToolbarCommand.FirstPage,
                    "|<",
                    PrintPreviewDialogPlanner.FirstPageButtonAutomationId),
                new(
                    PrintPreviewToolbarCommand.PreviousPage,
                    "<",
                    PrintPreviewDialogPlanner.PreviousPageButtonAutomationId),
                new(
                    PrintPreviewToolbarCommand.NextPage,
                    ">",
                    PrintPreviewDialogPlanner.NextPageButtonAutomationId),
                new(
                    PrintPreviewToolbarCommand.LastPage,
                    ">|",
                    PrintPreviewDialogPlanner.LastPageButtonAutomationId)
            ],
            PageLabelText: Text(textResolver, "PrintPreview_PageLabel", "Page:"),
            PageNumberText: "1",
            PageStatusText: PrintPreviewNavigationState.Create(1, totalPages).StatusText,
            ZoomLabelText: Text(textResolver, "PrintPreview_ZoomLabel", "Zoom:"),
            ZoomComboWidth: DocumentZoomComboWidth,
            ZoomOptions: PrintPreviewToolbarStatePlanner.CreateZoomOptions(textResolver),
            ZoomSelectedIndex: PrintPreviewToolbarStatePlanner.DefaultZoomOptionIndex,
            MarginsButtonText: Text(textResolver, "PrintPreview_MarginsButton", "Margins"),
            PageSetupButtonText: Text(textResolver, "PrintPreview_PageSetupButton", "Page Setup"));

    public static PrintPreviewFindBarPlan CreateFindBarPlan(
        PrintSettingsTextResolver? textResolver = null) =>
        new(
            Text(textResolver, "PrintPreview_FindPlaceholder", "Type text to find..."),
            "<",
            ">");

    public static PrintPreviewSettingsRailPlan CreateSettingsRailPlan(
        Sheet? sheet,
        int totalPages,
        string printerName,
        PrintPreviewSettings currentSettings,
        bool hasSelection,
        bool canUpdatePrintPreviewSettings,
        PrintSettingsTextResolver? textResolver = null,
        bool stripMnemonics = true)
    {
        var panelPlan = PrintPreviewSettingsPanelPlanner.Build(
            sheet,
            currentSettings,
            hasSelection,
            canUpdatePrintPreviewSettings,
            textResolver);

        return new PrintPreviewSettingsRailPlan(
            panelPlan,
            CopiesSectionText: Text(textResolver, "PrintPreview_CopiesSectionLabel", "Copies:", stripMnemonics),
            CopiesText: panelPlan.Copies.ToString(CultureInfo.InvariantCulture),
            CopiesBoxWidth: SettingsCopiesBoxWidth,
            PrinterSectionText: Text(textResolver, "PrintPreview_PrinterSectionLabel", "Printer:", stripMnemonics),
            PrinterName: NormalizePrinterName(printerName),
            PrinterComboWidth: SettingsChoiceComboWidth,
            PrinterPropertiesButtonText: Text(textResolver, "PrintPreview_PrinterPropertiesButton", "Printer Properties", stripMnemonics),
            PrintWhatLabelText: Text(textResolver, "PrintPreview_PrintWhatLabel", "Print What:", stripMnemonics),
            PagesLabelText: Text(textResolver, "PrintPreview_PagesLabel", "Pages:", stripMnemonics),
            PageRange: CreateSettingsPageRangePlan(totalPages, textResolver, stripMnemonics),
            SidesSectionText: Text(textResolver, "PrintPreview_SidesSectionLabel", "Print Sides:", stripMnemonics),
            CollationSectionText: Text(textResolver, "PrintPreview_CollatedSectionLabel", "Collation:", stripMnemonics),
            OrientationLabelText: Text(textResolver, "PrintPreview_OrientationLabel", "Orientation:", stripMnemonics),
            PaperSizeLabelText: Text(textResolver, "PageSetup_PaperSize", "Paper size:", stripMnemonics),
            MarginsLabelText: Text(textResolver, "PrintPreview_MarginsButton", "Margins", stripMnemonics),
            ScalingLabelText: Text(textResolver, "PrintPreview_ScalingLabel", "Scaling:", stripMnemonics),
            IgnorePrintAreaText: Text(textResolver, "PrintPreview_IgnorePrintArea", "Ignore print area", stripMnemonics),
            PrintOptionsSectionText: Text(textResolver, "PrintPreview_PrintOptionsSection", "Print Options", stripMnemonics),
            PrintGridlinesText: Text(textResolver, "PageSetup_PrintGridlines", "Print gridlines", stripMnemonics),
            PrintHeadingsText: Text(textResolver, "PageSetup_PrintRowAndColumnHeadings", "Print headings", stripMnemonics),
            PageSetupLinkText: Text(textResolver, "PrintPreview_PageSetupLink", "Page Setup", stripMnemonics),
            ChoiceComboWidth: SettingsChoiceComboWidth);
    }

    private static PrintPreviewPageRangeFieldsPlan CreateSettingsPageRangePlan(
        int totalPages,
        PrintSettingsTextResolver? textResolver,
        bool stripMnemonics)
    {
        var normalizedTotalPages = Math.Max(1, totalPages);

        return new PrintPreviewPageRangeFieldsPlan(
            "1",
            Text(textResolver, "PrintPreview_PageRangeToText", "To:", stripMnemonics),
            normalizedTotalPages.ToString(CultureInfo.InvariantCulture),
            SettingsPageRangeBoxWidth);
    }

    private static string NormalizePrinterName(string printerName) =>
        string.IsNullOrWhiteSpace(printerName) ? "Windows print dialog" : printerName.Trim();

    private static string Text(
        PrintSettingsTextResolver? textResolver,
        string key,
        string fallback,
        bool stripMnemonics = true)
        => new ResourceTextDescriptor(key, fallback).Resolve(
            textResolver is null ? null : candidate => textResolver.Get(candidate, fallback),
            stripMnemonics);
}
