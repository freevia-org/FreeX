using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class InCanvasRichClipboardTests
{
    [Fact]
    public void CaptureAndCodecRoundTrip_PreservesRunsListsSoftBreaksAndTypingStyle()
    {
        var source = RichBody();
        var typingRun = new Run
        {
            FontFamily = "Aptos Display",
            FontSizePt = 18,
            Bold = true,
            Underline = true,
            Color = new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
        };

        var payload = InCanvasRichClipboardPlanner.Capture(
            source,
            new InCanvasEditorTextSelection(0, InCanvasTextEditPlanner.ExtractPlainText(source).Length),
            typingRun);
        var decoded = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));

        decoded.Should().NotBeNull();
        decoded!.PlainText.Should().Be("Alpha\nBeta\nGamma\nOmega");
        decoded.Body.Paragraphs.Should().HaveCount(3);
        decoded.Body.Paragraphs[0].BulletKind.Should().Be(BulletKind.Auto);
        decoded.Body.Paragraphs[0].Level.Should().Be(1);
        decoded.Body.Paragraphs[0].Runs.Select(run => run.Text)
            .Should().Equal("Alpha", "\n", "Beta");
        decoded.Body.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        decoded.Body.Paragraphs[0].Runs[1].Italic.Should().BeTrue();
        decoded.Body.Paragraphs[1].BulletKind.Should().Be(BulletKind.Char);
        decoded.Body.Paragraphs[1].BulletChar.Should().Be("•");
        decoded.TypingRun!.FontFamily.Should().Be("Aptos Display");
        decoded.TypingRun.Underline.Should().BeTrue();
        decoded.TypingRun.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
    }

    [Fact]
    public void Apply_PastesRichFragmentAtSelectionAndPreservesDestinationTypingStyle()
    {
        var source = RichBody();
        var payload = InCanvasRichClipboardPlanner.Capture(
            source,
            new InCanvasEditorTextSelection(0, InCanvasTextEditPlanner.ExtractPlainText(source).Length));
        var target = Body("BeforeAfter");
        var buffer = new InCanvasRichTextEditBuffer(target);
        buffer.SelectionAndApplyForTest(payload, 6, 6, out var caret);

        caret.Should().Be(6 + payload.PlainText.Length);
        buffer.PlainText.Should().Be("BeforeAlpha\nBeta\nGamma\nOmegaAfter");
        buffer.Body.Paragraphs.Should().HaveCount(3);
        buffer.Body.Paragraphs[0].BulletKind.Should().Be(BulletKind.Auto);
        buffer.Body.Paragraphs[0].Runs.Should().Contain(run => run.Bold);
        buffer.Body.Paragraphs[1].BulletKind.Should().Be(BulletKind.Char);
        buffer.Body.Paragraphs[1].Runs.Should().Contain(run => run.Text.Contains("Gamma"));
        buffer.Body.Paragraphs[2].Runs.Should().Contain(run => run.Text.Contains("Omega"));
    }

    [Fact]
    public void PlainTextFallback_CreatesParagraphsAndUsesTypingStyle()
    {
        var payload = InCanvasRichClipboardPayload.FromPlainText(
            "one\r\ntwo",
            new InCanvasEditorTextStyleState(
                "Calibri", 12, true, false, false, false, null));

        payload.PlainText.Should().Be("one\ntwo");
        payload.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Text)
            .Should().Equal("one", "two");
        payload.TypingRun!.FontFamily.Should().Be("Calibri");
        payload.TypingRun.Bold.Should().BeTrue();
    }

    private static TextBody RichBody()
    {
        var body = new TextBody { DefaultParaAlign = TextAlign.Left };
        body.Paragraphs.Add(new Paragraph
        {
            Level = 1,
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.RomanUcPeriod,
            AutoNumStartAt = 3,
            AutoNumStartAtSpecified = true,
            Runs =
            {
                new Run
                {
                    Text = "Alpha",
                    FontFamily = "Aptos",
                    FontSizePt = 16,
                    Bold = true,
                    BoldSet = true,
                    Color = new ThemeAwareColor(SrgbColor.FromRgb(0xC00000)),
                },
                new Run { Text = "\n", Italic = true, ItalicSet = true },
                new Run { Text = "Beta", Underline = true, Strikethrough = true },
            },
        });
        body.Paragraphs.Add(new Paragraph
        {
            Level = 2,
            BulletKind = BulletKind.Char,
            BulletChar = "•",
            Align = TextAlign.Right,
            Runs = { new Run { Text = "Gamma", Hyperlink = new Hyperlink { Url = "https://example.test" } } },
        });
        body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Omega" } } });
        return body;
    }

    private static TextBody Body(string text) =>
        InCanvasRichClipboardPayload.FromPlainText(text).Body;
}

internal static class InCanvasRichTextEditBufferTestExtensions
{
    internal static void SelectionAndApplyForTest(
        this InCanvasRichTextEditBuffer buffer,
        InCanvasRichClipboardPayload payload,
        int start,
        int end,
        out int caret) =>
        buffer.ApplyClipboardPayload(
            payload,
            new InCanvasEditorTextSelection(start, end),
            out caret);
}
