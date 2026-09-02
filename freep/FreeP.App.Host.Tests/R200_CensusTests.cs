using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r200: FreeP's share of the census. Two of the three are cases where a fix was applied to ONE of
/// several siblings -- FlipShapeCommand got its <c>HasEffect</c> and the four commands guarded by
/// the identical predicate did not -- which is the failure mode this program keeps meeting.
/// </summary>
public class R200_CensusTests
{
    // Through the interface, whose default is true -- so a missing override reads as "has effect".
    private static bool HasEffect(IPresentationCommand command, Presentation presentation) =>
        command.HasEffect(presentation);

    private static Presentation DeckWithLockedChart(out uint shapeId)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Chart,
            Chart = new ChartShape { ChartObjectProtected = true },
            OffsetXEmu = 100_000,
            OffsetYEmu = 100_000,
            ExtentCxEmu = 500_000,
            ExtentCyEmu = 500_000,
        };
        slide.Shapes.Add(chart);
        presentation.Slides.Add(slide);
        shapeId = chart.Id;
        return presentation;
    }

    [Fact]
    public void MovingAProtectionLockedChart_HasNoEffect()
    {
        var presentation = DeckWithLockedChart(out var shapeId);

        HasEffect(new MoveShapeCommand(0, shapeId, 50_000, 50_000), presentation)
            .Should().BeFalse("the guard in Apply already refuses, so the bus must not push an entry");
    }

    [Fact]
    public void ResizingAProtectionLockedChart_HasNoEffect()
    {
        var presentation = DeckWithLockedChart(out var shapeId);

        HasEffect(new ResizeShapeCommand(0, shapeId, 0, 0, 900_000, 900_000), presentation)
            .Should().BeFalse();
    }

    [Fact]
    public void DeletingAProtectionLockedChart_HasNoEffect()
    {
        var presentation = DeckWithLockedChart(out var shapeId);

        HasEffect(new DeleteShapeCommand(0, shapeId), presentation).Should().BeFalse();
    }

    [Fact]
    public void MovingAnUnlockedShape_StillHasEffect()
    {
        // The control: only the protection guard suppresses these.
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 100_000,
            OffsetYEmu = 100_000,
            ExtentCxEmu = 500_000,
            ExtentCyEmu = 500_000,
        });
        presentation.Slides.Add(slide);

        HasEffect(new MoveShapeCommand(0, 7, 50_000, 50_000), presentation).Should().BeTrue();
    }

    // ── Re-applying formatting the run already has ────────────────────────────────────────────

    private static Presentation DeckWithRun(string font, double sizePt, out uint shapeId)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape { Id = 11, Kind = SlideShapeKind.AutoShape };
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run { Text = "hello", FontFamily = font, FontSizePt = sizePt } },
        });
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        shapeId = shape.Id;
        return presentation;
    }

    [Fact]
    public void SettingTheFontARunAlreadyHas_HasNoEffect()
    {
        var presentation = DeckWithRun("Calibri", 18, out var shapeId);

        HasEffect(new SetRunFontCommand(0, shapeId, 0, 0, "Calibri"), presentation)
            .Should().BeFalse("re-confirming the ribbon combo's current value is an ordinary action");
    }

    [Fact]
    public void SettingADifferentFont_HasEffect()
    {
        var presentation = DeckWithRun("Calibri", 18, out var shapeId);

        HasEffect(new SetRunFontCommand(0, shapeId, 0, 0, "Arial"), presentation).Should().BeTrue();
    }

    [Fact]
    public void SettingTheSizeARunAlreadyHas_HasNoEffect()
    {
        var presentation = DeckWithRun("Calibri", 18, out var shapeId);

        HasEffect(new SetRunFontSizeCommand(0, shapeId, 0, 0, 18), presentation).Should().BeFalse();
    }

    [Fact]
    public void SettingADifferentSize_HasEffect()
    {
        var presentation = DeckWithRun("Calibri", 18, out var shapeId);

        HasEffect(new SetRunFontSizeCommand(0, shapeId, 0, 0, 24), presentation).Should().BeTrue();
    }

    // ── Remove Link on a shape that has none ──────────────────────────────────────────────────

    [Fact]
    public void RemovingAHyperlinkFromAShapeWithNone_HasNoEffect()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape { Id = 3, Kind = SlideShapeKind.AutoShape });
        presentation.Slides.Add(slide);

        HasEffect(new SetShapeHyperlinkCommand(0, 3, null), presentation).Should().BeFalse();
    }

    [Fact]
    public void SettingAHyperlink_HasEffect()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape { Id = 3, Kind = SlideShapeKind.AutoShape });
        presentation.Slides.Add(slide);

        HasEffect(new SetShapeHyperlinkCommand(0, 3, new Hyperlink { Url = "https://example.com" }), presentation)
            .Should().BeTrue();
    }
}
