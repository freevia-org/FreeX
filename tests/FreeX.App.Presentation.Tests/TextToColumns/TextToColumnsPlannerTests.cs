using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Presentation.Tests.TextToColumns;

public sealed class TextToColumnsPlannerTests
{
    [Fact]
    public void Plan_DelimitedRows_SplitsEachInput()
    {
        var sources = new[] { "a,b,c", "x,y,z" };
        var options = TextToColumnsOptions.Delimited(",");

        var result = TextToColumnsPlanner.Plan(sources, options);

        result.Rows.Should().HaveCount(2);
        result.Rows[0].Source.Should().Be("a,b,c");
        result.Rows[0].Fields.Should().Equal("a", "b", "c");
        result.Rows[1].Fields.Should().Equal("x", "y", "z");
    }

    [Fact]
    public void Plan_ColumnCount_IsWidestRow()
    {
        var sources = new[] { "a,b", "x,y,z", "p" };
        var options = TextToColumnsOptions.Delimited(",");

        var result = TextToColumnsPlanner.Plan(sources, options);

        result.ColumnCount.Should().Be(3);
    }

    [Fact]
    public void Plan_EmptyInput_ReturnsEmptyResult()
    {
        var result = TextToColumnsPlanner.Plan([], TextToColumnsOptions.Delimited(","));

        result.IsEmpty.Should().BeTrue();
        result.Rows.Should().BeEmpty();
        result.ColumnCount.Should().Be(0);
    }

    [Fact]
    public void Plan_NullEntry_TreatedAsEmptyText()
    {
        var sources = new string?[] { null };
        var options = TextToColumnsOptions.Delimited(",");

        var result = TextToColumnsPlanner.Plan(sources, options);

        result.Rows.Should().HaveCount(1);
        result.Rows[0].Source.Should().BeEmpty();
        result.Rows[0].Fields.Should().Equal(string.Empty);
        result.ColumnCount.Should().Be(1);
    }

    [Fact]
    public void Plan_FixedWidth_SlicesRows()
    {
        var sources = new[] { "ABCDEF", "GHIJKL" };
        var options = TextToColumnsOptions.FixedWidth([2, 4]);

        var result = TextToColumnsPlanner.Plan(sources, options);

        result.Rows[0].Fields.Should().Equal("AB", "CD", "EF");
        result.Rows[1].Fields.Should().Equal("GH", "IJ", "KL");
        result.ColumnCount.Should().Be(3);
    }

    [Fact]
    public void Plan_FixedWidth_RaggedRows_DetectsWidestColumnCount()
    {
        var sources = new[] { "ABCDEF", "AB" };
        var options = TextToColumnsOptions.FixedWidth([2, 4]);

        var result = TextToColumnsPlanner.Plan(sources, options);

        result.Rows[0].FieldCount.Should().Be(3);
        result.Rows[1].FieldCount.Should().Be(1);
        result.ColumnCount.Should().Be(3);
    }

