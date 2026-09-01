using System.Text.RegularExpressions;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowAnimationOverlayIndexSourceTests
{
    [Fact]
    public void OverlayBuild_IndexesShapesAnimationsAndParagraphBuildsOnce()
    {
        var overlaySource = TestWorkspaceFileLocator.ReadAllText(
            "freep",
            "FreeP.App.Presentation",
            "SlideShowAnimationRendererSession.cs");
        var buildSource = TestWorkspaceFileLocator.ReadAllText(
            "freep",
            "FreeP.App.Presentation",
            "SlideShowAnimationBuildPlanner.cs");
        var build = ExtractMethod(
            overlaySource,
            "public static SlideShowAnimationOverlayPlan Build(Presentation presentation, Slide slide)");

        build.Should()
            .Contain("animationsByShapeId.Add(animation.ShapeId, shapeAnimations)")
            .And.Contain("animatedShapeIdSet.Add(animation.ShapeId)")
            .And.Contain("SlideShapeTraversal.EnumerateDepthFirst(slide)")
            .And.Contain("shapesById.TryAdd(shape.Id, shape)")
            .And.Contain("shapesById.TryGetValue(shapeId, out var shape)")
            .And.Contain("ReadParagraphBuildShapeIds(slide)")
            .And.Contain("shapeAnimations.Any(animation =>")
            .And.NotContain("SlideShapeTraversal.FindById(slide, shapeId)")
            .And.NotContain("SlideShowAnimationBuildPlanner.IsParagraphBuild(slide, shapeId)")
            .And.NotContain("slide.Animations.Any(")
            .And.NotContain("slide.Animations\n                .Where(");

        foreach (var signature in new[]
                 {
                     "private static IReadOnlyList<SlideShowAnimationAuxiliaryOverlayPlan> BuildAuxiliaryLayers(",
                     "private static SlideShape? BuildFillMaskShape(",
                     "private static SlideShape? BuildLineColorShape(",
                     "private static SlideShape? BuildFontStyleShape(",
                     "private static SlideShape? BuildFontSizeShape(",
                 })
        {
            ExtractMethod(overlaySource, signature).Should()
                .Contain("IReadOnlyList<ShapeAnimation> animations")
                .And.NotContain("slide.Animations");
        }

        ExtractMethod(buildSource, "public static bool IsParagraphBuild(").Should()
            .Contain("ReadParagraphBuildShapeIds(slide).Contains(shapeId)");
        ExtractMethod(buildSource, "public static HashSet<uint> ReadParagraphBuildShapeIds(").Should()
            .Contain("foreach (var build in root.Elements(P + \"bldP\"))")
            .And.Contain("shapeIds.Add(spid)");
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
