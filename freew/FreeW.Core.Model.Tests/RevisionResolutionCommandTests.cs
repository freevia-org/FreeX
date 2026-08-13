namespace FreeW.Core.Model.Tests;

public class RevisionResolutionCommandTests
{
    [Fact]
    public void AcceptOne_IsSharedUndoableAndRedoable()
    {
        var inserted = new Run("new")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Alice",
            RevisionDateXml = "2026-08-13T10:00:00Z"
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(inserted);
        var (document, bus) = Create(paragraph);
        var entry = RevisionList.Enumerate(document).Single();

        RevisionResolutionCoordinator.Accept(bus, document, entry).Should().BeTrue();

        inserted.Revision.Should().Be(RevisionKind.None);
        inserted.RevisionAuthor.Should().BeNull();
        bus.CanUndo.Should().BeTrue();
        bus.NextUndoMutationKind.Should().Be(DocumentCommandMutationKind.Mixed);

        bus.Undo().Should().BeTrue();
        paragraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(inserted);
        inserted.Revision.Should().Be(RevisionKind.Inserted);
        inserted.RevisionAuthor.Should().Be("Alice");

        bus.Redo().Should().BeTrue();
        inserted.Revision.Should().Be(RevisionKind.None);
    }

    [Fact]
    public void RejectOne_RemovesInsertionAndUndoRestoresSameRun()
    {
        var inserted = new Run("discard") { Revision = RevisionKind.Inserted };
        var paragraph = new Paragraph("keep");
        paragraph.Runs.Add(inserted);
        var (document, bus) = Create(paragraph);
        var entry = RevisionList.Enumerate(document).Single();

        RevisionResolutionCoordinator.Reject(bus, document, entry).Should().BeTrue();
        paragraph.Runs.Should().NotContain(inserted);

        bus.Undo().Should().BeTrue();
        paragraph.Runs.Should().HaveCount(2);
        paragraph.Runs[1].Should().BeSameAs(inserted);
        inserted.Revision.Should().Be(RevisionKind.Inserted);
    }

    [Fact]
    public void RejectFormatting_UndoRestoresAuthoredAndPreviousFormatting()
    {
        var previous = RunFormatting.Default with { Italic = true };
        var authored = RunFormatting.Default with { Bold = true };
        var formatRevision = new FormatRevision(previous, "Alice", "2026-08-13T10:00:00Z");
        var run = new Run("styled", authored) { FormatRevision = formatRevision };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(run);
        var (document, bus) = Create(paragraph);
        var entry = RevisionList.Enumerate(document).Single();

        RevisionResolutionCoordinator.Reject(bus, document, entry).Should().BeTrue();
        run.Formatting.Should().Be(previous);
        run.FormatRevision.Should().BeNull();

        bus.Undo().Should().BeTrue();
        run.Formatting.Should().Be(authored);
        run.FormatRevision.Should().BeSameAs(formatRevision);
    }

    [Fact]
    public void StaleOrForeignEntry_DoesNotCreateUndoHistory()
    {
        var run = new Run("new") { Revision = RevisionKind.Inserted };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(run);
        var (document, bus) = Create(paragraph);
        var entry = RevisionList.Enumerate(document).Single();

        run.Revision = RevisionKind.None;
        RevisionResolutionCoordinator.Accept(bus, document, entry).Should().BeFalse();
        bus.CanUndo.Should().BeFalse();

        run.Revision = RevisionKind.Inserted;
        var (otherDocument, otherBus) = Create(new Paragraph("other"));
        RevisionResolutionCoordinator.Accept(otherBus, otherDocument, entry).Should().BeFalse();
        otherBus.CanUndo.Should().BeFalse();
        run.Revision.Should().Be(RevisionKind.Inserted);
    }

    [Fact]
    public void AcceptAll_UndoRestoresParagraphMergeAndOriginalInstances()
    {
        var first = new Paragraph("First")
        {
            MarkRevision = RevisionKind.Deleted,
            MarkRevisionAuthor = "Alice",
            MarkRevisionDateXml = "2026-08-13T10:00:00Z"
        };
        var second = new Paragraph("Second");
        var (document, bus) = Create(first, second);
        var firstRun = first.Runs.Single();
        var secondRun = second.Runs.Single();

        RevisionResolutionCoordinator.AcceptAll(bus, document).Should().BeTrue();
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(second);
        second.PlainText.Should().Be("FirstSecond");

        bus.Undo().Should().BeTrue();
        document.Blocks.Should().Equal(first, second);
        first.Runs.Should().ContainSingle().Which.Should().BeSameAs(firstRun);
        second.Runs.Should().ContainSingle().Which.Should().BeSameAs(secondRun);
        first.MarkRevision.Should().Be(RevisionKind.Deleted);
        first.MarkRevisionAuthor.Should().Be("Alice");

        bus.Redo().Should().BeTrue();
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(second);
        second.PlainText.Should().Be("FirstSecond");
    }

