namespace FreeW.Core.Model.Tests;

public sealed class CrossReferenceCommandTests
{
    [Fact]
    public void InsertCrossReference_RestoresHostRunsAndTargetBookmarksOnUndo()
    {
        var target = new Paragraph("Heading");
        target.BookmarkNames.AddRange(["chapter", "_Ref2"]);
        var host = new Paragraph("See ");
        var document = new TextDocument();
        document.Blocks.Add(target);
        document.Blocks.Add(host);
        var bus = new DocumentCommandBus(new TestContext(document));
        var field = Run.CrossReferenceFieldRun(
            new CrossReferenceField(CrossRefFieldKind.Ref, "_Ref3", CrossRefInsertAs.Text, true),
            "Heading");

        bus.Execute(new InsertCrossReferenceCommand(1, field, 0, "_Ref3"));
        target.BookmarkNames.Should().Equal("chapter", "_Ref2", "_Ref3");
        host.Runs.Should().Contain(field);

        bus.Undo().Should().BeTrue();
        target.BookmarkNames.Should().Equal("chapter", "_Ref2");
        host.PlainText.Should().Be("See ");

        bus.Redo().Should().BeTrue();
        target.BookmarkNames.Should().Equal("chapter", "_Ref2", "_Ref3");
        host.Runs.Should().Contain(field);
    }

    [Fact]
    public void InsertCrossReference_WrapsOnlyNoteMarkerAndRestoresBoundariesOnUndo()
    {
        var target = new Paragraph();
        target.Runs.Add(new Run("Before"));
        target.Runs.Add(Run.FootnoteReference(3));
        target.Runs.Add(new Run("After"));
        target.BookmarkNames.Add("existing");
        target.BookmarkBoundaries.Add(new BookmarkBoundary(
            "existing", BookmarkBoundaryKind.Start, 0, "existing"));
        target.BookmarkBoundaries.Add(new BookmarkBoundary(
            "existing", BookmarkBoundaryKind.End, 3));
        var host = new Paragraph("See ");
        var document = new TextDocument();
        document.Blocks.Add(target);
        document.Blocks.Add(host);
        var bus = new DocumentCommandBus(new TestContext(document));
        var field = Run.CrossReferenceFieldRun(
            new CrossReferenceField(CrossRefFieldKind.NoteRef, "_Ref1", CrossRefInsertAs.Text, true),
            "1");

        bus.Execute(new InsertCrossReferenceCommand(1, field, 0, "_Ref1", targetRunIndex: 1));

        target.BookmarkNames.Should().Equal("existing", "_Ref1");
        target.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.Start, 1, "_Ref1"));
        target.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.End, 2));

        bus.Undo().Should().BeTrue();
        target.BookmarkNames.Should().Equal("existing");
        target.BookmarkBoundaries.Should().Equal(
            new BookmarkBoundary("existing", BookmarkBoundaryKind.Start, 0, "existing"),
            new BookmarkBoundary("existing", BookmarkBoundaryKind.End, 3));

        bus.Redo().Should().BeTrue();
        target.BookmarkBoundaries.Should().Contain(boundary => boundary.PairKey == "auto:_Ref1");
    }

    [Fact]
    public void InsertCrossReference_WrapsTableCellNoteMarker()
    {
        var marker = new Paragraph();
        marker.Runs.Add(new Run("Cell"));
        marker.Runs.Add(Run.EndnoteReference(2));
        var cell = new TableCell();
        cell.Paragraphs.Add(marker);
        var row = new TableRow();
        row.Cells.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        var host = new Paragraph("See ");
        var document = new TextDocument();
        document.Blocks.Add(table);
        document.Blocks.Add(host);
        var bus = new DocumentCommandBus(new TestContext(document));
        var field = Run.CrossReferenceFieldRun(
            new CrossReferenceField(CrossRefFieldKind.NoteRef, "_Ref1", CrossRefInsertAs.Text, true),
            "1");

        bus.Execute(new InsertCrossReferenceCommand(
            1, field, 0, "_Ref1", targetRunIndex: 1, targetNoteId: 2, targetIsFootnote: false));

        marker.BookmarkNames.Should().Contain("_Ref1");
        marker.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.Start, 1, "_Ref1"));
        marker.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.End, 2));

        bus.Undo().Should().BeTrue();
        marker.BookmarkNames.Should().BeEmpty();
        marker.BookmarkBoundaries.Should().BeEmpty();
    }

    [Fact]
    public void InsertCrossReference_WrapsNestedTableCellNoteMarker()
    {
        // Same as InsertCrossReference_WrapsTableCellNoteMarker but the marker paragraph lives inside a
        // table nested in the outer table's cell — resolving the note target must find it there too.
        var marker = new Paragraph();
        marker.Runs.Add(new Run("Cell"));
        marker.Runs.Add(Run.EndnoteReference(2));
        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(1, 1);
        nestedTable.Rows[0].Cells[0].Paragraphs[0] = marker;
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        var host = new Paragraph("See ");
        var document = new TextDocument();
        document.Blocks.Add(outerTable);
        document.Blocks.Add(host);
        var bus = new DocumentCommandBus(new TestContext(document));
        var field = Run.CrossReferenceFieldRun(
            new CrossReferenceField(CrossRefFieldKind.NoteRef, "_Ref1", CrossRefInsertAs.Text, true),
            "1");

        bus.Execute(new InsertCrossReferenceCommand(
            1, field, 0, "_Ref1", targetRunIndex: 1, targetNoteId: 2, targetIsFootnote: false));

        marker.BookmarkNames.Should().Contain("_Ref1");
        marker.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.Start, 1, "_Ref1"));
        marker.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.End, 2));

        bus.Undo().Should().BeTrue();
        marker.BookmarkNames.Should().BeEmpty();
        marker.BookmarkBoundaries.Should().BeEmpty();
    }

    [Fact]
    public void InsertCrossReference_WrapsCaptionTextWithoutItsSeparatorAndRestoresRuns()
    {
        var target = Captions.BuildCaption(CaptionLabel.Figure, 1, "Sample caption text");
        var originalRuns = target.Runs.ToArray();
        var host = new Paragraph("See ");
        var document = new TextDocument();
        document.Blocks.Add(target);
        document.Blocks.Add(host);
        var bus = new DocumentCommandBus(new TestContext(document));
        var field = Run.CrossReferenceFieldRun(
            new CrossReferenceField(CrossRefFieldKind.Ref, "_Ref1", CrossRefInsertAs.CaptionText, true),
            "Sample caption text");

        bus.Execute(new InsertCrossReferenceCommand(
            1, field, 0, "_Ref1", targetTextStartOffset: 10, targetTextEndOffset: 29));

        target.Runs.Select(run => run.Text).Should().Equal("Figure ", "1", ": ", "Sample caption text");
        target.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.Start, 3, "_Ref1"));
        target.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.End, 4));

        bus.Undo().Should().BeTrue();
        target.Runs.Should().Equal(originalRuns);
        target.BookmarkNames.Should().BeEmpty();
        target.BookmarkBoundaries.Should().BeEmpty();

        bus.Redo().Should().BeTrue();
        target.Runs.Select(run => run.Text).Should().Equal("Figure ", "1", ": ", "Sample caption text");
    }

    private sealed class TestContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
