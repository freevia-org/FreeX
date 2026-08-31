using System.IO;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 176. Both screen renderers opened RenderShapeEffects with
/// <c>if (shape.Text is not null &amp;&amp; shape.Fill is ResolvedFill.None) return;</c>, abandoning
/// EVERY outer effect for an unfilled text box. The case that guard exists for is the shadow -- a
/// shape with nothing in it casting a shadow is a dark blob floating behind transparent text -- but
/// taking glow and soft edge down with it made the screen disagree with print:
/// PresentationPdfExporter has never had an equivalent guard and draws the glow for exactly these
/// shapes. A glow authored on a text box was invisible on screen and present in the exported PDF.
///
/// This is a source contract because the guard is a local inside a private static render method,
/// with no seam to observe it through short of a full headless slide render in each toolkit. What
/// it pins is one sentence: the suppression is scoped to the shadow pass, and it is scoped that way
/// in BOTH renderers -- the two shells drifting apart here is the same class of defect as the
/// screen/print split it fixes.
/// </summary>
public sealed class Round176_UnfilledTextBoxEffectSuppressionTests
{
    [Fact]
    public void BothRenderersSuppressOnlyTheShadowPassForAnUnfilledTextBox()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");

        foreach (var renderer in new[] { "FreeP.App.Rendering.Wpf", "FreeP.App.Rendering.Avalonia" })
        {
            var source = File.ReadAllText(
                Path.Combine(root, "freep", renderer, "SlideCanvas.cs"));

            source.Should().NotContain(
                "if (shape.Text is not null && shape.Fill is ResolvedFill.None) return;",
                $"{renderer} must not abandon glow and soft edge along with the shadow -- the PDF " +
                "exporter draws them for these shapes, so an early return here splits screen from print");
            source.Should().Contain(
                "var suppressShadow = shape.Text is not null && shape.Fill is ResolvedFill.None;",
                $"{renderer} must compute the suppression as a shadow-scoped flag");
            source.Should().Contain(
                "if (plan.ShadowPasses.Count > 0 && !suppressShadow)",
                $"{renderer} must apply that flag to the shadow pass and to nothing else");
        }
    }
}
