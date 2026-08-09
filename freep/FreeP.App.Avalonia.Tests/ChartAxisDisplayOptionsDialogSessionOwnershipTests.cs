using System.IO;

public sealed class ChartAxisDisplayOptionsDialogSessionOwnershipTests
{
    [Theory]
    [InlineData("ChartAxisOptionsDialog.cs", "ChartAxisOptionsDialogSession")]
    [InlineData("ChartDisplayOptionsDialog.cs", "ChartDisplayOptionsDialogSession")]
    public void AvaloniaDialogsOwnOnlyNativeProjectionAndLifecycle(
        string fileName,
        string sessionType)
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", fileName));

        source.Should().Contain($"new {sessionType}(editor");
        source.Should().Contain("_session.BuildCommitPlanForTests(_form.CaptureValues())");
        source.Should().Contain("_session.Submit(ReadInput())");
        source.Should().Contain("ReadInput()");
        source.Should().Contain("ChartOptionsDialogChrome.");
        source.Should().NotContain("private readonly EditingSession");
        source.Should().NotContain("private readonly ChartAxisOptionsPlanner");
        source.Should().NotContain("private readonly ChartDisplayOptionsPlanner");
        source.Should().NotContain("_planner");
        source.Should().NotContain("_editor");
        source.Should().NotContain("UpdatePlannerFromControls");
        source.Should().NotContain("ChartDialogOptionProjection.");
        source.Should().NotContain("ApplyChart");
        source.Should().NotContain("TryParse(");
        source.Should().NotContain("NumberStyles");
    }

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(parts)}");
    }
}
