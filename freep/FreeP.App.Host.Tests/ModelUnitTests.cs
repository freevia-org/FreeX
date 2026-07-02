using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Unit tests for the new presentation domain model types:
/// Presentation, Slide, SlideShape, TextBody, ShapeFill, ShapeOutline, PresentationTheme.
/// </summary>
public sealed class ModelUnitTests
{
    // ── Presentation ─────────────────────────────────────────────────────────────

    [Fact]
    public void Presentation_DefaultSlideSize_Is16x9Widescreen()
    {
        var p = new Presentation();
        p.SlideSizeCxEmu.Should().Be(12192000);
        p.SlideSizeCyEmu.Should().Be(6858000);
    }

    [Fact]
    public void Presentation_CreateEmpty_HasOneSlideOneMasterOneLayout()
    {
        var p = Presentation.CreateEmpty();
        p.Slides.Should().HaveCount(1);
        p.Masters.Should().HaveCount(1);
        p.Layouts.Should().HaveCount(1);
        p.Theme.Should().NotBeNull();
    }

    [Fact]
    public void Presentation_CreateEmpty_SlideHasTitleSlide1()
    {
        var p = Presentation.CreateEmpty();
        p.Slides[0].Title.Should().Be("Slide 1");
    }

    // ── Slide ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Slide_TitleSetter_CreatesTitlePlaceholderShape()
    {
        var s = new Slide();
        s.Title = "Hello World";

        var titleShape = s.Shapes.FirstOrDefault(sh =>
            sh.Placeholder?.Type is PlaceholderType.Title or PlaceholderType.CenteredTitle);
        titleShape.Should().NotBeNull();
        titleShape!.PlainText.Should().Be("Hello World");
    }

