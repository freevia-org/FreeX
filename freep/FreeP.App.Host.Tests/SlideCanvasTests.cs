using System.Windows;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Smoke tests for <see cref="SlideCanvas"/>: the control constructs without throwing and
/// renders a sample slide using the real <see cref="FreeP.App.Presentation.SlideCompositor"/> pipeline.
/// STA is required because SlideCanvas is a WPF FrameworkElement.
/// </summary>
public sealed class SlideCanvasTests
{
    [StaFact]
    public void SlideCanvas_ConstructsWithNullModel_DoesNotThrow()
    {
        var canvas = new SlideCanvas();
        canvas.Should().NotBeNull();
        canvas.Presentation.Should().BeNull();
        canvas.Slide.Should().BeNull();
    }

    [StaFact]
    public void SlideCanvas_SetPresentationAndSlide_DoesNotThrow()
    {
        var canvas = new SlideCanvas();
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];

        var act = () =>
        {
            canvas.Presentation = p;
            canvas.Slide = slide;
            canvas.Refresh();
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void SlideCanvas_WithShapes_DoesNotThrow()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];

        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1143000,
            Fill = new ShapeFill.Solid(new SrgbColor(0x44, 0x72, 0xC4)),
            Outline = new ShapeOutline.Visible(SrgbColor.Black, 0.75)
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Ellipse,
            OffsetXEmu = 1000000,
            OffsetYEmu = 500000,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1500000,
        });

        var canvas = new SlideCanvas
        {
            Presentation = p,
            Slide = slide
        };

        // Force a measure (simulates layout pass) — should not throw.
        canvas.Measure(new Size(1280, 720));
        canvas.Should().NotBeNull();
    }

    [StaFact]
    public void SlideCanvas_WithTextShape_DoesNotThrow()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 }
        };
        shape.Text = "Hello FreeP!";
        slide.Shapes.Add(shape);

        var canvas = new SlideCanvas { Presentation = p, Slide = slide };

        var act = () => canvas.Measure(new Size(1280, 720));
        act.Should().NotThrow();
    }

    [StaFact]
    public void SlideCanvas_Refresh_DoesNotThrow_WhenCalledMultipleTimes()
    {
        var p = Presentation.CreateEmpty();
        var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };

        // Multiple refreshes should be idempotent.
        canvas.Refresh();
        canvas.Refresh();
        canvas.Refresh();

        canvas.Should().NotBeNull();
    }

    [StaFact]
    public void MainWindow_WithSlideCanvas_ConstructsSuccessfully()
    {
        var window = new MainWindow();
        try
        {
            window.Should().NotBeNull();
            window.Title.Should().Contain("FreeP");
            window.Content.Should().NotBeNull();
        }
        finally
        {
            window.Close();
        }
    }
}
