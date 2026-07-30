using System.IO;
using System.Windows.Documents;
using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using ModelTextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Host.Tests;

public sealed class ShapeEditPointsInteractionTests
{
    [StaFact]
    public void SetSelectedShapeTextDirection_updates_text_box_model_for_wpf_route()
    {
        var shape = Shape.TextBoxWith("Rotate me", widthPt: 120, heightPt: 60);
        var document = TextDocument.CreateEmpty();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        var container = view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(item => item.Inlines)
            .OfType<InlineUIContainer>()
            .Single();
        view.Selection.Select(container.ElementStart, container.ElementEnd);
        view.CaretPosition = container.ElementStart;

        view.SetSelectedShapeTextDirection(ShapeTextDirection.Rotate90);

        shape.TextDirection.Should().Be(ShapeTextDirection.Rotate90);
    }

    [StaFact]
    public void SetSelectedShapeTextDirection_targets_nested_group_child_and_docx_round_trips_it()
    {
        var leaf = Shape.TextBoxWith("Nested direction", 120, 60);
        leaf.RotationAngle = 17;
        leaf.FlipH = true;
        var sibling = Shape.TextBoxWith("Sibling", 90, 40);
        var inner = new DrawingGroup { WidthPt = 160, HeightPt = 80, RotationAngle = 23 };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 20, 20));
        inner.ChildOffsets.Add((0, 0));
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((30, 10));
        var outer = new DrawingGroup { WidthPt = 240, HeightPt = 120, RotationAngle = 31, FlipV = true };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((12, 8));
        outer.Children.Add(sibling);
        outer.ChildOffsets.Add((180, 70));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        view.SelectFloatingGroupChild(outer, [0, 1]);
        view.SetSelectedShapeTextDirection(ShapeTextDirection.Rotate90);
        leaf.TextDirection.Should().Be(ShapeTextDirection.Rotate90);
        sibling.TextDirection.Should().Be(ShapeTextDirection.Horizontal);
        view.Undo();
        leaf.TextDirection.Should().Be(ShapeTextDirection.Horizontal);
        view.Redo();
        leaf.TextDirection.Should().Be(ShapeTextDirection.Rotate90);

        view.SelectFloatingGroupChild(outer, [0, 1]);
        view.SetSelectedShapeTextDirection(ShapeTextDirection.Rotate270);
        leaf.TextDirection.Should().Be(ShapeTextDirection.Rotate270);
        view.SelectFloatingGroupChild(outer, [0, 1]);
        view.SetSelectedShapeTextDirection(ShapeTextDirection.Horizontal);
        leaf.TextDirection.Should().Be(ShapeTextDirection.Horizontal);

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        var reopened = DocxReader.Read(stream);
        var reopenedOuter = ((Paragraph)reopened.Blocks[0]).Runs[0].DrawingGroup!;
        var reopenedInner = (DrawingGroup)reopenedOuter.Children[0];
        var reopenedLeaf = (Shape)reopenedInner.Children[1];
        reopenedLeaf.TextDirection.Should().Be(ShapeTextDirection.Horizontal);
        reopenedOuter.RotationAngle.Should().Be(31);
        reopenedOuter.FlipV.Should().BeTrue();
        reopenedInner.RotationAngle.Should().Be(23);
        reopenedLeaf.RotationAngle.Should().Be(17);
        reopenedLeaf.FlipH.Should().BeTrue();
    }

    [StaFact]
    public void SetSelectedShapeAlignment_formats_nested_shape_text_but_direct_shape_keeps_document_paragraph_alignment()
    {
        var leaf = Shape.TextBoxWith("Nested alignment", 120, 60);
        var sibling = Shape.TextBoxWith("Sibling", 90, 40);
        var inner = new DrawingGroup { WidthPt = 160, HeightPt = 80, RotationAngle = 23 };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 20, 20));
        inner.ChildOffsets.Add((0, 0));
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((30, 10));
        var outer = new DrawingGroup { WidthPt = 240, HeightPt = 120, RotationAngle = 31, FlipV = true };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((12, 8));
        outer.Children.Add(sibling);
        outer.ChildOffsets.Add((180, 70));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { Alignment = ModelTextAlignment.Right }
        };
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        view.SelectFloatingGroupChild(outer, [0, 1]);
        view.SetSelectedShapeAlignment(ModelTextAlignment.Justify);

        leaf.TextParagraphs.Should().OnlyContain(item => item.Formatting.Alignment == ModelTextAlignment.Justify);
        sibling.TextParagraphs.Should().OnlyContain(item => item.Formatting.Alignment == ModelTextAlignment.Left);
        paragraph.Formatting.Alignment.Should().Be(ModelTextAlignment.Right);
        outer.RotationAngle.Should().Be(31);
        outer.FlipV.Should().BeTrue();
        inner.RotationAngle.Should().Be(23);
        leaf.RotationAngle.Should().Be(0);

        view.Undo();
        leaf.TextParagraphs.Should().OnlyContain(item => item.Formatting.Alignment == ModelTextAlignment.Left);
        view.Redo();
        leaf.TextParagraphs.Should().OnlyContain(item => item.Formatting.Alignment == ModelTextAlignment.Justify);

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        var reopened = DocxReader.Read(stream);
        var reopenedOuter = ((Paragraph)reopened.Blocks[0]).Runs[0].DrawingGroup!;
        var reopenedInner = (DrawingGroup)reopenedOuter.Children[0];
        var reopenedLeaf = (Shape)reopenedInner.Children[1];
        reopenedLeaf.TextParagraphs.Should().OnlyContain(item => item.Formatting.Alignment == ModelTextAlignment.Justify);
        reopenedOuter.RotationAngle.Should().Be(31);
        reopenedOuter.FlipV.Should().BeTrue();
        reopenedInner.RotationAngle.Should().Be(23);
    }

    [StaFact]
    public void SetSelectedShapeAlignment_direct_shape_still_aligns_the_containing_document_paragraph()
    {
        var shape = Shape.TextBoxWith("Direct shape", 120, 60);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        var container = view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(item => item.Inlines)
            .OfType<InlineUIContainer>()
            .Single();
        view.Selection.Select(container.ElementStart, container.ElementEnd);
        view.CaretPosition = container.ElementStart;

        view.SetSelectedShapeAlignment(ModelTextAlignment.Center);

        var modelParagraph = (Paragraph)view.Model.Blocks[0];
        modelParagraph.Formatting.Alignment.Should().Be(ModelTextAlignment.Center);
        modelParagraph.Runs.Single().Shape!.TextParagraphs.Single().Formatting.Alignment
            .Should().Be(ModelTextAlignment.Left);
    }

    [StaFact]
    public void BeginShapeEditPoints_ConvertsAndMovesTheSelectedVertexThroughTheCommandBus()
    {
        var shape = new Shape(ShapeKind.Rectangle, 120, 60);
        var document = TextDocument.CreateEmpty();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        var container = view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(item => item.Inlines)
            .OfType<InlineUIContainer>()
            .Single();
        view.Selection.Select(container.ElementStart, container.ElementEnd);
        view.CaretPosition = container.ElementStart;

        view.BeginShapeEditPoints();

        shape.CustomGeometry.Should().NotBeNull();
        shape.CustomGeometry!.Segments[0].Point.Should().Be(new CustomPoint(0, 0));

        view.MoveActiveShapeEditPoint(segmentIndex: 0, x: 3_600, y: 7_200).Should().BeTrue();

        shape.CustomGeometry.Segments[0].Point.Should().Be(new CustomPoint(3_600, 7_200));
    }

    [StaFact]
    public void BeginShapeEditPoints_AddsOneHandleForEachCustomGeometryVertex()
    {
        var shape = new Shape(ShapeKind.Rectangle, 120, 60)
        {
            CustomGeometry = CustomGeometry.RectanglePoly()
        };
        var document = TextDocument.CreateEmpty();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        var container = view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(item => item.Inlines)
            .OfType<InlineUIContainer>()
            .Single();
        view.Selection.Select(container.ElementStart, container.ElementEnd);
        view.CaretPosition = container.ElementStart;

        var window = new Window
        {
            Content = new AdornerDecorator { Child = view },
            Width = 640,
            Height = 480,
            ShowInTaskbar = false
        };
        try
        {
            window.Show();
            view.UpdateLayout();

            view.BeginShapeEditPoints();

            view.ActiveShapeEditPointHandleCount.Should().Be(4);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MoveActiveShapeEditPoint_PreservesCubicBezierControls()
    {
        var geometry = new CustomGeometry();
        geometry.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(0, 10_800)));
        geometry.Segments.Add(new CustomSegment(
            CustomSegmentKind.CubicBezierTo,
            new CustomPoint(21_600, 10_800),
            new CustomPoint(7_200, 0),
            new CustomPoint(14_400, 21_600)));
        var shape = new Shape(ShapeKind.Rectangle, 120, 60) { CustomGeometry = geometry };
        var document = TextDocument.CreateEmpty();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        var container = view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(item => item.Inlines)
            .OfType<InlineUIContainer>()
            .Single();
        view.Selection.Select(container.ElementStart, container.ElementEnd);
        view.CaretPosition = container.ElementStart;
        view.BeginShapeEditPoints();

        view.MoveActiveShapeEditPoint(segmentIndex: 1, x: 20_000, y: 9_000).Should().BeTrue();

        var cubic = shape.CustomGeometry!.Segments[1];
        cubic.Point.Should().Be(new CustomPoint(20_000, 9_000));
        cubic.ControlPoint1.Should().Be(new CustomPoint(7_200, 0));
        cubic.ControlPoint2.Should().Be(new CustomPoint(14_400, 21_600));
    }

    [StaFact]
    public void InlineCustomGeometry_RendersCubicSegmentAsBezier()
    {
        var geometry = new CustomGeometry();
        geometry.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(0, 10_800)));
        geometry.Segments.Add(new CustomSegment(
            CustomSegmentKind.CubicBezierTo,
            new CustomPoint(21_600, 10_800),
            new CustomPoint(7_200, 0),
            new CustomPoint(14_400, 21_600)));
        var shape = new Shape(ShapeKind.Rectangle, 120, 60) { CustomGeometry = geometry };
        var document = TextDocument.CreateEmpty();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        var path = view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(item => item.Inlines)
            .OfType<InlineUIContainer>()
            .Single()
            .Child.Should().BeOfType<System.Windows.Shapes.Path>().Subject;

        var figure = System.Windows.Media.PathGeometry.CreateFromGeometry(path.Data).Figures.Single();

        figure.Segments.Should().ContainSingle().Which.Should().BeOfType<System.Windows.Media.BezierSegment>();
    }

    [StaFact]
    public void BeginShapeEditPoints_UsesPathAwareCommandForNestedGroupLeaf()
    {
        var inner = new DrawingGroup { WidthPt = 128, HeightPt = 76 };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 52, 28));
        var leaf = new Shape(ShapeKind.Rectangle, 64, 32) { RotationAngle = 10, FlipH = true };
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

        var view = new DocumentView();
        view.LoadModel(document);
        view.SelectFloatingGroupChild(outer, [0, 1]);
        view.BeginShapeEditPoints();

        leaf.HasCustomGeometry.Should().BeTrue();
        view.MoveActiveShapeEditPoint(0, 3_600, 7_200).Should().BeTrue();
        leaf.CustomGeometry!.Segments[0].Point.Should().Be(new CustomPoint(3_600, 7_200));
        view.Undo();
        leaf.CustomGeometry.Segments[0].Point.Should().Be(new CustomPoint(0, 0));
    }
}