    [Fact]
    public void RejectAll_UndoRestoresNestedRemovedRowAndCellParagraph()
    {
        var outer = Table.Create(1, 1);
        var nested = Table.Create(2, 1);
        var insertedRow = nested.Rows[1];
        insertedRow.RowRevision = RevisionKind.Inserted;
        insertedRow.RowRevisionAuthor = "Row Author";
        insertedRow.Cells[0].Paragraphs[0].Runs.Add(new Run("tracked row"));

        var outerCell = outer.Rows[0].Cells[0];
        outerCell.NestedTables.Add(nested);
        var trailing = new Paragraph
        {
            MarkRevision = RevisionKind.Inserted,
            MarkRevisionAuthor = "Paragraph Author"
        };
        outerCell.Paragraphs.Add(trailing);
        var (document, bus) = Create(outer);

        RevisionResolutionCoordinator.RejectAll(bus, document).Should().BeTrue();
        nested.Rows.Should().ContainSingle();
        outerCell.Paragraphs.Should().ContainSingle();

        bus.Undo().Should().BeTrue();
        nested.Rows.Should().HaveCount(2);
        nested.Rows[1].Should().BeSameAs(insertedRow);
        insertedRow.RowRevision.Should().Be(RevisionKind.Inserted);
        insertedRow.RowRevisionAuthor.Should().Be("Row Author");
        outerCell.Paragraphs.Should().HaveCount(2);
        outerCell.Paragraphs[1].Should().BeSameAs(trailing);
        trailing.MarkRevision.Should().Be(RevisionKind.Inserted);

        bus.Redo().Should().BeTrue();
        nested.Rows.Should().ContainSingle();
        outerCell.Paragraphs.Should().ContainSingle();
    }

    [Fact]
    public void RejectAll_UndoRestoresRunAndParagraphFormattingRevisions()
    {
        var previousRunFormatting = RunFormatting.Default with { Italic = true };
        var authoredRunFormatting = RunFormatting.Default with { Bold = true };
        var runRevision = new FormatRevision(previousRunFormatting, "Run Author", "2026-08-13T10:00:00Z");
        var run = new Run("styled", authoredRunFormatting) { FormatRevision = runRevision };

        var previousParagraphFormatting = ParagraphFormatting.Default with { SpaceAfterPt = 6 };
        var authoredParagraphFormatting = ParagraphFormatting.Default with { SpaceAfterPt = 24 };
        var paragraphRevision = new ParagraphFormatRevision(
            previousParagraphFormatting,
            "Paragraph Author",
            "2026-08-13T11:00:00Z");
        var paragraph = new Paragraph
        {
            Formatting = authoredParagraphFormatting,
            ParagraphFormatRevision = paragraphRevision
        };
        paragraph.Runs.Add(run);
        var (document, bus) = Create(paragraph);

        RevisionResolutionCoordinator.RejectAll(bus, document).Should().BeTrue();
        run.Formatting.Should().Be(previousRunFormatting);
        run.FormatRevision.Should().BeNull();
        paragraph.Formatting.Should().Be(previousParagraphFormatting);
        paragraph.ParagraphFormatRevision.Should().BeNull();

        bus.Undo().Should().BeTrue();
        run.Formatting.Should().Be(authoredRunFormatting);
        run.FormatRevision.Should().BeSameAs(runRevision);
        paragraph.Formatting.Should().Be(authoredParagraphFormatting);
        paragraph.ParagraphFormatRevision.Should().BeSameAs(paragraphRevision);

        bus.Redo().Should().BeTrue();
        run.Formatting.Should().Be(previousRunFormatting);
        paragraph.Formatting.Should().Be(previousParagraphFormatting);
    }

    [Fact]
    public void EmptyAllResolution_DoesNotCreateUndoHistory()
    {
        var (document, bus) = Create(new Paragraph("plain"));

        RevisionResolutionCoordinator.AcceptAll(bus, document).Should().BeFalse();
        RevisionResolutionCoordinator.RejectAll(bus, document).Should().BeFalse();
        bus.CanUndo.Should().BeFalse();
    }

    private static (TextDocument Document, DocumentCommandBus Bus) Create(params Block[] blocks)
    {
        var document = new TextDocument();
        document.Blocks.AddRange(blocks);
        return (document, new DocumentCommandBus(new Context(document)));
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
