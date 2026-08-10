using System.IO;
using System.Linq;

namespace FreeW.App.Host.Tests;

public sealed class TableCellBorderVisualPlannerSourceGuardTests
{
    [Fact]
    public void WpfDocumentView_UsesSharedCellBorderPlannerAndChrome()
    {
        var viewSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));
        var chromeSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "TableCellBorderChrome.cs"));

        viewSource.Should().Contain("TableCellBorderVisualPlanner.Build(modelCell.Borders, PxPerPoint)");
        viewSource.Should().Contain("new TableCellBorderChrome");
        viewSource.Should().Contain("cell => cell.EffectiveFill");
        viewSource.Should().Contain("DocumentTableCellEffectiveFillPlan.Empty");
        viewSource.Should().NotContain("ResolveCellStyle(");
        viewSource.Should().NotContain("Use the first non-null edge colour as the cell border colour");

        chromeSource.Should().Contain("TableCellBorderVisualPlanner.BuildStrokeSegments(");
        chromeSource.Should().Contain("waveRegistrationDip: 2.0");
        chromeSource.Should().Contain("edge.StrokeOpacity");
        chromeSource.Should().NotContain("BuildWaveOffsets(");
        chromeSource.Should().NotContain("ProjectEdgeSegment(");
        chromeSource.Should().NotContain("WavePoint(");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeW.slnx", parts);
}
