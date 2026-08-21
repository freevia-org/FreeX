using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// freex-pivot-refresh F1: <see cref="SlicerModel.SourceFieldName"/> is captured ONCE, at slicer
/// creation, and never updated afterward. <see cref="PivotTableRefreshService"/>'s cache-field
/// reconciliation (via <c>PivotCacheFieldFactory.ReconcileFields</c>) matches the live source purely by
/// name/position: retyping the bound field's source header cell to a new name makes the old-named
/// cache field disappear and a brand-new field appear under the new name, at the SAME position. Before
/// the fix, <see cref="SetSlicerSelectionCommand"/>'s by-name lookup of the stale
/// <see cref="SlicerModel.SourceFieldName"/> then failed FOREVER (every subsequent click), even though
/// the pivot table itself kept working fine (its own <c>PivotFieldModel</c> row/column/page fields
/// address the field by <c>SourceFieldIndex</c> -- position -- not name).
/// <para>
/// These tests drive the real product entry points (<see cref="AddSlicerCommand"/>,
/// <see cref="SetSlicerSelectionCommand"/>, <see cref="PivotTableRefreshService.Refresh"/>) end to end,
/// exactly mirroring the finding's user gesture: insert a pivot + slicer, rename the source header,
/// refresh (F5), then click the slicer again.
/// </para>
/// </summary>
public sealed class R163_PivotSlicerSourceFieldRenameResilienceTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static NumberValue GrandTotal(Sheet sheet, PivotTableModel pivot)
    {
        var range = pivot.LastRenderedRange ?? pivot.TargetRange;
        var labelCell = sheet.GetCell(new CellAddress(range.Start.Sheet, range.End.Row, range.Start.Col));
        labelCell.Should().NotBeNull("the pivot must have rendered a Grand Total row");
        labelCell!.Value.Should().Be(new TextValue("Grand Total"));

        var valueCell = sheet.GetCell(new CellAddress(range.Start.Sheet, range.End.Row, range.Start.Col + 1));
        valueCell.Should().NotBeNull();
        return (NumberValue)valueCell!.Value!;
    }

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) BuildPivotOnRegionAmount()
    {
        var workbook = new Workbook("R163PivotSlicerRenameResilience");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var cache = new PivotCacheModel { CacheId = 1, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = "Data", SourceReference = "A1:B4" };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West", "North"]));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PT1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D3", "F10"),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        return (workbook, sheet, pivot);
    }

    [Fact]
    public void SetSlicerSelectionCommand_Apply_AfterSourceHeaderRenamedAndRefreshed_SelfHealsInsteadOfFailingForever()
    {
        var (workbook, sheet, pivot) = BuildPivotOnRegionAmount();

        // Insert the slicer through the real product command, exactly like the user gesture.
        var addOutcome = new AddSlicerCommand("Region Slicer", "PT1", "Region").Apply(new TestCommandContext(workbook));
        addOutcome.Success.Should().BeTrue(addOutcome.ErrorMessage);
        var slicer = workbook.Slicers.Single();

        var ctx = new TestCommandContext(workbook);

        // Sanity: the slicer works BEFORE the rename.
        var before = new SetSlicerSelectionCommand("Region Slicer", ["East"]).Apply(ctx);
        before.Success.Should().BeTrue(before.ErrorMessage);
        GrandTotal(sheet, pivot).Should().Be(new NumberValue(10));

        // The user gesture: retype the source header cell to a new name, then refresh (F5 / Refresh All).
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Territory"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot, rescanCacheSharedItems: true);

        // The cache field really did get renamed (not just re-keyed) -- confirms the premise of the bug.
        workbook.PivotCaches.Single().Fields[0].Name.Should().Be("Territory");

        // THE FIX: clicking the slicer again after the rename must still work, not fail forever with
        // "Connected PivotTable field was not found." (this assertion is what fails before the fix).
        var after = new SetSlicerSelectionCommand("Region Slicer", ["West"]).Apply(ctx);
        after.Success.Should().BeTrue(after.ErrorMessage);
        GrandTotal(sheet, pivot).Should().Be(new NumberValue(20));

        // Self-heal: the slicer's stale name must be corrected to the live header, so a subsequent
        // by-name lookup (e.g. after a save/reload, or from PivotTableRefreshService.ExtendBoundSlicerCacheItems)
        // resolves directly again instead of depending on the fallback every time.
        slicer.SourceFieldName.Should().Be("Territory");
    }

    [Fact]
    public void SetSlicerSelectionCommand_Apply_NameStaleAndNoKnownFieldIndex_StillFailsCleanly()
    {
        // Sibling / no-regression case: a slicer whose SourceFieldIndex was never resolved (e.g. loaded
        // from an older saved file, before this in-session cache existed) and whose SourceFieldName no
        // longer matches any live header must still fail with the existing, safe
        // "Connected PivotTable field was not found." outcome -- the fallback must never mask a
        // genuinely unresolvable binding.
        var (workbook, sheet, pivot) = BuildPivotOnRegionAmount();

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PT1",
            SourceFieldName = "DoesNotExist",
            // SourceFieldIndex deliberately left null, as it would be for any slicer that pre-dates the
            // R163 fix and was loaded from disk without ever going through a successful in-session Apply.
        };
        workbook.Slicers.Add(slicer);

        var ctx = new TestCommandContext(workbook);
        var outcome = new SetSlicerSelectionCommand("Region Slicer", ["East"]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Connected PivotTable field was not found.");

        // The pivot itself must be completely untouched by the failed attempt.
        GrandTotal(sheet, pivot).Should().Be(new NumberValue(60));
    }
}
