using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R78-services-clipboard-formats-5-3
/// (src/FreeX.App.Services/HtmlClipboardTableParser.cs).
///
/// Before the fix: pasting an HTML table whose cell contained an &lt;img&gt; (e.g. a product
/// thumbnail next to its price) silently and completely lost that cell's content -- the tag-skip
/// loop only special-cased &lt;br&gt;, so every other tag (including &lt;img&gt;) contributed
/// nothing to the decoded cell text, with no user-visible sign anything was dropped and no way to
/// recover it from the paste.
///
/// The shared parser falls back to the img's alt text (the HTML author's own
/// stand-in for the image's content) when present, instead of leaving the cell blank. Full
/// picture-paste (fetching/decoding the src and creating a floating Picture object the way
/// TryPasteClipboardImage does for a pure CF_Bitmap clipboard payload) remains a larger follow-up
/// out of scope here.
/// </summary>
public sealed class R78_HtmlClipboardImageAltTextTests
{
    private static string DecodeCellText(string innerHtml) =>
        HtmlClipboardTableParser.Parse($"<table><tr><td>{innerHtml}</td></tr></table>")![0][0];

    [Fact]
    public void ImgWithAltText_FallsBackToAltTextInsteadOfBeingSilentlyDropped()
    {
        var text = DecodeCellText("<img src=\"thumb.jpg\" alt=\"Widget A\">");

        text.Should().Be("Widget A");
    }

    // Sibling no-regression: an <img> with no alt attribute at all must still decode to an empty
    // string (not throw, not produce a placeholder), and a <br> elsewhere in the same cell must be
    // completely unaffected by the new img handling.
    [Fact]
    public void ImgWithoutAltText_DecodesToEmptyStringAndBrStillBecomesNewline()
    {
        var text = DecodeCellText("Line1<br><img src=\"thumb.jpg\">Line2");

        text.Should().Be("Line1\nLine2");
    }
}
