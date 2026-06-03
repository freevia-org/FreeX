using System.IO;
using System.Printing;
using System.Windows.Documents;
using FluentAssertions;
using FreeX.Core.Calc;
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
    public void PrintPreviewDialog_ContainsNativePrintCommandButton()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = UiText.Get(\"PrintPreview_PrintButton\")");
        source.Should().Contain("ShowNativePrintDialog");
        source.Should().Contain("ResolvePrintPaginator(previewDocument, selectedPageRangeMode, currentPrintPage, selectedPageRange)");
        source.Should().Contain("PrintDocument(paginator");
    }

    [Fact]
    public void PrintPreviewDialogOpenedFromKeyboard_FocusesPrintButton()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget(printButton);");
        source.Should().Contain("private static void FocusInitialKeyboardTarget(Button printButton)");
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

    [Theory]
    [InlineData(1, 3, 1, 3, false, false, true, true, "Page 1 of 3")]
    [InlineData(2, 3, 2, 3, true, true, true, true, "Page 2 of 3")]
    [InlineData(3, 3, 3, 3, true, true, false, false, "Page 3 of 3")]
    [InlineData(0, 0, 1, 1, false, false, false, false, "Page 1 of 1")]
    [InlineData(5, 3, 3, 3, true, true, false, false, "Page 3 of 3")]
    public void PrintPreviewDialog_CreateNavigationState_NormalizesPageStatusAndButtonStates(
        int currentPage,
        int totalPages,
        int expectedCurrentPage,
        int expectedTotalPages,
        bool canGoFirst,
        bool canGoPrevious,
        bool canGoNext,
        bool canGoLast,
        string statusText)
    {
        var state = PrintPreviewDialog.CreateNavigationState(currentPage, totalPages);

        state.CurrentPage.Should().Be(expectedCurrentPage);
        state.TotalPages.Should().Be(expectedTotalPages);
        state.CanGoFirst.Should().Be(canGoFirst);
        state.CanGoPrevious.Should().Be(canGoPrevious);
        state.CanGoNext.Should().Be(canGoNext);
        state.CanGoLast.Should().Be(canGoLast);
        state.StatusText.Should().Be(statusText);
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
    public void PrintSettingsPlanner_SummarizesExcelLikeActiveSheetSettings()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1")
        {
            PageOrientation = WorksheetPageOrientation.Landscape,
            PaperSize = WorksheetPaperSize.Letter,
            PrintGridlines = true,
            PrintHeadings = true,
            ScaleToFit = new WorksheetScaleToFit(85, 1, 2)
        };

        var plan = PrintSettingsPlanner.Build(sheet);

        plan.Lines.Should().Equal(
            "Print active sheet",
            "Orientation: Landscape",
            "Paper size: Letter",
            "Scaling: 85%; fit 1 page wide by 2 tall",
            "Gridlines: on",
            "Headings: on");
        plan.Summary.Should().Be("Print active sheet; Orientation: Landscape; Paper size: Letter; Scaling: 85%; fit 1 page wide by 2 tall; Gridlines: on; Headings: on");
    }

    [Fact]
    public void PrintSettingsPlanner_SummarizesIgnoredPrintAreaForBackstagePreview()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1")
        {
            PrintArea = GridRange.Parse("B2:D10", sheetId)
        };

        var normal = PrintSettingsPlanner.Build(sheet);
        var ignored = PrintSettingsPlanner.Build(sheet, ignorePrintArea: true);

        normal.Lines[0].Should().Be("Print selected print area");
        ignored.Lines[0].Should().Be("Print active sheet (ignore print area)");
    }

    [Fact]
    public void PrintPreviewDialog_DisplaysPrintSettingsSummary()
    {
        var source = ReadPrintPreviewDialogSources();
        var printExport = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PrintExport.cs"));

        source.Should().Contain("PrintSettingsPlan settings");
        source.Should().Contain("Action? showMargins = null");
        source.Should().Contain("Action? showPageSetup = null");
        source.Should().Contain("Func<(FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreview = null");
        source.Should().Contain("Func<PrintPreviewSettings, (FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreviewWithSettings = null");
        source.Should().Contain("settings.Summary");
        printExport.Should().Contain("PrintSettingsPlanner.Build(sheet)");
        printExport.Should().Contain("showMargins: () => PageMarginsBtn_Click");
        printExport.Should().Contain("showPageSetup: () => PageSetupDialogBtn_Click");
        printExport.Should().Contain("refreshPreviewWithSettings: BuildActiveSheetPrintPreview");
        printExport.Should().Contain("PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService, ignorePrintArea: settings.IgnorePrintArea)");
        printExport.Should().Contain("PrintSettingsPlanner.Build(sheet, settings.IgnorePrintArea)");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesKeyboardPrintGridlineAndHeadingToggles()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = UiText.Get(\"PageSetup_PrintGridlines\")");
        source.Should().Contain("Content = UiText.Get(\"PageSetup_PrintRowAndColumnHeadings\")");
        source.Should().Contain("gridlinesBox.Checked +=");
        source.Should().Contain("gridlinesBox.Unchecked +=");
        source.Should().Contain("headingsBox.Checked +=");
        source.Should().Contain("headingsBox.Unchecked +=");
        source.Should().Contain("new SetPrintOptionsCommand(");
        source.Should().Contain("refreshPreview();");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesIgnorePrintAreaBackstageSetting()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = UiText.Get(\"PrintPreview_IgnorePrintArea\")");
        source.Should().Contain("new PrintPreviewSettings(ignorePrintAreaBox.IsChecked == true)");
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
        source.Should().Contain("AddLabel(UiText.Get(\"PrintPreview_OrientationLabel\"), orientBox);");
        source.Should().Contain("AddLabel(UiText.Get(\"PageSetup_PaperSize\"), paperBox);");
        source.Should().Contain("AddLabel(UiText.Get(\"PageSetup_Margins\"), marginsBox);");
        source.Should().Contain("AddLabel(UiText.Get(\"PrintPreview_ScalingLabel\"), scaleBox);");
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

        source.Should().Contain("SetToolbarAutomation(firstButton, \"PrintPreviewFirstPageButton\", UiText.Get(\"PrintPreview_FirstPageAutomationName\")");
        source.Should().Contain("SetToolbarAutomation(previousButton, \"PrintPreviewPreviousPageButton\", UiText.Get(\"PrintPreview_PreviousPageAutomationName\")");
        source.Should().Contain("SetToolbarAutomation(nextButton, \"PrintPreviewNextPageButton\", UiText.Get(\"PrintPreview_NextPageAutomationName\")");
        source.Should().Contain("SetToolbarAutomation(lastButton, \"PrintPreviewLastPageButton\", UiText.Get(\"PrintPreview_LastPageAutomationName\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(printButton, \"PrintPreviewPrintButton\")");
        source.Should().Contain("SetToolbarAutomation(closeButton, \"PrintPreviewCloseButton\", UiText.Get(\"PrintPreview_CloseAutomationName\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(pageNumberBox, \"PrintPreviewPageNumberBox\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(pageStatusText, \"PrintPreviewPageStatusText\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(zoomBox, \"PrintPreviewZoomBox\")");
        source.Should().Contain("SetToolbarAutomation(marginsButton, \"PrintPreviewMarginsButton\", UiText.Get(\"PrintPreview_MarginsAutomationName\")");
        source.Should().Contain("SetToolbarAutomation(pageSetupButton, \"PrintPreviewPageSetupButton\", UiText.Get(\"PrintPreview_PageSetupAutomationName\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(settingsSummaryText, \"PrintPreviewSettingsSummaryText\")");
        source.Should().Contain("private static void SetToolbarAutomation(Control control, string automationId, string name, string helpText)");
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
        source.Should().Contain("UiText.Format(\"PrintPreview_InvalidPageNumberMessage\", totalPages)");
        source.Should().Contain("pageNumberBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(pageNumberBox);");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesHonestPrinterCopiesAndStatusSurface()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = UiText.Get(\"PrintPreview_PrinterLabel\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_CopiesLabel\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_CollatedLabel\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_SidesLabel\")");
        source.Should().Contain("sidesBox.Items.Add(UiText.Get(\"PrintPreview_SidesOneSided\"))");
        source.Should().Contain("sidesBox.Items.Add(UiText.Get(\"PrintPreview_SidesFlipLongEdge\"))");
        source.Should().Contain("sidesBox.Items.Add(UiText.Get(\"PrintPreview_SidesFlipShortEdge\"))");
        source.Should().Contain("printerBox");
        source.Should().Contain("copiesBox");
        source.Should().Contain("collatedBox");
        source.Should().Contain("sidesBox");
        source.Should().Contain("statusText");
        source.Should().Contain("TryParseCopyCount(copiesBox.Text, out var copies)");
        source.Should().Contain("ShowInvalidCopiesWarning(copiesBox)");
        source.Should().Contain("dialog.PrintTicket.CopyCount = copies");
        source.Should().Contain("dialog.PrintTicket.Collation = collated ? Collation.Collated : Collation.Uncollated");
        source.Should().Contain("dialog.PrintTicket.Duplexing = ResolvePrintTicketDuplexing(sidesMode)");
        source.Should().Contain("ResolveSelectedSidesMode(sidesBox)");
        source.Should().Contain("collatedBox.IsChecked == true");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, UiText.Get(\"PrintPreview_InvalidCopiesMessage\"), Title);");
        source.Should().Contain("copiesBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(copiesBox);");
        source.Should().Contain("AutomationProperties.SetHelpText");
        source.Should().Contain("RefreshPrintStatus");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesKeyboardPrintRangeChoices()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = UiText.Get(\"PrintPreview_AllPagesLabel\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_CurrentPageLabel\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_PagesLabel\")");
        source.Should().Contain("fromPageBox");
        source.Should().Contain("toPageBox");
        source.Should().Contain("PrintPreviewPageRangeMode.CurrentPage");
        source.Should().Contain("PrintPreviewPageRangeMode.Pages");
        source.Should().Contain("ResolvePrintPaginator(previewDocument, selectedPageRangeMode, currentPrintPage, selectedPageRange)");
        source.Should().Contain("ExportPlanner.TryCreatePageRange(fromPageBox.Text, toPageBox.Text, out selectedPageRange, out var pageRangeError)");
        source.Should().Contain("ExportPlanner.TryValidatePageRange(selectedPageRange, totalPages, out var validatedPageRangeError)");
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
                "Content = UiText.Get(\"PrintPreview_AllPagesLabel\")",
                "Content = UiText.Get(\"PrintPreview_CurrentPageLabel\")",
                "Content = UiText.Get(\"PrintPreview_PagesLabel\")"
            ]);
        accessKeys.Should().OnlyHaveUniqueItems("Print Preview range choices share one access-key scope");
    }

    private static string ReadPrintPreviewDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewDialog.Layout.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewDialog.Helpers.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewSettingsPanelFactory.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewToolbarPlanner.cs")));

    private static char ExtractAccessKey(string label)
    {
        var underscoreIndex = label.IndexOf('_', StringComparison.Ordinal);

        underscoreIndex.Should().BeGreaterThanOrEqualTo(0, $"label '{label}' should declare an access key");
        underscoreIndex.Should().BeLessThan(label.Length - 1, $"label '{label}' should include a character after '_'");

        return char.ToUpperInvariant(label[underscoreIndex + 1]);
    }
}
