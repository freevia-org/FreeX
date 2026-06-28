using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeRecipientFilterSortPlannerTests
{
    [Fact]
    public void PreviewColumns_LimitsToFirstEightColumns()
    {
        var header = Enumerable.Range(1, 10).Select(index => $"Field{index}").ToArray();

        var previewColumns = MailMergeRecipientFilterSortPlanner.GetPreviewColumns(header);

        previewColumns.Should().Equal("Field1", "Field2", "Field3", "Field4", "Field5", "Field6", "Field7", "Field8");
    }

    [Fact]
    public void FormatPreviewText_MatchesDialogRows()
    {
        var columns = new[] { "Name", "City" };
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Alice",
            ["City"] = "Paris",
        };

        MailMergeRecipientFilterSortPlanner.FormatPreviewHeader(columns)
            .Should().Be("  Name  |  City");
        MailMergeRecipientFilterSortPlanner.FormatPreviewRow(2, row, columns)
            .Should().Be("3. Alice  |  Paris");
    }

    [Fact]
    public void Apply_IncludeAllRows_ReturnsSameRowsSortedAscending()
    {
        var data = SampleData();

        var result = MailMergeRecipientFilterSortPlanner.Apply(data, [0, 1, 2, 3], "Name", ascending: true);

        result.Count.Should().Be(4);
        result.Rows.Select(row => row["Name"]).Should().Equal("Alice", "Bob", "Carol", "Dave");
    }

    [Fact]
    public void Apply_ExcludesUncheckedRowsAndSortsDescending()
    {
        var data = SampleData();

        var result = MailMergeRecipientFilterSortPlanner.Apply(data, [0, 1, 3], "Name", ascending: false);

        result.Count.Should().Be(3);
        result.Rows.Select(row => row["Name"]).Should().Equal("Dave", "Carol", "Alice");
    }

    [Fact]
    public void Apply_IgnoresInvalidAndDuplicateIndexes()
    {
        var data = SampleData();

        var result = MailMergeRecipientFilterSortPlanner.Apply(data, [-1, 2, 2, 99], "Name", ascending: true);

        result.Count.Should().Be(1);
        result.Rows.Single()["Name"].Should().Be("Bob");
    }

    [Fact]
    public void Apply_PreservesHeaderOrderWhenRebuildingRows()
    {
        var data = SampleData();

        var result = MailMergeRecipientFilterSortPlanner.Apply(data, [3], "Missing", ascending: true);

        result.Header.Should().Equal("Name", "City", "Amount");
        result.Rows.Single()["Name"].Should().Be("Dave");
        result.Rows.Single()["City"].Should().Be("Denver");
        result.Rows.Single()["Amount"].Should().Be("4");
    }

    private static MergeData SampleData() => new(
        ["Name", "City", "Amount"],
        [
            ["Carol", "Chicago", "3"],
            ["Alice", "Austin", "1"],
            ["Bob", "Boston", "2"],
            ["Dave", "Denver", "4"],
        ]);
}
