using System.Collections;
using System.Reflection;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

/// <summary>
/// F1 (large-document-scaling, round 149): RevisionResolveCommandBase.Apply() used to unconditionally
/// deep-copy the ENTIRE document -- every paragraph, table row/cell (incl. nested tables), header/footer
/// slot, footnote and endnote -- for undo, even for <see cref="ResolveOneRevisionCommand"/>, which is what
/// every single Accept/Reject click in the Reviewing Pane routes through in both shells. But
/// <see cref="RevisionList"/>.Accept/Reject (the only thing single-entry resolution ever calls) only ever
/// mutates the target paragraph's own Runs list -- it never merges paragraphs away or drops table rows the
/// way TrackChanges.AcceptAll/RejectAll can. The fix adds a <c>TargetParagraph</c> hook so single-entry
/// commands snapshot just that one paragraph instead of walking the whole document.
///
/// <see cref="ResolveOneRevisionCommand_Apply_SnapshotsOnlyTheTargetParagraph_NotTheWholeDocument"/> is the
/// scope proof: it inspects the private <c>_paragraphSnapshots</c> list via reflection (not wall-clock
/// timing, which would be flaky) and asserts its count is exactly 1 in a 500-paragraph document, then
/// confirms Apply/Revert still behave correctly. Before the fix this asserted count was 500 (verified via
/// the mandated cp-backup/hand-revert technique).
///
/// <see cref="AcceptAllRevisionsCommand_Apply_StillSnapshotsEveryParagraph_BulkPathUnaffected"/> is the
/// sibling no-regression: Accept All/Reject All DO resolve via TrackChanges and so DO need the full
/// structural snapshot; this proves the new hook left their scope untouched.
///
/// <see cref="ResolveOneRevisionCommand_OnParagraphNestedInsideATableCell_UndoesExactly"/> is the adjacent
/// case: the scoped snapshot bypasses CaptureTable/CaptureParagraphContainer entirely for single-entry
/// commands, so this proves round-tripping still works when the target paragraph lives inside a table
/// cell, not just as a top-level body paragraph.
/// </summary>
public sealed class RevisionCommandsScopedSnapshotTests
{
    [Fact]
    public void ResolveOneRevisionCommand_Apply_SnapshotsOnlyTheTargetParagraph_NotTheWholeDocument()
    {
        const int paragraphCount = 500;
        var document = new TextDocument();
        Paragraph? markedParagraph = null;
        for (var i = 0; i < paragraphCount; i++)
        {
            var paragraph = new Paragraph($"paragraph {i}");
            if (i == paragraphCount / 2)
            {
                paragraph.Runs.Clear();
                paragraph.Runs.Add(new Run("marked") { Revision = RevisionKind.Inserted });
                markedParagraph = paragraph;
            }
            document.Blocks.Add(paragraph);
        }

        var entries = RevisionList.Enumerate(document);
        entries.Should().ContainSingle();
        var target = new RevisionTargetDecision(0, paragraphCount / 2, entries[0]);

        var command = CreateCommand("ResolveOneRevisionCommand", target, RevisionResolutionAction.Accept);
        command.Apply(new Context(document));

        GetParagraphSnapshotCount(command).Should().Be(1,
            "single-entry resolution must snapshot only the one paragraph the revision lives in, " +
            $"not all {paragraphCount} paragraphs in the document");

        markedParagraph!.Runs.Should().OnlyContain(run => run.Revision == RevisionKind.None);

        command.Revert(new Context(document));
        markedParagraph.Runs.Should().ContainSingle(run => run.Revision == RevisionKind.Inserted);
    }

    [Fact]
    public void AcceptAllRevisionsCommand_Apply_StillSnapshotsEveryParagraph_BulkPathUnaffected()
    {
        const int paragraphCount = 40;
        var document = new TextDocument();
        for (var i = 0; i < paragraphCount; i++)
        {
            var paragraph = new Paragraph($"paragraph {i}");
            if (i == 5)
            {
                paragraph.Runs.Clear();
                paragraph.Runs.Add(new Run("marked") { Revision = RevisionKind.Inserted });
            }
            document.Blocks.Add(paragraph);
        }

        var command = CreateCommand("AcceptAllRevisionsCommand");
        command.Apply(new Context(document));

        GetParagraphSnapshotCount(command).Should().Be(paragraphCount,
            "Accept All must still snapshot the whole document -- TrackChanges.AcceptAll can merge/drop " +
            "content anywhere, not just in the paragraph that started with a revision mark");
    }

    [Fact]
    public void ResolveOneRevisionCommand_OnParagraphNestedInsideATableCell_UndoesExactly()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("before"));
        var table = Table.Create(1, 1);
        var cellParagraph = table.Rows[0].Cells[0].Paragraphs[0];
        cellParagraph.Runs.Clear();
        cellParagraph.Runs.Add(new Run("cell text"));
        cellParagraph.Runs.Add(new Run("deleted") { Revision = RevisionKind.Deleted });
        document.Blocks.Add(table);

        var entries = RevisionList.Enumerate(document);
        entries.Should().ContainSingle();
        var target = new RevisionTargetDecision(0, 1, entries[0]);

        var command = CreateCommand("ResolveOneRevisionCommand", target, RevisionResolutionAction.Reject);

        // Rejecting a deletion keeps the text, clearing its revision mark (mirrors Word: "reject deletion"
        // restores the struck-through text to ordinary content rather than removing it).
        command.Apply(new Context(document));
        cellParagraph.PlainText.Should().Be("cell textdeleted");
        cellParagraph.Runs.Should().OnlyContain(run => run.Revision == RevisionKind.None);

        command.Revert(new Context(document));
        cellParagraph.Runs.Select(run => run.Text).Should().Equal("cell text", "deleted");
        cellParagraph.Runs[1].Revision.Should().Be(RevisionKind.Deleted);
        table.Rows.Should().ContainSingle();
    }

    // ResolveOneRevisionCommand/AcceptAllRevisionsCommand are internal to FreeW.App.Presentation, and this
    // test project has no InternalsVisibleTo grant into that assembly, so they're constructed and inspected
    // by reflection here. Apply/Revert are called through the public IDocumentCommand interface once
    // constructed -- casting an internal type to a public interface it implements is legal even without
    // compile-time visibility of the concrete type.
    private static IDocumentCommand CreateCommand(string typeName, params object[] args)
    {
        var assembly = typeof(RevisionTargetDecision).Assembly;
        var type = assembly.GetType($"FreeW.App.Presentation.Editing.{typeName}")
            ?? throw new InvalidOperationException($"{typeName} type not found via reflection.");
        var instance = Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: args,
            culture: null);
        return (IDocumentCommand)(instance
            ?? throw new InvalidOperationException($"Could not construct {typeName}."));
    }

    private static int GetParagraphSnapshotCount(IDocumentCommand command)
    {
        var baseType = command.GetType().BaseType
            ?? throw new InvalidOperationException("Command has no base type.");
        var field = baseType.GetField("_paragraphSnapshots", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_paragraphSnapshots field not found via reflection.");
        var snapshots = field.GetValue(command) as IList;
        snapshots.Should().NotBeNull("Apply() must have populated the paragraph snapshot list");
        return snapshots!.Count;
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
