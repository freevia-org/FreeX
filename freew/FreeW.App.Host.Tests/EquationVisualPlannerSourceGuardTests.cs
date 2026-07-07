using System.IO;
using System.Linq;

namespace FreeW.App.Host.Tests;

public sealed class EquationVisualPlannerSourceGuardTests
{
    [Fact]
    public void WpfEquationVisual_UsesSharedPlannerAndStyledSegments()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));

        source.Should().Contain("EquationVisualPlanner.Build(equation)");
        source.Should().Contain("AppendEquationVisualSegment");
        source.Should().Contain("EquationVisualBaselineRole.Superscript");
        source.Should().NotContain("Text = equation.LinearText");
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
