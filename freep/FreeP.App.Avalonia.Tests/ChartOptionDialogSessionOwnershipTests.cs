using System.IO;

public sealed class ChartOptionDialogSessionOwnershipTests
{
    [Fact]
    public void AvaloniaBatchBDialogsOwnOnlyNativeControlProjectionAndLifecycle()
    {
        foreach (var (family, session) in DialogFamilies)
        {
            var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", $"{family}Dialog.cs"));

            source.Should().Contain($"new {session}(editor", family);
            source.Should().Contain($"ChartOptionsDialogHost<{session}>", family);
            source.Should().Contain("session.TryCommit(session.BuildInput(values)", family);
            source.Should().Contain(": base(session, session.BuildDialogPlan(", family);
            source.Should().NotContain("ReadInput()", family);
            source.Should().NotContain("ChartOptionsDialogChrome.", family);
            source.Should().NotContain("private readonly EditingSession", family);
            source.Should().NotContain("private readonly ChartAreaOptionsPlanner", family);
            source.Should().NotContain("private readonly ChartDataTableOptionsPlanner", family);
            source.Should().NotContain("private readonly ChartLayoutOptionsPlanner", family);
            source.Should().NotContain("private readonly ChartPieOptionsPlanner", family);
            source.Should().NotContain("_planner.", family);
            source.Should().NotContain("_editor.ApplyChart", family);
            source.Should().NotContain("ChartDialogOptionProjection.Parse", family);
            source.Should().NotContain("double.TryParse", family);
            source.Should().NotContain("int.TryParse", family);
        }
    }

    private static readonly (string Family, string Session)[] DialogFamilies =
    [
        ("ChartAreaOptions", "ChartAreaOptionsDialogSession"),
        ("ChartDataTableOptions", "ChartDataTableOptionsDialogSession"),
        ("ChartLayoutOptions", "ChartLayoutOptionsDialogSession"),
        ("ChartPieOptions", "ChartPieOptionsDialogSession"),
    ];

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
