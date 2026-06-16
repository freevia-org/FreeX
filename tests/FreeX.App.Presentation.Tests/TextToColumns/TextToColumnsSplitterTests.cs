using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Presentation.Tests.TextToColumns;

public sealed class TextToColumnsSplitterTests
{
    [Fact]
    public void SplitDelimited_CommaSeparated_SplitsIntoFields()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a,b,c", ",");

        fields.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void SplitDelimited_TabSeparated_SplitsIntoFields()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a\tb\tc", "\t");

        fields.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void SplitDelimited_SemicolonSeparated_SplitsIntoFields()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a;b;c", ";");

        fields.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void SplitDelimited_SpaceSeparated_SplitsIntoFields()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a b c", " ");

        fields.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void SplitDelimited_CustomCharacter_SplitsIntoFields()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a|b|c", "|");

        fields.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void SplitDelimited_MultipleDelimiterCharacters_SplitsOnAny()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a,b;c d", ",;\t ");

        fields.Should().Equal("a", "b", "c", "d");
    }

    [Fact]
    public void SplitDelimited_EmptyDelimiters_FallsBackToComma()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a,b", string.Empty);

        fields.Should().Equal("a", "b");
    }

    [Fact]
    public void SplitDelimited_ConsecutiveDelimiters_KeepsEmptyFieldsByDefault()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a,,b", ",");

        fields.Should().Equal("a", string.Empty, "b");
    }

    [Fact]
    public void SplitDelimited_ConsecutiveDelimiters_CollapsesWhenRequested()
    {
        var fields = TextToColumnsSplitter.SplitDelimited(
            "a,,b",
            ",",
            textQualifier: null,
            treatConsecutiveDelimitersAsOne: true);

        fields.Should().Equal("a", "b");
    }

    [Fact]
    public void SplitDelimited_CollapseRuns_OfMixedDelimiters()
    {
        var fields = TextToColumnsSplitter.SplitDelimited(
            "a, ;b",
            ", ;",
            textQualifier: null,
            treatConsecutiveDelimitersAsOne: true);

        fields.Should().Equal("a", "b");
    }

    [Fact]
    public void SplitDelimited_TrailingDelimiter_ProducesTrailingEmptyField()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a,b,", ",");

        fields.Should().Equal("a", "b", string.Empty);
    }

    [Fact]
    public void SplitDelimited_LeadingDelimiter_ProducesLeadingEmptyField()
    {
        var fields = TextToColumnsSplitter.SplitDelimited(",a,b", ",");

        fields.Should().Equal(string.Empty, "a", "b");
    }

    [Fact]
    public void SplitDelimited_NoDelimiter_ReturnsSingleField()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("abc", ",");

        fields.Should().Equal("abc");
    }

    [Fact]
    public void SplitDelimited_QuotedField_KeepsEmbeddedDelimiter()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a,\"b,c\",d", ",", '"');

        fields.Should().Equal("a", "b,c", "d");
    }

    [Fact]
    public void SplitDelimited_EscapedQuoteInsideQualifiedField_IsLiteralQuote()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("\"a\"\"b\",c", ",", '"');

        fields.Should().Equal("a\"b", "c");
    }

    [Fact]
    public void SplitDelimited_QuotedFieldWithDelimitersAndQuotes_Combined()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("\"x,\"\"y\"\"\",z", ",", '"');

        fields.Should().Equal("x,\"y\"", "z");
    }

    [Fact]
    public void SplitDelimited_SingleQuoteQualifier_KeepsEmbeddedDelimiter()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("'a,b',c", ",", '\'');

        fields.Should().Equal("a,b", "c");
    }

    [Fact]
    public void SplitDelimited_QualifierAbsentFromText_TakesUnqualifiedPath()
    {
        // No qualifier char present, so quoting logic is bypassed entirely.
        var fields = TextToColumnsSplitter.SplitDelimited("a,b,c", ",", '"');

        fields.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void SplitDelimited_CollapseInsideQualifiedField_IsNotApplied()
    {
        var fields = TextToColumnsSplitter.SplitDelimited(
            "\"a,,b\",c",
            ",",
            '"',
            treatConsecutiveDelimitersAsOne: true);

        fields.Should().Equal("a,,b", "c");
    }

    [Fact]
    public void SplitFixedWidth_BreaksAtPositions()
    {
        var fields = TextToColumnsSplitter.SplitFixedWidth("ABCDEF", [2, 4]);

        fields.Should().Equal("AB", "CD", "EF");
    }

    [Fact]
    public void SplitFixedWidth_NoBreaks_ReturnsWholeText()
    {
        var fields = TextToColumnsSplitter.SplitFixedWidth("ABCDEF", []);

        fields.Should().Equal("ABCDEF");
    }

    [Fact]
    public void SplitFixedWidth_ShortRow_ProducesFewerFields()
    {
        var fields = TextToColumnsSplitter.SplitFixedWidth("AB", [2, 4]);

        fields.Should().Equal("AB");
    }

    [Fact]
    public void SplitFixedWidth_RaggedRow_StopsAtTextEnd()
    {
        var fields = TextToColumnsSplitter.SplitFixedWidth("ABC", [2, 4]);

        fields.Should().Equal("AB", "C");
    }

    [Fact]
    public void SplitFixedWidth_EmptyText_ReturnsSingleEmptyField()
    {
        var fields = TextToColumnsSplitter.SplitFixedWidth(string.Empty, [2, 4]);

        fields.Should().Equal(string.Empty);
    }

    [Fact]
    public void SplitFixedWidth_UnsortedDuplicatePositions_AreNormalized()
    {
        var fields = TextToColumnsSplitter.SplitFixedWidth("ABCDEF", [4, 2, 2]);

        fields.Should().Equal("AB", "CD", "EF");
    }

    [Fact]
    public void SplitFixedWidth_NonPositivePositions_AreIgnored()
    {
        var fields = TextToColumnsSplitter.SplitFixedWidth("ABCDEF", [0, -1, 3]);

        fields.Should().Equal("ABC", "DEF");
    }

    [Fact]
    public void SplitFixedWidth_BreakBeyondLength_AddsNoExtraField()
    {
        var fields = TextToColumnsSplitter.SplitFixedWidth("ABCD", [2, 10]);

        fields.Should().Equal("AB", "CD");
    }
}
