using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R17-pivot-cache-deep-1: PageFieldFiltersMatch returned on the FIRST page field found in
/// the requested filters dictionary, so a second page-field constraint (e.g. Year) was never
/// checked when a first one (e.g. Region) already matched. GETPIVOTDATA must check EVERY
/// requested page-field constraint against the pivot's current page selection and only
/// succeed if all of them match; a mismatch on any one of them must yield #REF!.
/// </summary>
public sealed class R17_getpivotdata_Tests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) BuildPivotWithTwoPageFields()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // Source data headers: Region (col1), Amount (col2), Year (col3)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Year"));

        // Materialized pivot output: Region row header (col5) / Sum of Amount data (col6)
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 6), new TextValue("Sum of Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 6), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), new TextValue("Grand Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 6), new NumberValue(25));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 4, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        // Two page fields, both with a currently-selected item: Region=East, Year=2020.
        // (Region is also a row field here to keep the materialized data grid simple; the
        // page-field selection additionally constrains the visible pivot to Region=East /
        // Year=2020, mirroring a filters-area pivot with two active page-field selections.)
        pivot.PageFields.Add(new PivotFieldModel(0, SelectedItem: "East"));
        pivot.PageFields.Add(new PivotFieldModel(2, SelectedItem: "2020"));

        sheet.PivotTables.Add(pivot);
        return (wb, sheet);
    }

    [Fact]
    public void GetPivotData_SecondPageFieldMismatch_ReturnsRefError()
    {
        var (wb, sheet) = BuildPivotWithTwoPageFields();

        // Region=East matches the visible page selection, but Year=2021 does NOT
        // (visible page selection is Year=2020). Before the fix, the loop returned on the
        // first page field (Region) that matched and never checked Year, incorrectly
        // returning the East value instead of #REF!.
        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Year\",\"2021\")", sheet, wb)
            .Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void GetPivotData_AllPageFieldsMatch_ReturnsValue()
    {
        var (wb, sheet) = BuildPivotWithTwoPageFields();

        // Both Region=East and Year=2020 match the visible page selections.
        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Year\",\"2020\")", sheet, wb)
            .Should().Be(new NumberValue(25));
    }
}
