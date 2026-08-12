using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class ChartOptionDialogSessionOwnershipTests
{
    [Fact]
    public void WpfBatchBDialogsOwnOnlyNativeControlProjectionAndFeedback()
    {
        foreach (var (family, session) in DialogFamilies)
        {
            var source = ReadHostSource($"{family}Dialog.cs");

            source.Should().Contain($"new {session}(editor", family);
            source.Should().Contain("_session.BuildCommitPlan(_session.BuildInput(", family);
            source.Should().Contain("_session.TryCommit(", family);
            source.Should().Contain("ReadInput()", family);
            source.Should().Contain("ChartOptionsDialogChrome.", family);
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

    private static string ReadHostSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", fileName));
    }
}
