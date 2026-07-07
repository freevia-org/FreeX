using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Focused regression tests for FreeX cleanup batch MED12 (round-10 MED/LOW findings).
/// </summary>
public sealed class FreeXCleanupMED12Tests
{
    /// <summary>
    /// P84: RenameSheetCommand must rewrite TimelineModel.SourceSheetName the same way it already
    /// rewrites SlicerModel.SourceSheetName, or a timeline anchored on the renamed sheet keeps
    /// pointing at the old (now-nonexistent) sheet name and silently stops rendering.
    /// </summary>
    [Fact]
    public void RenameSheetCommand_RewritesTimelineSourceSheetNameAndUndoRestoresOriginal()
    {
        var workbook = new Workbook("RenameSheetTimelineTest");
        var sheet = workbook.AddSheet("Sheet1");
        var timeline = new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            SourceSheetName = "Sheet1"
        };
        workbook.Timelines.Add(timeline);
        var ctx = new TestCommandContext(workbook);

        var command = new RenameSheetCommand(sheet.Id, "Dashboard");
        command.Apply(ctx).Success.Should().BeTrue();

        timeline.SourceSheetName.Should().Be("Dashboard");

        command.Revert(ctx);

        timeline.SourceSheetName.Should().Be("Sheet1");
    }

    /// <summary>
    /// P84: RemoveSheetCommand's dangling-name cleanup must also clear TimelineModel.SourceSheetName
    /// when the timeline was anchored on the deleted sheet (mirroring the existing Slicer/PivotCache/
    /// Picture cleanup), so it can never silently reattach to an unrelated sheet later re-created
    /// with the same name. Undo must restore the original name.
    /// </summary>
    [Fact]
    public void RemoveSheetCommand_ClearsTimelineSourceSheetNameAndUndoRestoresOriginal()
    {
        var workbook = new Workbook("RemoveSheetTimelineTest");
        var deletedSheet = workbook.AddSheet("ToDelete");
        workbook.AddSheet("Survivor");
        var timeline = new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            SourceSheetName = "ToDelete"
        };
        workbook.Timelines.Add(timeline);
        var ctx = new TestCommandContext(workbook);

        var command = new RemoveSheetCommand(deletedSheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        timeline.SourceSheetName.Should().BeNull();

        command.Revert(ctx);

        timeline.SourceSheetName.Should().Be("ToDelete");
    }

    /// <summary>
    /// P114: PasteCommentsCommand must pre-materialize each source comment's author/shown state
    /// (not read it live mid-mutation) so an overlapping same-sheet paste doesn't attribute a note
    /// to whichever author/shown-state a PRIOR iteration in the same Apply already overwrote onto
    /// that source cell. A1 (Alice, shown/pinned) and A2 (Bob, not shown) copied to A2:A3 must land
    /// Bob's own author/pinned state on A3, not Alice's just-written values.
    /// </summary>
    [Fact]
    public void PasteCommentsCommand_OverlappingSameSheetPaste_PreservesEachSourceCommentsOwnAuthorAndShownState()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);

        sheet.Comments[a1] = "Alice's note";
        sheet.CommentAuthors[a1] = "Alice";
        sheet.ShownComments.Add(a1);

        sheet.Comments[a2] = "Bob's note";
        sheet.CommentAuthors[a2] = "Bob";
        // Bob's note is not shown/pinned.

        // Copy A1:A2, paste at A2 (destination one row below source start) -> A1 lands on A2,
        // A2 lands on A3. Row-major iteration (GridRange.AllCells) writes A2 in iteration 1 before
        // reading A2 as a source in iteration 2, so a live read would observe Alice's just-written
        // values instead of Bob's originals.
        var command = new PasteCommentsCommand(
            sheet.Id,
            new GridRange(a1, a2),
            a2,
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Comments[a3].Should().Be("Bob's note");
        sheet.CommentAuthors[a3].Should().Be("Bob", "A3 must keep Bob's own author, not Alice's");
        sheet.ShownComments.Contains(a3).Should().BeFalse("A3 must keep Bob's own unshown state, not Alice's pinned state");

        sheet.Comments[a2].Should().Be("Alice's note");
        sheet.CommentAuthors[a2].Should().Be("Alice");
        sheet.ShownComments.Contains(a2).Should().BeTrue();
    }
}
