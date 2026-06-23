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
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildMarginsCommand(sheetId, WorksheetPageMargins.Normal)");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildOrientationCommand(sheetId, WorksheetPageOrientation.Portrait)");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildPaperSizeCommand(sheetId, WorksheetPaperSize.Letter)");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildSetBackgroundCommand(sheetId, background)");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildClearBackgroundCommand(sheetId)");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PageSetupDialogBtn_Click(")
            .Should().Contain("ShowPageSetupDialog(PageSetupInitialFocusTarget.PageOrientation);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PrintAreaBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PageBreaksBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void BackgroundBtn_Click(")
            .Should().Contain("BackgroundChooseMenuItem_Click(sender, e);");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildSetPrintAreaCommand(sheetId, range)");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildClearPrintAreaCommand(sheetId)");
        source.Should().Contain("ShowPageSetupDialog(PageSetupInitialFocusTarget.ScaleToFit)");
        source.Should().Contain("InitializePageLayoutScaleToFitControls()");
        source.Should().Contain("PageLayoutInputParser.TryParseScalePages(text, out var wide)");
        source.Should().Contain("SyncPageLayoutScaleToFitControls(_workbook.GetSheet(_currentSheetId))");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.ResolveScaleToFitFromPageDimensions(current, wide, current.FitToPagesTall)");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.PlanInsertPageBreaks(");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.PlanRemovePageBreaks(");
        source.Should().Contain("PageBreakDialogPlanner.BuildDefaultInput(SheetGrid.SelectedRange)");
        source.Should().Contain("PageBreakDialogPlanner.PlanPageBreaks(");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildPageBreaksCommand(sheetId, plan)");
        source.Should().Contain("PageSetupSubmissionPlanner.TryBuild(sheet, fields, dialog.RequestedAction)");
        source.Should().Contain("TryBuildCompositeCommandForTarget(sheet, sheetId)");
        source.Should().Contain("TryExecuteCommand(command, \"Page Setup\")");
        source.Should().NotContain("PageSetupDialogModel.TryBuildCommandPlan(sheet, fields, sheetId).Plan!.ToComposite()");
        source.Should().Contain("NativePrintDialogService.ShowPrinterOptionsDialog(this)");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildPrintGridlinesCommand(_currentSheetId, isChecked, sheet?.PrintHeadings ?? false)");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildPrintHeadingsCommand(_currentSheetId, sheet?.PrintGridlines ?? false, isChecked)");
    }

}
