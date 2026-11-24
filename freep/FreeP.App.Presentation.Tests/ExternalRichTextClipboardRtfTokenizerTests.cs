using System.Text;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// FreeP reads RTF control words through the shared <c>Free.Shared.IO.RtfControlTokenizer</c>; these pin the
/// specification alignments that came with sharing it (see the FreeW sibling tests).
/// </summary>
public sealed class ExternalRichTextClipboardRtfTokenizerTests
{
    private static InCanvasRichClipboardPayload? Parse(string rtf) =>
        ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.Latin1.GetBytes(rtf));

    [Fact]
    public void BareMinusAfterAControlWordIsNoParameterSoTheToggleStaysOn()
    {
        // \b- used to read as \b0 (value 0); a bare '-' carries no value, so \b keeps its default of "on".
        var payload = Parse(@"{\rtf1\ansi\b-Text}");

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs[0].Runs[0].Text.Should().Be("Text");
        payload.Body.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
    }

    [Fact]
    public void ExplicitNegativeParameterStillApplies()
    {
        var payload = Parse(@"{\rtf1\ansi\b0 Text}");

        payload!.Body.Paragraphs[0].Runs[0].Bold.Should().BeFalse();
    }

    [Fact]
    public void NonAsciiByteEndsTheControlWord()
    {
        var payload = Parse("{\\rtf1\\ansi A\\paréB}");

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Contain("éB");
        payload.Body.Paragraphs.Should().HaveCount(2);
    }

    [Fact]
    public void ParametersBeyondThirtyTwoBitsAreClampedWithoutDerailingTheRead()
    {
        var payload = Parse(@"{\rtf1\ansi\fs99999999999999999999 Text\bin99999999999 skipped}");

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Text");
    }

    [Fact]
    public void FallbackSkipCountsAWholeControlWordAsOneUnit()
    {
        var payload = Parse("{\\rtf1\\ansi\\uc2\\u8364\\bullet 5Z}");

        payload!.PlainText.Should().Be("€Z");
    }

    [Fact]
    public void HexEscapeNeedsTwoHexDigits()
    {
        var payload = Parse(@"{\rtf1\ansi A\'zzB}");

        payload!.PlainText.Should().Be("AB");
    }
}
