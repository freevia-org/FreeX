using System.Text;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class ExternalRichTextClipboardTests
{
    [Fact]
    public void Rtf1Success_PreservesParagraphsRunsFontColorUnicodeTabsAndLineBreaks()
    {
        const string rtf =
            @"{\rtf1\ansi\ansicpg1252\deff0\uc1
{\fonttbl{\f0 Calibri;}{\f1 Arial;}}
{\colortbl;\red192\green0\blue0;\red0\green128\blue0;}
\f0\fs24\b Bold\b0\tab\cf1 Red\cf0\ul Under\ul0\par
\f1\fs18\i\u945?\i0\line Plain}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Bold\tRedUnder\n\u03B1\nPlain");
        payload.Body.Paragraphs.Should().HaveCount(2);
        payload.Body.Paragraphs[0].Runs.Should().HaveCount(4);
        payload.Body.Paragraphs[0].Runs[0].Text.Should().Be("Bold");
        payload.Body.Paragraphs[0].Runs[0].FontFamily.Should().Be("Calibri");
        payload.Body.Paragraphs[0].Runs[0].FontSizePt.Should().Be(12);
        payload.Body.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        payload.Body.Paragraphs[0].Runs[1].Text.Should().Be("\t");
        payload.Body.Paragraphs[0].Runs[2].Text.Should().Be("Red");
        payload.Body.Paragraphs[0].Runs[2].Color!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        payload.Body.Paragraphs[0].Runs[3].Text.Should().Be("Under");
        payload.Body.Paragraphs[0].Runs[3].Underline.Should().BeTrue();
        payload.Body.Paragraphs[1].Runs[0].Text.Should().Be("\u03B1");
        payload.Body.Paragraphs[1].Runs[0].FontFamily.Should().Be("Arial");
        payload.Body.Paragraphs[1].Runs[0].FontSizePt.Should().Be(9);
        payload.Body.Paragraphs[1].Runs[0].Italic.Should().BeTrue();
        payload.Body.Paragraphs[1].Runs[1].Text.Should().Be("\nPlain");
    }

    [Fact]
    public void UnsupportedAndMalformedRtf_IsBoundedAndNeverThrows()
    {
        var partial = ExternalRichTextClipboardPlanner.TryParseRtf(
            Encoding.ASCII.GetBytes(@"{\rtf1\ansi Before {\*\generator ignored} After\b bold"));

        partial.Should().NotBeNull();
        partial!.PlainText.Should().Be("Before  Afterbold");
        ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes("plain text"))
            .Should().BeNull();

        var oversized = Encoding.ASCII.GetBytes(
            "{\\rtf1\\ansi " + new string('x', ExternalRichTextClipboardPlanner.MaxOutputCharacters + 1));
        ExternalRichTextClipboardPlanner.TryParseRtf(oversized).Should().BeNull();
    }

    [Fact]
    public void PlannerApply_PastesExternalFragmentWithItsRichRuns()
    {
        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(
            Encoding.ASCII.GetBytes(@"{\rtf1\ansi\b External\b0\par second}"));
        var destination = InCanvasRichClipboardPayload.FromPlainText("BeforeAfter").Body;

        payload.Should().NotBeNull();
        var updated = InCanvasRichClipboardPlanner.Apply(
            destination,
            new InCanvasEditorTextSelection(6, 6),
            payload!,
            out var caret);

        caret.Should().Be(6 + payload!.PlainText.Length);
        InCanvasTextEditPlanner.ExtractPlainText(updated).Should().Be("BeforeExternal\nsecondAfter");
        updated.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().Contain(run => run.Text == "External" && run.Bold);
    }
}
