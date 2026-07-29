using System.Windows.Documents;
using System.Windows;
using FreeW.App.Host.Editing;

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
}
