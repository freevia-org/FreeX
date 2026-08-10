using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class HtmlClipboardTableParserTests
{
    [Fact]
    public void Parse_UsesTheMarkedFragmentAndFirstTableWithinIt()
    {
        const string html = """
            <table><tr><td>Outside</td></tr></table>
            <!--StartFragment-->
            <section><table><tr><th>Inside</th><td>First</td></tr></table></section>
            <table><tr><td>Second</td></tr></table>
            <!--EndFragment-->
            """;

        var rows = HtmlClipboardTableParser.Parse(html);

        rows.Should().NotBeNull();
        rows!.Should().ContainSingle();
        rows[0].Should().Equal("Inside", "First");
    }

    [Fact]
    public void Parse_WithoutFragmentMarkersFallsBackToTheWholePayload()
    {
        var rows = HtmlClipboardTableParser.Parse("<table><tr><td>Whole payload</td></tr></table>");

        rows.Should().NotBeNull();
        rows![0].Should().Equal("Whole payload");
    }

    [Fact]
    public void Parse_PreservesRowAndColumnSpans()
    {
        const string html = """
            <table>
              <tr><td rowspan="2">A</td><td colspan='2'>Header</td></tr>
              <tr><td>B</td><td>C</td></tr>
            </table>
            """;

        var rows = HtmlClipboardTableParser.Parse(html);

        rows.Should().NotBeNull();
        rows!.Should().HaveCount(2);
        rows[0].Should().Equal("A", "Header", "Header");
        rows[1].Should().Equal(string.Empty, "B", "C");
    }

    [Fact]
    public void Parse_DecodesEntitiesWhilePreservingInnerWhitespaceAndBreaks()
    {
        const string html = """
            <table><tr><td>  <strong>A &amp; <em>B</em></strong><br> C&nbsp;D  </td></tr></table>
            """;

        var rows = HtmlClipboardTableParser.Parse(html);

        rows.Should().NotBeNull();
        rows![0].Should().Equal("A & B\n C\u00a0D");
    }

    [Fact]
    public void Parse_HonorsNestedTableAndCellTags()
    {
        const string html = """
            <table><tr><td>Outer<table><tr><td>Inner</td></tr></table>End</td><td>Tail</td></tr></table>
            """;

        var rows = HtmlClipboardTableParser.Parse(html);

        rows.Should().NotBeNull();
        rows!.Should().ContainSingle();
        rows[0].Should().Equal("OuterInnerEnd", "Tail");
    }

    [Fact]
    public void Parse_PreservesImageAltTextInsteadOfDroppingIt()
    {
        const string html = """
            <table><tr><td>Before <img src="thumb.png" alt="Widget &amp; Gear"> after</td></tr></table>
            """;

        var rows = HtmlClipboardTableParser.Parse(html);

        rows.Should().NotBeNull();
        rows![0].Should().Equal("Before Widget & Gear after");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<div>No table</div>")]
    [InlineData("<table")]
    [InlineData("<table><tr><td")]
    public void Parse_MalformedOrTablelessInputReturnsNull(string? html)
    {
        HtmlClipboardTableParser.Parse(html).Should().BeNull();
    }

    [Fact]
    public void Parse_MalformedSpansFallBackToSingleCells()
    {
        const string html = """
            <table><tr><td colspan="bogus">A</td><td rowspan="0">B</td></tr></table>
            """;

        var rows = HtmlClipboardTableParser.Parse(html);

        rows.Should().NotBeNull();
        rows![0].Should().Equal("A", "B");
    }

    [Fact]
    public void Parse_PreservesTheMsoTextFormatEscape()
    {
        const string html = """
            <table><tr><td style="mso-number-format:'\@'">00501</td></tr></table>
            """;

        var rows = HtmlClipboardTableParser.Parse(html);

        rows.Should().NotBeNull();
        rows![0].Should().Equal("'00501");
    }
}
