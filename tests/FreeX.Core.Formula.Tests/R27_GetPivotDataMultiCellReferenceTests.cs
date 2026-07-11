using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R27-lookup-reference-remaining-2: GETPIVOTDATA's pivot_table argument unconditionally rejected
/// (with #REF!) any reference that resolved to more than a single cell, via a
/// "RangeValue { RowCount: 1, ColCount: 1 }" pattern match. Real Excel's documented signature
/// accepts "a reference to any cell, range of cells, or range named that is in a PivotTable" --
/// e.g. a defined name spanning the whole pivot table. FindPivotTableForReference only ever reads
/// the reference's top-left cell (StartRow/StartCol), so a multi-cell reference anchored on the
/// pivot must resolve identically to a single-cell reference to that same anchor cell.
/// </summary>
public sealed class R27_GetPivotDataMultiCellReferenceTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) BuildPivot()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 6), new TextValue("Sum of Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 6), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 6), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new TextValue("Grand Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 6), new NumberValue(70));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return (wb, sheet);
    }

    [Fact]
    public void GetPivotData_MultiCellNamedRangeAnchoredOnPivot_ResolvesPivotTable()
    {
        var (wb, sheet) = BuildPivot();

        // A named range spanning the whole pivot table output (e.g. selecting the pivot and
        // naming it via Name Manager), not just its top-left cell. Before the fix, args[1]
        // evaluating to a multi-cell RangeValue was unconditionally rejected with #REF! even
        // though its top-left cell (E2) sits inside the pivot's TargetRange.
        wb.DefineNamedRange("PivotArea", new GridRange(
            new CellAddress(sheet.Id, 2, 5),
            new CellAddress(sheet.Id, 5, 6)));

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",PivotArea,\"Region\",\"West\")", sheet, wb)
            .Should()
            .Be(new NumberValue(45));
    }

    [Fact]
    public void GetPivotData_SingleCellReference_StillResolvesPivotTable()
    {
        // Representative already-working sibling case that must keep working unchanged: a plain
        // single-cell reference to a cell inside the pivot (the overwhelmingly common real-world
        // usage -- what Excel itself generates when you click a pivot cell while authoring a
        // GETPIVOTDATA formula).
        var (wb, sheet) = BuildPivot();

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"West\")", sheet, wb)
            .Should()
            .Be(new NumberValue(45));
    }

    [Fact]
    public void GetPivotData_MultiCellReferenceNotOverAnyPivot_ReturnsRef()
    {
        // A multi-cell reference whose top-left cell is NOT inside any pivot table must still
        // yield #REF!, exactly like the existing single-cell "no pivot at reference" case --
        // relaxing the shape check must not make GETPIVOTDATA locate a pivot it shouldn't.
        var (wb, sheet) = BuildPivot();

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",A1:B2,\"Region\",\"West\")", sheet, wb)
            .Should()
            .Be(ErrorValue.Ref);
    }
}
