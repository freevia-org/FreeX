using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintPreviewSurfacePlannerTests
{
    [Fact]
    public void CreateTopToolbarPlan_OwnsPrintPreviewToolbarDescriptors()
    {
        var plan = PrintPreviewSurfacePlanner.CreateTopToolbarPlan(
            totalPages: 3,
            printerName: "Office Printer");

        plan.PrintButtonText.Should().Be("Print...");
        plan.PrinterLabelText.Should().Be("Printer:");
        plan.PrinterName.Should().Be("Office Printer");
        plan.PrinterComboWidth.Should().Be(190);
        plan.CopiesLabelText.Should().Be("Copies:");
        plan.CopiesText.Should().Be("1");
        plan.CollatedText.Should().Be("Collated");
        plan.SidesOptions.Select(option => option.Value).Should().Equal(
            PrintPreviewSidesMode.OneSided,
            PrintPreviewSidesMode.TwoSidedLongEdge,
            PrintPreviewSidesMode.TwoSidedShortEdge);
        plan.SidesSelectedIndex.Should().Be(0);
        plan.StatusText.Should().Be("Ready: Office Printer; 1 copy; 3 pages");
        plan.PageRangeText.Should().Be("All pages");
        plan.CloseButtonText.Should().Be("Close");
        PrintPreviewSurfacePlanner.ParityPrinterName.Should().Be("FreeX Parity Printer");
    }

    [Fact]
    public void CreateDocumentToolbarPlan_OwnsNavigationZoomAndCommandDescriptors()
    {
        var plan = PrintPreviewSurfacePlanner.CreateDocumentToolbarPlan(4);

        plan.NavigationButtons.Should().Equal(
            new PrintPreviewNavigationGlyphPlan(
                PrintPreviewToolbarCommand.FirstPage,
                "|<",
                PrintPreviewDialogPlanner.FirstPageButtonAutomationId),
            new PrintPreviewNavigationGlyphPlan(
                PrintPreviewToolbarCommand.PreviousPage,
                "<",
                PrintPreviewDialogPlanner.PreviousPageButtonAutomationId),
            new PrintPreviewNavigationGlyphPlan(
                PrintPreviewToolbarCommand.NextPage,
                ">",
                PrintPreviewDialogPlanner.NextPageButtonAutomationId),
            new PrintPreviewNavigationGlyphPlan(
                PrintPreviewToolbarCommand.LastPage,
                ">|",
                PrintPreviewDialogPlanner.LastPageButtonAutomationId));
        plan.PageLabelText.Should().Be("Page:");
        plan.PageNumberText.Should().Be("1");
        plan.PageStatusText.Should().Be("Page 1 of 4");
        plan.ZoomLabelText.Should().Be("Zoom:");
        plan.ZoomComboWidth.Should().Be(82);
        plan.ZoomSelectedIndex.Should().Be(PrintPreviewToolbarStatePlanner.DefaultZoomOptionIndex);
        plan.MarginsButtonText.Should().Be("Margins");
        plan.PageSetupButtonText.Should().Be("Page Setup");
    }

    [Fact]
    public void DocumentToolbarChrome_UsesSharedWpfDocumentViewerMetrics()
    {
        var chrome = PrintPreviewSurfacePlanner.DocumentToolbarChrome;

        PrintPreviewSurfacePlanner.ParityClientWidth.Should().Be(1106);
        PrintPreviewSurfacePlanner.ParityClientHeight.Should().Be(663);
        PrintPreviewSurfacePlanner.SettingsRailWidth.Should().Be(220);
        PrintPreviewSurfacePlanner.TopToolbarHeight.Should().Be(38);
        PrintPreviewSurfacePlanner.TopToolbarPrintButtonWidth.Should().Be(68);
        PrintPreviewSurfacePlanner.PreviewPageLeftPadding.Should().Be(86);
        chrome.Height.Should().Be(34);
        chrome.ButtonWidth.Should().Be(28);
        chrome.ButtonHeight.Should().Be(26);
        chrome.IconSize.Should().Be(16);
        chrome.LeftPadding.Should().Be(6);
        chrome.ButtonSpacing.Should().Be(4);
        chrome.SeparatorHeight.Should().Be(18);
        PrintPreviewSurfacePlanner.SettingsTextBoxHeight.Should().Be(20);
        PrintPreviewSurfacePlanner.SettingsButtonHeight.Should().Be(22);
        PrintPreviewSurfacePlanner.SettingsRailTopMargin.Should().Be(16);
        PrintPreviewSurfacePlanner.SettingsRailSpacing.Should().Be(7);
    }

    [Fact]
    public void CreateSettingsRailPlan_OwnsRailLabelsMetricsAndPanelOptions()
    {
        var sheet = new Workbook("Book1").AddSheet("Sheet1");
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PrintGridlines = true;

        var plan = PrintPreviewSurfacePlanner.CreateSettingsRailPlan(
            sheet,
            totalPages: 2,
            printerName: "Office Printer",
            new PrintPreviewSettings(Copies: 2),
            hasSelection: false,
            canUpdatePrintPreviewSettings: false);

        plan.CopiesSectionText.Should().Be("Copies:");
        plan.CopiesText.Should().Be("2");
        plan.PrinterSectionText.Should().Be("Printer:");
        plan.PrinterName.Should().Be("Office Printer");
        plan.PrinterPropertiesButtonText.Should().Be("Printer Properties");
        plan.PrintWhatLabelText.Should().Be("Print What:");
        plan.PagesLabelText.Should().Be("Pages:");
        plan.PageRange.Should().Be(new PrintPreviewPageRangeFieldsPlan("1", "To:", "2", 44));
        plan.SidesSectionText.Should().Be("Print Sides:");
        plan.CollationSectionText.Should().Be("Collation:");
        plan.OrientationLabelText.Should().Be("Orientation:");
        plan.PaperSizeLabelText.Should().Be("Paper size:");
        plan.MarginsLabelText.Should().Be("Margins");
        plan.ScalingLabelText.Should().Be("Scaling:");
        plan.IgnorePrintAreaText.Should().Be("Ignore print area");
        plan.PrintOptionsSectionText.Should().Be("Print Options");
        plan.PrintGridlinesText.Should().Be("Print gridlines");
        plan.PrintHeadingsText.Should().Be("Print headings");
        plan.PageSetupLinkText.Should().Be("Page Setup");
        plan.Settings.OrientationSelectedIndex.Should().Be(1);
        plan.Settings.PrintGridlines.Should().BeTrue();
    }

    [Fact]
    public void CreateSurfacePlans_StripMissingResourcesAndAccessKeyMarkers()
    {
        var resolver = new PrintSettingsTextResolver(
            key => key switch
            {
                "PrintPreview_PrinterLabel" => "_Printer:",
                "PrintPreview_PageSetupButton" => "[[PrintPreview_PageSetupButton]]",
                _ => "[[" + key + "]]"
            },
            (_, _) => "");

        var topToolbar = PrintPreviewSurfacePlanner.CreateTopToolbarPlan(1, "", resolver);
        var documentToolbar = PrintPreviewSurfacePlanner.CreateDocumentToolbarPlan(1, resolver);

        topToolbar.PrinterLabelText.Should().Be("Printer:");
        topToolbar.PrinterName.Should().Be("Windows print dialog");
        documentToolbar.PageSetupButtonText.Should().Be("Page Setup");
    }

    [Fact]
    public void CreateSettingsRailPlan_CanPreserveAccessKeyMarkersForWpfLabels()
    {
        var resolver = new PrintSettingsTextResolver(
            key => key switch
            {
                "PrintPreview_CopiesSectionLabel" => "_Copies:",
                "PrintPreview_ScalingLabel" => "Sc_aling:",
                _ => key
            },
            (_, _) => "");

        var avaloniaPlan = PrintPreviewSurfacePlanner.CreateSettingsRailPlan(
            sheet: null,
            totalPages: 1,
            printerName: "",
            currentSettings: new PrintPreviewSettings(),
            hasSelection: false,
            canUpdatePrintPreviewSettings: false,
            textResolver: resolver);
        var wpfPlan = PrintPreviewSurfacePlanner.CreateSettingsRailPlan(
            sheet: null,
            totalPages: 1,
            printerName: "",
            currentSettings: new PrintPreviewSettings(),
            hasSelection: false,
            canUpdatePrintPreviewSettings: false,
            textResolver: resolver,
            stripMnemonics: false);

        avaloniaPlan.CopiesSectionText.Should().Be("Copies:");
        avaloniaPlan.ScalingLabelText.Should().Be("Scaling:");
        wpfPlan.CopiesSectionText.Should().Be("_Copies:");
        wpfPlan.ScalingLabelText.Should().Be("Sc_aling:");
    }

    [Fact]
    public void CreateSurfacePlans_PreservePseudoLocalizedBracketTextThatIsNotMissingKey()
    {
        var resolver = new PrintSettingsTextResolver(
            key => key switch
            {
                "PrintPreview_PrinterLabel" => "[[PPrriinntteerr:]]",
                _ => "[[" + key + "]]"
            },
            (_, _) => "");

        var plan = PrintPreviewSurfacePlanner.CreateTopToolbarPlan(1, "Office Printer", resolver);

        plan.PrinterLabelText.Should().Be("[[PPrriinntteerr:]]");
        plan.CopiesLabelText.Should().Be("Copies:");
    }
}