    [Fact]
    public void Plan_DelimitedKinds_ResolvesCharacters()
    {
        var sources = new[] { "a\tb;c" };
        var options = TextToColumnsOptions.Delimited(
            [TextToColumnsDelimiterKind.Tab, TextToColumnsDelimiterKind.Semicolon]);

        var result = TextToColumnsPlanner.Plan(sources, options);

        result.Rows[0].Fields.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Plan_CustomDelimiterKind_UsesProvidedChar()
    {
        var sources = new[] { "a|b|c" };
        var options = TextToColumnsOptions.Delimited(
            [TextToColumnsDelimiterKind.Custom],
            customDelimiter: "|");

        var result = TextToColumnsPlanner.Plan(sources, options);

        result.Rows[0].Fields.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Plan_QualifiedFields_StayIntact()
    {
        var sources = new[] { "a,\"b,c\",d" };
        var options = TextToColumnsOptions.Delimited(",", textQualifier: '"');

        var result = TextToColumnsPlanner.Plan(sources, options);

        result.Rows[0].Fields.Should().Equal("a", "b,c", "d");
    }

    [Fact]
    public void Plan_QualifierKind_None_DisablesQualifierHandling()
    {
        var sources = new[] { "\"a,b\",c" };
        var options = TextToColumnsOptions.Delimited(
            [TextToColumnsDelimiterKind.Comma],
            textQualifier: TextToColumnsTextQualifier.None);

        var result = TextToColumnsPlanner.Plan(sources, options);

        // With no qualifier, the quotes are literal and the inner comma still splits.
        result.Rows[0].Fields.Should().Equal("\"a", "b\"", "c");
    }

    [Fact]
    public void Plan_CarriesColumnFormats()
    {
        var formats = new[]
        {
            TextToColumnsColumnFormat.Text,
            TextToColumnsColumnFormat.Skip,
            TextToColumnsColumnFormat.DateMDY
        };
        var options = TextToColumnsOptions.Delimited(",", columnFormats: formats);

        var result = TextToColumnsPlanner.Plan(new[] { "a,b,c" }, options);

        result.ColumnFormats.Should().Equal(formats);
        result.FormatFor(0).Should().Be(TextToColumnsColumnFormat.Text);
        result.FormatFor(1).Should().Be(TextToColumnsColumnFormat.Skip);
        result.FormatFor(2).Should().Be(TextToColumnsColumnFormat.DateMDY);
    }

    [Fact]
    public void Result_FormatFor_OutOfRange_DefaultsToGeneral()
    {
        var result = TextToColumnsPlanner.Plan(new[] { "a,b" }, TextToColumnsOptions.Delimited(","));

        result.FormatFor(0).Should().Be(TextToColumnsColumnFormat.General);
        result.FormatFor(5).Should().Be(TextToColumnsColumnFormat.General);
    }

    [Fact]
    public void Preview_LimitsSampleRows_ButKeepsFullColumnCount()
    {
        var sources = new[] { "a,b,c", "d,e", "f", "g,h,i,j" };
        var options = TextToColumnsOptions.Delimited(",");

        var preview = TextToColumnsPlanner.Preview(sources, options, sampleRowLimit: 2);

        preview.SampleRows.Should().HaveCount(2);
        preview.SampleRows[0].Fields.Should().Equal("a", "b", "c");
        preview.ColumnCount.Should().Be(4);
    }

    [Fact]
    public void Preview_LimitLargerThanRowCount_ReturnsAllRows()
    {
        var sources = new[] { "a,b", "c,d" };
        var options = TextToColumnsOptions.Delimited(",");

        var preview = TextToColumnsPlanner.Preview(sources, options, sampleRowLimit: 100);

        preview.SampleRows.Should().HaveCount(2);
    }

    [Fact]
    public void Preview_NegativeLimit_ReturnsNoSampleRows()
    {
        var sources = new[] { "a,b" };
        var options = TextToColumnsOptions.Delimited(",");

        var preview = TextToColumnsPlanner.Preview(sources, options, sampleRowLimit: -5);

        preview.SampleRows.Should().BeEmpty();
        preview.ColumnCount.Should().Be(2);
    }

    [Fact]
    public void Plan_ConsecutiveDelimiterCollapse_FlowsThroughOptions()
    {
        var sources = new[] { "a,,b" };
        var collapse = TextToColumnsOptions.Delimited(
            ",",
            treatConsecutiveDelimitersAsOne: true,
            textQualifier: null);
        var keep = TextToColumnsOptions.Delimited(",", textQualifier: null);

        TextToColumnsPlanner.Plan(sources, collapse).Rows[0].Fields.Should().Equal("a", "b");
        TextToColumnsPlanner.Plan(sources, keep).Rows[0].Fields.Should().Equal("a", string.Empty, "b");
    }

    [Fact]
    public void Split_DispatchesByMode()
    {
        TextToColumnsPlanner.Split("a,b", TextToColumnsOptions.Delimited(","))
            .Should().Equal("a", "b");
        TextToColumnsPlanner.Split("ABCD", TextToColumnsOptions.FixedWidth([2]))
            .Should().Equal("AB", "CD");
    }
}
