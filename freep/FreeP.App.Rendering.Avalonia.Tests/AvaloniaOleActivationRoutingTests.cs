using Avalonia.Controls;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class AvaloniaOleActivationRoutingTests
{
    [Fact]
    public void OleDoubleClick_PrefersInPlaceHost_AndSkipsExternalActivation()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Ole,
            OleObject = new OleObjectInfo { EmbeddedBytes = [1, 2, 3] },
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
        };
        slide.Shapes.Add(shape);
        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        var inPlaceCalls = 0;
        var externalCalls = 0;
        using var handler = new AvaloniaCanvasGestureHandler(
            new SlideCanvas { Presentation = presentation, Slide = slide },
            editor,
            new SelectionAdornerLayer(),
            tryOpenOleInPlace: _ =>
            {
                inPlaceCalls++;
                return true;
            },
            tryActivateOleExternally: _ =>
            {
                externalCalls++;
                return true;
            });

        handler.HandleOleDoubleClickForTests(shape).Should().BeTrue();
        inPlaceCalls.Should().Be(1);
        externalCalls.Should().Be(0);
    }

    [Fact]
    public void OleDoubleClick_FallsBackExternally_WhenInPlaceHostDeclines()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 8,
            Kind = SlideShapeKind.Ole,
            OleObject = new OleObjectInfo { EmbeddedBytes = [4, 5, 6] },
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
        };
        slide.Shapes.Add(shape);
        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        var externalCalls = 0;
        using var handler = new AvaloniaCanvasGestureHandler(
            new SlideCanvas { Presentation = presentation, Slide = slide },
            editor,
            new SelectionAdornerLayer(),
            tryOpenOleInPlace: _ => false,
            tryActivateOleExternally: ole =>
            {
                ole.Should().BeSameAs(shape.OleObject);
                externalCalls++;
                return true;
            });

        handler.HandleOleDoubleClickForTests(shape).Should().BeTrue();
        externalCalls.Should().Be(1);
    }
}
