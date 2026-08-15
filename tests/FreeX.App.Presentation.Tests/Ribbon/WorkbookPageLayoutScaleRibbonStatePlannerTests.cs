using FluentAssertions;
using Free.Shared.Ribbon;
using FreeX.App.Presentation.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Ribbon;

public sealed class WorkbookPageLayoutScaleRibbonStatePlannerTests
{
    [Fact]
    public void Build_ProjectsFitToPageValuesToCanonicalScaleCommands()
    {
        var plan = WorkbookPageLayoutScaleRibbonStatePlanner.Build(
            new WorksheetScaleToFit(null, 2, 3));

        plan.WidthValue.Should().Be("2 pages");
        plan.HeightValue.Should().Be("3 pages");
        plan.PercentValue.Should().Be("Automatic");
        plan.GetCommandState("Scale Width").Value.Should().Be("2 pages");
        plan.GetCommandState("Scale Height").Value.Should().Be("3 pages");
        plan.GetCommandState("Scale Percent").Value.Should().Be("Automatic");
        plan.GetCommandState("unknown").Should().Be(RibbonCommandState.Default);
    }

    [Fact]
    public void Build_ProjectsPercentModeAndNullInputThroughOneDefaultPolicy()
    {
        var percent = WorkbookPageLayoutScaleRibbonStatePlanner.Build(
            new WorksheetScaleToFit(125, null, null));
        var defaults = WorkbookPageLayoutScaleRibbonStatePlanner.Build(null);

        percent.Should().Be(new WorkbookPageLayoutScaleRibbonStatePlan(
            "Automatic", "Automatic", "125%"));
        defaults.Should().Be(new WorkbookPageLayoutScaleRibbonStatePlan(
            "Automatic", "Automatic", "100%"));
    }

    [Fact]
    public void Publish_UpdatesAllValuesAndDeduplicatesAnUnchangedProjection()
    {
        var store = new RibbonStateStore();
        var changes = 0;
        store.StateChanged += (_, _) => changes++;
        var plan = WorkbookPageLayoutScaleRibbonStatePlanner.Build(
            new WorksheetScaleToFit(null, 1, 2));

        plan.Publish(store);
        var firstPublishChanges = changes;
        plan.Publish(store);

        store.GetState("Scale Width").Value.Should().Be("1 page");
        store.GetState("Scale Height").Value.Should().Be("2 pages");
        store.GetState("Scale Percent").Value.Should().Be("Automatic");
        firstPublishChanges.Should().Be(3);
        changes.Should().Be(firstPublishChanges);
    }

    [Fact]
    public void BothRenderers_ConsumeTheSharedScaleValueProjection()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Host", "MainWindow.PageLayout.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs"));

        wpf.Should().Contain("WorkbookPageLayoutScaleRibbonStatePlanner.Build(sheet?.ScaleToFit)")
            .And.Contain("state.Publish(_ribbonState);")
            .And.NotContain("PageLayoutInputParser.FormatScalePages(scaleToFit")
            .And.NotContain("PageLayoutInputParser.FormatScalePercent(scaleToFit");

        avalonia.Should().Contain("private WorkbookPageLayoutScaleRibbonStatePlan GetPageLayoutScaleRibbonState()")
            .And.Contain("GetPageLayoutScaleRibbonState().GetCommandState(\"Scale Width\")")
            .And.Contain("GetPageLayoutScaleRibbonState().GetCommandState(\"Scale Height\")")
            .And.Contain("GetPageLayoutScaleRibbonState().GetCommandState(\"Scale Percent\")")
            .And.NotContain("PageLayoutInputParser.FormatScalePages(_session.ActiveSheet.ScaleToFit")
            .And.NotContain("PageLayoutInputParser.FormatScalePercent(_session.ActiveSheet.ScaleToFit");
    }
}
