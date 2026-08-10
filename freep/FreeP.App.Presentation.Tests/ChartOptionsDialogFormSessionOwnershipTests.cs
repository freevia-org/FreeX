namespace FreeP.App.Compositor.Tests;

public sealed class ChartOptionsDialogFormSessionOwnershipTests
{
    [Fact]
    public void NativeChartOptionFormsDelegateStateOwnershipToPortableSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        foreach (var source in new[]
                 {
                     Read(root, "freep", "FreeP.App.Host", "ChartOptionsDialogChrome.cs"),
                     Read(root, "freep", "FreeP.App.Avalonia", "ChartOptionsDialogChrome.cs"),
                 })
        {
            source.Should().Contain("ChartOptionsDialogFormSession<Control,")
                .And.Contain("_formSession.CaptureValues()")
                .And.Contain("_formSession.ApplyValues(values)")
                .And.Contain("_formSession.ApplyPlan(plan)")
                .And.NotContain("Dictionary<ChartOptionsDialogFieldId")
                .And.NotContain("foreach (var (fieldId, value) in values.Fields)")
                .And.NotContain("foreach (var field in plan.Fields.Values)")
                .And.NotContain("_applyingPlan");
        }
    }

    [Fact]
    public void PortableFormSessionOwnsRegistryProjectionAndPlanApplicationWithoutRendererDependencies()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = Read(
            root,
            "freep",
            "FreeP.App.Presentation",
            "ChartOptionsDialogFormSession.cs");

        source.Should().Contain("public sealed class ChartOptionsDialogFormSession<TControl, TRow>")
            .And.Contain("public ChartOptionsDialogValues CaptureValues()")
            .And.Contain("public void ApplyValues(ChartOptionsDialogValues values)")
            .And.Contain("public void ApplyPlan(ChartOptionsDialogPlan plan)")
            .And.Contain("public bool IsApplyingPlan { get; private set; } = true;")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
