using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

public sealed class ShapeEditPointsParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task OnUiThread(Action action) => Session.Dispatch(action, CancellationToken.None);

    [Fact]
    public async Task EditPoints_ConvertsShape_ExposesVertices_AndMovesThroughUndoBus()
    {
        Shape? shape = null;
        var handleCount = 0;
        var moved = false;
        await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            shape = new Shape(ShapeKind.Rectangle, 120, 60)
            {
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor = VerticalAnchor.Paragraph
                }
            };
            paragraph.Runs.Add(Run.FromShape(shape));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 0);
            view.BeginShapeEditPoints();

            handleCount = view.ActiveShapeEditPointHandleCount;
            moved = view.MoveActiveShapeEditPoint(0, 3_600, 7_200);
            view.Undo();
        });

        shape.Should().NotBeNull();
        shape!.HasCustomGeometry.Should().BeTrue();
        handleCount.Should().Be(4);
        moved.Should().BeTrue();
        shape.CustomGeometry!.Segments[0].Point.Should().Be(new CustomPoint(0, 0));
    }

    [Fact]
    public async Task EditPoints_UsesPageSpaceVertexHandleRects()
    {
        var handleCount = 0;
        await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            var shape = new Shape(ShapeKind.RoundedRectangle, 120, 60)
            {
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor = VerticalAnchor.Paragraph
                },
                CustomGeometry = CustomGeometry.RoundedRectPoly()
            };
            paragraph.Runs.Add(Run.FromShape(shape));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 0);
            view.BeginShapeEditPoints();
            handleCount = view.ShapeEditPointRectsForSelection().Count;
        });

        handleCount.Should().Be(8);
    }

    [Fact]
    public async Task EditPoints_MapsRotatedAndFlippedPageCoordinatesBackToCustomSpace()
    {
        CustomPoint? movedPoint = null;
        var moved = false;
        var target = new CustomPoint(5_400, 10_800);

        await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            var shape = new Shape(ShapeKind.Rectangle, 120, 60)
            {
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor = VerticalAnchor.Paragraph
                },
                CustomGeometry = CustomGeometry.RectanglePoly(),
                RotationAngle = 90,
                FlipH = true,
                FlipV = true
            };
            paragraph.Runs.Add(Run.FromShape(shape));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 0);
            view.BeginShapeEditPoints();

            var rect = view.FloatingShapeRects[0].Rect;
            var geometry = shape.CustomGeometry!;
            var rawX = rect.X + target.X / (double)geometry.Width * rect.Width;
            var rawY = rect.Y + target.Y / (double)geometry.Height * rect.Height;
            var cx = rect.X + rect.Width / 2;
            var cy = rect.Y + rect.Height / 2;
            var dx = rawX - cx;
            var dy = rawY - cy;
            dx = -dx;
            dy = -dy;
            var radians = shape.RotationAngle * Math.PI / 180.0;
            var pagePoint = new Point(
                cx + dx * Math.Cos(radians) - dy * Math.Sin(radians),
                cy + dx * Math.Sin(radians) + dy * Math.Cos(radians));

            moved = view.MoveActiveShapeEditPointFromPageForTest(1, pagePoint);
            movedPoint = shape.CustomGeometry!.Segments[1].Point;
        });

        moved.Should().BeTrue();
        movedPoint.Should().Be(target);
    }

    [Fact]
    public async Task EditPoints_EscapeRestoresPointAndClosesUndoGroup()
    {
        CustomPoint? pointAfterEscape = null;
        var undoGroupOpen = true;

        await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            var shape = new Shape(ShapeKind.Rectangle, 120, 60)
            {
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor = VerticalAnchor.Paragraph
                },
                CustomGeometry = CustomGeometry.RectanglePoly()
            };
            paragraph.Runs.Add(Run.FromShape(shape));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 0);
            view.BeginShapeEditPoints();
            view.BeginShapeEditPointDragForTest(0).Should().BeTrue();
            view.MoveActiveShapeEditPoint(0, 3_600, 7_200).Should().BeTrue();
            view.RaiseKeyDownForContextMenuTests(new KeyEventArgs { Key = Key.Escape });

            pointAfterEscape = shape.CustomGeometry!.Segments[0].Point;
            undoGroupOpen = view.IsShapeEditPointUndoGroupOpenForTest;
        });

        pointAfterEscape.Should().Be(new CustomPoint(0, 0));
        undoGroupOpen.Should().BeFalse();
    }

    [Fact]
    public async Task EditPoints_EscapeWithoutMoveAbortsEmptyGroupAndReleasesCapture()
    {
        var canUndoAfterEscape = true;

        await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            var shape = new Shape(ShapeKind.Rectangle, 120, 60)
            {
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor = VerticalAnchor.Paragraph
                },
                CustomGeometry = CustomGeometry.RectanglePoly()
            };
            paragraph.Runs.Add(Run.FromShape(shape));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 0);
            view.BeginShapeEditPoints();
            view.BeginShapeEditPointDragForTest(0).Should().BeTrue();
            view.RaiseKeyDownForContextMenuTests(new KeyEventArgs { Key = Key.Escape });
            canUndoAfterEscape = view.CanUndo;
        });

        canUndoAfterEscape.Should().BeFalse();
    }
}
