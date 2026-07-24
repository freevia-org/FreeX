using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R83-app-view-modes-5-1: each window keeps its own view mode/zoom for a sheet, independent of
/// any sibling window sharing the same underlying Sheet object (Excel "New Window").
/// </summary>
public sealed class WorksheetViewStateStoreTests
{
    private static Sheet NewSheet(WorksheetViewMode viewMode = WorksheetViewMode.Normal, int zoomPercent = 100)
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { ViewMode = viewMode, ZoomPercent = zoomPercent };
        return sheet;
    }

    [Fact]
    public void GetOrSeed_FirstCall_SeedsFromSheetsCurrentValues()
    {
        var store = new WorksheetViewStateStore();
        var sheet = NewSheet(WorksheetViewMode.PageLayout, 150);

        var snapshot = store.GetOrSeed(sheet);

        snapshot.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        snapshot.ZoomPercent.Should().Be(150);
    }

    [Fact]
    public void GetOrSeed_AfterSheetMutatesElsewhere_KeepsTheOriginallySeededSnapshot()
    {
        // This is the crux of the "New Window" independence bug: another window mutating the
        // SAME shared Sheet object must never leak into a window that already rendered it.
        var store = new WorksheetViewStateStore();
        var sheet = NewSheet(WorksheetViewMode.Normal, 100);

        var firstRead = store.GetOrSeed(sheet);

        // Simulate a sibling window changing the shared Sheet's view fields directly (exactly
        // what SetWorksheetViewModeCommand/SetWorksheetZoomCommand do).
        sheet.ViewMode = WorksheetViewMode.PageLayout;
        sheet.ZoomPercent = 200;

        var secondRead = store.GetOrSeed(sheet);

        firstRead.ViewMode.Should().Be(WorksheetViewMode.Normal);
        firstRead.ZoomPercent.Should().Be(100);
        secondRead.Should().Be(firstRead, "a window that already saw this sheet must not silently pick up a sibling's change");
    }

    [Fact]
    public void Set_RecordsThisWindowsOwnChange_AndGetOrSeedReturnsIt()
    {
        var store = new WorksheetViewStateStore();
        var sheet = NewSheet();
        store.GetOrSeed(sheet); // seed at defaults first, as UpdateViewport would

        store.Set(sheet.Id, new WorksheetViewStateSnapshot(WorksheetViewMode.PageBreakPreview, 175));

        var result = store.GetOrSeed(sheet);
        result.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
        result.ZoomPercent.Should().Be(175);
    }

    [Fact]
    public void Remove_DropsSheetsSnapshot_SoNextGetOrSeedReseedsFromSheet()
    {
        var store = new WorksheetViewStateStore();
        var sheet = NewSheet(WorksheetViewMode.Normal, 100);
        store.Set(sheet.Id, new WorksheetViewStateSnapshot(WorksheetViewMode.PageLayout, 200));

        store.Remove(sheet.Id);
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;
        sheet.ZoomPercent = 80;

        var result = store.GetOrSeed(sheet);
        result.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
        result.ZoomPercent.Should().Be(80);
    }

    [Fact]
    public void Clear_ForgetsEverySheet()
    {
        var store = new WorksheetViewStateStore();
        var s1 = NewSheet(WorksheetViewMode.PageLayout, 150);
        var s2 = NewSheet(WorksheetViewMode.PageBreakPreview, 75);
        store.GetOrSeed(s1);
        store.GetOrSeed(s2);

        store.Clear();
        s1.ViewMode = WorksheetViewMode.Normal;
        s1.ZoomPercent = 100;

        // After Clear, the store has forgotten s1's prior snapshot and re-seeds from the sheet.
        store.GetOrSeed(s1).Should().Be(new WorksheetViewStateSnapshot(WorksheetViewMode.Normal, 100));
    }
}
