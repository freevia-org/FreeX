using FluentAssertions;
using Free.Shared.Ribbon;
using FreeX.App.Presentation.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Ribbon;

public sealed class WorkbookViewRibbonStatePlannerTests
{
    [Fact]
    public void Build_ProjectsCompletePerWindowViewStateAndAliases()
    {
        var plan = WorkbookViewRibbonStatePlanner.Build(
            WorksheetViewMode.PageLayout,
            showGridlines: true,
            showHeadings: false,
            showRulers: true,
            showFormulas: true,
            isSplit: true);

        plan.GetCommandState("Gridlines").IsChecked.Should().BeTrue();
        plan.GetCommandState("View Gridlines").IsChecked.Should().BeTrue();
        plan.GetCommandState("Headings").IsChecked.Should().BeFalse();
        plan.GetCommandState("View Headings").IsChecked.Should().BeFalse();
        plan.GetCommandState("Ruler").Should().Match<RibbonCommandState>(state =>
            state.IsEnabled && state.IsChecked);
        plan.GetCommandState("Show Formulas").IsChecked.Should().BeTrue();
        plan.GetCommandState("Split").IsChecked.Should().BeTrue();
        plan.GetCommandState("Normal").IsChecked.Should().BeFalse();
        plan.GetCommandState("Page Layout").IsChecked.Should().BeTrue();
        plan.GetCommandState("Page Break Preview").IsChecked.Should().BeFalse();
    }

    [Theory]
    [InlineData(WorksheetViewMode.Normal, true, false, false, false)]
    [InlineData(WorksheetViewMode.PageLayout, false, true, false, true)]
    [InlineData(WorksheetViewMode.PageBreakPreview, false, false, true, false)]
    public void Build_MapsExclusiveViewModeAndRulerAvailability(
        WorksheetViewMode viewMode,
        bool normal,
        bool pageLayout,
        bool pageBreakPreview,
        bool rulerEnabled)
    {
        var plan = WorkbookViewRibbonStatePlanner.Build(
            viewMode,
            showGridlines: false,
            showHeadings: false,
            showRulers: true,
            showFormulas: false,
            isSplit: false);

        plan.NormalChecked.Should().Be(normal);
        plan.PageLayoutChecked.Should().Be(pageLayout);
        plan.PageBreakPreviewChecked.Should().Be(pageBreakPreview);
        plan.RulerEnabled.Should().Be(rulerEnabled);
    }

    [Fact]
    public void Publish_UpdatesEveryCanonicalViewStateAndDeduplicatesSecondProjection()
    {
        var store = new RibbonStateStore();
        var changes = 0;
        store.StateChanged += (_, _) => changes++;
        var plan = WorkbookViewRibbonStatePlanner.Build(
            WorksheetViewMode.PageBreakPreview,
            showGridlines: true,
            showHeadings: true,
            showRulers: false,
            showFormulas: true,
            isSplit: true);

        plan.Publish(store);
        var firstPublishChanges = changes;
        plan.Publish(store);

        store.GetState("Show Formulas").IsChecked.Should().BeTrue();
        store.GetState("Split").IsChecked.Should().BeTrue();
        store.GetState("Page Break Preview").IsChecked.Should().BeTrue();
        store.GetState("Ruler").IsEnabled.Should().BeFalse();
        firstPublishChanges.Should().BeGreaterThan(0);
        changes.Should().Be(firstPublishChanges);
    }

    [Fact]
    public void BothRenderers_ConsumeSharedPerWindowViewProjection()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpfViewport = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Host", "MainWindow.Viewport.cs"));
        var wpfCommands = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs"));

        wpfViewport.Should().Contain("WorkbookViewRibbonStatePlanner.Build(");
        wpfViewport.Should().Contain(".Publish(_ribbonState);");
        wpfViewport.Should().Contain("viewState.ShowFormulas");
        wpfViewport.Should().Contain("viewState.SplitRow is not null || viewState.SplitColumn is not null");
        wpfViewport.Should().NotContain("_ribbonState.SetChecked(\"Split\"");
        wpfCommands.Should().NotContain("SyncWorkbookViewModeToggleState");

        avalonia.Should().Contain("private WorkbookViewRibbonStatePlan GetWorkbookViewRibbonState()");
        avalonia.Should().Contain("[\"Show Formulas\"] = () => GetWorkbookViewRibbonState().GetCommandState(\"Show Formulas\")");
        avalonia.Should().Contain("[\"Split\"] = () => GetWorkbookViewRibbonState().GetCommandState(\"Split\")");
        avalonia.Should().Contain("[\"View Gridlines\"] = () => GetWorkbookViewRibbonState().GetCommandState(\"View Gridlines\")");
        avalonia.Should().Contain("[\"View Headings\"] = () => GetWorkbookViewRibbonState().GetCommandState(\"View Headings\")");
    }
}
