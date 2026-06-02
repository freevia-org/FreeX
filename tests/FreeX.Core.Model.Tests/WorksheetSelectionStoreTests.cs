using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class WorksheetSelectionStoreTests
{
    private static CellAddress A(SheetId s, uint row, uint col) => new(s, row, col);
    private static GridRange R(SheetId s, uint r1, uint c1, uint r2, uint c2) => new(A(s, r1, c1), A(s, r2, c2));

    [Fact]
    public void Save_ThenTryGet_ReturnsStoredSnapshot()
    {
        var store = new WorksheetSelectionStore();
        var sheet = SheetId.New();
        var snap = new WorksheetSelectionSnapshot(A(sheet, 2, 3), A(sheet, 5, 7), R(sheet, 2, 3, 5, 7), null);

        store.Save(sheet, snap);

        store.TryGet(sheet, out var restored).Should().BeTrue();
        restored.Should().Be(snap);
    }

    [Fact]
    public void TryGet_UnknownSheet_ReturnsFalse()
    {
        var store = new WorksheetSelectionStore();
        store.TryGet(SheetId.New(), out _).Should().BeFalse();
    }

    [Fact]
    public void Save_OverwritesPreviousSnapshotForSameSheet()
    {
        var store = new WorksheetSelectionStore();
        var sheet = SheetId.New();
        store.Save(sheet, new WorksheetSelectionSnapshot(A(sheet, 1, 1), A(sheet, 1, 1), R(sheet, 1, 1, 1, 1), null));
        var latest = new WorksheetSelectionSnapshot(A(sheet, 9, 9), A(sheet, 9, 9), R(sheet, 9, 9, 9, 9), null);

        store.Save(sheet, latest);

        store.TryGet(sheet, out var restored).Should().BeTrue();
        restored.Should().Be(latest);
    }

    [Fact]
    public void Remove_DropsSheetSnapshot()
    {
        var store = new WorksheetSelectionStore();
        var sheet = SheetId.New();
        store.Save(sheet, new WorksheetSelectionSnapshot(A(sheet, 1, 1), A(sheet, 1, 1), R(sheet, 1, 1, 1, 1), null));

        store.Remove(sheet);

        store.TryGet(sheet, out _).Should().BeFalse();
    }

    [Fact]
    public void Clear_ForgetsAllSnapshots()
    {
        var store = new WorksheetSelectionStore();
        var s1 = SheetId.New();
        var s2 = SheetId.New();
        store.Save(s1, new WorksheetSelectionSnapshot(A(s1, 1, 1), A(s1, 1, 1), R(s1, 1, 1, 1, 1), null));
        store.Save(s2, new WorksheetSelectionSnapshot(A(s2, 2, 2), A(s2, 2, 2), R(s2, 2, 2, 2, 2), null));

        store.Clear();

        store.TryGet(s1, out _).Should().BeFalse();
        store.TryGet(s2, out _).Should().BeFalse();
    }

    [Fact]
    public void Remap_RewritesAnchorCursorAndRangesOntoTargetSheet_PreservingCoordinates()
    {
        var source = SheetId.New();
        var target = SheetId.New();
        var snap = new WorksheetSelectionSnapshot(
            A(source, 2, 3),
            A(source, 5, 7),
            R(source, 2, 3, 5, 7),
            new[] { R(source, 10, 1, 12, 4) });

        var remapped = snap.Remap(target);

        remapped.Anchor.Should().Be(A(target, 2, 3));
        remapped.Cursor.Should().Be(A(target, 5, 7));
        remapped.PrimaryRange.Should().Be(R(target, 2, 3, 5, 7));
        remapped.AdditionalRanges.Should().NotBeNull();
        remapped.AdditionalRanges![0].Should().Be(R(target, 10, 1, 12, 4));
        // coordinates unchanged, only the sheet differs
        remapped.Anchor.Row.Should().Be(snap.Anchor.Row);
        remapped.Anchor.Col.Should().Be(snap.Anchor.Col);
    }

    [Fact]
    public void Remap_NullAdditionalRanges_StaysNull()
    {
        var source = SheetId.New();
        var target = SheetId.New();
        var snap = new WorksheetSelectionSnapshot(A(source, 1, 1), A(source, 1, 1), R(source, 1, 1, 1, 1), null);

        snap.Remap(target).AdditionalRanges.Should().BeNull();
    }
}
