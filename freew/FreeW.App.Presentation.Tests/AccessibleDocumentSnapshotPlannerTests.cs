using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class AccessibleDocumentSnapshotPlannerTests
{
    [Fact]
    public void Build_reports_global_caret_paragraph_line_and_word_ranges()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Hello world"));
        document.Blocks.Add(new Paragraph("Second line"));

        var snapshot = AccessibleDocumentSnapshotPlanner.Build(
            document,
            AccessibleDocumentLocation.Body(1, 3));

        snapshot.Text.Should().Be("Hello world\nSecond line");
        snapshot.CaretOffset.Should().Be(15);
        snapshot.Paragraph.Should().Be(new AccessibleTextRange(12, 11));
        snapshot.ParagraphNumber.Should().Be(2);
        snapshot.LogicalLine.Should().Be(new AccessibleTextRange(12, 11));
        snapshot.LogicalLineNumber.Should().Be(2);
        snapshot.Word.Should().Be(new AccessibleTextRange(12, 6));
        snapshot.Status.Should().Be("Caret 15 of 23; paragraph 2 of 2; logical line 2 of 2; word: Second");
        snapshot.RangeAt(AccessibleTextUnit.Document, 15).Should().Be(new AccessibleTextRange(0, 23));
        snapshot.RangeAt(AccessibleTextUnit.Character, 15).Should().Be(new AccessibleTextRange(15, 1));
        snapshot.GetText(snapshot.RangeAt(AccessibleTextUnit.Word, 15)!).Should().Be("Second");
        snapshot.AdjacentRange(snapshot.Word!, AccessibleTextUnit.Word, 1)
            .Should().Be(new AccessibleTextRange(19, 4));
    }

    [Fact]
    public void Build_maps_table_cells_to_the_same_flat_text_as_the_model()
    {
        var table = new Table();
        var firstRow = new TableRow();
        firstRow.Cells.Add(new TableCell("North") { GridSpan = 2 });
        firstRow.Cells.Add(new TableCell("120"));
        table.Rows.Add(firstRow);
        var secondRow = new TableRow();
        secondRow.Cells.Add(new TableCell("South"));
        secondRow.Cells.Add(new TableCell("98"));
        table.Rows.Add(secondRow);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(table);

        var snapshot = AccessibleDocumentSnapshotPlanner.Build(
            document,
            AccessibleDocumentLocation.TableCell(0, 1, 1, 0, 1));

        snapshot.Text.Should().Be(document.PlainText).And.Be("North\t120\nSouth\t98");
        snapshot.CaretOffset.Should().Be(17);
        snapshot.Paragraph.Should().Be(new AccessibleTextRange(16, 2));
        snapshot.Word.Should().Be(new AccessibleTextRange(16, 2));

        var mergedColumnSnapshot = AccessibleDocumentSnapshotPlanner.Build(
            document,
            AccessibleDocumentLocation.TableCell(0, 0, 2, 0, 1));
        mergedColumnSnapshot.CaretOffset.Should().Be(7,
            "table locations use grid columns, so a cell after GridSpan=2 begins at column 2");
    }

    [Fact]
    public void Build_normalizes_selection_direction_and_reports_a_bounded_preview()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Alpha beta"));
        document.Blocks.Add(new Paragraph("Gamma"));

        var snapshot = AccessibleDocumentSnapshotPlanner.Build(
            document,
            AccessibleDocumentLocation.Body(0, 2),
            AccessibleDocumentLocation.Body(1, 3));

        snapshot.Selection.Should().Be(new AccessibleTextRange(2, 12));
        snapshot.Status.Should().Contain("selected 12 characters: pha beta\nGam");
    }

    [Fact]
    public void Build_clamps_invalid_locations_and_handles_an_empty_document()
    {
        var document = new TextDocument();

        var snapshot = AccessibleDocumentSnapshotPlanner.Build(
            document,
            AccessibleDocumentLocation.Body(99, 99));

        snapshot.Text.Should().BeEmpty();
        snapshot.CaretOffset.Should().Be(0);
        snapshot.Paragraph.Should().Be(new AccessibleTextRange(0, 0));
        snapshot.LogicalLine.Should().Be(new AccessibleTextRange(0, 0));
        snapshot.Word.Should().BeNull();
    }

    [Fact]
    public void BuildHeaderFooter_reports_story_local_text_position_and_label()
    {
        var story = new HeaderFooter();
        story.Paragraphs.Add(new Paragraph("Chapter title"));
        story.Paragraphs.Add(new Paragraph("Page 4"));

        var snapshot = AccessibleDocumentSnapshotPlanner.BuildHeaderFooter(
            story,
            paragraphIndex: 1,
            offset: 5,
            storyLabel: "Section 2 default footer");

        snapshot.Text.Should().Be("Chapter title\nPage 4");
        snapshot.CaretOffset.Should().Be(19);
        snapshot.ParagraphNumber.Should().Be(2);
        snapshot.Word.Should().Be(new AccessibleTextRange(19, 1));
        snapshot.Status.Should().StartWith("Section 2 default footer; Caret 19 of 20;");
    }
}
