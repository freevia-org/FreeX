using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the H-protection2 review-4 fixes:
/// J15 (a form-control click must not flip the control's visible state when sheet protection
/// rejects the linked-cell write), J40 (SetTimelineGranularityCommand must gate on
/// UsePivotTableReports like every sibling slicer/timeline command), and J56 (slicer/timeline
/// commands must check protection of the sheet hosting the WIDGET itself, not just the sheet
/// hosting the connected PivotTable, since the two can differ).
/// </summary>
public sealed class HProtection2FixesTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static void ProtectFully(Sheet sheet)
    {
        sheet.ProtectionPermissions.Clear();
        sheet.IsProtected = true;
    }

    // ── J15: form control click must not desync visible state on protection rejection ──────────

    [Fact]
    public void ToggleCheckBox_LinkedCellOnProtectedSheet_ReturnsNullAndLeavesIsCheckedUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var addr = Addr(sheet, "A1");
        ProtectFully(sheet);

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = "A1",
        };

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, wb);

        cmd.Should().BeNull("the linked cell's sheet is protected and the cell is locked (default style)");
        control.IsChecked.Should().BeFalse("a rejected write must never flip the checkbox's visible state");
        sheet.GetCell(addr).Should().BeNull("the linked cell itself must be untouched");
    }

    [Fact]
    public void ToggleCheckBox_LinkedCellInAllowEditRange_StillSucceedsOnProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var addr = Addr(sheet, "A1");
        ProtectFully(sheet);
        sheet.AllowEditRanges.Add(Range(sheet, "A1", "A1"));

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = "A1",
        };

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, wb);

        cmd.Should().NotBeNull("A1 is inside an AllowEditRange, so the write is permitted");
        control.IsChecked.Should().BeTrue("model flips once the write is confirmed to be allowed");

        var ctx = new TestCommandContext(wb);
        var outcome = cmd!.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetCell(addr)!.Value.Should().Be(new BoolValue(true));
    }

    [Fact]
    public void SelectOptionButton_LinkedCellOnProtectedSheet_ReturnsNullAndLeavesGroupUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        ProtectFully(sheet);

        var first = new FormControlModel
        {
            Kind = FormControlKind.OptionButton,
            IsChecked = true,
            LinkedCell = "A1",
        };
        var second = new FormControlModel
        {
            Kind = FormControlKind.OptionButton,
            IsChecked = false,
            LinkedCell = "A1",
        };
        var all = new List<FormControlModel> { first, second };

        var cmd = FormControlInteractionService.CreateSelectOptionButtonCommand(second, all, sheet.Id, wb);

        cmd.Should().BeNull("the shared linked cell's sheet is protected");
        first.IsChecked.Should().BeTrue("the previously-selected sibling must stay selected");
        second.IsChecked.Should().BeFalse("the clicked button must not be marked selected on a rejected write");
    }

    [Fact]
    public void StepSpinner_LinkedCellOnProtectedSheet_ReturnsNullAndLeavesValueUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        ProtectFully(sheet);

        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 5,
            Min = 1,
            Max = 10,
            Increment = 1,
            LinkedCell = "A1",
        };

        var cmd = FormControlInteractionService.CreateStepCommand(control, delta: 1, sheet.Id, wb);

        cmd.Should().BeNull("the linked cell's sheet is protected");
        control.Value.Should().Be(5, "a rejected write must never change the spinner's displayed value");
    }

    [Fact]
    public void SelectListItem_LinkedCellOnProtectedSheet_ReturnsNullAndLeavesSelectedIndexUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        ProtectFully(sheet);

        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            SelectedIndex = 1,
            LinkedCell = "A1",
        };

        var cmd = FormControlInteractionService.CreateSelectListItemCommand(control, oneBasedIndex: 2, sheet.Id, wb);

        cmd.Should().BeNull("the linked cell's sheet is protected");
        control.SelectedIndex.Should().Be(1, "a rejected write must never change the list's visible selection");
    }

    // ── J40 / J56 shared pivot fixture ───────────────────────────────────────────────────────────

    private static (Workbook Workbook, Sheet DataSheet, PivotTableModel Pivot) MakeConnectedPivot(string dataSheetName = "Data")
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet(dataSheetName);
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Product"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Sales"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(200));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(wb, sheet, pivot);

        return (wb, sheet, pivot);
    }

    // ── J40: SetTimelineGranularityCommand must gate on UsePivotTableReports ────────────────────

    [Fact]
    public void SetTimelineGranularityCommand_PivotSheetProtectedWithoutPermission_RejectsAndLeavesLevelUnchanged()
    {
        var (wb, sheet, pivot) = MakeConnectedPivot();
        var timeline = new TimelineModel
        {
            Name = "Timeline1",
            CacheName = "Timeline_1",
            SourcePivotTableName = pivot.Name,
            SourceFieldName = "Region",
            Level = 2,
        };
        wb.Timelines.Add(timeline);
        ProtectFully(sheet);

        var ctx = new TestCommandContext(wb);
        var command = new SetTimelineGranularityCommand("Timeline1", newLevel: 0);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse("the connected PivotTable's sheet is protected without UsePivotTableReports");
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        timeline.Level.Should().Be(2, "a rejected command must leave the granularity unchanged");
    }

    [Fact]
    public void SetTimelineGranularityCommand_PivotSheetProtectedWithPermission_Succeeds()
    {
        var (wb, sheet, pivot) = MakeConnectedPivot();
        var timeline = new TimelineModel
        {
            Name = "Timeline1",
            CacheName = "Timeline_1",
            SourcePivotTableName = pivot.Name,
            SourceFieldName = "Region",
            Level = 2,
        };
        wb.Timelines.Add(timeline);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);

        var ctx = new TestCommandContext(wb);
        var command = new SetTimelineGranularityCommand("Timeline1", newLevel: 0);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        timeline.Level.Should().Be(0);

        command.Revert(ctx);
        timeline.Level.Should().Be(2, "undo must restore the previous granularity");
    }

    [Fact]
    public void SetTimelineGranularityCommand_NoConnectedPivotTable_StillAppliesWithNoProtectionLookup()
    {
        // A timeline that isn't (yet) connected to any PivotTable must not throw or require a
        // sheet lookup — it simply has no protection gate to check.
        var wb = new Workbook("test");
        var timeline = new TimelineModel
        {
            Name = "Timeline1",
            CacheName = "Timeline_1",
            Level = 2,
        };
        wb.Timelines.Add(timeline);

        var ctx = new TestCommandContext(wb);
        var command = new SetTimelineGranularityCommand("Timeline1", newLevel: 1);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        timeline.Level.Should().Be(1);
    }

    // ── J56: slicer/timeline commands must also check the WIDGET's own host sheet ───────────────

    [Fact]
    public void SetSlicerSelectionCommand_WidgetHostSheetProtectedWithoutPermission_RejectsEvenThoughPivotSheetIsUnprotected()
    {
        var (wb, dataSheet, pivot) = MakeConnectedPivot();
        var dashboardSheet = wb.AddSheet("Dashboard");
        ProtectFully(dashboardSheet);

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = pivot.Name,
            SourceFieldName = "Region",
            SourceSheetName = dashboardSheet.Name,
        };
        wb.Slicers.Add(slicer);

        dataSheet.IsProtected.Should().BeFalse("the pivot table's own sheet is not protected");

        var ctx = new TestCommandContext(wb);
        var command = new SetSlicerSelectionCommand("Region Slicer", ["West"]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse(
            "the slicer widget itself lives on a protected sheet (Dashboard) even though the connected pivot's sheet (Data) is unprotected");
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        slicer.SelectedItems.Should().BeEmpty("a rejected command must not mutate the slicer's selection");
    }

    [Fact]
    public void SetSlicerSelectionCommand_WidgetHostSheetProtectedWithPermission_Succeeds()
    {
        var (wb, dataSheet, pivot) = MakeConnectedPivot();
        var dashboardSheet = wb.AddSheet("Dashboard");
        dashboardSheet.IsProtected = true;
        dashboardSheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = pivot.Name,
            SourceFieldName = "Region",
            SourceSheetName = dashboardSheet.Name,
        };
        wb.Slicers.Add(slicer);

        var ctx = new TestCommandContext(wb);
        var command = new SetSlicerSelectionCommand("Region Slicer", ["West"]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        slicer.SelectedItems.Should().Contain("West");
    }

    [Fact]
    public void SetTimelineRangeCommand_WidgetHostSheetProtectedWithoutPermission_RejectsEvenThoughPivotSheetIsUnprotected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Product"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Sales"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "B2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "B3"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(200));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(wb, sheet, pivot);

        var dashboardSheet = wb.AddSheet("Dashboard");
        ProtectFully(dashboardSheet);

        var timeline = new TimelineModel
        {
            Name = "Timeline1",
            CacheName = "Timeline_1",
            SourcePivotTableName = pivot.Name,
            SourceFieldName = "Date",
            SourceSheetName = dashboardSheet.Name,
        };
        wb.Timelines.Add(timeline);

        var ctx = new TestCommandContext(wb);
        var command = new SetTimelineRangeCommand("Timeline1", "2026-01-01", "2026-01-31");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse(
            "the timeline widget itself lives on a protected sheet (Dashboard) even though the connected pivot's sheet (Data) is unprotected");
        timeline.SelectedStartDate.Should().BeNull("a rejected command must not mutate the timeline's selection");
    }

    [Fact]
    public void SetTimelineGranularityCommand_WidgetHostSheetProtectedWithoutPermission_RejectsEvenThoughPivotSheetIsUnprotected()
    {
        var (wb, dataSheet, pivot) = MakeConnectedPivot();
        var dashboardSheet = wb.AddSheet("Dashboard");
        ProtectFully(dashboardSheet);

        var timeline = new TimelineModel
        {
            Name = "Timeline1",
            CacheName = "Timeline_1",
            SourcePivotTableName = pivot.Name,
            SourceFieldName = "Region",
            SourceSheetName = dashboardSheet.Name,
            Level = 2,
        };
        wb.Timelines.Add(timeline);

        dataSheet.IsProtected.Should().BeFalse();

        var ctx = new TestCommandContext(wb);
        var command = new SetTimelineGranularityCommand("Timeline1", newLevel: 0);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse(
            "the timeline widget itself lives on a protected sheet (Dashboard) even though the connected pivot's sheet (Data) is unprotected");
        timeline.Level.Should().Be(2, "a rejected command must leave the granularity unchanged");
    }
}
