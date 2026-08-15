using FluentAssertions;
using FreeX.App.Presentation.Tests;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageLayoutSourceGuardTests
{
    [Fact]
    public void PageLayoutPresentationPlanners_DoNotReferencePlatformUiAssemblies()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "PageLayout");

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            var source = string.Join(
                Environment.NewLine,
                File.ReadLines(file).Where(PortableBoundaryGuard.IsNonCommentLine));

            source.Should().NotContain("System.Windows");
            source.Should().NotMatchRegex(
                @"(?m)(?:^\s*(?:global\s+)?using\s+(?:global::)?Avalonia(?:[.;])|(?<![\w.])(?:global::)?Avalonia\.[A-Za-z_]\w*)");
            source.Should().NotContain("FreeX.App.Host");
            source.Should().NotContain("FreeX.App.Avalonia");
        }
    }

    [Fact]
    public void WpfPrintRenderer_DelegatesWorksheetPrintPlanningToPresentation()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host");
        var source = File.ReadAllText(Path.Combine(directory, "PrintRenderer.cs"));

        source.Should().Contain("WorksheetPrintRenderPlanner.TryBuild(");
        source.Should().NotContain("sheet.PrintAreas");
        source.Should().NotContain("sheet.GetUsedRange()");
        source.Should().NotContain("PagePaginationPlanner.BuildPlan(");
        source.Should().NotContain("PrintPageGridPlanner.Build(");
        source.Should().NotContain("WorksheetPageLayout.GetPageSizeInches(");
    }

    [Fact]
    public void WpfPrintRenderer_DelegatesCommentSummaryPlanningToPresentation()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host");
        var presentationDirectory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "PageLayout");
        var rendererSource = File.ReadAllText(Path.Combine(directory, "PrintRenderer.cs"));
        var commentsSource = File.ReadAllText(Path.Combine(directory, "PrintRenderer.Comments.cs"));
        var contentPlannerSource = File.ReadAllText(Path.Combine(
            presentationDirectory,
            "WorksheetPrintPageContentPlanner.cs"));

        rendererSource.Should().Contain("WorksheetPrintPageContentPlanner.BuildCommentSummaryPages(");
        rendererSource.Should().NotContain("PrintCommentSummaryPlanner.BuildPages(");
        contentPlannerSource.Should().Contain("PrintCommentSummaryPlanner.BuildPages(");
        commentsSource.Should().Contain("PrintCommentSummaryPlanner.WrapOverlayText(");
        commentsSource.Should().NotContain("CommentNavigationPlanner.FormatThreadedComment(");
        commentsSource.Should().NotContain("result.Sort(static (left, right) =>");
        commentsSource.Should().NotContain("BuildCommentSummaryPages(");
    }

    [Fact]
    public void PlatformPageLayoutHandlers_DelegateCommandCompositionToPortableSessions()
    {
        var wpfDirectory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host");
        var avaloniaDirectory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Avalonia");
        var wpfSource = File.ReadAllText(Path.Combine(wpfDirectory, "MainWindow.PageLayout.cs"));
        var avaloniaSource = string.Join(
            Environment.NewLine,
            new[]
            {
                "MainWindow.PageLayout.cs",
                "MainWindow.PageLayoutRibbon.cs",
                "MainWindow.PageBreakActions.cs",
                "MainWindow.RibbonMenuWires.cs",
                "MainWindow.SheetOptionsNotes.cs",
                "MainWindow.Themes.cs",
            }
                .Select(fileName => File.ReadAllText(Path.Combine(avaloniaDirectory, fileName))));
        var combinedSource = wpfSource + Environment.NewLine + avaloniaSource;

        combinedSource.Should().Contain("PageLayoutCommandSession");
        combinedSource.Should().Contain("WorkbookThemeCommandPlanner.PlanApply(");
        combinedSource.Should().NotContain("PageLayoutRibbonCommandPlanner.Build");
        combinedSource.Should().NotContain("PageSetupCommandFactory.BuildHeaderFooterCommand");
        combinedSource.Should().NotContain("new SetWorkbookThemeCommand(");
        combinedSource.Should().NotContain("new SetPageMarginsCommand(");
        combinedSource.Should().NotContain("new SetPageOrientationCommand(");
        combinedSource.Should().NotContain("new SetPaperSizeCommand(");
        combinedSource.Should().NotContain("new SetScaleToFitCommand(");
        combinedSource.Should().NotContain("new SetPageBreaksCommand(");
        combinedSource.Should().NotContain("new SetWorksheetBackgroundCommand(");
        combinedSource.Should().NotContain("new ClearWorksheetBackgroundCommand(");
        avaloniaSource.Should().NotContain("private sealed record HeaderFooterEditorState");
        avaloniaSource.Should().NotContain("new CompositeWorkbookCommand(\"Header & Footer\"");
    }

    [Fact]
    public void PlatformPageLayoutHandlers_KeepPortableWorkflowStateInPresentation()
    {
        var wpfDirectory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host");
        var avaloniaDirectory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Avalonia");
        var wpfSource = File.ReadAllText(Path.Combine(wpfDirectory, "MainWindow.PageLayout.cs"));
        var avaloniaPageLayout = File.ReadAllText(Path.Combine(avaloniaDirectory, "MainWindow.PageLayout.cs"));
        var avaloniaRibbon = File.ReadAllText(Path.Combine(avaloniaDirectory, "MainWindow.PageLayoutRibbon.cs"));
        var avaloniaQuickActions = string.Join(
            Environment.NewLine,
            new[] { "MainWindow.RibbonMenuWires.cs", "MainWindow.PageBreakActions.cs" }
                .Select(fileName => File.ReadAllText(Path.Combine(avaloniaDirectory, fileName))));
        var combinedSource = wpfSource + Environment.NewLine + avaloniaPageLayout + Environment.NewLine + avaloniaRibbon;

        wpfSource.Should().Contain("CreatePageLayoutCommandSession().TryPlanPageSetup(");
        avaloniaPageLayout.Should().Contain("TryPlanPageSetup(");
        combinedSource.Should().NotContain("TryBuildCompositeCommandForTarget(");
        combinedSource.Should().NotContain("TryBuildCompositeCommandForTargets(");

        combinedSource.Should().Contain("PlanScaleCommit(PageLayoutScaleField.");
        combinedSource.Should().NotContain("PageLayoutRibbonPolicyPlanner.PlanScaleWidthCommit(");
        combinedSource.Should().NotContain("PageLayoutRibbonPolicyPlanner.PlanScaleHeightCommit(");
        combinedSource.Should().NotContain("PageLayoutRibbonPolicyPlanner.PlanScalePercentCommit(");

        wpfSource.Should().Contain("PlanMovePageBreak(");
        wpfSource.Should().NotContain("RowPageBreaks.ToList(");
        wpfSource.Should().NotContain("breaks.Remove(originalIndex)");

        avaloniaPageLayout.Should().Contain("HeaderFooterEditorPlanner.BuildResult(");
        avaloniaPageLayout.Should().Contain("editedState.WithPictures(");
        avaloniaPageLayout.Should().Contain("HeaderFooterEditorPlanner.EditorFieldLabelResourceKey(");
        avaloniaPageLayout.Should().NotContain("HeaderFooterEditorScope.Footer => footerPictures");
        avaloniaPageLayout.Should().NotContain("var editedHeader = ReadEditorScope(");

        avaloniaPageLayout.Should().Contain("PageLayoutStatusPlanner.ResolveCommandStatus(");
        avaloniaPageLayout.Should().Contain("plan.Execution,");
        avaloniaRibbon.Should().Contain("PageLayoutStatusPlanner.ResolveCommandStatus(");
        avaloniaRibbon.Should().Contain("commandPlan,");
        avaloniaRibbon.Should().NotContain("result.ErrorMessage ?? \"Scale to fit failed.\"");

        avaloniaRibbon.Should().Contain("ExecutePageLayoutCommandWithShellRefresh(");
        avaloniaQuickActions.Should().Contain("ExecutePageLayoutCommandWithShellRefresh(");
        avaloniaQuickActions.Should().NotContain("plan.SuccessStatusText ?? UiText.Get(\"PageBreak_Failed\")");
        avaloniaQuickActions.Should().NotContain("result.ErrorMessage ?? UiText.Get(\"RibbonWire_BackgroundSet\")");

        // "Share FreeX sheet options ribbon state" moved the gridlines/headings print options out of
        // MainWindow.SheetOptionsNotes.cs (which is now only the comment-list surface) and into the
        // Page Layout ribbon, so the print path routes through the shared command session and status
        // planner instead of resolving status locally. Assert that new ownership on both sides rather
        // than the old file, which no longer participates.
        avaloniaRibbon.Should().Contain("TogglePrintGridlines(");
        avaloniaRibbon.Should().Contain("TogglePrintHeadings(");
        avaloniaRibbon.Should().Contain("CreatePageLayoutCommandSession().PlanPrintGridlines(");
        avaloniaRibbon.Should().Contain("CreatePageLayoutCommandSession().PlanPrintHeadings(");

        var sheetOptions = File.ReadAllText(Path.Combine(avaloniaDirectory, "MainWindow.SheetOptionsNotes.cs"));
        sheetOptions.Should().NotContain("PrintOption");
        sheetOptions.Should().NotContain("ShowGridlinesSheetOptionsAsync");
        sheetOptions.Should().NotContain("ShowHeadingsSheetOptionsAsync");
        sheetOptions.Should().NotContain("result.ErrorMessage ?? UiText.Get(\"ShellLoc_CouldNotUpdatePrintOptions\")");
    }
}
