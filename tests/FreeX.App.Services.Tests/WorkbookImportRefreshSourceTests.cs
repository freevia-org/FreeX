using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookImportRefreshSourceTests
{
    [Fact]
    public void CanRefresh_AcceptsOnlyOwningWorkbookWithExistingAnchorSheet()
    {
        var workbook = new Workbook("Import target");
        var sheet = workbook.AddSheet("Data");
        var adapter = new TestFileAdapter(extension: ".csv", formatName: "CSV");
        var source = new WorkbookImportRefreshSource(
            workbook.Id,
            "data.csv",
            ".csv",
            adapter,
            "CSV",
            new CellAddress(sheet.Id, 4, 2),
            8,
            3);

        source.CanRefresh(workbook).Should().BeTrue();
        source.CanRefresh(new Workbook("Replacement")).Should().BeFalse();

        workbook.RemoveSheet(sheet.Id).Should().BeTrue();
        source.CanRefresh(workbook).Should().BeFalse();
    }

    [Fact]
    public void CanRefresh_RejectsBlankPath()
    {
        var workbook = new Workbook("Import target");
        var sheet = workbook.AddSheet("Data");
        var source = new WorkbookImportRefreshSource(
            workbook.Id,
            "   ",
            ".csv",
            new TestFileAdapter(),
            "CSV",
            new CellAddress(sheet.Id, 1, 1),
            1,
            1);

        source.CanRefresh(workbook).Should().BeFalse();
    }

    [Fact]
    public void PreviousExtentFor_IsScopedToExactWorkbookAndAnchor()
    {
        var workbook = new Workbook("Import target");
        var sheet = workbook.AddSheet("Data");
        var anchor = new CellAddress(sheet.Id, 7, 5);
        var source = new WorkbookImportRefreshSource(
            workbook.Id,
            "data.csv",
            ".csv",
            new TestFileAdapter(),
            "CSV",
            anchor,
            12,
            6);

        source.PreviousExtentFor(workbook.Id, anchor).Should().Be((12u, 6u));
        source.PreviousExtentFor(WorkbookId.New(), anchor).Should().BeNull();
        source.PreviousExtentFor(workbook.Id, new CellAddress(sheet.Id, 7, 6)).Should().BeNull();
    }

    [Fact]
    public void WithWrittenExtent_PreservesRecipeAndUpdatesDimensions()
    {
        var workbook = new Workbook("Import target");
        var sheet = workbook.AddSheet("Data");
        var adapter = new TestFileAdapter(extension: ".xml", formatName: "Spreadsheet XML");
        var source = new WorkbookImportRefreshSource(
            workbook.Id,
            "data.xml",
            ".xml",
            adapter,
            "Spreadsheet XML",
            new CellAddress(sheet.Id, 3, 4),
            20,
            9);

        var updated = source.WithWrittenExtent(5, 2);

        updated.Should().Be(source with { LastRowCount = 5, LastColCount = 2 });
        updated.Adapter.Should().BeSameAs(adapter);
    }
}
