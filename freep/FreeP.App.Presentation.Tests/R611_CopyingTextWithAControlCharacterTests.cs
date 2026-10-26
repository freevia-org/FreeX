using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r611: copying a shape whose text carries a character XML cannot encode must not throw.
///
/// <para>This repo already knows the class: a control char or lone surrogate in model text used to
/// kill a whole SAVE, and the answer was XmlTextSanitizer behind three write boundaries -- OPC
/// packages via OpcXml.WriteXmlEntry/ReplaceXmlEntry, the ODF package writer, and any writer that
/// creates its own zip entry.</para>
///
/// <para>ExternalXamlClipboardWriter is the third kind and was not covered: it creates
/// Xaml/Document.xaml itself and writes model text through a raw XmlWriter with default settings, so
/// CheckCharacters is on and an illegal character throws ArgumentException out of Copy. Nothing on
/// the path catches it -- and the READ side of the same format is wrapped in try/catch, which is the
/// asymmetry that gives it away: the side that can ENCOUNTER a bad character defensively is guarded,
/// the side that ORIGINATES it is not.</para>
///
/// <para>Such text is reachable: it arrives from a pasted external payload or an imported file,
/// which is exactly why the sanitizer exists at the other boundaries. The offending characters are
/// built from their code points here rather than written literally, so this file stays readable and
/// carries no invisible bytes of its own.</para>
/// </summary>
public sealed class R611_CopyingTextWithAControlCharacterTests
{
    private static InCanvasRichClipboardPayload PayloadWithText(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        return new InCanvasRichClipboardPayload(body, text);
    }

    [Theory]
    [InlineData(0x0001)] // SOH: the bare control character that killed saves before the sanitizer
    [InlineData(0x000B)] // vertical tab
    [InlineData(0x001F)] // unit separator
    [InlineData(0xD800)] // lone high surrogate: not a valid scalar value
    public void CopyingSucceedsWhateverTheTextCarries(int codePoint)
    {
        var text = "before" + (char)codePoint + "after";

        var act = () => ExternalXamlClipboardPlanner.SerializeXamlPackage(PayloadWithText(text));

        act.Should().NotThrow(
            "copying a shape must not fail because its text carries a character XML cannot encode");
        act().Should().NotBeEmpty();
    }

    [Fact]
    public void OrdinaryTextStillSerializes() =>
        ExternalXamlClipboardPlanner.SerializeXamlPackage(PayloadWithText("ordinary text"))
            .Should().NotBeEmpty("the control case -- a fix must sanitize, not short-circuit the writer");
}
