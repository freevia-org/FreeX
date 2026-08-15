using FluentAssertions;
using Free.Shared.Ribbon;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Presentation.Tests.Ribbon;

public sealed class WorkbookPageLayoutSheetOptionsRibbonStatePlannerTests
{
    [Fact]
    public void Build_ProjectsFourIndependentCanonicalCheckboxStates()
    {
        var plan = WorkbookPageLayoutSheetOptionsRibbonStatePlanner.Build(
            viewGridlines: true,
            printGridlines: false,
            viewHeadings: false,
            printHeadings: true);

        plan.GetCommandState("View Gridlines").IsChecked.Should().BeTrue();
        plan.GetCommandState("Print Gridlines").IsChecked.Should().BeFalse();
        plan.GetCommandState("View Headings").IsChecked.Should().BeFalse();
        plan.GetCommandState("Print Headings").IsChecked.Should().BeTrue();
        plan.GetCommandState("unknown").Should().Be(RibbonCommandState.Default);
    }

    [Fact]
    public void Publish_UpdatesAllFourStatesAndDeduplicatesAnUnchangedProjection()
    {
        var store = new RibbonStateStore();
        var changes = 0;
        store.StateChanged += (_, _) => changes++;
        var plan = WorkbookPageLayoutSheetOptionsRibbonStatePlanner.Build(true, false, false, true);

        plan.Publish(store);
        var firstPublishChanges = changes;
        plan.Publish(store);

        store.GetState("View Gridlines").IsChecked.Should().BeTrue();
        store.GetState("Print Gridlines").IsChecked.Should().BeFalse();
        store.GetState("View Headings").IsChecked.Should().BeFalse();
        store.GetState("Print Headings").IsChecked.Should().BeTrue();
        firstPublishChanges.Should().Be(4);
        changes.Should().Be(firstPublishChanges);
    }

    [Fact]
    public void BothRenderers_ConsumeSharedStateAndAvaloniaUsesDirectToggleActions()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpfViewport = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Host", "MainWindow.Viewport.cs"));
        var wpfPageLayout = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Host", "MainWindow.PageLayout.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var avaloniaWires = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuWires.cs"));
        var obsoletePopup = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.SheetOptionsNotes.cs"));

        wpfViewport.Should().Contain("WorkbookPageLayoutSheetOptionsRibbonStatePlanner.Build(")
            .And.Contain(".Publish(_ribbonState);");
        wpfPageLayout.Should().Contain("CreatePageLayoutCommandSession().PlanPrintGridlines(")
            .And.Contain("CreatePageLayoutCommandSession().PlanPrintHeadings(");
        avalonia.Should().Contain("GetPageLayoutSheetOptionsRibbonState().GetCommandState(\"Print Gridlines\")")
            .And.Contain("GetPageLayoutSheetOptionsRibbonState().GetCommandState(\"Print Headings\")")
            .And.Contain("_printGridlinesMenuItem.Click += (_, _) => TogglePrintGridlines();")
            .And.Contain("_printHeadingsMenuItem.Click += (_, _) => TogglePrintHeadings();");
        avaloniaWires.Should().Contain("PageLayoutRibbonActionKind.ToggleViewGridlines => ToggleShowGridlines")
            .And.Contain("PageLayoutRibbonActionKind.TogglePrintGridlines => TogglePrintGridlines")
            .And.Contain("PageLayoutRibbonActionKind.ToggleViewHeadings => ToggleShowHeadings")
            .And.Contain("PageLayoutRibbonActionKind.TogglePrintHeadings => TogglePrintHeadings")
            .And.NotContain("ShowGridlinesSheetOptionsAsync")
            .And.NotContain("ShowHeadingsSheetOptionsAsync");
        obsoletePopup.Should().NotContain("ShowSheetOptionTwoToggleAsync")
            .And.NotContain("SheetOptionDialog");
    }
}