    [Fact]
    public void Slide_TitleGetter_ReadsFromPlaceholderShape()
    {
        var s = new Slide();
        var shape = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 }
        };
        shape.Text = "My Title";
        s.Shapes.Add(shape);

        s.Title.Should().Be("My Title");
    }

    [Fact]
    public void Slide_TitleSetter_UpdatesExistingPlaceholder()
    {
        var s = new Slide { Title = "First" };
        s.Title = "Updated";

        s.Title.Should().Be("Updated");
        s.Shapes.Count(sh => sh.Placeholder?.Type == PlaceholderType.Title).Should().Be(1);
    }

    // ── SlideShape ────────────────────────────────────────────────────────────────

    [Fact]
    public void SlideShape_Text_SetterCreatesTextBody()
    {
        var shape = new SlideShape();
        shape.Text = "Hello";

        shape.TextBody.Should().NotBeNull();
        shape.PlainText.Should().Be("Hello");
    }

    [Fact]
    public void SlideShape_Text_MultilinePlainTextPreservesNewlines()
    {
        var shape = new SlideShape();
        var tb = new TextBody();
        var p1 = new Paragraph();
        p1.Runs.Add(new Run { Text = "Line 1" });
        var p2 = new Paragraph();
        p2.Runs.Add(new Run { Text = "Line 2" });
        tb.Paragraphs.Add(p1);
        tb.Paragraphs.Add(p2);
        shape.TextBody = tb;

        shape.PlainText.Should().Be("Line 1\nLine 2");
    }

    [Fact]
    public void SlideShape_DefaultKind_IsAutoShape()
    {
        var shape = new SlideShape();
        shape.Kind.Should().Be(SlideShapeKind.AutoShape);
        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Rectangle);
    }

    // ── ShapeFill ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ShapeFill_None_IsSingleton()
    {
        ShapeFill.None.Instance.Should().BeSameAs(ShapeFill.None.Instance);
    }

    [Fact]
    public void ShapeFill_Solid_ExposesColor()
    {
        var fill = new ShapeFill.Solid(new SrgbColor(0xFF, 0x00, 0x00));
        fill.Color.Resolved.R.Should().Be(0xFF);
        fill.Color.Resolved.G.Should().Be(0x00);
    }

    [Fact]
    public void ShapeFill_Gradient_DefaultAngle_Is90Degrees()
    {
        var fill = new ShapeFill.Gradient(ThemeAwareColor.Black, ThemeAwareColor.White);
        fill.AngleDegrees.Should().Be(90);
    }

    // ── ShapeOutline ──────────────────────────────────────────────────────────────

    [Fact]
    public void ShapeOutline_None_IsSingleton()
    {
        ShapeOutline.None.Instance.Should().BeSameAs(ShapeOutline.None.Instance);
    }

    [Fact]
    public void ShapeOutline_Visible_DefaultDash_IsSolid()
    {
        var outline = new ShapeOutline.Visible(SrgbColor.Black);
        outline.Dash.Should().Be(OutlineDash.Solid);
        outline.WidthPt.Should().Be(0.75);
    }

    // ── PresentationTheme ─────────────────────────────────────────────────────────

    [Fact]
    public void PresentationTheme_Default_HasOfficeColorScheme()
    {
        var theme = PresentationTheme.CreateDefault();
        // Office 2013 Accent1 = #4472C4
        theme.ColorScheme[ThemeColorSlot.Accent1].R.Should().Be(0x44);
        theme.ColorScheme[ThemeColorSlot.Accent1].G.Should().Be(0x72);
        theme.ColorScheme[ThemeColorSlot.Accent1].B.Should().Be(0xC4);
    }

    [Fact]
    public void PresentationTheme_Default_HasCalibriFont()
    {
        var theme = PresentationTheme.CreateDefault();
        theme.FontScheme.MajorLatinFont.Should().Be("Calibri Light");
        theme.FontScheme.MinorLatinFont.Should().Be("Calibri");
    }

    // ── TextBody ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TextBody_DefaultAnchor_IsNull()
    {
        // Anchor = null means "not explicitly set; inherit from layout/master".
        // The compositor resolves this to Top by default (or Middle for CenteredTitle).
        var tb = new TextBody();
        tb.Anchor.Should().BeNull("default anchor is unset so the compositor can inherit from the layout/master");
        tb.Wrap.Should().BeTrue();
        tb.AutoFit.Should().BeFalse();
        tb.AutoFitKind.Should().Be(TextAutoFitKind.None);
    }

    [Fact]
    public void Run_AllPropertiesDefaultToFalse_Except_Text()
    {
        var run = new Run { Text = "hello" };
        run.Bold.Should().BeFalse();
        run.Italic.Should().BeFalse();
        run.Underline.Should().BeFalse();
        run.FontFamily.Should().BeNull();
        run.Color.Should().BeNull();
    }

    // ── Geometry integration ──────────────────────────────────────────────────────

    [Fact]
    public void ShapeGeometryBuilder_Rectangle_HasSingleClosedContour()
    {
        var geom = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, new LayoutRect(0, 0, 100, 50));
        geom.Contours.Should().HaveCount(1);
        geom.Contours[0].Closed.Should().BeTrue();
        geom.Contours[0].Filled.Should().BeTrue();
    }

    [Fact]
    public void ShapeGeometryBuilder_Ellipse_UsesTwoArcs()
    {
        var geom = ShapeGeometryBuilder.Build(DrawingShapeKind.Ellipse, new LayoutRect(0, 0, 80, 80));
        geom.Contours.Should().HaveCount(1);
        geom.Contours[0].Segments.Should().HaveCount(2);
        geom.Contours[0].Segments.All(s => s.Kind == ShapeSegmentKind.Arc).Should().BeTrue();
    }

    [Fact]
    public void ShapeGeometryBuilder_Line_IsNotClosed()
    {
        var geom = ShapeGeometryBuilder.Build(DrawingShapeKind.Line, new LayoutRect(0, 0, 200, 1));
        geom.Contours.Should().HaveCount(1);
        geom.Contours[0].Closed.Should().BeFalse();
    }

    [Fact]
    public void ShapeGeometryBuilder_AllPresets_DoNotThrow()
    {
        var rect = new LayoutRect(0, 0, 100, 60);
        foreach (DrawingShapeKind kind in Enum.GetValues<DrawingShapeKind>())
        {
            var act = () => ShapeGeometryBuilder.Build(kind, rect);
            act.Should().NotThrow($"kind {kind} should not throw");
        }
    }

    [Fact]
    public void ShapeGeometryBuilder_ZeroSizeBounds_ReturnsEmpty()
    {
        var geom = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, new LayoutRect(0, 0, 0, 0));
        geom.Contours.Should().BeEmpty();
    }
}
