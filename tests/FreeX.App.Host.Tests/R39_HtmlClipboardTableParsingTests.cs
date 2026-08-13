using System.Collections.Generic;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R39-io-external-clipboard-2-3: HTML clipboard paste (read side) is
/// entirely unimplemented, so a pasted web-table cell whose rendered text contains an embedded
/// line break (e.g. a two-line address, or a &lt;br&gt;) got misread by the plain-text
/// tab/newline splitter as a ROW boundary, shifting every subsequent row by one.
///
/// The parser is renderer-neutral, so this historical host regression exercises the canonical
/// service owner without driving the real OS clipboard.
/// </summary>
public sealed class R39_HtmlClipboardTableParsingTests
{
    private static IReadOnlyList<IReadOnlyList<string>>? ParseHtmlClipboardTableRows(string htmlPayload) =>
        HtmlClipboardTableParser.Parse(htmlPayload);

    [Fact]
    public void EmbeddedLineBreakInACellStaysWithinThatCellInsteadOfSplittingIntoANewRow()
    {
        const string html = """
            <html><body>
            <!--StartFragment-->
            <table><tr><td>Line1<br>Line2</td><td>B1</td></tr><tr><td>A2</td><td>B2</td></tr></table>
            <!--EndFragment-->
            </body></html>
            """;

        var rows = ParseHtmlClipboardTableRows(html);

        rows.Should().NotBeNull();
        rows!.Should().HaveCount(2);
        rows[0].Should().Equal("Line1\nLine2", "B1");
        rows[1].Should().Equal("A2", "B2");
    }

    [Fact]
    public void DecodesHtmlEntitiesAndStripsFormattingTagsFromCellText()
    {
        const string html = """
            <!--StartFragment-->
            <table><tr><td><b>Bold</b> &amp; <i>Italic</i></td></tr></table>
            <!--EndFragment-->
            """;

        var rows = ParseHtmlClipboardTableRows(html);

        rows.Should().NotBeNull();
        rows!.Should().ContainSingle();
        rows[0].Should().Equal("Bold & Italic");
    }

    [Fact]
    public void UsesThOrTdCellsAndRestrictsParsingToTheFirstTable()
    {
        const string html = """
            <!--StartFragment-->
            <table><tr><th>Header</th><td>Value</td></tr></table>
            <table><tr><td>SecondTableIgnored</td></tr></table>
            <!--EndFragment-->
            """;

        var rows = ParseHtmlClipboardTableRows(html);

        rows.Should().NotBeNull();
        rows!.Should().ContainSingle();
        rows[0].Should().Equal("Header", "Value");
    }

    [Fact]
    public void ReturnsNullWhenThePayloadHasNoTableSoThePlainTextFallbackIsUsed()
    {
        const string html = """
            <!--StartFragment-->
            <div>just some text, no table markup</div>
            <!--EndFragment-->
            """;

        var rows = ParseHtmlClipboardTableRows(html);

        rows.Should().BeNull();
    }
}
