using System.Text.RegularExpressions;

namespace FreeP.App.Compositor.Tests;

public sealed class CanvasGestureSelectionIndexSourceTests
{
    [Fact]
    public void SelectionStateCapture_IndexesTheShapeTreeOncePerRequest()
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "freep",
            "FreeP.App.Presentation",
            "CanvasGesturePlanner.cs");
        var move = ExtractMethod(source, "public static IReadOnlyList<CanvasMoveShapeState> CaptureMoveState(");
        var transform = ExtractMethod(source, "public static IReadOnlyList<CanvasTransformShapeState> CaptureTransformState(");
        var index = ExtractMethod(source, "private static Dictionary<uint, SlideShape> IndexShapesById(");

        foreach (var capture in new[] { move, transform })
        {
            capture.Should()
                .Contain("shapesById ??= IndexShapesById(slide)",
                    "empty selections should remain allocation-free")
                .And.Contain("shapesById.TryGetValue(id, out var shape)")
                .And.NotContain("ShapeHitTester.FindShape(");
        }

        index.Should()
            .Contain("SlideShapeTraversal.EnumerateDepthFirst(slide)")
            .And.Contain("shapesById.TryAdd(shape.Id, shape)",
                "the first depth-first duplicate id must retain existing lookup semantics");
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
