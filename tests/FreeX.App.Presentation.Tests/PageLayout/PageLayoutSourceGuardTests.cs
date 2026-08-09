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
            source.Should().NotContain("Avalonia");
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
        var rendererSource = File.ReadAllText(Path.Combine(directory, "PrintRenderer.cs"));
        var commentsSource = File.ReadAllText(Path.Combine(directory, "PrintRenderer.Comments.cs"));

        rendererSource.Should().Contain("PrintCommentSummaryPlanner.BuildPages(");
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
    }
}
