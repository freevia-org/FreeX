using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Integration.Tests;

/// <summary>
/// Smoke tests for the print/export feature.
///
/// <c>PrintRenderer</c> lives in App.Host (WPF-only assembly) and requires a WPF
/// application context, so we cannot call it directly from this non-WPF test project.
/// Instead we test the underlying sheet model helpers that the renderer relies on:
/// <see cref="Sheet.GetUsedRange"/> and <see cref="Sheet.GetUsedCells"/>.
/// These are the two key inputs that drive pagination and cell layout.
/// </summary>
public class PrintTests
{
    // ── GetUsedRange (drives page count logic in PrintRenderer) ───────────────

    [Fact]
    public void GetUsedRange_EmptySheet_ReturnsNull()
    {
        var wb    = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");

        var range = sheet.GetUsedRange();

        range.Should().BeNull("an empty sheet has no used range, so PrintRenderer returns 0 pages");
    }

    [Fact]
    public void GetUsedRange_SheetWithTenRows_ReturnsCorrectBounds()
    {
        var wb    = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");

        for (uint r = 1; r <= 10; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        var range = sheet.GetUsedRange();

        range.Should().NotBeNull();
        range!.Value.End.Row.Should().Be(10);
        range.Value.End.Col.Should().Be(1);
    }

    [Fact]
    public void GetUsedRange_SheetWithMultipleColumns_ReturnsMaxColumnBound()
    {
        var wb    = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");

        // Simulate a 5-row × 8-column dataset (like a print source)
        for (uint r = 1; r <= 5; r++)
            for (uint c = 1; c <= 8; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(r * c));

        var range = sheet.GetUsedRange();

        range.Should().NotBeNull();
        range!.Value.End.Row.Should().Be(5);
        range.Value.End.Col.Should().Be(8);
        range.Value.Start.Row.Should().Be(1);
        range.Value.Start.Col.Should().Be(1);
    }

    // ── GetUsedCells (feeds the cell lookup in PrintRenderer) ─────────────────

    [Fact]
    public void GetUsedCells_EmptySheet_ReturnsEmptyDictionary()
    {
        var wb    = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.GetUsedCells().Should().BeEmpty();
    }

    [Fact]
    public void GetUsedCells_SheetWithCells_AllCellsPresent()
    {
        var wb    = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(200));

        var cells = sheet.GetUsedCells();

        cells.Should().HaveCount(3);
        cells.Values.Should().Contain(c => c.Value is NumberValue && ((NumberValue)c.Value).Value == 100);
        cells.Values.Should().Contain(c => c.Value is TextValue  && ((TextValue)c.Value).Value  == "Hello");
    }

    // ── Pagination math (mirrors PrintRenderer.RenderWorksheet logic) ─────────

    [Theory]
    [InlineData(10,  55, 1)]   // 10 rows, 55 rows/page  → 1 page
    [InlineData(56,  55, 2)]   // 56 rows, 55 rows/page  → 2 pages
    [InlineData(110, 55, 2)]   // 110 rows, 55 rows/page → 2 pages
    [InlineData(111, 55, 3)]   // 111 rows, 55 rows/page → 3 pages
    public void PageCount_Calculation_IsCorrect(uint rowCount, uint rowsPerPage, uint expectedPages)
    {
        // This replicates the ceiling-division used in PrintRenderer.RenderWorksheet
        uint actualPages = (uint)Math.Ceiling((double)rowCount / rowsPerPage);
        actualPages.Should().Be(expectedPages);
    }
}
