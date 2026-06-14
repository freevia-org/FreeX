using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PivotLoadedStyleApplicationTests
{
    // A pivot loaded from xlsx arrives with output cells but no pivot-style formatting (Excel applies
    // PivotStyleLight16 dynamically rather than baking it into per-cell styles).  ApplyLoadedPivotStyles
    // must materialize that formatting onto the existing cells.  This strips the styling that Refresh
    // applies (simulating a freshly-loaded, unstyled pivot) and verifies it is restored.
    [Fact]
    public void ApplyLoadedPivotStyles_RestylesAnUnstyledLoadedPivot()
    {
        var workbook = new Workbook("PivotLoadedStyle");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 5, 3),
            TargetRange = Range(sheet, 2, 5, 12, 9),
            StyleName = "PivotStyleLight16",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var range = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot);

        // Simulate a freshly-loaded pivot: clear the per-cell styling Refresh applied.
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        for (var col = range.Start.Col; col <= range.End.Col; col++)
            if (sheet.GetCell(row, col) is { } cell)
                cell.StyleId = StyleId.Default;

        AnyBoldInRange(workbook, sheet, range).Should().BeFalse("the loaded pivot starts unstyled");

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook).Should().BeTrue();

        AnyBoldInRange(workbook, sheet, range)
            .Should().BeTrue("the built-in pivot style bolds header/group/total cells like Excel");
    }

    [Fact]
    public void ApplyLoadedPivotStyles_NoPivots_ReturnsFalseAndDoesNotThrow()
    {
        var workbook = new Workbook("NoPivots");
        workbook.AddSheet("Sheet1");

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook).Should().BeFalse();
    }

    private static bool AnyBoldInRange(Workbook workbook, Sheet sheet, GridRange range)
    {
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var cell = sheet.GetCell(row, col);
            if (cell is null)
                continue;
            if (workbook.GetStyle(cell.StyleId).Bold)
                return true;
        }

        return false;
    }

    private static void SeedSalesData(Sheet sheet)
    {
        string[][] rows =
        [
            ["Region", "Quarter", "Amount"],
            ["East", "Q1", "100"],
            ["East", "Q2", "120"],
            ["West", "Q1", "90"],
            ["West", "Q2", "80"],
        ];

        for (var r = 0; r < rows.Length; r++)
        for (var c = 0; c < rows[r].Length; c++)
        {
            var address = new CellAddress(sheet.Id, (uint)(r + 1), (uint)(c + 1));
            // Only the Amount column (index 2) holds numbers; Region/Quarter are text.
            sheet.SetCell(address, r >= 1 && c == 2
                ? new NumberValue(double.Parse(rows[r][c]))
                : new TextValue(rows[r][c]));
        }
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));
}
