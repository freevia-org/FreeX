using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public sealed record PrintPreviewTopToolbarPlan(
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
    double PageRangeComboWidth);

public sealed record PrintPreviewNavigationGlyphPlan(
    PrintPreviewToolbarCommand Command,
    string Text);

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
    double ChoiceComboWidth);

public static class PrintPreviewSurfacePlanner
{
    public const double PrinterComboWidth = 190;
    public const double ToolbarCopiesBoxWidth = 44;
    public const double ToolbarSidesComboWidth = 178;
    public const double ToolbarPageRangeComboWidth = 96;
    public const double SettingsChoiceComboWidth = 183;
    public const double SettingsCopiesBoxWidth = 60;
    public const double SettingsPageRangeBoxWidth = 44;
    public const double DocumentZoomComboWidth = 82;

    public static PrintPreviewTopToolbarPlan CreateTopToolbarPlan(
        int totalPages,
        string printerName,
        PrintSettingsTextResolver? textResolver = null)
    {
        var rangePlan = PrintPreviewToolbarStatePlanner.CreatePageRangeToolbarPlan(totalPages, textResolver);

        return new PrintPreviewTopToolbarPlan(
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
            PageRangeComboWidth: ToolbarPageRangeComboWidth);
    }

    public static PrintPreviewDocumentToolbarPlan CreateDocumentToolbarPlan(
        int totalPages,
        PrintSettingsTextResolver? textResolver = null) =>
        new(
            NavigationButtons:
            [
                new(PrintPreviewToolbarCommand.FirstPage, "|<"),
                new(PrintPreviewToolbarCommand.PreviousPage, "<"),
                new(PrintPreviewToolbarCommand.NextPage, ">"),
                new(PrintPreviewToolbarCommand.LastPage, ">|")
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
        PrintSettingsTextResolver? textResolver = null)
    {
        var panelPlan = PrintPreviewSettingsPanelPlanner.Build(
            sheet,
            currentSettings,
            hasSelection,
            canUpdatePrintPreviewSettings,
            textResolver);

        return new PrintPreviewSettingsRailPlan(
            panelPlan,
            CopiesSectionText: Text(textResolver, "PrintPreview_CopiesSectionLabel", "Copies:"),
            CopiesText: panelPlan.Copies.ToString(CultureInfo.InvariantCulture),
            CopiesBoxWidth: SettingsCopiesBoxWidth,
            PrinterSectionText: Text(textResolver, "PrintPreview_PrinterSectionLabel", "Printer:"),
            PrinterName: NormalizePrinterName(printerName),
            PrinterComboWidth: SettingsChoiceComboWidth,
            PrinterPropertiesButtonText: Text(textResolver, "PrintPreview_PrinterPropertiesButton", "Printer Properties"),
            PrintWhatLabelText: Text(textResolver, "PrintPreview_PrintWhatLabel", "Print What:"),
            PagesLabelText: Text(textResolver, "PrintPreview_PagesLabel", "Pages:"),
            PageRange: CreateSettingsPageRangePlan(totalPages, textResolver),
            SidesSectionText: Text(textResolver, "PrintPreview_SidesSectionLabel", "Print Sides:"),
            CollationSectionText: Text(textResolver, "PrintPreview_CollatedSectionLabel", "Collation:"),
            OrientationLabelText: Text(textResolver, "PrintPreview_OrientationLabel", "Orientation:"),
            PaperSizeLabelText: Text(textResolver, "PageSetup_PaperSize", "Paper size:"),
            MarginsLabelText: Text(textResolver, "PrintPreview_MarginsButton", "Margins"),
            ScalingLabelText: Text(textResolver, "PrintPreview_ScalingLabel", "Scaling:"),
            IgnorePrintAreaText: Text(textResolver, "PrintPreview_IgnorePrintArea", "Ignore print area"),
            PrintOptionsSectionText: Text(textResolver, "PrintPreview_PrintOptionsSection", "Print Options"),
            PrintGridlinesText: Text(textResolver, "PageSetup_PrintGridlines", "Print gridlines"),
            ChoiceComboWidth: SettingsChoiceComboWidth);
    }

    private static PrintPreviewPageRangeFieldsPlan CreateSettingsPageRangePlan(
        int totalPages,
        PrintSettingsTextResolver? textResolver)
    {
        var normalizedTotalPages = Math.Max(1, totalPages);

        return new PrintPreviewPageRangeFieldsPlan(
            "1",
            Text(textResolver, "PrintPreview_PageRangeToText", "To:"),
            normalizedTotalPages.ToString(CultureInfo.InvariantCulture),
            SettingsPageRangeBoxWidth);
    }

    private static string NormalizePrinterName(string printerName) =>
        string.IsNullOrWhiteSpace(printerName) ? "Windows print dialog" : printerName.Trim();

    private static string Text(PrintSettingsTextResolver? textResolver, string key, string fallback)
    {
        var text = textResolver?.Get(key, fallback) ?? fallback;
        if (IsMissingResourceToken(text))
            text = fallback;

        return text.Replace("_", string.Empty, StringComparison.Ordinal);
    }

    private static bool IsMissingResourceToken(string text) =>
        text.StartsWith("[[", StringComparison.Ordinal) &&
        text.EndsWith("]]", StringComparison.Ordinal);
}
