using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round-65 findings:
/// R65-services-autofilter-6-1 (one unsupported filterColumn on ANY column must not bail the whole
/// sheet's materialization -- the SUPPORTED columns must still materialize), and
/// R65-services-autofilter-6-3 (Top10/Average filter columns must rank/average only over rows still
/// visible under every OTHER active column's filter on load, mirroring
/// TopBottomFilterCommand.SelectBestRows/AverageFilterCommand's live scoping, instead of ranking over
/// the whole column).
/// </summary>
public sealed class R65_AutoFilterPartialUnsupportedAndScopedRankingTests
{
    // -----------------------------------------------------------------------------------------
    // R65-services-autofilter-6-1
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void MaterializeFilters_UnsupportedCustomFilterOnOtherColumn_StillMaterializesSupportedValueListColumn()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(300));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        // Column A: a plain, fully-supported value-list filter.
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["East"]));
        // Column B: an unsupported customFilter ("Amount > 100") the materializer cannot represent.
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            1,
            [],
            IncludeBlank: false,
            CustomFilters: [new WorksheetAutoFilterCustomFilterModel("greaterThan", "100")],
            CustomFiltersAnd: false,
            NativeCustomFiltersAttributes: null,
            NativeFilterXmls: []));

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        // Before the fix, the count-guard saw 1 supported filter + 0 counted-unfiltered columns !=
        // 2 total FilterColumns and bailed the ENTIRE sheet, leaving all three collections below
        // empty even though column A's value-list filter is perfectly representable.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
        sheet.ActiveValueFilterColumns.Should().ContainKey(1);
        sheet.ActiveValueFilterColumns[1].Should().BeEquivalentTo(["East"]);
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([3u]);

        // "Clear Filter From Region" must now actually have a materialized filter to clear.
        var ctx = new TestCommandContext(workbook);
        var clear = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: []);
        clear.Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void MaterializeFilters_SingleUnsupportedColumn_ReclassifiesRawHiddenRowAsFilterHidden()
    {
        // R98-io-autofilter-unsupported-hiddenrows-1: this test used to assert the pre-fix (buggy)
        // behavior -- that a row hidden purely by an unsupported customFilter stayed stranded in
        // sheet.HiddenRows forever, since no filter-clearing path (FilterCommand.RecomputeHiddenRows,
        // ToggleWorksheetAutoFilterCommand.Apply) ever mutates HiddenRows. Real Excel always un-hides
        // such a row on Clear Filter / Toggle AutoFilter off regardless of which filter mechanism hid
        // it, so the row must now be reclassified into FilterHiddenRows on load instead.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(200));

        // Simulates the raw <row hidden="1"/> bit XlsxFileAdapter.ApplySheetXmlLayout already applied
        // to sheet.HiddenRows from the row-layout XML *before* MaterializeFilters runs, for a row this
        // (unsupported) customFilter was actually hiding in the source workbook.
        sheet.HiddenRows.Add(3u);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            0,
            [],
            IncludeBlank: false,
            CustomFilters: [new WorksheetAutoFilterCustomFilterModel("greaterThan", "100")],
            CustomFiltersAnd: false,
            NativeCustomFiltersAttributes: null,
            NativeFilterXmls: []));

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        // Nothing supported was built, so no *value-list* ownership is registered...
        sheet.ActiveValueFilterColumns.Should().BeEmpty();
        // ...but the raw hidden-row bit -- fully explained by the skipped customFilter column -- must
        // be reclassified as filter-hidden so Clear Filter / Toggle AutoFilter off can restore it.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
        sheet.HiddenRows.Should().NotContain(3u);
    }

    // -----------------------------------------------------------------------------------------
    // R65-services-autofilter-6-3
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void MaterializeFilters_Top10CombinedWithValueListFilter_RanksOnlyOverRowsVisibleUnderOtherFilter()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(1000));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new NumberValue(20));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["Apple"]));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(1, [])
        {
            Top10 = new WorksheetAutoFilterTop10Model(Top: true, Percent: false, Value: 1)
        });

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        // The whole-column Top 1 by Amount is row 5 (1000, Banana) -- but Banana already fails the
        // Fruit="Apple" filter, so ranking over the WHOLE column (the pre-fix behavior) leaves every
        // row hidden. Excel/the live TopBottomFilterCommand instead ranks only over rows still visible
        // under the OTHER active column's filter (the three Apple rows: 10, 100, 20), so the true
        // Top 1 is row 3 (100) -- exactly what a live apply of these two filters, one at a time, would
        // keep visible.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u, 5u, 6u]);
    }

    [Fact]
    public void MaterializeFilters_AboveAverageCombinedWithValueListFilter_ScopesAverageToOtherFilterVisibleRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(1000));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(30));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["Keep"]));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(1, [])
        {
            DynamicFilter = new WorksheetAutoFilterDynamicFilterModel(Type: "aboveAverage")
        });

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        // Whole-column average of 10/1000/20/30 is 265, so only row 3 (1000) is "above average" --
        // but row 3 already fails Category="Keep". Ranking the whole column (pre-fix) therefore hides
        // every row. Scoped to the Category-visible rows (10, 20, 30), the average is 20, and only row
        // 5 (30) is strictly above it, matching what live sequential apply of these two filters keeps.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u]);
    }

    [Fact]
    public void MaterializeFilters_StandaloneTop10WithoutOtherFilter_IsUnaffectedByOtherFilterScoping()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(20));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, [])
        {
            Top10 = new WorksheetAutoFilterTop10Model(Top: true, Percent: false, Value: 2, FilterValue: 20)
        });

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        // No other active column filter exists, so there is nothing to scope against: the
        // filterVal-based keep-threshold (>= 20) behaves exactly as before the fix.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u]);
    }
}
