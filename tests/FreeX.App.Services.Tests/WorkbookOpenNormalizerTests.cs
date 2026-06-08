using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookOpenNormalizerTests
{
    [Theory]
    [InlineData(".csv")]
    [InlineData(".txt")]
    [InlineData(".tsv")]
    [InlineData(".tab")]
    public void ApplyTextWorkbookSheetName_RenamesSingleSheetTextWorkbook(string extension)
    {
        var workbook = new Workbook("Loaded");
        workbook.AddSheet("Sheet1");

        WorkbookOpenNormalizer.ApplyTextWorkbookSheetName(
            workbook,
            extension,
            "Very Long Sales [Draft] Import Name 2026");

        workbook.Sheets.Single().Name.Should().Be("Very Long Sales _Draft_ Import");
    }

    [Fact]
    public void ApplyTextWorkbookSheetName_LeavesNonTextWorkbookAlone()
    {
        var workbook = new Workbook("Loaded");
        workbook.AddSheet("Sheet1");

        WorkbookOpenNormalizer.ApplyTextWorkbookSheetName(workbook, ".xlsx", "Loaded workbook");

        workbook.Sheets.Single().Name.Should().Be("Sheet1");
    }
}
