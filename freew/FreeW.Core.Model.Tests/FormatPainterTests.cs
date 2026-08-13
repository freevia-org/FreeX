namespace FreeW.Core.Model.Tests;

public class FormatPainterTests
{
    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static RunFormatting SourceRun() => new()
    {
        Bold = true,
        Italic = true,
        Underline = true,
        Strikethrough = true,
        SmallCaps = true,
        AllCaps = false,
        VerticalAlign = VerticalAlign.Superscript,
        FontFamily = "Cambria",
        FontSizePt = 18,
        ColorHex = "#FF0000",
        HighlightColorHex = "#FFFF00"
    };

    private static ParagraphFormatting SourceParagraph() => ParagraphFormatting.Default with
    {
        Alignment = TextAlignment.Center,
        SpaceBeforePt = 6,
        SpaceAfterPt = 12,
        LineSpacing = 2.0,
        IndentLeftPt = 36,
        FirstLineIndentPt = 18,
        ShadingColorHex = "#DDDDDD"
    };

    [Fact]
    public void ApplyTo_StampsCapturedRunFormatting_OntoTargetRun()
    {
        var source = SourceRun();
        var target = RunFormatting.Default; // an unformatted target run

        var clipboard = FormatPainterClipboard.Capture(source, ParagraphFormatting.Default);
        var result = clipboard.ApplyTo(target);

        // Applying the captured formatting reproduces the source's run formatting exactly.
        result.Should().Be(source);
    }

    [Fact]
    public void ApplyTo_ReplacesTargetFormatting_RatherThanMerging()
    {
        var source = RunFormatting.Default with { Bold = true, ColorHex = "#0000FF" };
        // A target that is heavily formatted in *different* ways — none of it should survive.
        var target = new RunFormatting
        {
            Italic = true,
            Underline = true,
            FontFamily = "Arial",
            FontSizePt = 24,
            HighlightColorHex = "#00FF00"
        };

        var result = FormatPainterClipboard.Capture(source, ParagraphFormatting.Default).ApplyTo(target);

        result.Should().Be(source);
        result.Italic.Should().BeFalse();
        result.Underline.Should().BeFalse();
        result.FontFamily.Should().BeNull();
        result.FontSizePt.Should().BeNull();
        result.HighlightColorHex.Should().BeNull();
    }

    [Fact]
    public void ApplyTo_StampsCapturedParagraphFormatting_OntoTargetParagraph()
    {
        var source = SourceParagraph();
        var target = ParagraphFormatting.Default;

        var result = FormatPainterClipboard.Capture(RunFormatting.Default, source).ApplyTo(target);

        result.Should().Be(source);
    }

    [Fact]
    public void Capture_TreatsNullSource_AsDefaultFormatting()
    {
        var clipboard = FormatPainterClipboard.Capture(null, null);

        clipboard.Run.Should().Be(RunFormatting.Default);
        clipboard.Paragraph.Should().Be(ParagraphFormatting.Default);

        // Replaying a "default capture" onto a formatted run clears it back to the document default.
        var formatted = new RunFormatting { Bold = true, FontSizePt = 20, ColorHex = "#123456" };
        clipboard.ApplyTo(formatted).Should().Be(RunFormatting.Default);
    }

    [Fact]
    public void Capture_RoundTrips_ThroughTheClipboard()
    {
        var run = SourceRun();
        var paragraph = SourceParagraph();

        var clipboard = FormatPainterClipboard.Capture(run, paragraph);

        clipboard.Run.Should().Be(run);
        clipboard.Paragraph.Should().Be(paragraph);
    }

    [Fact]
    public void ActivationSession_UsesInjectedClockForDoubleClickBoundary()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-06T10:00:00Z"));
        var session = new FormatPainterActivationSession(clock);

        session.Activate().Should().BeFalse();
        clock.Now += TimeSpan.FromMilliseconds(500);
        session.Activate().Should().BeTrue();
        clock.Now += TimeSpan.FromMilliseconds(501);
        session.Activate().Should().BeFalse();
    }

    [Fact]
    public void ActivationSession_ResetAndBackwardClockMovementStartNewGesture()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-06T10:00:00Z"));
        var session = new FormatPainterActivationSession(clock);

        session.Activate().Should().BeFalse();
        clock.Now -= TimeSpan.FromSeconds(1);
        session.Activate().Should().BeFalse();
        clock.Now += TimeSpan.FromMilliseconds(100);
        session.Reset();
        session.Activate().Should().BeFalse();
    }
}
