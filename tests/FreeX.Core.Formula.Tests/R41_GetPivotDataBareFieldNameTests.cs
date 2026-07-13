using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R41-formula-getpivotdata-3-3: GETPIVOTDATA's data_field argument only matched the data
/// field's full displayed caption (e.g. "Sum of Sales"), not the bare underlying source-field
/// name ("Sales") that real Excel also accepts. Both forms must resolve to the same data field,
/// case-insensitively, and a name that matches neither must still yield #REF!.
/// </summary>
public sealed class R41_GetPivotDataBareFieldNameTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) BuildPivot()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // Source data headers: Region (col1), Sales (col2)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(45));

        // Materialized pivot output: Region row header (col5) / Sum of Sales data (col6)
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 6), new TextValue("Sum of Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 5), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 6), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 5), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 6), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 5), new TextValue("Grand Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 6), new NumberValue(70));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 8, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);

        return (wb, sheet);
    }

    [Fact]
    public void GetPivotData_BareSourceFieldName_ResolvesSameAsFullCaption()
    {
        var (wb, sheet) = BuildPivot();

        var viaBareName = _eval.Evaluate("=GETPIVOTDATA(\"Sales\",E5,\"Region\",\"West\")", sheet, wb);
        var viaCaption = _eval.Evaluate("=GETPIVOTDATA(\"Sum of Sales\",E5,\"Region\",\"West\")", sheet, wb);

        viaBareName.Should().Be(new NumberValue(45));
        viaCaption.Should().Be(new NumberValue(45));
    }

    [Fact]
    public void GetPivotData_BareSourceFieldNameCaseInsensitive_ResolvesValue()
    {
        var (wb, sheet) = BuildPivot();

        _eval.Evaluate("=GETPIVOTDATA(\"sALES\",E5,\"Region\",\"East\")", sheet, wb)
            .Should()
            .Be(new NumberValue(25));
    }

    [Fact]
    public void GetPivotData_UnknownDataFieldName_ReturnsRef()
    {
        var (wb, sheet) = BuildPivot();

        _eval.Evaluate("=GETPIVOTDATA(\"Bogus\",E5,\"Region\",\"East\")", sheet, wb)
            .Should()
            .Be(ErrorValue.Ref);
    }
}
