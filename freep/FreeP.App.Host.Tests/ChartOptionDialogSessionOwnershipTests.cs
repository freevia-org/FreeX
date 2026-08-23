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
            var testSupport = ReadChartOptionsTestSupport();

            source.Should().Contain($"new {session}(editor", family);
            source.Should().Contain($"ChartOptionsDialogHost<{session}>", family);
            source.Should().Contain("session.TryCommit(session.BuildInput(values)", family);
            source.Should().Contain(": base(session, session.BuildDialogPlan(", family);
            source.Should().NotContain("ReadInput()", family);
            source.Should().NotContain("ChartOptionsDialogChrome.", family);
            source.Should().NotContain("DialogMessageHelper.ShowWarning(", family);
            source.Should().NotContain("ForTests", family);
            testSupport.Should().Contain($"partial class {family}Dialog", family);
            testSupport.Should().Contain("BuildCommitPlanForTests()", family);
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

    private static string ReadChartOptionsTestSupport()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return File.ReadAllText(Path.Combine(
            root,
            "freep",
            "TestSupport",
            "HostAccess.Wpf",
            "ChartOptionsDialogs.TestAccess.cs"));
    }
}
