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
            "MainWindow.GridStatus.cs",
            "MainWindow.Startup.cs",
            "MainWindow.Viewport.cs");
        var policySource = DialogSourceTestSupport.ReadPresentationSources("PageLayout", "PageLayoutRibbonPolicyPlanner.cs");
        var actionSource = DialogSourceTestSupport.ReadPresentationSources("PageLayout", "PageLayoutRibbonActionPlanner.cs");
        var sessionSource = DialogSourceTestSupport.ReadPresentationSources("PageLayout", "PageLayoutCommandSession.cs");
        var commandPlannerSource = DialogSourceTestSupport.ReadPresentationSources("PageLayout", "PageLayoutRibbonCommandPlanner.cs");
        var inputParserSource = DialogSourceTestSupport.ReadPresentationSources("PageLayout", "PageLayoutInputParser.cs");

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
        source.Should().Contain("WorkbookThemeCommandPlanner.PlanApply(theme)");
        source.Should().NotContain("new SetWorkbookThemeCommand(theme)");
        source.Should().Contain("ApplyPageMarginsPreset(PageLayoutMarginPreset.Normal)");
        source.Should().Contain("ApplyPageOrientationPreset(PageLayoutOrientationPreset.Portrait)");
        source.Should().Contain("ApplyPagePaperSizePreset(PageLayoutPaperSizePreset.Letter)");
        source.Should().Contain("ApplyPagePaperSizePreset(PageLayoutPaperSizePreset.B4)");
        source.Should().Contain("ApplyPagePaperSizePreset(PageLayoutPaperSizePreset.B5)");
        source.Should().Contain("CreatePageLayoutCommandSession().PlanMarginsPreset(preset)");
        source.Should().Contain("CreatePageLayoutCommandSession().PlanOrientationPreset(preset)");
        source.Should().Contain("CreatePageLayoutCommandSession().PlanPaperSizePreset(preset)");
        source.Should().Contain("CreatePageLayoutCommandSession().PlanScaleToFit(scaleToFit)");
        actionSource.Should().Contain("PageLayoutRibbonPolicyPlanner.ResolveMargins(preset)");
        actionSource.Should().Contain("PageLayoutRibbonPolicyPlanner.ResolveOrientation(preset)");
        actionSource.Should().Contain("PageLayoutRibbonPolicyPlanner.ResolvePaperSize(preset)");
        actionSource.Should().Contain("PageMarginsCommandLabel");
        actionSource.Should().Contain("PrintAreaCommandLabel");
        actionSource.Should().Contain("PageBreaksCommandLabel");
        actionSource.Should().Contain("PrintGridlinesCommandLabel");
        actionSource.Should().Contain("PrintHeadingsCommandLabel");
        policySource.Should().Contain("PageLayoutMarginPreset.Wide => WorksheetPageMargins.Wide");
        policySource.Should().Contain("PageLayoutOrientationPreset.Landscape");
        policySource.Should().Contain("PageLayoutPaperSizePreset.Letter => WorksheetPaperSize.Letter");
        policySource.Should().Contain("PageLayoutPaperSizePreset.B4 => WorksheetPaperSize.B4");
        policySource.Should().Contain("PageLayoutPaperSizePreset.B5 => WorksheetPaperSize.B5");
        source.Should().Contain("CreatePageLayoutCommandSession().PlanSetBackground(background)");
        source.Should().Contain("CreatePageLayoutCommandSession().PlanClearBackground()");
        sessionSource.Should().Contain("PageLayoutRibbonCommandPlanner.BuildMarginsCommand(");
        source.Should().NotContain("new SetPageMarginsCommand(sheetId, margins)");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PageSetupDialogBtn_Click(")
            .Should().Contain("OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.DialogButton);");
        policySource.Should().Contain("PageSetupDialogPlanner.ResolveInitialFocusTarget(source)");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PrintAreaBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void PageBreaksBtn_Click(")
            .Should().Contain("OpenRibbonContextMenu(btn, cm);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void BackgroundBtn_Click(")
            .Should().Contain("BackgroundChooseMenuItem_Click(sender, e);");
        source.Should().Contain("CreatePageLayoutCommandSession().PlanSetPrintArea(range)");
        source.Should().Contain("CreatePageLayoutCommandSession().PlanClearPrintArea()");
        sessionSource.Should().Contain("PageLayoutRibbonActionPlanner.PrintAreaCommandLabel");
        source.Should().Contain("OpenPageSetupFromRibbon(PageLayoutPageSetupOpenSource.ScaleToFit)");
        source.Should().Contain("InitializePageLayoutScaleToFitControls()");
        source.Should().Contain("PlanScaleCommit(PageLayoutScaleField.Width, current, text)");
        source.Should().Contain("PlanScaleCommit(PageLayoutScaleField.Height, current, text)");
        source.Should().Contain("PlanScaleCommit(PageLayoutScaleField.Percent, current, text)");
        source.Should().Contain("SyncPageLayoutScaleToFitControls(_workbook.GetSheet(_currentSheetId))");
        policySource.Should().Contain("PageLayoutInputParser.TryParseScalePages(text, out var pagesWide)");
        policySource.Should().Contain("PageLayoutRibbonCommandPlanner.ResolveScaleToFitFromPageDimensions(");
        source.Should().Contain("CreatePageLayoutCommandSession().PlanPageBreakAction(");
        source.Should().Contain("ApplyPageBreakAction(PageBreakMenuAction.Insert)");
        source.Should().Contain("ApplyPageBreakAction(PageBreakMenuAction.Remove)");
        source.Should().Contain("ApplyPageBreakAction(PageBreakMenuAction.ResetAll)");
        source.Should().Contain("PageBreakMenuAction.Insert");
        source.Should().Contain("PageBreakMenuAction.Remove");
        sessionSource.Should().Contain("PageLayoutRibbonActionPlanner.PageBreaksCommandLabel");
        source.Should().NotContain("PageBreakActionPlanner.ResetAll()");
        source.Should().NotContain("PageLayoutRibbonCommandPlanner.PlanInsertPageBreaks(");
        source.Should().NotContain("PageLayoutRibbonCommandPlanner.PlanRemovePageBreaks(");
        commandPlannerSource.Should().NotContain("PlanInsertPageBreaks(");
        commandPlannerSource.Should().NotContain("PlanRemovePageBreaks(");
        inputParserSource.Should().NotContain("TryParseAbsoluteR1C1CellReference(");
        source.Should().Contain("PageBreakDialogPlanner.BuildDefaultInput(SheetGrid.SelectedRange)");
        source.Should().Contain("PageBreakDialogPlanner.PlanPageBreaks(");
        source.Should().Contain("CreatePageLayoutCommandSession().PlanPageBreaks(plan)");
        source.Should().Contain("CreatePageLayoutCommandSession().TryPlanPageSetup(");
        source.Should().Contain("TryExecutePageLayoutCommand(plan.Execution)");
        source.Should().Contain("CreatePageLayoutCommandSession().PlanMovePageBreak(");
        source.Should().NotContain("var rowBreaks = sheet.RowPageBreaks.ToList()");
        source.Should().NotContain("TryBuildCompositeCommandForTargets(");
        source.Should().NotContain("PageSetupDialogModel.TryBuildCommandPlan(sheet, fields, sheetId).Plan!.ToComposite()");
        source.Should().Contain("NativePrintDialogService.ShowPrinterOptionsDialog(this)");
        source.Should().Contain("new PageLayoutCommandSession([_currentSheetId]).PlanPrintGridlines(");
        source.Should().Contain("new PageLayoutCommandSession([_currentSheetId]).PlanPrintHeadings(");
        sessionSource.Should().Contain("PageLayoutRibbonActionPlanner.PrintGridlinesCommandLabel");
        sessionSource.Should().Contain("PageLayoutRibbonActionPlanner.PrintHeadingsCommandLabel");
        source.Should().NotContain("\"Print Gridlines\");");
        source.Should().NotContain("\"Print Headings\");");
    }

}
