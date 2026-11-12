using System.IO;
using System.Linq;

namespace FreeW.App.Host.Tests;

public sealed class EquationVisualPlannerSourceGuardTests
{
    [Fact]
    public void WpfEquationVisual_UsesSharedMathLayoutEngine()
    {
        var rendering = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.EquationRendering.cs"));
        var visual = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "EquationMathVisual.cs"));
        var documentView = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));

        rendering.Should().Contain("new EquationMathVisual(equation");
        rendering.Should().Contain("BaselineAlignment = BaselineAlignment.Baseline");
        visual.Should().Contain("EquationMathLayout.Layout(equation, WpfMathLayoutFont.Instance");
        visual.Should().Contain("TextBlock.SetBaselineOffset(this, Layout.BoxAscent)");
        visual.Should().Contain("DrawGlyphRun");
        documentView.Should().Contain("BuildEquationRun(equation, run.Formatting.FontSizePt, document)");
        documentView.Should().NotContain("Text = equation.LinearText");
        documentView.Should().NotContain("BuildEquationFractionElement",
            "the per-structure StackPanel approximations were replaced by the shared box-layout engine");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeW.slnx", parts);
}
