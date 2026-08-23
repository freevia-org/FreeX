using System.IO;

public sealed class ChartPointSeriesOptionsDialogSessionOwnershipTests
{
    [Fact]
    public void AvaloniaPointAndSeriesDialogsOwnOnlyNativeProjectionAndLifecycle()
    {
        foreach (var (family, session) in DialogFamilies)
        {
            var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", $"{family}Dialog.cs"));

            source.Should().Contain($"new {session}(", family);
            source.Should().Contain($"ChartOptionsDialogHost<{session}>", family);
            source.Should().Contain("session.TryCommit(session.BuildInput(values)", family);
            source.Should().NotContain("ReadInput()", family);
            source.Should().NotContain("ChartOptionsDialogChrome.", family);
            source.Should().NotContain("private readonly EditingSession", family);
            source.Should().NotContain("private readonly ChartPointOptionsPlanner", family);
            source.Should().NotContain("private readonly ChartSeriesOptionsPlanner", family);
            source.Should().NotContain("_planner.", family);
            source.Should().NotContain("_editor.ApplyChart", family);
            source.Should().NotContain("ChartDialogOptionProjection.Parse", family);
            source.Should().NotContain("double.TryParse", family);
            source.Should().NotContain("int.TryParse", family);
        }
    }

    private static readonly (string Family, string Session)[] DialogFamilies =
    [
        ("ChartPointOptions", "ChartPointOptionsDialogSession"),
        ("ChartSeriesOptions", "ChartSeriesOptionsDialogSession"),
    ];

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
