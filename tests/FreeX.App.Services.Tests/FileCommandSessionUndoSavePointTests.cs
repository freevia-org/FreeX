using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Covers <see cref="FileCommandSession.MarkSavedAtUndoDepth(int, long)"/> and
/// <see cref="FileCommandSession.TryMarkCleanIfAtSavePoint(int, long)"/>: the shared session's
/// forwarding surface over <c>WorkbookDocumentState</c>'s undo-savepoint clean detection, so hosts
/// built on <see cref="FileCommandSession"/> (FreeW, FreeP) can offer the same "undo back to the
/// saved bytes clears the dirty marker" behavior FreeX already wires through
/// <c>WorkbookSession.RecordUndoSavePoint</c> / <c>TryMarkCleanIfAtSavePoint</c>.
/// </summary>
public sealed class FileCommandSessionUndoSavePointTests
{
    [Fact]
    public void TryMarkCleanIfAtSavePoint_UndoBackToRecordedDepthAndVersion_ClearsDirty()
    {
        var session = new FileCommandSession();

        // Save completes at undo depth 3, version 300.
        session.MarkSavedAtUndoDepth(3, 300);

        // One more edit happens (depth 4), then the user undoes it back to depth 3 -- the
        // in-memory content is now byte-identical to what was saved.
        session.MarkDirty();
        session.IsDirty.Should().BeTrue();

        session.TryMarkCleanIfAtSavePoint(3, 300).Should().BeTrue();
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void TryMarkCleanIfAtSavePoint_WithNotification_InvokesCallbackOnlyOnTransition()
    {
        var session = new FileCommandSession();
        session.MarkSavedAtUndoDepth(1, 100);
        session.MarkDirty();

        var changes = 0;

        // Not yet back at the save point: no notification, still dirty.
        session.TryMarkCleanIfAtSavePoint(2, 200, () => changes++).Should().BeFalse();
        changes.Should().Be(0);
        session.IsDirty.Should().BeTrue();

        // Back at the save point: notifies exactly once and clears dirty.
        session.TryMarkCleanIfAtSavePoint(1, 100, () => changes++).Should().BeTrue();
        changes.Should().Be(1);
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void TryMarkCleanIfAtSavePoint_DepthMatchesButVersionDoesNot_StaysDirty()
    {
        // Guards against the depth-cap aliasing scenario the version token exists to catch: the
        // stack trimmed and refilled to the same depth but with different entries underneath.
        var session = new FileCommandSession();
        session.MarkSavedAtUndoDepth(2, 200);
        session.MarkDirty();

        session.TryMarkCleanIfAtSavePoint(2, 999).Should().BeFalse();
        session.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void TryMarkCleanIfAtSavePoint_NoSavePointRecorded_NeverClearsDirty()
    {
        // Sibling/no-regression case: a session that was never saved at a recorded undo depth
        // (SavedUndoDepth stays -1) must never spuriously report "at save point".
        var session = new FileCommandSession();
        session.MarkDirty();

        session.TryMarkCleanIfAtSavePoint(0, 0).Should().BeFalse();
        session.IsDirty.Should().BeTrue();
    }
}
