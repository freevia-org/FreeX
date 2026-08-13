using System.Printing;
using System.Windows.Documents;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
{
    [Fact]
    public void PrintPreviewDialog_CreateTitle_IncludesWorkbookName()
    {
        PrintPreviewDialog.CreateTitle("Book1").Should().Be(UiText.Format("PrintPreview_TitleFormat", "Book1"));
    }

    [Fact]
    public void PrintPreviewDialog_DelegatesShellConstantsAndParsingToSharedPlanner()
    {
        var source = ReadPrintPreviewDialogSources();
        var parityCaptureSource = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("PrintPreviewDialogPlanner.TitleFormatResourceKey");
        source.Should().Contain("PrintPreviewDialogPlanner.NormalizeWorkbookName(workbookName)");
        source.Should().Contain("PrintPreviewDialogPlanner.TryParseCopyCount(text, out copies)");
        source.Should().Contain("PrintPreviewDialogPlanner.TryParsePageNumber(text, totalPages, out pageNumber)");
        source.Should().Contain("PrintPreviewDialogPlanner.WindowWidth");
        source.Should().Contain("PrintPreviewDialogPlanner.DialogAutomationId");
        parityCaptureSource.Should().Contain("CaptureDialog(results, \"dialog.PrintPreview\", outDir");
        parityCaptureSource.Should().Contain("CreatePrintPreviewDocument()");
        parityCaptureSource.Should().Contain("PrintPreviewParityFixture.Pages");
        parityCaptureSource.Should().Contain("PrintPreviewParityFixture.PageWidth");
    }

    [Fact]
    public void PrintPreviewDialog_ContainsNativePrintCommandButton()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("PrintPreviewToolbarCommand.Print");
        source.Should().Contain("\"PrintPreview_PrintButton\"");
        source.Should().Contain("ShowNativePrintDialog");
        source.Should().Contain("NativePrintDialogService.ShowPrintDialogAndPrint");
        source.Should().Contain("Forms.PrintDialog");
        source.Should().Contain("ResolvePrintPaginator(previewDocument, selectedPageRangeMode, currentPrintPage, selectedPageRange)");
        source.Should().Contain("PrintDocument(paginator");
    }

    [Fact]
    public void PrintPreviewDialogOpenedFromKeyboard_FocusesPrintButton()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget(PrintPreviewDialogPlanner.InitialFocusCommand, printButton);");
        source.Should().Contain("private static void FocusInitialKeyboardTarget(PrintPreviewToolbarCommand focusCommand, Button printButton)");
        source.Should().Contain("printButton.Focus();");
        source.Should().Contain("Keyboard.Focus(printButton);");
    }

    [Theory]
    [InlineData(null, false, 0)]
    [InlineData("", false, 0)]
    [InlineData("0", false, 0)]
    [InlineData("2", true, 2)]
    [InlineData("999", true, 999)]
    [InlineData("1000", false, 0)]
    [InlineData("not a number", false, 0)]
    public void PrintPreviewDialog_TryParseCopyCount_ValidatesExcelCopiesRange(string? text, bool expectedResult, int expectedCopies)
    {
        PrintPreviewDialog.TryParseCopyCount(text, out var copies).Should().Be(expectedResult);
        copies.Should().Be(expectedCopies);
    }

    [Theory]
    [InlineData(null, 5, false, 0)]
    [InlineData("", 5, false, 0)]
    [InlineData("0", 5, false, 0)]
    [InlineData("1", 5, true, 1)]
    [InlineData("5", 5, true, 5)]
    [InlineData("6", 5, false, 0)]
    [InlineData("2.5", 5, false, 0)]
    [InlineData("not a number", 5, false, 0)]
    public void PrintPreviewDialog_TryParsePageNumber_ValidatesPreviewPageRange(
        string? text,
        int totalPages,
        bool expectedResult,
        int expectedPage)
    {
        PrintPreviewDialog.TryParsePageNumber(text, totalPages, out var pageNumber).Should().Be(expectedResult);
        pageNumber.Should().Be(expectedPage);
    }

    [Fact]
    public void PrintPreviewDialog_CreateNavigationState_DelegatesToSharedNavigationState()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("PrintPreviewToolbarStatePlanner.CreateNavigationState(currentPage, totalPages)");
        source.Should().NotContain("Math.Clamp(currentPage");
        source.Should().NotContain("StatusText: $\"Page");
    }

    [Theory]
    [InlineData(PrintPreviewSidesMode.OneSided, Duplexing.OneSided)]
    [InlineData(PrintPreviewSidesMode.TwoSidedLongEdge, Duplexing.TwoSidedLongEdge)]
    [InlineData(PrintPreviewSidesMode.TwoSidedShortEdge, Duplexing.TwoSidedShortEdge)]
    public void PrintPreviewDialog_MapsExcelSidesChoicesToPrintTicketDuplexing(
        PrintPreviewSidesMode mode,
        Duplexing expected)
    {
        PrintPreviewDialog.ResolvePrintTicketDuplexing(mode).Should().Be(expected);
    }

    [Fact]
    public void PrintPreviewDialog_ResolvesCurrentPagePaginatorForPrintRange()
    {
        StaTestRunner.Run(() =>
        {
            var document = new FixedDocument();
            document.Pages.Add(new PageContent());
            document.Pages.Add(new PageContent());
            document.Pages.Add(new PageContent());

            var allPages = PrintPreviewDialog.ResolvePrintPaginator(document, PrintPreviewPageRangeMode.AllPages, currentPage: 2);
            var currentPage = PrintPreviewDialog.ResolvePrintPaginator(document, PrintPreviewPageRangeMode.CurrentPage, currentPage: 2);
            var pageRange = PrintPreviewDialog.ResolvePrintPaginator(
                document,
                PrintPreviewPageRangeMode.Pages,
                currentPage: 1,
                new ExportPageRange(2, 3));

            allPages.PageCount.Should().Be(3);
            currentPage.PageCount.Should().Be(1);
            pageRange.PageCount.Should().Be(2);
            currentPage.GetPage(1).Should().Be(DocumentPage.Missing);
            pageRange.GetPage(2).Should().Be(DocumentPage.Missing);
        });
    }

    [Fact]
    public void PrintPreviewDialog_DisplaysPrintSettingsSummary()
    {
        var source = ReadPrintPreviewDialogSources();
        var printExport = DialogSourceTestSupport.ReadHostSources("MainWindow.PrintExport.cs");

        source.Should().Contain("PrintSettingsPlan settings");
        source.Should().Contain("Action? showMargins = null");
        source.Should().Contain("Action? showPageSetup = null");
        source.Should().Contain("Func<(FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreview = null");
        source.Should().Contain("Func<PrintPreviewSettings, (FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreviewWithSettings = null");
        source.Should().Contain("settings.Summary");
        printExport.Should().Contain("PrintSettingsPlanner.Build(sheet, textResolver: WpfPrintSettingsTextResolver.Instance)");
        printExport.Should().Contain("showMargins: () => PageMarginsBtn_Click");
        printExport.Should().Contain("showPageSetup: () => PageSetupDialogBtn_Click");
        printExport.Should().Contain("refreshPreviewWithSettings: BuildActiveSheetPrintPreview");
        printExport.Should().Contain("ignorePrintArea: settings.IgnorePrintArea");
        printExport.Should().Contain("settings.IgnorePrintArea");
        printExport.Should().Contain("WpfPrintSettingsTextResolver.Instance");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesKeyboardPrintGridlineAndHeadingToggles()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = railPlan.PrintGridlinesText");
        source.Should().Contain("Content = railPlan.PrintHeadingsText");
        source.Should().Contain("gridlinesBox.Checked +=");
        source.Should().Contain("gridlinesBox.Unchecked +=");
        source.Should().Contain("headingsBox.Checked +=");
        source.Should().Contain("headingsBox.Unchecked +=");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildPrintOptionsCommand(");
        source.Should().NotContain("new SetPrintOptionsCommand(");
        source.Should().Contain("refreshPreview();");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesIgnorePrintAreaBackstageSetting()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = railPlan.IgnorePrintAreaText");
        source.Should().Contain("IgnorePrintArea");
        source.Should().Contain("ignorePrintAreaBox.Checked +=");
        source.Should().Contain("ignorePrintAreaBox.Unchecked +=");
        source.Should().Contain("ToolTip = UiText.Get(\"PrintPreview_IgnorePrintAreaToolTip\")");
        source.Should().Contain("AutomationProperties.SetName(ignorePrintAreaBox, UiText.Get(\"PrintPreview_IgnorePrintAreaAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(ignorePrintAreaBox, UiText.Get(\"PrintPreview_IgnorePrintAreaHelpText\"));");
    }

    [Fact]
    public void PrintPreviewDialog_SettingsCombosHaveAccessKeyLabels()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("void AddLabel(string text, Control target)");
        source.Should().Contain("Content = text");
        source.Should().Contain("Target = target");
        source.Should().Contain("PrintPreviewSurfacePlanner.CreateSettingsRailPlan(");
        source.Should().Contain("AddLabel(railPlan.OrientationLabelText, orientBox);");
        source.Should().Contain("AddLabel(railPlan.PaperSizeLabelText, paperBox);");
        source.Should().Contain("AddLabel(railPlan.MarginsLabelText, marginsBox);");
        source.Should().Contain("AddLabel(railPlan.ScalingLabelText, scaleBox);");
        source.Should().Contain("PrintPreviewSettingsPanelPlanner.Build(");
        source.Should().NotContain("AddLabel(UiText.Get(\"PrintPreview_OrientationLabel\")");
        source.Should().NotContain("AddLabel(UiText.Get(\"PageSetup_PaperSize\")");
        source.Should().NotContain("AddLabel(UiText.Get(\"PageSetup_Margins\")");
        source.Should().NotContain("AddLabel(UiText.Get(\"PrintPreview_ScalingLabel\")");
        source.Should().NotContain("UiText.Get(\"PrintPreview_PrintWhatActiveSheets\")");
        source.Should().NotContain("UiText.Get(\"PrintPreview_ScaleFitColumns\")");
        source.Should().NotContain("PrintSettingsPlanner.ScaleIndexToScaleToFit(scaleBox.SelectedIndex)");
    }

    [Fact]
    public void PrintPreviewDialog_ToolbarZoomAccessKeyTargetsZoomCombo()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("var zoomBox = new ComboBox");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_ZoomLabel\")");
        source.Should().Contain("Target = zoomBox");
    }

    [Fact]
    public void PrintPreviewDialog_ToolbarControlsExposeStableAutomation()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("PrintPreviewDialogPlanner.CreateToolbarCommandPlan(command)");
        source.Should().Contain("PrintPreviewDialogPlanner.CreateNavigationCommandPlans()");
        source.Should().Contain("PrintPreviewToolbarCommand.FirstPage");
        source.Should().Contain("PrintPreviewToolbarCommand.PreviousPage");
        source.Should().Contain("PrintPreviewToolbarCommand.NextPage");
        source.Should().Contain("PrintPreviewToolbarCommand.LastPage");
        source.Should().Contain("PrintButtonAutomationId,");
        source.Should().Contain("CloseButtonAutomationId,");
        source.Should().Contain("PrintPreviewDialogPlanner.PageNumberBoxAutomationId");
        source.Should().Contain("PrintPreviewDialogPlanner.PageStatusTextAutomationId");
        source.Should().Contain("PrintPreviewDialogPlanner.ZoomBoxAutomationId");
        source.Should().Contain("PrintPreviewDialogPlanner.SettingsSummaryTextAutomationId");
        source.Should().Contain("SetToolbarAutomation(button, plan)");
        source.Should().Contain("private static void SetToolbarAutomation(Control control, PrintPreviewToolbarCommandPlan plan)");
        source.Should().Contain("plan.AutomationId");
        source.Should().Contain("private static void SetToolbarAutomation(Control control, string automationId, string name, string helpText)");
    }

    [Fact]
    public void PrintPreviewDialogAndSettingsPanel_SharePrinterBoxPopulation()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("WpfPrintPreviewToolbarPlanner.PopulatePrinterBox(");
        source.Should().Contain("public static void PopulatePrinterBox(");
        source.Should().NotContain("private static void PopulatePrinterBox(ComboBox printerBox)");
    }

    [Fact]
    public void PrintPreviewDialog_WiresMarginsAndPageSetupToolbarCallbacks()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("showMargins?.Invoke()");
        source.Should().Contain("showPageSetup?.Invoke()");
        source.Should().Contain("RefreshPreviewDocument()");
        source.Should().Contain("viewer.Document = previewDocument");
        source.Should().Contain("settingsSummaryText.Text = refreshed.Settings.Summary");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesPageEntryAndStatus()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = UiText.Get(\"PrintPreview_PageLabel\")");
        source.Should().Contain("pageNumberBox");
        source.Should().Contain("pageStatusText");
        source.Should().Contain("CreateNavigationState(1, totalPages).StatusText");
        source.Should().Contain("CreateNavigationState(pageNumber, totalPages).StatusText");
        source.Should().Contain("NavigationCommands.GoToPage");
        source.Should().Contain("TryParsePageNumber(pageNumberBox.Text, totalPages, out var pageNumber)");
        source.Should().Contain("ShowInvalidPageNumberWarning(pageNumberBox, totalPages)");
        source.Should().Contain("PrintPreviewDialogPlanner.DescribeInvalidPageNumber(totalPages)");
        source.Should().Contain("presentation.Message.Resolve(UiText.Get, UiText.Format)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesHonestPrinterCopiesAndStatusSurface()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = UiText.Get(\"PrintPreview_PrinterLabel\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_CopiesLabel\")");
        source.Should().Contain("PrintPreview_CollatedLabel");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_SidesLabel\")");
        source.Should().Contain("PrintPreviewToolbarStatePlanner.CreateToolbarCollatedText(WpfPrintSettingsTextResolver.Instance)");
        source.Should().Contain("PrintPreviewToolbarStatePlanner.CreateSidesOptions(WpfPrintSettingsTextResolver.Instance)");
        source.Should().Contain("sidesBox.Items.Add(option.Text)");
        source.Should().Contain("printerBox");
        source.Should().Contain("copiesBox");
        source.Should().Contain("collatedBox");
        source.Should().Contain("sidesBox");
        source.Should().Contain("statusText");
        source.Should().Contain("TryParseCopyCount(copiesBox.Text, out var copies)");
        source.Should().Contain("ShowInvalidCopiesWarning(copiesBox)");
        source.Should().Contain("documentPrinter.PrintTicket.CopyCount = Math.Clamp((int)dialog.PrinterSettings.Copies, 1, 999)");
        source.Should().Contain("documentPrinter.PrintTicket.Collation =");
        source.Should().Contain("dialog.PrinterSettings.Collate");
        source.Should().Contain("Collation.Collated");
        source.Should().Contain("documentPrinter.PrintTicket.Duplexing = ResolveDuplexing(dialog.PrinterSettings.Duplex, sidesMode)");
        source.Should().Contain("using var document = CreatePrinterSelectionDocument(printQueue, copies, collated, sidesMode, paginator)");
        source.Should().Contain("Document = document");
        source.Should().Contain("UseEXDialog = false");
        source.Should().Contain("ResolveSelectedSidesMode(sidesBox)");
        source.Should().Contain("collatedBox.IsChecked == true");
        source.Should().Contain("PrintPreviewDialogPlanner.DescribeInvalidCopies()");
        source.Should().Contain("presentation.Message.Resolve(UiText.Get, UiText.Format)");
        source.Should().Contain("AutomationProperties.SetHelpText");
        source.Should().Contain("RefreshPrintStatus");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesKeyboardPrintRangeChoices()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("PrintPreviewToolbarStatePlanner.CreatePageRangeToolbarPlan(");
        source.Should().Contain("PrintPreviewToolbarStatePlanner.CreatePageRangeSelectionPlan(mode)");
        source.Should().Contain("Content = allPagesChoice.Text");
        source.Should().Contain("Content = currentPageChoice.Text");
        source.Should().Contain("Content = pagesChoice.Text");
        source.Should().Contain("fromPageBox");
        source.Should().Contain("toPageBox");
        source.Should().Contain("PrintPreviewPageRangeMode.CurrentPage");
        source.Should().Contain("PrintPreviewPageRangeMode.Pages");
        source.Should().Contain("ResolvePrintPaginator(previewDocument, selectedPageRangeMode, currentPrintPage, selectedPageRange)");
        source.Should().Contain("ExportPlanner.TryCreatePageRange(fromPageBox.Text, toPageBox.Text, out selectedPageRange, out var pageRangeError, WpfExportPlannerTextResolver.Instance)");
        source.Should().Contain("ExportPlanner.TryValidatePageRange(selectedPageRange, totalPages, out var validatedPageRangeError, WpfExportPlannerTextResolver.Instance)");
        source.Should().Contain("TryParsePageNumber(pageNumberBox.Text, totalPages, out currentPrintPage)");
        source.Should().Contain("ShowInvalidPageNumberWarning(pageNumberBox, totalPages)");
        source.Should().Contain("ShowInvalidPageRangeWarning(fromPageBox, toPageBox, pageRangeError)");
    }

    [Fact]
    public void PrintPreviewDialog_PrintRangeAccessKeysAreUnique()
    {
        var source = ReadPrintPreviewDialogSources();
        var rangeLabels = new[]
        {
            UiText.Get("PrintPreview_AllPagesLabel"),
            UiText.Get("PrintPreview_CurrentPageLabel"),
            UiText.Get("PrintPreview_PagesLabel")
        };

        var accessKeys = rangeLabels.Select(ExtractAccessKey).ToList();

        source.Should().ContainAll(
            [
                "PrintPreview_AllPagesLabel",
                "PrintPreview_CurrentPageLabel",
                "PrintPreview_PagesLabel"
            ]);
        accessKeys.Should().OnlyHaveUniqueItems("Print Preview range choices share one access-key scope");
    }

    private static string ReadPrintPreviewDialogSources() =>
        DialogSourceTestSupport.ReadHostSources(
            "PrintPreviewDialog.cs",
            "PrintPreviewDialog.Layout.cs",
            "PrintPreviewDialog.Helpers.cs",
            "NativePrintDialogService.cs",
            "PrintPreviewSettingsPanelFactory.cs",
            "WpfPrintPreviewToolbarPlanner.cs")
        + Environment.NewLine
        + DialogSourceTestSupport.ReadPresentationSources("PageLayout", "PrintPreviewDialogPlanner.cs")
        + Environment.NewLine
        + DialogSourceTestSupport.ReadPresentationSources("PageLayout", "PrintPreviewToolbarStatePlanner.cs")
        + Environment.NewLine
        + DialogSourceTestSupport.ReadPresentationSources("PageLayout", "PrintPreviewSurfacePlanner.cs")
        + Environment.NewLine
        + DialogSourceTestSupport.ReadPresentationSources("PageLayout", "PrintPreviewSettingsPanelPlanner.cs");

    private static char ExtractAccessKey(string label)
    {
        var underscoreIndex = label.IndexOf('_', StringComparison.Ordinal);

        underscoreIndex.Should().BeGreaterThanOrEqualTo(0, $"label '{label}' should declare an access key");
        underscoreIndex.Should().BeLessThan(label.Length - 1, $"label '{label}' should include a character after '_'");

        return char.ToUpperInvariant(label[underscoreIndex + 1]);
    }
}
