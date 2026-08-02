using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R116-commands-pivot-column-shift-fieldindex: PivotFieldModel/PivotDataFieldModel.SourceFieldIndex
/// (and the analogous PivotSortModel.FieldIndex / PivotLabelFilterModel.SourceFieldIndex /
/// PivotValueFilterModel.SourceFieldIndex bindings) are raw ordinals naming one of the pivot's live
/// source columns, captured once when the field is added to the layout. Every refresh re-derives
/// headers/rows/cache.Fields fresh from the CURRENT physical column layout
/// (PivotTableRefreshService.ReadHeaders/ReadSourceRows/ReconcileCacheFields), so inserting an ordinary
/// column strictly INSIDE a pivot's SourceRange (an everyday "add a new field to my data" edit) grows
/// the SourceRange and shifts every column after the insert point right by one -- but nothing used to
/// re-map the pivot's already-placed RowFields/DataFields/etc. SourceFieldIndex values for that shift.
/// The stale index stayed in-bounds (so IsValidField's bounds-only pruning never caught it) but silently
/// started naming a DIFFERENT column, so "Sum of Amount" would silently sum a completely different
/// column's values (typically producing 0, since the newly-misindexed column held text) with no error.
///
/// Both tests drive the real product entry point: InsertColumnsCommand (the command Insert Column(s)
/// constructs) followed by RefreshPivotTableCommand (the ribbon's Data &gt; Refresh action), not the
/// internal service/shift methods directly.
/// </summary>
public sealed class R116_PivotColumnInsertPreservesFieldBindingsTests
{
    // Region(A) | Product(B) | Amount(C) | Date(D) -- matches the defect's own worked example.
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) CreateRegionProductAmountDatePivot(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Date"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("2024-01-01"));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Gadget"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new TextValue("2024-01-02"));

        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("2024-01-03"));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 10, 8)),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0)); // Region
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum")); // Amount
        sheet.PivotTables.Add(pivot);

        var initialRefresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        initialRefresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        return (workbook, sheet, pivot);
    }

    private static string Text(Sheet sheet, uint row, uint col) =>
        sheet.GetCell(new CellAddress(sheet.Id, row, col))?.Value is TextValue text ? text.Value : "";

    private static double Number(Sheet sheet, uint row, uint col) =>
        sheet.GetCell(new CellAddress(sheet.Id, row, col))?.Value is NumberValue number ? number.Value : double.NaN;

    // --- bug case: inserting a column strictly inside SourceRange must not re-bind an existing
    // data field to a different (now shifted) column ---

    [Fact]
    public void InsertColumn_InsideSourceRange_KeepsDataFieldBoundToSameSourceColumnAfterRefresh()
    {
        var (workbook, sheet, pivot) = CreateRegionProductAmountDatePivot("PivotColumnInsertFieldBindingTest");

        // Sanity: before the insert, Sum of Amount correctly totals column C (Amount).
        var outputCol = pivot.TargetRange.Start.Col;
        Text(sheet, 1, outputCol).Should().Be("Region");
        Text(sheet, 1, outputCol + 1).Should().Be("Sum of Amount");
        Number(sheet, 2, outputCol + 1).Should().Be(25, "East = Widget(10) + Gadget(15) before the insert");
        Number(sheet, 3, outputCol + 1).Should().Be(20, "West = Widget(20) before the insert");

        // Insert a new column at B (between Region and Product) -- an ordinary "add a field to my
        // data" edit strictly inside the pivot's SourceRange. Region stays at A(0); the old
        // Product/Amount/Date columns all shift one column right (B->C, C->D, D->E). Because the
        // pivot's own TargetRange (col F onward) also sits at/after the insert point, it shifts
        // right by one column too -- re-read it after the insert rather than assuming it stayed put.
        var insert = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 1);
        insert.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        outputCol = pivot.TargetRange.Start.Col;

        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Notes"));

        // The pivot's own SourceRange must have grown to keep covering every original column plus
        // the new one (A:E instead of A:D) -- this part already worked before this fix.
        pivot.SourceRange.End.Col.Should().Be(5);

        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        refresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        // Bug (before fix): the DataField's SourceFieldIndex stayed at 2, which after the insert
        // names the OLD "Product" column (now shifted from B to C) instead of "Amount" (now shifted
        // from C to D) -- so "Sum of Amount" would silently sum the wrong (now-misaligned) column
        // instead of the real Amount column, typically producing 0 because the misindexed column
        // holds text, not numbers.
        Text(sheet, 1, outputCol + 1).Should().Be("Sum of Amount", "the data field's own label must not change");
        Number(sheet, 2, outputCol + 1).Should().Be(25,
            "Sum of Amount must still total the ORIGINAL Amount column's values (East = 10 + 15) " +
            "after the insert shifted that column from C to D -- not silently switch to summing " +
            "whatever column now happens to sit at the old index 2");
        Number(sheet, 3, outputCol + 1).Should().Be(20, "West = Widget(20) must still resolve to the Amount column, not Product");

        pivot.DataFields.Should().ContainSingle();
        pivot.DataFields[0].SourceFieldIndex.Should().Be(3,
            "Amount lived at relative index 2 (column C) before the insert and must be remapped to " +
            "index 3 (column D) after a column was inserted at B, exactly tracking the column's new " +
            "physical position instead of being left stale");

        // The row field ("Region", at column A/index 0, BEFORE the insert point) must be completely
        // unaffected -- it never moved.
        pivot.RowFields.Should().ContainSingle();
        pivot.RowFields[0].SourceFieldIndex.Should().Be(0);
        Text(sheet, 2, outputCol).Should().Be("East");
        Text(sheet, 3, outputCol).Should().Be("West");
    }

    // --- no-regression sibling: inserting a column BEFORE every bound field's source column must
    // shift every binding uniformly (including the row field itself) ---

    [Fact]
    public void InsertColumn_BeforeAllBoundColumns_ShiftsEveryFieldBindingUniformly()
    {
        var (workbook, sheet, pivot) = CreateRegionProductAmountDatePivot("PivotColumnInsertBeforeAllFieldsTest");

        var outputCol = pivot.TargetRange.Start.Col;
        Number(sheet, 2, outputCol + 1).Should().Be(25);

        // Insert a new column at A (before Region itself) -- every original column, including the
        // ones the pivot is already bound to, shifts one column right, and so does the pivot's own
        // SourceRange/TargetRange (both sit at/after the insert point).
        var insert = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1);
        insert.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        outputCol = pivot.TargetRange.Start.Col;

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Id"));

        pivot.SourceRange.Start.Col.Should().Be(2, "Start.Col shifts right too when the insert point is at/before the range's own start");
        pivot.SourceRange.End.Col.Should().Be(5);

        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        refresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        Text(sheet, 1, outputCol).Should().Be("Region");
        Text(sheet, 1, outputCol + 1).Should().Be("Sum of Amount");
        Number(sheet, 2, outputCol + 1).Should().Be(25, "Region/Amount grouping must still resolve correctly after every column shifted right by the leading insert");
        Number(sheet, 3, outputCol + 1).Should().Be(20);

        pivot.RowFields[0].SourceFieldIndex.Should().Be(0, "Region's OFFSET within SourceRange is unchanged (both the field's column and SourceRange.Start moved right together)");
        pivot.DataFields[0].SourceFieldIndex.Should().Be(2, "Amount's OFFSET within SourceRange is unchanged (both the field's column and SourceRange.Start moved right together)");
    }
}
