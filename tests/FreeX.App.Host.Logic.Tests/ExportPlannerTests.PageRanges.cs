using FluentAssertions;

namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
{
    [Theory]
    [InlineData("", "", true, null, null)]
    [InlineData("2", "4", true, 2, 4)]
    [InlineData("3", "3", true, 3, 3)]
    [InlineData("0", "3", false, null, null)]
    [InlineData("4", "2", false, null, null)]
    [InlineData("x", "2", false, null, null)]
    [InlineData("2", "", false, null, null)]
    public void TryCreatePageRange_ValidatesOptionalOneBasedPageRange(
        string fromText,
        string toText,
        bool expectedSuccess,
        int? expectedFrom,
        int? expectedTo)
    {
        var success = ExportPlanner.TryCreatePageRange(fromText, toText, out var range, out var error);

        success.Should().Be(expectedSuccess);
        if (expectedSuccess && expectedFrom is not null && expectedTo is not null)
        {
            range.Should().Be(new ExportPageRange(expectedFrom.Value, expectedTo.Value));
            error.Should().BeNull();
        }
        else if (expectedSuccess)
        {
            range.Should().BeNull();
            error.Should().BeNull();
        }
        else
        {
            range.Should().BeNull();
            error.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData(null, null, 3, true, null)]
    [InlineData(1, 2, 3, true, null)]
    [InlineData(3, 3, 3, true, null)]
    [InlineData(4, 4, 3, false, "Export_PageRangeStartsAfterLastPage")]
    [InlineData(1, 4, 3, false, "Export_PageRangeEndsAfterLastPage")]
    [InlineData(1, 1, 0, false, "Export_NoExportablePagesError")]
    public void TryValidatePageRange_ChecksRenderedPageCount(
        int? fromPage,
        int? toPage,
        int pageCount,
        bool expectedSuccess,
        string? expectedErrorKey)
    {
        var pageRange = fromPage is null || toPage is null ? null : new ExportPageRange(fromPage.Value, toPage.Value);

        var success = ExportPlanner.TryValidatePageRange(pageRange, pageCount, out var error);

        success.Should().Be(expectedSuccess);
        var expectedError = expectedErrorKey switch
        {
            "Export_PageRangeStartsAfterLastPage" => UiText.Format(expectedErrorKey, pageCount),
            "Export_PageRangeEndsAfterLastPage" => UiText.Format(expectedErrorKey, pageCount),
            null => null,
            _ => UiText.Get(expectedErrorKey)
        };
        error.Should().Be(expectedError);
    }
}
