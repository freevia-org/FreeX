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
}
