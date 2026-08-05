using System.IO;

public sealed class ChartSmallOptionsDialogSessionOwnershipTests
{
    [Theory]
    [MemberData(nameof(SessionOwnedDialogs))]
    public void AvaloniaDialogLeavesPortableDecisionsAndDispatchWithSession(
        string fileName,
        string sessionType)
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", fileName));

        source.Should().Contain($"new {sessionType}(editor");
        source.Should().Contain("_session.BuildCommitPlan(ReadInput())");
        source.Should().Contain("_session.Submit(ReadInput())");
        source.Should().Contain("ReadInput()");
        source.Should().NotContain("ChartDialogOptionProjection.");
        source.Should().NotContain("_planner");
        source.Should().NotContain("_editor");
        source.Should().NotContain("UpdatePlannerFromControls");
        source.Should().NotContain("ApplyChart");
        source.Should().NotContain("TryParse(");
        source.Should().NotContain("NumberStyles");
    }

    public static TheoryData<string, string> SessionOwnedDialogs => new()
    {
        { "Chart3DViewOptionsDialog.cs", "Chart3DViewOptionsDialogSession" },
        { "ChartBubbleOptionsDialog.cs", "ChartBubbleOptionsDialogSession" },
        { "ChartPlotStyleOptionsDialog.cs", "ChartPlotStyleOptionsDialogSession" },
        { "ChartProtectionOptionsDialog.cs", "ChartProtectionOptionsDialogSession" },
        { "ChartTextOptionsDialog.cs", "ChartTextOptionsDialogSession" },
    };

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
