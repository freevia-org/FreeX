namespace FreeW.Core.Model.Tests;

// Round 174, finding freew-mail-merge F1: MergeData's row dictionary is keyed case-insensitively
// (StringComparer.OrdinalIgnoreCase), so a recipient-list header with two columns that differ only by
// case (e.g. "Email"/"email") silently collapses to one value per row with no signal anywhere. These
// tests pin the new MergeData.DuplicateHeaderNames diagnostic that exposes the collision.
public class R174_MergeDataDuplicateHeaderTests
{
    [Fact]
    public void DuplicateHeaderNames_FlagsColumnsDifferingOnlyByCase()
    {
        var data = new MergeData(
            ["Name", "Email", "email"],
            [["Ada", "first@example.test", "second@example.test"]]);

        data.DuplicateHeaderNames.Should().BeEquivalentTo(["Email"], options => options.WithStrictOrdering());
    }

    [Fact]
    public void DuplicateHeaderNames_FlagsMultipleCollidingGroupsInFirstAppearanceOrder()
    {
        var data = new MergeData(
            ["Zip", "State", "zip", "ZIP", "state"],
            [["1", "2", "3", "4", "5"]]);

        data.DuplicateHeaderNames.Should().BeEquivalentTo(["Zip", "State"], options => options.WithStrictOrdering());
    }

    [Fact]
    public void DuplicateHeaderNames_IsEmptyWhenAllHeaderNamesAreDistinct()
    {
        var data = new MergeData(["Name", "Email", "Zip"], [["Ada", "a@example.test", "12345"]]);

        data.DuplicateHeaderNames.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateHeaderNames_IsEmptyForEmptyHeader()
    {
        var data = new MergeData([], []);

        data.DuplicateHeaderNames.Should().BeEmpty();
    }

    [Fact]
    public void FromCsv_FlagsDuplicateHeaderNamesDifferingOnlyByCase()
    {
        var data = MergeData.FromCsv("Name,Email,email\nAda,first@example.test,second@example.test");

        data.DuplicateHeaderNames.Should().BeEquivalentTo(["Email"]);
    }
}
