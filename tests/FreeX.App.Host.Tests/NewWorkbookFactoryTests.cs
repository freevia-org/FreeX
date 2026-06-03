using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class NewWorkbookFactoryTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(0, 1)]
    [InlineData(300, 255)]
    public void Create_HonorsNormalizedDefaultSheetCount(
        int defaultSheetCount,
        int expectedSheetCount)
    {
        var workbook = NewWorkbookFactory.Create(defaultSheetCount);

        workbook.SheetCount.Should().Be(expectedSheetCount);
        workbook.Sheets
            .Select(sheet => sheet.Name)
            .Should()
            .Equal(Enumerable.Range(1, expectedSheetCount).Select(index => $"Sheet{index}"));
    }
}
