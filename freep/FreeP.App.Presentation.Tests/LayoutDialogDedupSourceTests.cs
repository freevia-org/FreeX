namespace FreeP.App.Compositor.Tests;

public sealed class LayoutDialogDedupSourceTests
{
    [Fact]
    public void HeaderFooterRenderers_DelegatePortableWorkflowToSharedSession()
    {
        foreach (var source in RendererSources("HeaderFooterDialog.cs"))
        {
            source.Should().Contain("new HeaderFooterDialogSession(editor, focus)");
            source.Should().Contain("var initial = _session.State;");
            source.Should().Contain("ItemsSource = initial.DateFormatOptions");
            source.Should().Contain("_session.SetInput(ReadInput()).Enabled");
            source.Should().Contain("_session.TryCommit(scope)");
            source.Should().NotContain("HeaderFooterCommandPlanner.BuildState(");
            source.Should().NotContain("HeaderFooterCommandPlanner.BuildDefaultOptions(");
            source.Should().NotContain("HeaderFooterCommandPlanner.TryApply(");
            source.Should().NotContain("HeaderFooterDialogSession.BuildEnabledState(");
            source.Should().NotContain("HeaderFooterDialogSession.CreateInput(");
            source.Should().NotContain("new HeaderFooterApplyOptions(");
            source.Should().NotContain("DateFormatOptions.FirstOrDefault");
        }
    }

    [Fact]
    public void SlideSizeRenderers_DelegatePortableWorkflowToSharedSession()
    {
        foreach (var source in RendererSources("SlideSizeDialog.cs"))
        {
            source.Should().Contain("new SlideSizeDialogSession(editor)");
            source.Should().Contain("_session.State.PresetIndex");
            source.Should().Contain("_session.SelectPreset(");
            source.Should().Contain("_session.ChangeUnit(");
            source.Should().Contain("_session.TryCommit(");
            source.Should().NotContain("SlideSizeDialogPlanner.");
            source.Should().NotContain("ToPresetIndex");
            source.Should().NotContain("PresetFromIndex");
            source.Should().NotContain("private SlideSizeDialogUnit _unit");
            source.Should().NotContain("private readonly EditingSession _editor");
            source.Should().NotContain("double.TryParse");
            source.Should().NotContain("Math.Round");
            source.Should().NotContain("SetSlideSize(");
        }
    }

    [Fact]
    public void PortableSessions_OwnPlanningValidationTransitionsAndDispatch()
    {
        var headerFooter = ReadWorkspaceFile(
            "freep", "FreeP.App.Presentation", "HeaderFooterDialogSession.cs");
        headerFooter.Should().Contain("State = BuildViewState(");
        headerFooter.Should().Contain("HeaderFooterCommandPlanner.BuildApplyPlan(");
        headerFooter.Should().Contain("HeaderFooterCommandPlanner.TryApply(_editor, plan)");

        var slideSize = ReadWorkspaceFile(
            "freep", "FreeP.App.Presentation", "SlideSizeDialogSession.cs");
        slideSize.Should().Contain("SlideSizeDialogPlanner.BuildInitialState(");
        slideSize.Should().Contain("SlideSizeDialogPlanner.BuildUnitChangeDisplay(");
        slideSize.Should().Contain("SlideSizeDialogPlanner.TryParsePositiveSize(");
        slideSize.Should().Contain("SlideSizeDialogPlanner.BuildOkResult(");
        slideSize.Should().Contain("SlideSizeDialogPlanner.TryApplyResult(");
    }

    private static IEnumerable<string> RendererSources(string fileName)
    {
        yield return ReadWorkspaceFile("freep", "FreeP.App.Host", fileName);
        yield return ReadWorkspaceFile("freep", "FreeP.App.Avalonia", fileName);
    }

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
