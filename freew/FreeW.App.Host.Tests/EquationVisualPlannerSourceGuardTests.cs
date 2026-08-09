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
        source.Should().Contain("BuildEquationVisualElement");
        source.Should().Contain("EquationVisualElementKind.Fraction");
        source.Should().Contain("EquationVisualElementKind.Radical");
        source.Should().Contain("EquationVisualElementKind.NAry");
        source.Should().Contain("EquationVisualElementKind.Matrix");
        source.Should().Contain("EquationVisualElementKind.Accent");
        source.Should().Contain("EquationVisualElementKind.Bar");
        source.Should().Contain("EquationVisualElementKind.Delimiter");
        source.Should().Contain("EquationVisualElementKind.GroupChar");
        source.Should().Contain("EquationVisualElementKind.FunctionApply");
        source.Should().Contain("EquationVisualBaselineRole.Superscript");
        source.Should().NotContain("Text = equation.LinearText");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeW.slnx", parts);
}
