using System.IO;
using System.Linq;

namespace FreeW.App.Host.Tests;

public sealed class TableCellBorderVisualPlannerSourceGuardTests
{
    [Fact]
    public void WpfDocumentView_UsesSharedCellBorderPlannerAndChrome()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));

        // The chrome was extracted from DocumentView into its own file; the guard follows it rather
        // than pinning the old location. What it protects is unchanged: the WPF view plans borders
        // with the shared planner and renders them through the shared chrome instead of
        // re-implementing either locally.
        var chrome = File.ReadAllText(
            RepositoryFile("freew", "FreeW.App.Host", "Editing", "TableCellBorderChrome.cs"));

        source.Should().Contain("TableCellBorderVisualPlanner.Build(modelCell.Borders, PxPerPoint)");
        source.Should().Contain("new TableCellBorderChrome(borderPlan)");
        source.Should().Contain("cell => cell.EffectiveFill");
        source.Should().Contain("DocumentTableCellEffectiveFillPlan.Empty");
        source.Should().NotContain("ResolveCellStyle(");
        source.Should().NotContain("Use the first non-null edge colour as the cell border colour");

        chrome.Should().Contain("public sealed class TableCellBorderChrome");
        chrome.Should().Contain("TableCellBorderEdgeVisualPlan edge");
        chrome.Should().Contain("BorderLineStyle.Double");
        chrome.Should().Contain("edge.Style == BorderLineStyle.Wave");
        chrome.Should().Contain("TableCellBorderVisualPlanner.BuildWaveOffsets(length)");
        chrome.Should().Contain("edge.StrokeOpacity");
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
