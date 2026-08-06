namespace FreeP.App.Compositor.Tests;

public sealed class ChartDataDialogDedupSourceTests
{
    [Fact]
    public void Renderers_DelegatePortableWorkflowToSharedSession()
    {
        foreach (var source in RendererSources())
        {
            source.Should().Contain("new ChartDataDialogSession(editor)");
            source.Should().Contain("_session.BuildDialogPlan()");
            source.Should().Contain(".ChartType.Choices");
            source.Should().Contain(".ChartType.SelectedIndex");
            source.Should().Contain(".ToolbarGroups");
            source.Should().Contain("actionHandlers[action.Id]");
            source.Should().Contain("SetAutomation(");
            source.Should().Contain("_session.SelectChartType(");
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
            source.Should().NotContain("ChartDataDialogPlanner.BuildSurfacePlan(");
            source.Should().NotContain("ChartDataDialogPlanner.ChartTypeOptions");
            source.Should().NotContain("AddSeriesLabel");
            source.Should().NotContain("RemoveSeriesLabel");
            source.Should().NotContain("MoveSeriesUpLabel");
            source.Should().NotContain("MoveCategoryRightLabel");
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

    [Fact]
    public void MotionPathRenderers_DelegateSurfaceValidationMutationAndAcceptanceToSession()
    {
        foreach (var source in RendererSources("MotionPathEditorDialog.cs"))
        {
            source.Should().Contain("new MotionPathEditorDialogSession(editor, animationIndex)");
            source.Should().Contain("_session.Surface");
            source.Should().Contain("_session.AddLine(ReadRowInputs())");
            source.Should().Contain("_session.AddCurve(ReadRowInputs())");
            source.Should().Contain("_session.Remove(ReadRowInputs(), rowIndex)");
            source.Should().Contain("_session.Submit(ReadRowInputs())");
            source.Should().Contain("MotionPathEditorRowProjection.BuildEnablement(");
            source.Should().Contain("MotionPathEditorRowProjection.Format(");
            source.Should().NotContain("MotionPathEditorRowProjection.TryParse(");
            source.Should().NotContain("MotionPathEditingPlanner.");
            source.Should().NotContain("Enum.GetValues<MotionPathSegmentKind>()");
            source.Should().NotContain("ReadRowsOrEmpty");
            source.Should().NotContain("catch (FormatException");
            source.Should().NotContain("_rows.RemoveAt(");
            source.Should().NotContain("\"Edit Motion Path\"");
            source.Should().NotContain("\"Add line\"");
            source.Should().NotContain("\"Add curve\"");
            source.Should().NotContain("\"Delete\"");
        }
    }

    private static IEnumerable<string> RendererSources()
    {
        return RendererSources("ChartDataDialog.cs");
    }

    private static IEnumerable<string> RendererSources(string fileName)
    {
        yield return ReadWorkspaceFile("freep", "FreeP.App.Host", fileName);
        yield return ReadWorkspaceFile("freep", "FreeP.App.Avalonia", fileName);
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
