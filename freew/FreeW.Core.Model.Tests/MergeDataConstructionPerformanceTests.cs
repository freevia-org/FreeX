namespace FreeW.Core.Model.Tests;

public class MergeDataConstructionPerformanceTests
{
    [Fact]
    public void Constructor_PreservesDuplicateOrderCasingAndLastColumnWins()
    {
        var data = new MergeData(
            [" Alpha ", "Beta", "BETA", "alpha"],
            [["alpha-first", "beta-first", "beta-last", "alpha-last"]]);

        data.Header.Should().Equal("Alpha", "Beta", "BETA", "alpha");
        data.DuplicateHeaderNames.Should().Equal("Alpha", "Beta");
        data.Rows[0].Keys.Should().Equal("Alpha", "Beta");
        data.Rows[0]["ALPHA"].Should().Be("alpha-last");
        data.Rows[0]["beta"].Should().Be("beta-last");
    }

    [Fact]
    public void Constructor_PreservesLastDuplicateColumnPaddingForShortRows()
    {
        var data = new MergeData(
            ["Name", "Other", "NAME"],
            [["first", "other"]]);

        data.Rows[0]["Name"].Should().BeEmpty();
        data.Rows[0]["Other"].Should().Be("other");
    }

    [Fact]
    public void Constructor_DenseDuplicateHeadersAndRowsStoreOneValuePerLogicalField()
    {
        const int count = 10_000;
        var header = Enumerable.Range(0, count)
            .Select(index => index % 2 == 0 ? "Name" : "name")
            .ToArray();
        var rows = Enumerable.Repeat<IReadOnlyList<string>>(Array.Empty<string>(), count);

        var data = new MergeData(header, rows);

        data.DuplicateHeaderNames.Should().Equal("Name");
        data.Rows.Should().HaveCount(count);
        data.Rows.Should().OnlyContain(row => row.Count == 1 && row["NAME"] == string.Empty);
    }

    [Fact]
    public void Constructor_SourceGuardKeepsHeaderIndexSharedAcrossRows()
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "freew", "FreeW.Core.Model", "MailMerge.cs");

        source.Should().Contain("var lastColumnIndexByName = new Dictionary<string, int>")
            .And.Contain("foreach (var name in uniqueHeaderNames)")
            .And.Contain("var index = lastColumnIndexByName[name];")
            .And.NotContain(".GroupBy(entry => entry.name")
            .And.NotContain("for (var i = 0; i < Header.Count; i++)");
    }
}
