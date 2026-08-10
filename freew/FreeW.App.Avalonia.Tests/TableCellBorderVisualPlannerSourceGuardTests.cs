namespace FreeW.App.Avalonia.Tests;

public sealed class TableCellBorderVisualPlannerSourceGuardTests
{
    [Fact]
    public void AvaloniaDocumentView_UsesSharedCellBorderPlannerForPerEdgeDrawing()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("TableCellBorderVisualPlanner.Build(cellModel.Borders, PxPerPoint)");
        source.Should().Contain("TableCellBorderVisualPlan? CellBorderPlan");
        source.Should().Contain("DrawCellEdgeLine(DrawingContext context, TableCellBorderEdgeVisualPlan edge, Rect rect)");
        source.Should().Contain("edge.IsWave ? WaveBorderBrush(edge)");
        source.Should().Contain("TableCellBorderVisualPlanner.BuildStrokeSegments(");
        source.Should().Contain("waveRegistrationDip: -4.0");
        source.Should().Contain("edge.StrokeOpacity");
        source.Should().Contain("cell => cell.EffectiveFill");
        source.Should().Contain("DocumentTableCellEffectiveFillPlan.Empty");
        source.Should().NotContain("ResolveCellStyle(");
        source.Should().NotContain("DrawCellEdgeLine(DrawingContext context, CellBorderEdge? edge");
        source.Should().NotContain("TableCellBorderVisualPlanner.BuildWaveOffsets(");
        source.Should().NotContain("TableCellBorderVisualPlanner.ProjectEdgeSegment(");
        source.Should().NotContain("WaveCellBorderPoint(");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeW.slnx", parts);
}
