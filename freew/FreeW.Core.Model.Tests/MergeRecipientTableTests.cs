namespace FreeW.Core.Model.Tests;

public sealed class MergeRecipientTableTests
{
    [Fact]
    public void FromTable_WithHeaderRow_NamesFieldsAndPadsShortRows()
    {
        var data = MergeRecipientTable.FromTable(
            [
                ["Name", " City ", "Email"],
                ["Ada", "London"],
                ["", "", ""],
                ["Grace", "New York", "grace@example.com", "ignored"],
            ],
            firstRowIsHeader: true);

        data.Header.Should().Equal("Name", "City", "Email", "F4");
        data.Count.Should().Be(2, "a fully blank row is not a record");
        data.Rows[0]["Email"].Should().BeEmpty();
        data.Rows[1]["city"].Should().Be("New York");
        data.Rows[1]["F4"].Should().Be("ignored");
    }

    [Fact]
    public void FromTable_WithoutHeaderRow_NamesFieldsByPosition()
    {
        var data = MergeRecipientTable.FromTable([["Ada", "London"], ["Grace", "New York"]], firstRowIsHeader: false);

        data.Header.Should().Equal("F1", "F2");
        data.Count.Should().Be(2);
        data.Rows[0]["F1"].Should().Be("Ada");
    }

    [Fact]
    public void FromTable_DisambiguatesBlankAndRepeatedHeaderNames()
    {
        var data = MergeRecipientTable.FromTable(
            [["Email", "", "email", "F2"], ["a", "b", "c", "d"]],
            firstRowIsHeader: true);

        data.Header.Should().Equal("Email", "F2", "email2", "F22");
        data.DuplicateHeaderNames.Should().BeEmpty("every column keeps its own value");
        MergeRecipientTable.RowValues(data, 0).Should().Equal("a", "b", "c", "d");
    }

    [Fact]
    public void FromTable_OfNothing_IsAnEmptySource()
    {
        var data = MergeRecipientTable.FromTable([], firstRowIsHeader: true);

        data.Header.Should().BeEmpty();
        data.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("Name,City\r\nAda,London\r\n", ',')]
    [InlineData("Name;City\nAda;London\n", ';')]
    [InlineData("Name\tCity\rAda\tLondon", '\t')]
    [InlineData("Name|City\nAda|London", '|')]
    public void DetectDelimiter_AndParse_HandleEachSupportedDelimiterAndLineEnding(string text, char expected)
    {
        MergeRecipientTable.DetectDelimiter(text).Should().Be(expected);

        var rows = MergeRecipientTable.ParseDelimited(text, expected);

        rows.Should().HaveCount(2);
        rows[0].Should().Equal("Name", "City");
        rows[1].Should().Equal("Ada", "London");
    }

    [Fact]
    public void DetectDelimiter_IgnoresDelimitersInsideQuotes_AndFallsBackForSingleColumn()
    {
        MergeRecipientTable.DetectDelimiter("\"Last, First\";City\n\"Lovelace, Ada\";London\n").Should().Be(';');
        MergeRecipientTable.DetectDelimiter("Name\nAda\nGrace\n", fallback: '\t').Should().Be('\t');
        MergeRecipientTable.DetectDelimiter(string.Empty).Should().Be(',');
    }

    [Fact]
    public void DetectDelimiter_PrefersTheConsistentSplit()
    {
        // Every line has exactly two tab-separated fields; commas appear irregularly inside the values.
        var text = "Name\tAddress\nAda\t1 Main St, London\nGrace\t2 Oak Ave, Apt 3, New York\n";

        MergeRecipientTable.DetectDelimiter(text).Should().Be('\t');
    }

    [Fact]
    public void ParseDelimited_HonoursQuotedFields_EmbeddedNewlines_AndDoubledQuotes()
    {
        var rows = MergeRecipientTable.ParseDelimited(
            "Name,Note\r\n\"Lovelace, Ada\",\"line one\nline two\"\r\n\r\nGrace,\"say \"\"hi\"\"\"\r\nMid,5\" pipe\r\n\"\",x",
            ',');

        rows.Should().HaveCount(5, "the blank line is skipped but a row starting with an empty quoted field is kept");
        rows[1].Should().Equal("Lovelace, Ada", "line one\nline two");
        rows[2].Should().Equal("Grace", "say \"hi\"");
        rows[3].Should().Equal("Mid", "5\" pipe");
        rows[4].Should().Equal(string.Empty, "x");
    }

    [Theory]
    [InlineData(',')]
    [InlineData('\t')]
    public void WriteDelimited_RoundTripsThroughParse(char delimiter)
    {
        var data = new MergeData(
            ["Name", "Note"],
            [
                ["Lovelace, Ada", "say \"hi\""],
                [" padded ", "two\r\nlines"],
                ["tab\there", string.Empty],
            ]);

        var text = MergeRecipientTable.WriteDelimited(data, delimiter);
        var reloaded = MergeRecipientTable.FromTable(
            MergeRecipientTable.ParseDelimited(text, delimiter),
            firstRowIsHeader: true);

        reloaded.Header.Should().Equal(data.Header);
        reloaded.Count.Should().Be(3);
        for (var index = 0; index < data.Count; index++)
            MergeRecipientTable.RowValues(reloaded, index).Should().Equal(MergeRecipientTable.RowValues(data, index));
    }

    [Fact]
    public void SelectRows_KeepsRequestedOrder_AndIgnoresInvalidOrRepeatedIndexes()
    {
        var data = new MergeData(["Name"], [["Ada"], ["Grace"], ["Alan"]]);

        var selected = MergeRecipientTable.SelectRows(data, [2, 0, 2, 9, -1]);

        selected.Header.Should().Equal("Name");
        selected.Rows.Select(row => row["Name"]).Should().Equal("Alan", "Ada");
    }
}
