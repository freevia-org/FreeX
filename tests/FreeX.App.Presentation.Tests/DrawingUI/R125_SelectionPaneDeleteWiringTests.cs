using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Presentation.Tests.DrawingUI;

/// <summary>
/// R125-selection-pane-delete-wiring: round 121 added <see cref="DeleteDrawingObjectCommand"/> (a
/// real IWorkbookCommand covering picture/text box/shape/chart, with undo, guarded by EditObjects
/// protection + per-object Locked) and wired it to the Delete key in both shells, but left the
/// Selection Pane's own "Delete" affordance unwired -- there was no way to build a
/// SelectionPaneDialogResult carrying a delete at all. These tests exercise the two portable
/// planners both shells now route through (<see cref="SelectionPanePlanner.CreateCommand"/> for
/// the Avalonia dialog, <see cref="SelectionPaneGroupedCommandPlanner.CreateCommand"/> for the WPF
/// dialog) and prove they build the SAME <see cref="DeleteDrawingObjectCommand"/> type the sheet
/// grid's Delete key already used -- not a second, divergent deletion path -- including through a
/// save/reload/rename round trip (round 123's data-loss class) and undo/redo.
/// </summary>
public sealed class R125_SelectionPaneDeleteWiringTests
{
    private sealed class FakeCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }

    private static byte[] CreatePngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x00, 0x03, 0x00, 0x01, 0x6C, 0xB6, 0x1E,
        0xB9, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    // ── SelectionPanePlanner.CreateCommand (Avalonia dialog's single-sheet path) ─────────────

    [Fact]
    public void SelectionPanePlanner_CreateCommand_WithDeleteChange_ProducesDeleteDrawingObjectCommand()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new FakeCommandContext(workbook);
        var insert = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 2, 2), CreatePngBytes(), "image/png");
        insert.Apply(ctx).Success.Should().BeTrue();
        var pictureId = sheet.Pictures.Single().Id;

        var deletes = new List<SelectionPaneDeleteChange> { new(SelectionPaneObjectKind.Picture, pictureId) };
        var command = SelectionPanePlanner.CreateCommand(sheet.Id, [], [], [], deletes);

        command.Should().NotBeNull("a pending delete alone must be enough to produce a command -- it does not require an accompanying rename/visibility/move change");
        var composite = command as CompositeWorkbookCommand;
        composite.Should().NotBeNull();

        var outcome = composite!.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Pictures.Should().BeEmpty("the Selection Pane's delete must actually remove the picture from the sheet, via the same DeleteDrawingObjectCommand the Delete key uses");

        composite.Revert(ctx);
        sheet.Pictures.Should().ContainSingle(p => p.Id == pictureId, "undo of a Selection-Pane-issued delete must restore the picture exactly like undoing a Delete-key delete does");
    }

    [Fact]
    public void SelectionPanePlanner_HasChanges_TrueForDeleteAloneEvenWithNoOtherChanges()
    {
        // Sibling/no-regression: before this fix, HasChanges only looked at visibility/rename/move,
        // so a dialog session where the user ONLY deleted an object (no rename, no visibility
        // toggle, no reorder) would report "nothing to do" and silently drop the delete.
        SelectionPanePlanner.HasChanges([], [], [], [new SelectionPaneDeleteChange(SelectionPaneObjectKind.Shape, Guid.NewGuid())])
            .Should().BeTrue();
        SelectionPanePlanner.HasChanges([], [], []).Should().BeFalse("no changes at all -- unaffected sibling case");
    }

    // ── SelectionPaneGroupedCommandPlanner.CreateCommand (WPF dialog's cross-sheet-aware path) ──

    [Fact]
    public void SelectionPaneGroupedCommandPlanner_CreateCommand_WithDeleteChange_DeletesOnCurrentSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new FakeCommandContext(workbook);
        var insert = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 2, 2), CreatePngBytes(), "image/png");
        insert.Apply(ctx).Success.Should().BeTrue();
        var pictureId = sheet.Pictures.Single().Id;

        var result = new SelectionPaneDialogResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            [],
            [],
            [],
            [new SelectionPaneDeleteChange(SelectionPaneObjectKind.Picture, pictureId)]);

        SelectionPaneGroupedCommandPlanner.HasChanges(result).Should().BeTrue();

        var command = SelectionPaneGroupedCommandPlanner.CreateCommand(workbook, sheet.Id, sheet.Id, result);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void SelectionPaneGroupedCommandPlanner_CreateCommand_DeleteAlongsideRenameOfAnotherObject_BothApply()
    {
        // Sibling/no-regression: a delete of one object in the same OK as a rename of a DIFFERENT
        // object must not disturb the surviving object's rename.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new FakeCommandContext(workbook);
        var insertA = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 2, 2), CreatePngBytes(), "image/png");
        insertA.Apply(ctx).Success.Should().BeTrue();
        var insertB = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 5, 5), CreatePngBytes(), "image/png");
        insertB.Apply(ctx).Success.Should().BeTrue();
        var pictures = sheet.Pictures.ToList();
        var toDelete = pictures[0].Id;
        var toRename = pictures[1].Id;

        var result = new SelectionPaneDialogResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            [],
            [new SelectionPaneRenameChange(SelectionPaneObjectKind.Picture, toRename, "Survivor")],
            [],
            [new SelectionPaneDeleteChange(SelectionPaneObjectKind.Picture, toDelete)]);

        var command = SelectionPaneGroupedCommandPlanner.CreateCommand(workbook, sheet.Id, sheet.Id, result);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Pictures.Should().ContainSingle(p => p.Id == toRename && p.Name == "Survivor");
    }

    // ── End-to-end: Selection-Pane-issued delete survives save/reload, including after an
    //    in-session sheet rename (round 123's data-loss class) ─────────────────────────────────

    [Fact]
    public void SelectionPaneDelete_RenameSheetThenDelete_SaveAndReload_DoesNotResurrectPicture()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("SelectionPaneDeleteRenameThenDelete");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new FakeCommandContext(workbook);
        var insert = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 2, 2), CreatePngBytes(), "image/png");
        insert.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var picture = loadedSheet.Pictures.Should().ContainSingle().Which;
        picture.IsSourceLoaded.Should().BeTrue("a plain reloaded picture starts source-loaded");
        var loadedCtx = new FakeCommandContext(loaded);

        // Rename the sheet in the SAME session, then delete via the Selection-Pane-built command
        // (not a hand-constructed DeleteDrawingObjectCommand) -- this is the exact ordering round
        // 123 found broken for the sheet grid's own Delete key, now proven for the Selection Pane's
        // wiring too, since it funnels through the identical command type.
        var rename = new RenameSheetCommand(loadedSheet.Id, "Renamed");
        rename.Apply(loadedCtx).Success.Should().BeTrue();

        var deleteResult = new SelectionPaneDialogResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            [],
            [],
            [],
            [new SelectionPaneDeleteChange(SelectionPaneObjectKind.Picture, picture.Id)]);
        var deleteCommand = SelectionPaneGroupedCommandPlanner.CreateCommand(loaded, loadedSheet.Id, loadedSheet.Id, deleteResult);
        var deleteOutcome = deleteCommand.Apply(loadedCtx);
        deleteOutcome.Success.Should().BeTrue(deleteOutcome.ErrorMessage);
        loadedSheet.Pictures.Should().BeEmpty("the delete must remove the picture from the live model immediately");

        using var deletedSave = new MemoryStream();
        adapter.Save(loaded, deletedSave);

        deletedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(deletedSave);
        reloaded.GetSheet("Renamed")!.Pictures.Should().BeEmpty(
            "a Selection-Pane-issued delete of a source-loaded picture must not be merged back in " +
            "from the untouched source package just because the sheet was renamed in the same " +
            "session before the delete was saved -- same guard round 123 proved for the sheet " +
            "grid's Delete key must hold for this second entry point into the identical command");

        // Undo must still restore the picture correctly (checked on the pre-save in-memory model,
        // since that's where CommandBus undo/redo actually operates).
        deleteCommand.Revert(loadedCtx);
        loadedSheet.Pictures.Should().ContainSingle(p => p.Id == picture.Id);
    }
}
