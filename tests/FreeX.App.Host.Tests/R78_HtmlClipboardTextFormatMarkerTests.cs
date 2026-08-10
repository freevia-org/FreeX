using System.Collections.Generic;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R78-services-clipboard-formats-5-1
/// (src/FreeX.App.Services/HtmlClipboardTableParser.cs).
///
/// Before the fix: pasting a CF_HTML table whose &lt;td&gt; carried the
/// "mso-number-format:'\@'" Text marker -- written by
/// ClipboardHtmlSerializer.RequiresTextFormatMarker for a Text-typed source cell, and by real
/// Excel for the same reason -- was silently discarded. Since FreeX's own ExecuteCopy always
/// places CF_HTML alongside plain text, and the HTML-preferred paste path prefers the HTML rows
/// whenever present, a Text-formatted
/// "00501" round-tripped through Escape-then-paste (or paste into a different FreeX window) as the
/// bare string "00501" with no escape, which PasteCommandFactory.ParseClipboardValue then parsed
/// as the NUMBER 501 -- losing both the leading zeros and the Text type, even though the
/// plain-text clipboard sibling already carries the identical leading-apostrophe escape for
/// exactly this case (ClipboardSerializer.GetSerializedFieldText).
///
/// The shared parser detects the "mso-number-format" Text (@) marker per cell
/// (either quoting convention: FreeX's own <c>'\@'</c> or Excel's <c>"\@"</c>), and
/// HtmlClipboardTableParser applies the same ClipboardSerializer.EscapeTextCellForPaste
/// escape the plain-text path already uses.
/// </summary>
public sealed class R78_HtmlClipboardTextFormatMarkerTests
{
    private static IReadOnlyList<IReadOnlyList<string>> ParseRows(string html) =>
        HtmlClipboardTableParser.Parse(html)!;

    [Fact]
    public void TextFormattedLeadingZeroCell_KeepsLeadingApostropheEscapeFromHtmlMarker()
    {
        const string html =
            "<html><body><!--StartFragment--><table>" +
            "<tr><td style=\"mso-number-format:'\\@';\">00501</td><td>19.99</td></tr>" +
            "</table><!--EndFragment--></body></html>";

        var rows = ParseRows(html);

        rows.Should().ContainSingle();
        // The escape must survive so a subsequent paste keeps "00501" as TEXT (leading zeros
        // intact) instead of PasteCommandFactory.ParseClipboardValue coercing it to the number 501.
        rows[0].Should().Equal("'00501", "19.99");
    }

    [Fact]
    public void ExcelStyleDoubleQuotedTextFormatMarker_IsAlsoHonored()
    {
        const string html =
            "<html><body><!--StartFragment--><table>" +
            "<tr><td style='mso-number-format:\"\\@\"'>00501</td></tr>" +
            "</table><!--EndFragment--></body></html>";

        var rows = ParseRows(html);

        rows.Should().ContainSingle();
        rows[0].Should().Equal("'00501");
    }

    // Sibling no-regression: a cell with NO mso-number-format marker (the common case -- an
    // ordinary numeric or General-formatted source cell) must NOT get an apostrophe added, or
    // every plain numeric HTML-table paste would be wrongly coerced to text.
    [Fact]
    public void CellWithoutTextFormatMarker_IsNotEscaped()
    {
        const string html =
            "<html><body><!--StartFragment--><table>" +
            "<tr><td>00501</td><td>19.99</td></tr>" +
            "</table><!--EndFragment--></body></html>";

        var rows = ParseRows(html);

        rows.Should().ContainSingle();
        rows[0].Should().Equal("00501", "19.99");
    }
}
