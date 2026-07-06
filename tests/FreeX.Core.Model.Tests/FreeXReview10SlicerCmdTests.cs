using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the round-10 SLICER-CMD review findings:
/// P8 (WPF on-grid slicer tile click must use Excel/Avalonia REPLACE semantics, not the additive
/// toggle a plain click on the native grid path was wrongly using — H45 regression),
/// P9 (timeline clear-filter must remove the pivot field's filter entirely, not install an explicit
/// "every date currently present" selection that still hides blank/text rows and any later-added
/// out-of-snapshot dates),
/// P13 (pivot-slicer tile captions resolved from raw pivot-cache shared items must be normalized the
/// same way PivotTableRefreshService.GroupKeyText/KeyText formats the row key, so a date/locale-number
/// tile actually matches rows instead of filtering the whole pivot to nothing).
/// </summary>
public sealed class FreeXReview10SlicerCmdTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    // ── P9: timeline clear must remove the filter, not re-select "every date present today" ───────

    [Fact]
    public void SetTimelineRangeCommand_ClearingRange_RemovesFieldFilterEntirely()
    {
        var workbook = new Workbook("P9TimelineClearTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Product"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Sales"));
        // Two dated rows plus one row with a BLANK date cell — Excel keys the blank row to its own
        // "(blank)" bucket and a genuine filter-clear must restore it along with the dated rows.
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "B2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "B3"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(200));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("Widget"));
        // B4 intentionally left blank.
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(400));

        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C4"),
            TargetRange = Range(sheet, "E3", "G7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Sanity: unfiltered grand total across all three rows (100 + 200 + 400 = 700).
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(700));

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date"
        });

        // Drag the timeline to January only: Widget's total narrows to the Jan row (100).
        new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-01-31").Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(100));

        // Click the timeline's clear (x) icon: both bounds go back to null.
        var clearOutcome = new SetTimelineRangeCommand("Date Timeline", null, null).Apply(ctx);
        clearOutcome.Success.Should().BeTrue(clearOutcome.ErrorMessage);

        // The field's SelectedItems must be gone entirely (null), not an explicit list of "every date
        // that existed at clear time" — the pre-fix code installed such a list via
        // ReadSelectedItems(MinValue, MaxValue), which silently dropped the blank-date row forever.
        var dateField = pivot.RowFields.Concat(pivot.ColumnFields).Concat(pivot.PageFields)
            .First(field => field.SourceFieldIndex == 1);
        dateField.SelectedItems.Should().BeNull("a real filter clear must remove the SelectedItems list, not replace it with a snapshot");
        dateField.SelectedItem.Should().BeNull();

        // The blank-date row (400) and both dated rows (100 + 200) must all be back: 700 total.
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(700),
            "clearing the timeline must restore every row, including the blank-date row that " +
            "ReadSelectedItems(MinValue, MaxValue) always excluded");
    }

    // ── P13: pivot-slicer captions must be normalized to match the refresh filter's row key ────────

    [Fact]
    public void SlicerItemResolver_DateField_NormalizesRawSharedItemToShortDateCaption()
    {
        var workbook = new Workbook("P13SlicerDateCaptionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Sales"));
        var seededDate = new DateTime(2026, 1, 5);
        sheet.SetCell(Addr(sheet, "A2"), DateTimeValue.FromDateTime(seededDate));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "A3"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(200));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var cache = new PivotCacheModel { CacheId = 1 };
        // Raw OOXML shared-item attribute string exactly as XlsxPivotCacheReader would parse a
        // <d v="2026-01-05T00:00:00"/> element — untouched by locale or grouping.
        cache.Fields.Add(new PivotCacheFieldModel(
            Name: "Date",
            ContainsDate: true,
            SharedItems: ["2026-01-05T00:00:00"],
            SharedItemKinds: ['d']));
        workbook.PivotCaches.Add(cache);

        var slicer = new SlicerModel
        {
            Name = "Date Slicer",
            CacheName = "Slicer_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            CacheItems = [new SlicerCacheItem(Index: 0, IsSelected: false)]
        };
        workbook.Slicers.Add(slicer);

        var resolved = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);

        // The caption must be reformatted to match GroupKeyText/KeyText's ToShortDateString() output
        // for an ungrouped date — NOT the raw "2026-01-05T00:00:00" attribute string.
        resolved.Should().ContainSingle().Which.Should().Be(seededDate.ToShortDateString());
        resolved.Should().NotContain("2026-01-05T00:00:00");

        // Behavioral round-trip: clicking that resolved tile must actually filter the pivot to the
        // matching row, not empty it out (the pre-fix raw-ISO caption never equals
        // GroupKeyText(row) == date.ToShortDateString(), so MatchesFieldSelections rejected every row).
        var ctx = new TestCommandContext(workbook);
        var command = new SetSlicerSelectionCommand("Date Slicer", resolved.ToList());
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue(seededDate.ToShortDateString()));
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new NumberValue(100));
        // Only one row group must remain (the Feb row is filtered out) — a Grand Total row plus the
        // single matched date row, not an empty report.
        sheet.GetCell(Addr(sheet, "D5"))!.Value.Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(Addr(sheet, "E5"))!.Value.Should().Be(new NumberValue(100));
    }

    [Fact]
    public void SlicerItemResolver_NumberField_NormalizesRawSharedItemToCurrentCultureCaption()
    {
        var workbook = new Workbook("P13SlicerNumberCaptionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Quantity"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Sales"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(1234.5));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(7));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(200));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var cache = new PivotCacheModel { CacheId = 1 };
        // Shared-item numbers are always stored with an invariant (dot-decimal) "v" attribute
        // regardless of the running locale.
        cache.Fields.Add(new PivotCacheFieldModel(
            Name: "Quantity",
            ContainsNumber: true,
            SharedItems: ["1234.5"],
            SharedItemKinds: ['n']));
        workbook.PivotCaches.Add(cache);

        var slicer = new SlicerModel
        {
            Name = "Quantity Slicer",
            CacheName = "Slicer_Quantity",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Quantity",
            CacheItems = [new SlicerCacheItem(Index: 0, IsSelected: false)]
        };
        workbook.Slicers.Add(slicer);

        var resolved = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);

        // Reformatted with CurrentCulture, matching KeyText(NumberValue) — the row's own key text.
        resolved.Should().ContainSingle().Which.Should().Be((1234.5).ToString(System.Globalization.CultureInfo.CurrentCulture));

        var ctx = new TestCommandContext(workbook);
        var command = new SetSlicerSelectionCommand("Quantity Slicer", resolved.ToList());
        command.Apply(ctx).Success.Should().BeTrue();

        // The 1234.5 row must be the only one left; the 7 row is filtered out.
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new NumberValue(100));
        sheet.GetCell(Addr(sheet, "D5"))!.Value.Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(Addr(sheet, "E5"))!.Value.Should().Be(new NumberValue(100));
    }
}
