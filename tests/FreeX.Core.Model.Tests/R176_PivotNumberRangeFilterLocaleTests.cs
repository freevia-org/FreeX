using System.Globalization;

using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// r176: the round-174 fix for locale-keyed pivot item filters (shared-localization-rtl F1) scoped
// itself to ungrouped fields and recorded, in its own doc comment, that "NumberRange-grouped bucket
// labels are a separate, not-yet-addressed instance of this same class of bug outside this finding's
// scope". This is that instance. NumberRangeKeyText builds its labels ("0.5-1", ">100", "<0") by string
// interpolation, which formats the bounds with CurrentCulture -- so the identical bucket is "1,5-2" on
// de-DE and "1.5-2" on en-US. A checked-item caption persisted under one culture then matches nothing
// when the workbook is reopened under another, and hasExplicitSelection stays true with no match, which
// empties the field's filter -- exactly the failure the ungrouped case produced.
public sealed partial class PivotTableRefreshServiceTests
{
    private static void SeedFractionalQuantityDataForGrouping(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Quantity"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(0.5));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(1.5));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new NumberValue(2.5));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
    }

    private static PivotTableModel NumberRangeGroupedPivot(Sheet sheet, IReadOnlyList<string> selectedItems)
    {
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D2", "F8")
        };
        // Buckets of width 1 starting at 0: 0.5 -> "0-1", 1.5 -> "1-2", 2.5 -> "2-3". A fractional
        // interval is what exposes the decimal separator; an integer interval would format identically
        // under both cultures and hide the defect.
        pivot.RowFields.Add(new PivotFieldModel(
            0,
            SelectedItems: selectedItems,
            Grouping: PivotFieldGrouping.NumberRange,
            GroupStart: 0,
            GroupEnd: null,
            GroupInterval: 0.5));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        return pivot;
    }

    [Fact]
    public void Refresh_NumberRangeGroupedSelectedItems_MatchesInvariantFormattedBucketCaption_UnderCommaDecimalCulture()
    {
        RunUnderCulture("de-DE", () =>
        {
            var workbook = new Workbook("PivotRefreshTest");
            var sheet = workbook.AddSheet("Data");
            SeedFractionalQuantityDataForGrouping(sheet);

            // "1.5-2" is the invariant spelling of the bucket holding 1.5 -- what the caption would be
            // if it had been persisted on an en-US machine (or by any invariant-formatting caption
            // source). Before r176 this matched nothing under de-DE, emptying the filter.
            var pivot = NumberRangeGroupedPivot(sheet, ["1.5-2"]);

            PivotTableRefreshService.Refresh(workbook, sheet, pivot);

            // The surviving row's own label is still rendered under the active culture, so it shows the
            // de-DE comma form -- the invariant spelling is a matching candidate only, never display.
            Text(sheet, "D3").Should().Be("1,5-2");
            Number(sheet, "E3").Should().Be(20);
            Text(sheet, "D4").Should().Be("Grand Total");
            Number(sheet, "E4").Should().Be(20);
            sheet.GetCell(Addr(sheet, "D5")).Should().BeNull();
        });
    }

    [Fact]
    public void Refresh_NumberRangeGroupedSelectedItems_StillMatchesCurrentCultureBucketCaption_UnderCommaDecimalCulture()
    {
        RunUnderCulture("de-DE", () =>
        {
            var workbook = new Workbook("PivotRefreshTest");
            var sheet = workbook.AddSheet("Data");
            SeedFractionalQuantityDataForGrouping(sheet);

            // No-regression sibling: a caption captured under the SAME de-DE culture the refresh runs
            // under must keep matching. The invariant candidate supplements the CurrentCulture match,
            // it does not replace it.
            var pivot = NumberRangeGroupedPivot(sheet, ["1,5-2"]);

            PivotTableRefreshService.Refresh(workbook, sheet, pivot);

            Text(sheet, "D3").Should().Be("1,5-2");
            Number(sheet, "E3").Should().Be(20);
            sheet.GetCell(Addr(sheet, "D5")).Should().BeNull();
        });
    }

    [Fact]
    public void Refresh_NumberRangeGroupedSelectedItems_NonMatchingBucketStillFiltersEverythingOut()
    {
        RunUnderCulture("de-DE", () =>
        {
            var workbook = new Workbook("PivotRefreshTest");
            var sheet = workbook.AddSheet("Data");
            SeedFractionalQuantityDataForGrouping(sheet);

            // The invariant fallback must not be so loose that an unrelated caption starts matching:
            // no source row falls in the 9-9.5 bucket, so the filter must still exclude everything.
            var pivot = NumberRangeGroupedPivot(sheet, ["9-9.5"]);

            PivotTableRefreshService.Refresh(workbook, sheet, pivot);

            // No bucket row renders at all -- the Grand Total moves up into the first data row slot,
            // which is what an all-rows-excluded refresh looks like.
            Text(sheet, "D3").Should().Be("Grand Total",
                "a caption naming a bucket no row belongs to must not match any row under either " +
                "culture, so no bucket row should render above the Grand Total");
            sheet.GetCell(Addr(sheet, "D4")).Should().BeNull();
        });
    }
}
