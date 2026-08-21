using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Editing;

/// <summary>
/// Regression coverage for finding shared-undo-across-panes F2: Modify Style must not push an undo
/// entry when the requested values are identical to the style already on the catalog (the "OK without
/// any edit" case StyleDialog.AskModify always returns a non-null result for), while a modify that
/// genuinely changes a field must still push exactly one undoable entry (the sibling case).
/// </summary>
public sealed class ModifyParagraphStyleNoOpUndoTests
{
    [Fact]
    public void ModifyParagraphStyle_WithIdenticalValues_PushesNoUndoEntry()
    {
        var document = TextDocument.CreateEmpty();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var created = session.CreateParagraphStyleAndApply(
            [],
            "Callout",
            "Normal",
            RunFormatting.Default with { Bold = true },
            ParagraphFormatting.Default,
            "Normal");
        created.Should().NotBeNull();

        // The undo group from creation is one entry; commit a clean baseline to undo from.
        session.Commands.CanUndo.Should().BeTrue();
        var undoCountBeforeModify = CountUndoDepth(session);

        // Re-submit the exact same field values StyleDialog.AskModify seeded from the existing style --
        // this is what an OK click with zero edits sends.
        var result = session.ModifyParagraphStyle(
            created!.Id,
            created.Run,
            created.Paragraph,
            created.BasedOnStyleId,
            created.NextStyleId);

        result.Should().NotBeNull();
        result!.Run.Should().Be(created.Run);

        // No new undo entry: the undo depth is unchanged, and the single Undo() still reverts the
        // *creation*, not a no-op "Modify Style" entry sitting on top of it.
        CountUndoDepth(session).Should().Be(undoCountBeforeModify);

        session.Commands.Undo().Should().BeTrue();
        document.Styles.Should().NotContainKey(created.Id);
    }

    [Fact]
    public void ModifyParagraphStyle_WithActualChange_StillPushesOneUndoEntry()
    {
        var document = TextDocument.CreateEmpty();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var created = session.CreateParagraphStyleAndApply(
            [],
            "Callout",
            "Normal",
            RunFormatting.Default with { Bold = true },
            ParagraphFormatting.Default,
            "Normal");
        created.Should().NotBeNull();
        var undoCountBeforeModify = CountUndoDepth(session);

        var changedRun = created!.Run with { Italic = true };
        var result = session.ModifyParagraphStyle(
            created.Id,
            changedRun,
            created.Paragraph,
            created.BasedOnStyleId,
            created.NextStyleId);

        result.Should().NotBeNull();
        result!.Run.Italic.Should().BeTrue();
        document.Styles[created.Id].Run.Italic.Should().BeTrue();

        // Exactly one new undo entry was pushed for the real edit.
        CountUndoDepth(session).Should().Be(undoCountBeforeModify + 1);

        session.Commands.Undo().Should().BeTrue();
        document.Styles[created.Id].Run.Italic.Should().BeFalse();

        // The earlier "New Style" entry is still there underneath.
        session.Commands.Undo().Should().BeTrue();
        document.Styles.Should().NotContainKey(created.Id);
    }

    /// <summary>Counts how many Undo() calls it takes to exhaust the stack, then redoes back to the top.</summary>
    private static int CountUndoDepth(DocumentEditingSession session)
    {
        var depth = 0;
        while (session.Commands.Undo())
            depth++;
        for (var i = 0; i < depth; i++)
            session.Commands.Redo().Should().BeTrue();
        return depth;
    }
}
