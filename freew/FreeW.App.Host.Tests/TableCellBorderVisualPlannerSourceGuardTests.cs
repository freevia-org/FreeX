using System.IO;
using System.Linq;

namespace FreeW.App.Host.Tests;

public sealed class TableCellBorderVisualPlannerSourceGuardTests
{
    [Fact]
    public void WpfDocumentView_UsesSharedCellBorderPlannerAndChrome()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));

        source.Should().Contain("TableCellBorderVisualPlanner.Build(modelCell.Borders, PxPerPoint)");
        source.Should().Contain("private sealed class TableCellBorderChrome");
        source.Should().Contain("TableCellBorderEdgeVisualPlan edge");
        source.Should().Contain("BorderLineStyle.Double");
        source.Should().Contain("edge.Style == BorderLineStyle.Wave");
        source.Should().Contain("TableCellBorderVisualPlanner.BuildWaveOffsets(length)");
        source.Should().Contain("edge.StrokeOpacity");
        source.Should().Contain("cell => cell.EffectiveFill");
        source.Should().Contain("DocumentTableCellEffectiveFillPlan.Empty");
        source.Should().NotContain("ResolveCellStyle(");
        source.Should().NotContain("Use the first non-null edge colour as the cell border colour");
    }

    private static string RepositoryFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FreeW.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("tests run from inside the repository tree");
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }
}
