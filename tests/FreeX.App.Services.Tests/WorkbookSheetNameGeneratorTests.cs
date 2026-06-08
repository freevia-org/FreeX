using FreeX.App.Services;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSheetNameGeneratorTests
{
    [Fact]
    public void GenerateUniqueSheetName_UsesNextWorkbookSheetNumber()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        WorkbookSheetNameGenerator.GenerateUniqueSheetName(workbook).Should().Be("Sheet3");
    }

    [Fact]
    public void GenerateUniqueSheetName_SkipsExistingNamesCaseInsensitively()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");
        workbook.AddSheet("sheet3");

        WorkbookSheetNameGenerator.GenerateUniqueSheetName(workbook).Should().Be("Sheet4");
    }
}
