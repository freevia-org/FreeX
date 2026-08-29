using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R168-commands-pivot-matrixwriter: three MatrixWriter fixes.
///
/// F1: Outline Form rendered byte-identical to Tabular Form -- the row-label loop only
/// special-cased Compact vs "everything else", so Outline never gave the outer row field its
/// own header row. Fixed by giving Outline its own branch that emits a separate header row per
/// outer level that changed since the previous row group (mirroring the non-matrix writer's
/// Compact multi-row indent pass, but writing to each level's own column instead of col 0).
///
/// F2: A column subtotal / grand-total slot summed rows by RAW column-key prefix match against
/// the UNFILTERED column-row map, so a nested column item hidden by a Label/Value filter still
/// leaked into its outer subtotal and the grand total. Fixed by intersecting the prefix match
/// with the currently-visible (post-filter) column key set in RowsForSlot.
///
/// F3: "Insert Blank Line After Each Item" was gated by rowFields.Count > 1, so it was a total
/// no-op for the (very common) single-row-field pivot. Fixed by also firing every row group when
/// there is exactly one row field (there is no "outer" item distinct from the leaf in that case).
/// </summary>
public sealed class R168_PivotMatrixWriterLayoutFixesTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static string Text(Sheet sheet, string a1) =>
        sheet.GetCell(Addr(sheet, a1))?.Value is TextValue text ? text.Value : "";

    private static double Number(Sheet sheet, string a1) =>
        sheet.GetCell(Addr(sheet, a1))?.Value is NumberValue number ? number.Value : double.NaN;

    private static bool IsBlank(Sheet sheet, string a1) =>
        sheet.GetCell(Addr(sheet, a1)) is null || sheet.GetCell(Addr(sheet, a1))!.Value is BlankValue;

    // Region/Quarter/Channel/Amount where Channel is constant ("Direct") -- gives a single-item
    // column field, just enough to force the pivot through WriteMatrixPivot (the file under
    // test) while keeping the row-group math (2 row fields x 2 quarters) easy to hand-trace.
    private static void SeedTwoRowFieldMatrixData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Channel"));
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new TextValue("Direct"));
        sheet.SetCell(Addr(sheet, "D2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new TextValue("Direct"));
        sheet.SetCell(Addr(sheet, "D3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C4"), new TextValue("Direct"));
        sheet.SetCell(Addr(sheet, "D4"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new TextValue("Direct"));
        sheet.SetCell(Addr(sheet, "D5"), new NumberValue(40));
    }

    private static void SeedSalesChannelData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Channel"));
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C3"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C4"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D4"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D5"), new NumberValue(25));
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D6"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A7"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B7"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C7"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D7"), new NumberValue(35));
        sheet.SetCell(Addr(sheet, "A8"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B8"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C8"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D8"), new NumberValue(40));
        sheet.SetCell(Addr(sheet, "A9"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B9"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C9"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D9"), new NumberValue(45));
    }

    // ── F1: Outline Form must give the outer row field its own header row ──────────────────
    //
    // Layout math: 2 row fields (Region outer, Quarter leaf), 1 column field with a single
    // constant item ("Direct") -> column header consumes exactly 1 row (row 2), so row groups
    // start at row 3. East -> header row 3 (Region only), then Q1 (row 4) and Q2 (row 5) each
    // shifted one column right, sharing no row with "East". West repeats the pattern at row 6-8.

    [Fact]
    public void Refresh_MatrixOutlineForm_GivesOuterRowFieldItsOwnHeaderRow()
    {
        var workbook = new Workbook("PivotOutlineMatrixTest");
        var sheet = workbook.AddSheet("Data");
        SeedTwoRowFieldMatrixData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "F2", "K12"),
            ReportLayout = PivotReportLayout.Outline,
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0)); // Region (outer)
        pivot.RowFields.Add(new PivotFieldModel(1)); // Quarter (leaf)
        pivot.ColumnFields.Add(new PivotFieldModel(2)); // Channel -- forces WriteMatrixPivot
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Row 3: "East" alone in the Region column -- its own header row, no data.
        Text(sheet, "F3").Should().Be("East");
        IsBlank(sheet, "G3").Should().BeTrue("Outline gives the outer item its own row with no data");
        IsBlank(sheet, "H3").Should().BeTrue("the header-only row carries no aggregate value");

        // Row 4: Quarter "Q1" shifted one column right, with East's Q1 data on the SAME row.
        IsBlank(sheet, "F4").Should().BeTrue("the outer field must not repeat on the subordinate row");
        Text(sheet, "G4").Should().Be("Q1");
        Number(sheet, "H4").Should().Be(10);

        // Row 5: Quarter "Q2", still under East (no new "East" header row).
        IsBlank(sheet, "F5").Should().BeTrue();
        Text(sheet, "G5").Should().Be("Q2");
        Number(sheet, "H5").Should().Be(20);

        // Row 6: "West" gets its own header row again, since the outer field changed.
        Text(sheet, "F6").Should().Be("West");
        IsBlank(sheet, "G6").Should().BeTrue();

        Text(sheet, "G7").Should().Be("Q1");
        Number(sheet, "H7").Should().Be(30);
        Text(sheet, "G8").Should().Be("Q2");
        Number(sheet, "H8").Should().Be(40);
    }

    [Fact]
    public void Refresh_MatrixTabularForm_KeepsAllRowFieldsOnTheSameRowAsData()
    {
        // Sibling no-regression: Tabular form (the case the buggy else-branch was ALSO handling)
        // must still put every row field's value on the SAME row as its data -- unaffected by
        // the new Outline-only branch. RepeatItemLabels defaults true, so the Region value is
        // NOT suppressed on the second Quarter row.
        var workbook = new Workbook("PivotTabularMatrixSiblingTest");
        var sheet = workbook.AddSheet("Data");
        SeedTwoRowFieldMatrixData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "F2", "K12"),
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F3").Should().Be("East");
        Text(sheet, "G3").Should().Be("Q1");
        Number(sheet, "H3").Should().Be(10);
        Text(sheet, "F4").Should().Be("East");
        Text(sheet, "G4").Should().Be("Q2");
        Number(sheet, "H4").Should().Be(20);
        Text(sheet, "F5").Should().Be("West");
        Text(sheet, "G5").Should().Be("Q1");
        Number(sheet, "H5").Should().Be(30);
    }

    // ── F2: a filtered nested column item must not leak into its outer subtotal/grand total ──

    [Fact]
    public void Refresh_MatrixColumnSubtotal_ExcludesRowsFromLabelFilteredNestedColumnItem()
    {
        var workbook = new Workbook("PivotColumnSubtotalFilterLeakTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "L10"),
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0)); // Region
        pivot.ColumnFields.Add(new PivotFieldModel(1)); // Quarter (outer)
        pivot.ColumnFields.Add(new PivotFieldModel(2)); // Channel (inner)
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));
        // Hide the "Retail" channel item -- only "Wholesale" remains visible under each quarter.
        pivot.LabelFilters.Add(new PivotLabelFilterModel(2, PivotLabelFilterKind.DoesNotEqual, "Retail"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Column layout post-filter: F=RowLabel, G=Q1/Wholesale, H=Q1 Total, I=Q2/Wholesale,
        // J=Q2 Total, K=Grand Total.
        Text(sheet, "G2").Should().Be("Q1");
        Text(sheet, "H2").Should().Be("Q1 Total");
        Text(sheet, "G3").Should().Be("Wholesale");

        Text(sheet, "F4").Should().Be("East");
        Number(sheet, "G4").Should().Be(15, "the only visible Q1 column is Wholesale");
        Number(sheet, "H4").Should().Be(15, "Q1 Total must equal its own visible Wholesale child, not include hidden Retail=10");
        Number(sheet, "I4").Should().Be(25);
        Number(sheet, "J4").Should().Be(25, "Q2 Total must not include hidden Retail=20");

        Text(sheet, "F5").Should().Be("West");
        Number(sheet, "G5").Should().Be(35);
        Number(sheet, "H5").Should().Be(35, "Q1 Total for West must not include hidden Retail=30");
        Number(sheet, "I5").Should().Be(45);
        Number(sheet, "J5").Should().Be(45, "Q2 Total for West must not include hidden Retail=40");

        Text(sheet, "F6").Should().Be("Grand Total");
        Number(sheet, "G6").Should().Be(50);
        Number(sheet, "H6").Should().Be(50, "the sheet-wide Q1 Total must equal the sum of its own visible Q1/Wholesale column (15+35), not 90");
        Number(sheet, "I6").Should().Be(70);
        Number(sheet, "J6").Should().Be(70, "the sheet-wide Q2 Total must equal the sum of its own visible Q2/Wholesale column (25+45), not 120");
    }

    [Fact]
    public void Refresh_MatrixColumnSubtotal_UnfilteredNestedColumnsStillSumAllVisibleChildren()
    {
        // Sibling no-regression: with NO Label/Value filter applied, a column subtotal must still
        // sum ALL of its (now fully visible) child columns exactly as before.
        var workbook = new Workbook("PivotColumnSubtotalNoFilterSiblingTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "N10"),
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F4").Should().Be("East");
        Number(sheet, "G4").Should().Be(10); // Q1/Retail
        Number(sheet, "H4").Should().Be(15); // Q1/Wholesale
        Number(sheet, "I4").Should().Be(25); // Q1 Total: 10 + 15, unchanged by the fix
        Text(sheet, "F6").Should().Be("Grand Total");
        Number(sheet, "G6").Should().Be(40);
        Number(sheet, "H6").Should().Be(50);
        Number(sheet, "I6").Should().Be(90); // Grand Q1 Total: 40 + 50, unchanged by the fix
    }

    // ── F3: "Insert Blank Line After Each Item" must fire with a single row field ────────────
    //
    // Layout math: 1 row field, 1 column field with a single constant item -> column header
    // consumes 1 row (row 2), so row groups start at row 3.

    [Fact]
    public void Refresh_MatrixBlankLineAfterItems_FiresForSingleRowField()
    {
        var workbook = new Workbook("PivotBlankLineSingleRowFieldTest");
        var sheet = workbook.AddSheet("Data");
        SeedTwoRowFieldMatrixData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "F2", "K12"),
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = false,
            BlankLineAfterItems = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0)); // Region -- the ONLY row field
        pivot.ColumnFields.Add(new PivotFieldModel(2)); // Channel -- forces WriteMatrixPivot
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F3").Should().Be("East");
        Number(sheet, "G3").Should().Be(30); // East total across both quarters (10+20)
        IsBlank(sheet, "F4").Should().BeTrue("a blank row must follow every item, even with only one row field");
        Text(sheet, "F5").Should().Be("West");
        Number(sheet, "G5").Should().Be(70);
        IsBlank(sheet, "F6").Should().BeTrue("a blank row must follow the last item too, before Grand Total");
        Text(sheet, "F7").Should().Be("Grand Total");
    }

    [Fact]
    public void Refresh_MatrixBlankLineAfterItems_MultiRowFieldStillOnlyBreaksOnOuterItem()
    {
        // Sibling no-regression: with 2+ row fields, blank lines must still be gated by
        // IsEndOfOuterItem (only after the OUTER field's item ends), not after every leaf row.
        var workbook = new Workbook("PivotBlankLineMultiRowFieldSiblingTest");
        var sheet = workbook.AddSheet("Data");
        SeedTwoRowFieldMatrixData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "F2", "K12"),
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = false,
            BlankLineAfterItems = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0)); // Region (outer)
        pivot.RowFields.Add(new PivotFieldModel(1)); // Quarter (leaf)
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F3").Should().Be("East");
        Text(sheet, "G3").Should().Be("Q1");
        Text(sheet, "F4").Should().Be("East");
        Text(sheet, "G4").Should().Be("Q2");
        // No blank line between East/Q1 and East/Q2 -- only after the outer item (Region) ends.
        IsBlank(sheet, "F5").Should().BeTrue("blank line only after the outer item ends");
        Text(sheet, "F6").Should().Be("West");
        Text(sheet, "G6").Should().Be("Q1");
    }
}
