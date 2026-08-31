using System.Text.RegularExpressions;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowPlaybackShapeIndexSourceTests
{
    [Fact]
    public void AnimationStep_BuildsOneLazyFirstWinsShapeIndexForFillColorResolution()
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "freep",
            "FreeP.App.Presentation",
            "SlideShowPlaybackPlanner.cs");
        var step = ExtractMethod(source, "public static IReadOnlyList<SlideShowShapeAnimationPlaybackPlan> PlanAnimationStep(");
        var index = ExtractMethod(source, "private static Dictionary<uint, SlideShape> IndexPresentationShapesById(");
        var fill = ExtractMethod(source, "private static (string? From, string? To) ResolveFillColorBehavior(");

        step.Should()
            .Contain("entry.Animation.Preset == AnimationPreset.ChangeFillColor")
            .And.Contain("shapesById = IndexPresentationShapesById(presentation)")
            .And.Contain("shapesById))");
        index.Should()
            .Contain("SlideShapeTraversal.EnumerateDepthFirst(slide)")
            .And.Contain("shapesById.TryAdd(shape.Id, shape)");
        fill.Should()
            .Contain("shapesById.GetValueOrDefault(animation.ShapeId)")
            .And.Contain(": FindSlideShape(presentation, animation.ShapeId)",
                "the standalone PlanShapeAnimation API must retain its existing lookup path");
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"method '{signature}' should exist");

        var nextMethod = Regex.Match(
            source[(start + signature.Length)..],
            @"\r?\n    (private|internal|public) static ");

        return nextMethod.Success
            ? source[start..(start + signature.Length + nextMethod.Index)]
            : source[start..];
    }
}
