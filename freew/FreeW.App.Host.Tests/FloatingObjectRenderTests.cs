using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

public sealed class FloatingObjectRenderTests
{
    private static TextDocument DocWithFloatingShape()
    {
        var shape = new Shape(ShapeKind.Rectangle, 72, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 2
            }
        };
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument DocWithInlineShape()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(new Shape(ShapeKind.Ellipse, 60, 30)));
        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument DocWithMixedFloatingBands(out Shape behindShape, out Shape frontShape)
    {
        behindShape = new Shape(ShapeKind.Rectangle, 72, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Behind,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 99
            }
        };
        frontShape = new Shape(ShapeKind.Ellipse, 72, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 72,
                VerticalOffsetPt = 36,
                ZOrderIndex = 1
            }
        };

        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(frontShape));
        para.Runs.Add(Run.FromShape(behindShape));
        doc.Blocks.Add(para);
        return doc;
    }

    [StaFact]
    public void FloatingShape_SurvivesCommitToModel()
    {
        var original = DocWithFloatingShape();
        var view = new DocumentView();
        view.LoadModel(original);
        view.CommitToModel();
        var recovered = view.Model;

        var para = (Paragraph)recovered.Blocks[0];
        var shape = para.Runs[0].Shape;
        shape.Should().NotBeNull();
        shape!.IsFloating.Should().BeTrue();
        shape.Placement!.Wrapping.Should().Be(ImageWrapping.Square);
        shape.Placement.HorizontalOffsetPt.Should().BeApproximately(36, 0.01);
        shape.Placement.ZOrderIndex.Should().Be(2);
    }

    [StaFact]
    public void InlineShape_Unaffected_ByFloatingPath()
    {
        var original = DocWithInlineShape();
        var view = new DocumentView();
        view.LoadModel(original);
        view.CommitToModel();
        var recovered = view.Model;

        var para = (Paragraph)recovered.Blocks[0];
        var shape = para.Runs[0].Shape;
        shape.Should().NotBeNull();
        shape!.IsFloating.Should().BeFalse();
    }

    [StaFact]
    public void FloatingOverlay_UsesPlannerBandDrawOrder()
    {
        var original = DocWithMixedFloatingBands(out var behindShape, out var frontShape);
        var view = new DocumentView();
        var canvas = new System.Windows.Controls.Canvas();

        view.LoadModel(original);
        view.SetFloatingCanvas(canvas);

        canvas.Children
            .OfType<System.Windows.FrameworkElement>()
            .Select(child => child.Tag)
            .Should()
            .Equal(behindShape, frontShape);
    }
}
