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
        var policySource = DialogSourceTestSupport.ReadPresentationSources("PageLayout", "PageLayoutRibbonPolicyPlanner.cs");

        source.Should().Contain("WorkbookThemeCatalog.FreeXColorfulThemePreset.CreateTheme()");
        source.Should().Contain("WorkbookThemeCatalog.GrayscaleThemePreset.CreateTheme()");
        source.Should().Contain("WorkbookThemeCatalog.OfficeColorPreset.ApplyColors(_workbook.Theme)");
        source.Should().Contain("WorkbookThemeCatalog.ArialFontPreset.ApplyFonts(_workbook.Theme)");
        source.Should().Contain("WorkbookThemeCatalog.SubtleEffectPreset.ApplyEffects(_workbook.Theme)");
        source.Should().NotContain("WorkbookThemeWorkflow.CreateColorfulTheme()");
        source.Should().NotContain("WorkbookThemeWorkflow.CreateGrayscaleTheme()");
        source.Should().NotContain(".WithFonts(\"Arial\", \"Arial\")");
        source.Should().NotContain(".WithEffects(\"Subtle\")");
        source.Should().Contain("new WorkbookThemeDialog(_workbook.Theme, mode)");
        source.Should().Contain("ShowWorkbookThemeDialog(WorkbookThemeDialogMode.Theme)");
        source.Should().Contain("ShowWorkbookThemeDialog(WorkbookThemeDialogMode.Colors)");
        source.Should().Contain("ShowWorkbookThemeDialog(WorkbookThemeDialogMode.Effects)");
        source.Should().Contain("new SetWorkbookThemeCommand(theme)");
        source.Should().Contain("ApplyPageMarginsPreset(PageLayoutMarginPreset.Normal)");
        source.Should().Contain("ApplyPageOrientationPreset(PageLayoutOrientationPreset.Portrait)");
        source.Should().Contain("ApplyPagePaperSizePreset(PageLayoutPaperSizePreset.Letter)");
        source.Should().Contain("PageLayoutRibbonPolicyPlanner.ResolveMargins(preset)");
        source.Should().Contain("PageLayoutRibbonPolicyPlanner.ResolveOrientation(preset)");
        source.Should().Contain("PageLayoutRibbonPolicyPlanner.ResolvePaperSize(preset)");
        policySource.Should().Contain("PageLayoutMarginPreset.Wide => WorksheetPageMargins.Wide");
        policySource.Should().Contain("PageLayoutOrientationPreset.Landscape");
        policySource.Should().Contain("PageLayoutPaperSizePreset.Letter => WorksheetPaperSize.Letter");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildSetBackgroundCommand(sheetId, background)");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildClearBackgroundCommand(sheetId)");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PageSetupDialogBtn_Click(")
            .Should().Contain("OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.DialogButton);");
        policySource.Should().Contain("PageLayoutPageSetupOpenSource.PrintTitles => PageSetupInitialFocusTarget.RepeatRows");
        policySource.Should().Contain("PageLayoutPageSetupOpenSource.ScaleToFit => PageSetupInitialFocusTarget.ScaleToFit");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PrintAreaBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PageBreaksBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void BackgroundBtn_Click(")
            .Should().Contain("BackgroundChooseMenuItem_Click(sender, e);");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildSetPrintAreaCommand(sheetId, range)");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildClearPrintAreaCommand(sheetId)");
        source.Should().Contain("OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.ScaleToFit)");
        source.Should().Contain("InitializePageLayoutScaleToFitControls()");
        source.Should().Contain("PageLayoutRibbonPolicyPlanner.PlanScaleWidthCommit(current, text)");
        source.Should().Contain("PageLayoutRibbonPolicyPlanner.PlanScaleHeightCommit(current, text)");
        source.Should().Contain("PageLayoutRibbonPolicyPlanner.PlanScalePercentCommit(current, text)");
        source.Should().Contain("SyncPageLayoutScaleToFitControls(_workbook.GetSheet(_currentSheetId))");
        policySource.Should().Contain("PageLayoutInputParser.TryParseScalePages(text, out var pagesWide)");
        policySource.Should().Contain("PageLayoutRibbonCommandPlanner.ResolveScaleToFitFromPageDimensions(");
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
