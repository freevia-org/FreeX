using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round-13 bucket S9 fix verification (Avalonia shell dirty-flag/undo-to-save-point parity).
/// See docs/../scratchpad r13-S9.md for the full finding text.
/// </summary>
public sealed class FreeXR13S9Tests
{
    // R13-autosave-recovery-2: WorkbookSession.MarkSaved() only cleared IsDirty and never recorded
    // an undo-stack save point, so UndoLastEdit()'s unconditional MarkDirty() (via
    // ApplySuccessfulHistoryResult -> ApplySuccessfulEditResult) left IsDirty permanently true even
    // after the user undid every edit made since the last save — a WPF<->Avalonia parity gap (the
    // WPF host's WorkbookDocumentState.TryMarkCleanIfAtSavePoint restores the clean state here).
    [Fact]
    public void UndoLastEdit_ClearsDirtyFlag_WhenUndoReturnsWorkbookToLastSavePoint()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);
        var sheet = session.ActiveSheet;
        var a1 = new CellAddress(sheet.Id, 1, 1);
        session.SelectCell(a1);

        // Establish a save point with one committed edit already on the undo stack.
        session.CommitCellText("1").Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        var savedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fxl");
        session.MarkSaved(savedPath);
        session.IsDirty.Should().BeFalse();

        // A further edit dirties the workbook again...
        session.CommitCellText("2").Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();

        // ...and undoing it back to the exact save point must clear the dirty flag again, matching
        // the WPF host: the on-disk file and the in-memory workbook are byte-identical again.
        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new NumberValue(1));
        session.IsDirty.Should().BeFalse();
    }
}
