using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R106: PivotTableRefreshService.Refresh writes every pivot body/header cell through SetPivotCell,
/// which previously had no merge guard at all. ClearRefreshRanges only ever un-merges/clears the
/// pivot's PREVIOUSLY known footprint (LastRenderedRange and TargetRange) -- never the new,
/// about-to-be-written extent -- so a pivot whose real render grows past that previously-known
/// footprint could silently plant a value into the covered (non-anchor) member of an unrelated,
/// pre-existing merged region. This corrupts the sheet's merge invariant (only a merge's top-left
/// anchor cell ever carries a value): the planted value stays hidden behind the merge's display,
/// yet a later unmerge or a formula reading that cell would suddenly see it.
/// </summary>
public sealed partial class R106_PivotRefreshMergedCellWriteGuardTests
{
    private static void SeedSalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new NumberValue(25));
    }

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Workbook workbook, Sheet sheet, PivotTableModel pivot) BuildGrowingPivot()
    {
        var workbook = new Workbook("PivotMergeGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);

        // TargetRange is deliberately just the anchor cell (E2:E2) -- the pivot's actual rendered
        // output (Region/Sum-of-Amount header + East/West rows + Grand Total, i.e. E2:F5) is much
        // larger than this anchor. ClearRefreshRanges only ever clears LastRenderedRange (null on
        // a first refresh) and TargetRange (E2:E2 here), so it never touches F3:F4 -- exactly the
        // gap the defect describes.
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "E2"),
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        return (workbook, sheet, pivot);
    }

    [Fact]
    public void Refresh_DoesNotPlantValueIntoNonAnchorMemberOfPreexistingMerge()
    {
        var (workbook, sheet, pivot) = BuildGrowingPivot();

        // A pre-existing merge (e.g. from an earlier, unrelated layout on this sheet) sitting
        // exactly where the pivot's Sum-of-Amount column will land for its two row groups: F3 is
        // the anchor, F4 is the covered member. Neither cell is inside the pivot's TargetRange.
        var foreignMerge = new GridRange(Addr(sheet, "F3"), Addr(sheet, "F4"));
        sheet.AddMergedRegion(foreignMerge);

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // The merge itself must survive untouched -- this pivot doesn't own it and never clears it.
        sheet.MergedRegions.Should().Contain(foreignMerge);
        sheet.GetMergeRegion(Addr(sheet, "F4")).Should().Be(foreignMerge);

        // F4 is the merge's non-anchor (covered) member: it must never receive West's sum (45),
        // matching the merge invariant every other writer (paste/sort/autofill/move/copy) upholds.
        sheet.GetCell(Addr(sheet, "F4")).Should().BeNull();

        // The anchor cell F3 is still fair game -- writing into a merge's own anchor is allowed
        // (PasteCellsCommand's guard only special-cases the non-anchor member too), so East's sum
        // still renders there.
        Number(sheet, "F3").Should().Be(25);

        // The row-label column (E) is untouched by the foreign merge and must render normally.
        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "E4").Should().Be("West");
    }

    /// <summary>
    /// Sibling/no-regression: a refresh with no conflicting pre-existing merges anywhere in its
    /// footprint must still render every cell exactly as before -- the new guard must not turn into
    /// an over-broad "skip everything" check.
    /// </summary>
    [Fact]
    public void Refresh_StillWritesFullOutputWhenNoMergeConflictExists()
    {
        var (workbook, sheet, pivot) = BuildGrowingPivot();

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Sum of Amount");
        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(25);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(70);
    }

    /// <summary>
    /// R106 sibling: MergeLabelRegion (MergeAndCenterLabels' own row-label merging) called
    /// sheet.AddMergedRegion with no check that the new region didn't already overlap an existing
    /// merge, unlike MergeCellsCommand's absorb-or-reject handling. Pre-seed a foreign merge that
    /// exactly coincides with the pivot's own future "East" outer-row-label merge (mirroring
    /// Refresh_MergeAndCenterLabelsMergesRepeatedOuterRowLabels' E3:E4/E5:E6 shape) and left outside
    /// ClearRefreshRanges' narrow pre-clear sweep (TargetRange is just the anchor cell) -- without
    /// the overlap check, AddMergedRegion would add a second, duplicate E3:E4 entry alongside the
    /// untouched original instead of replacing it.
    /// </summary>
    [Fact]
    public void Refresh_MergeAndCenterLabelsDoesNotDuplicateOverlappingMergedRegion()
    {
        var workbook = new Workbook("PivotMergeLabelOverlapTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);

        // Pre-seed a foreign merge sitting exactly where the pivot's own "East" outer-row-label
        // merge will land.
        var eastRegion = new GridRange(Addr(sheet, "E3"), Addr(sheet, "E4"));
        sheet.AddMergedRegion(eastRegion);

        // TargetRange is deliberately just the anchor cell (E2:E2), like BuildGrowingPivot above --
        // otherwise ClearRefreshRanges' own un-merge-inside-TargetRange sweep would already remove
        // this foreign merge before MergeLabelRegion ever runs, masking the defect this test targets
        // (MergeLabelRegion's own missing overlap check, not ClearRefreshRanges' narrower one).
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "E2"),
            ShowSubtotals = false,
            MergeAndCenterLabels = true,
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // The pivot's own outer-label merges must land exactly like the no-conflict case.
        sheet.MergedRegions.Should().Contain(eastRegion);
        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "E5"), Addr(sheet, "E6")));

        // Exactly one entry for the East region -- the pre-existing one must have been replaced,
        // not left alongside a freshly-added duplicate covering the same cells.
        sheet.MergedRegions.Count(region => region == eastRegion).Should().Be(1);

        // No two merged regions may overlap each other after the refresh.
        var regions = sheet.MergedRegions;
        for (var i = 0; i < regions.Count; i++)
        for (var j = i + 1; j < regions.Count; j++)
            regions[i].Overlaps(regions[j]).Should().BeFalse(
                $"merged regions {regions[i]} and {regions[j]} must not overlap");
    }

    private static string Text(Sheet sheet, string a1) =>
        sheet.GetCell(Addr(sheet, a1))?.Value is TextValue text ? text.Value : "";

    private static double Number(Sheet sheet, string a1) =>
        sheet.GetCell(Addr(sheet, a1))?.Value is NumberValue number ? number.Value : double.NaN;
}
