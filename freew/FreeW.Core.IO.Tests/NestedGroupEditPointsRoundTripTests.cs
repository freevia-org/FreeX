using System.IO;

namespace FreeW.Core.IO.Tests;

public sealed class NestedGroupEditPointsRoundTripTests
{
    [Fact]
    public void NestedLeafCustomGeometry_SurvivesDocxRoundTrip()
    {
        var leaf = new Shape(ShapeKind.Rectangle, 64, 32)
        {
            CustomGeometry = CustomGeometry.RectanglePoly(),
            RotationAngle = 10,
            FlipH = true
        };
        leaf.CustomGeometry!.Segments[0] = leaf.CustomGeometry.Segments[0]
            with { Point = new CustomPoint(3_600, 7_200) };

        var inner = new DrawingGroup { WidthPt = 128, HeightPt = 76 };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 52, 28));
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((8, 8));
        inner.ChildOffsets.Add((34, 21));

        var outer = new DrawingGroup { WidthPt = 240, HeightPt = 150 };
        outer.Children.Add(inner);
        outer.Children.Add(new Shape(ShapeKind.Rectangle, 58, 28));
        outer.ChildOffsets.Add((58, 38));
        outer.ChildOffsets.Add((166, 92));
        var document = TextDocument.CreateEmpty();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        document.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        var roundTripped = DocxReader.Read(stream);
        var root = roundTripped.Blocks
            .OfType<Paragraph>()
            .SelectMany(item => item.Runs)
            .Select(item => item.DrawingGroup)
            .Single(item => item is not null)!;

        root.Children[0].Should().BeOfType<DrawingGroup>();
        var nested = (DrawingGroup)root.Children[0];
        nested.Children[1].Should().BeOfType<Shape>();
        var readLeaf = (Shape)nested.Children[1];
        readLeaf.CustomGeometry.Should().NotBeNull();
        readLeaf.CustomGeometry!.Segments[0].Point.Should().Be(new CustomPoint(3_600, 7_200));
        readLeaf.RotationAngle.Should().Be(10);
        readLeaf.FlipH.Should().BeTrue();
    }
}
