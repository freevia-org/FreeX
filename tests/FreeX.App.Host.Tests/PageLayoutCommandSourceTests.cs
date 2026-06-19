using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host.Tests;

public sealed class PageLayoutCommandSourceTests
{

    [Fact]
    public void PageLayoutHandlers_RouteThroughExpectedThemePageSetupAndPrintCommands()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.PageLayout.cs",
            "MainWindow.Startup.cs",
            "MainWindow.Viewport.cs");

        source.Should().Contain("WorkbookThemeWorkflow.CreateColorfulTheme()");
        source.Should().Contain("WorkbookThemeWorkflow.CreateGrayscaleTheme()");
        source.Should().Contain("new WorkbookThemeDialog(_workbook.Theme, mode)");
        source.Should().Contain("ShowWorkbookThemeDialog(WorkbookThemeDialogMode.Theme)");
        source.Should().Contain("ShowWorkbookThemeDialog(WorkbookThemeDialogMode.Colors)");
        source.Should().Contain("ShowWorkbookThemeDialog(WorkbookThemeDialogMode.Effects)");
        source.Should().Contain("new SetWorkbookThemeCommand(theme)");
        source.Should().Contain("new SetPageMarginsCommand(sheetId, WorksheetPageMargins.Normal)");
        source.Should().Contain("new SetPageOrientationCommand(sheetId, WorksheetPageOrientation.Portrait)");
        source.Should().Contain("new SetPaperSizeCommand(sheetId, WorksheetPaperSize.Letter)");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PageSetupDialogBtn_Click(")
            .Should().Contain("ShowPageSetupDialog(PageSetupInitialFocusTarget.PageOrientation);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PrintAreaBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PageBreaksBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void BackgroundBtn_Click(")
            .Should().Contain("BackgroundChooseMenuItem_Click(sender, e);");
        source.Should().Contain("new SetPrintAreaCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId))");
        source.Should().Contain("new ClearPrintAreaCommand(sheetId)");
        source.Should().Contain("ShowPageSetupDialog(PageSetupInitialFocusTarget.ScaleToFit)");
        source.Should().Contain("InitializePageLayoutScaleToFitControls()");
        source.Should().Contain("PageLayoutInputParser.TryParseScalePages(text, out var wide)");
        source.Should().Contain("SyncPageLayoutScaleToFitControls(_workbook.GetSheet(_currentSheetId))");
        source.Should().Contain("CreateScaleToFitFromPageDimensions(current, wide, current.FitToPagesTall)");
        source.Should().Contain("PageBreakSelectionPlanner.Insert(selectedRange, sheet.RowPageBreaks, sheet.ColumnPageBreaks)");
        source.Should().Contain("PageBreakSelectionPlanner.Remove(selectedRange, sheet.RowPageBreaks, sheet.ColumnPageBreaks)");
        source.Should().Contain("new SetPageBreaksCommand(sheetId, rowBreaks, columnBreaks)");
        source.Should().Contain("PageSetupCommandBuilder.Build(sheetId, dialog)");
        source.Should().Contain("NativePrintDialogService.ShowPrinterOptionsDialog(this)");
        source.Should().Contain("new SetPrintOptionsCommand(_currentSheetId, isChecked, sheet?.PrintHeadings ?? false)");
        source.Should().Contain("new SetPrintOptionsCommand(_currentSheetId, sheet?.PrintGridlines ?? false, isChecked)");
    }

}
