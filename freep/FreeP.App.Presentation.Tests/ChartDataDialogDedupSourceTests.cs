namespace FreeP.App.Compositor.Tests;

public sealed class ChartDataDialogDedupSourceTests
{
    [Fact]
    public void Renderers_DelegatePortableWorkflowToSharedSession()
    {
        foreach (var source in RendererSources())
        {
            source.Should().Contain("new ChartDataDialogSession(editor)");
            source.Should().Contain("ChartDataDialogPlanner.BuildSurfacePlan()");
            source.Should().Contain("_session.BuildTableProjection()");
            source.Should().Contain("_session.TryApplyEdits(");
            source.Should().Contain("_session.TryCommit(");
            source.Should().Contain("_session.RemoveActiveSeries()");
            source.Should().Contain("_session.MoveActiveSeries(delta)");
            source.Should().Contain("_session.RemoveActiveCategory()");
            source.Should().Contain("_session.MoveActiveCategory(delta)");
            source.Should().Contain("_session.SwitchRowsAndColumns()");
            source.Should().NotContain("private readonly ChartDataDialogPlanner");
            source.Should().NotContain("ChartDataDialogPlanner.FromChart(");
            source.Should().NotContain("_planner.");
            source.Should().NotContain("ReplaceChartData(");
            source.Should().NotContain("double.TryParse(");
            source.Should().NotContain("private int _activeSeriesIndex");
            source.Should().NotContain("private int _activeCategoryIndex");
        }
    }

    [Fact]
    public void Renderers_RetainOnlyNativeGridFocusAndErrorBoundaries()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartDataDialog.cs");
        wpf.Should().Contain("private readonly DataGrid _grid");
        wpf.Should().Contain("NullableDoubleConverter");
        wpf.Should().Contain("TryCommitNativeEdit()");
        wpf.Should().Contain("FindVisualDescendants<TextBox>");

        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Avalonia", "ChartDataDialog.cs");
        avalonia.Should().Contain("private readonly Grid _tableGrid");
        avalonia.Should().Contain("private ChartDataDialogEdits ReadEdits()");
        avalonia.Should().Contain("_valueBoxes[validation.InvalidValueEditIndex].TextBox.Focus()");
    }

    private static IEnumerable<string> RendererSources()
    {
        yield return ReadWorkspaceFile("freep", "FreeP.App.Host", "ChartDataDialog.cs");
        yield return ReadWorkspaceFile("freep", "FreeP.App.Avalonia", "ChartDataDialog.cs");
    }

    private static string ReadWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = directory.FullName;
            relativeParts.CopyTo(parts, 1);

            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate workspace file.",
            Path.Combine(relativeParts));
    }
}
