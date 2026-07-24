using System.Windows.Documents;
using System.Windows;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

public sealed class ShapeEditPointsInteractionTests
{
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
}
