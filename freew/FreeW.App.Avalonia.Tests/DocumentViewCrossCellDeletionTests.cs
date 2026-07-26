using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Strict interaction/model coverage for WPF-style table selection editing. These tests deliberately
/// do not swallow dispatcher failures: a missing headless backend is a test failure, not an excuse to
/// skip the parity assertions.
/// </summary>
public sealed class DocumentViewCrossCellDeletionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task OnUiThread(Action action) => Session.Dispatch(action, CancellationToken.None);

    private static (DocumentView View, int TableBlock, Table Table) MakeTable(int rows, int columns)
    {
        var document = TextDocument.CreateEmpty();
        var table = Table.Create(rows, columns);
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
            table.Rows[row].Cells[column] = new TableCell($"R{row}C{column}");

        document.Blocks.Add(table);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(900, 4000));
        return (view, document.Blocks.IndexOf(table), table);
    }

    [Fact]
    public async Task MultiParagraphSameCell_DeleteForward_joins_paragraphs_and_collapses_to_start()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 1);
            var cell = table.Rows[0].Cells[0];
            cell.Paragraphs.Clear();
            cell.Paragraphs.Add(new Paragraph("AB")
            {
                StyleId = "StartStyle",
                Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center }
            });
            cell.Paragraphs.Add(new Paragraph("CD") { StyleId = "EndStyle" });

            view.PlaceCaretInCell(tableBlock, 0, 0, 1, 1);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 0, 0, 1);
            view.DeleteForwardPublic();

            cell.Paragraphs.Should().ContainSingle();
            cell.PlainText.Should().Be("AD");
            cell.Paragraphs[0].StyleId.Should().Be("StartStyle");
            cell.Paragraphs[0].Formatting.Alignment.Should().Be(TextAlignment.Center);
            view.CellCaretInfo.Should().Be((tableBlock, 0, 0, 0, 1));
        });
    }

    [Fact]
    public async Task MultiParagraphSameCell_ReversedBackspace_has_the_same_result_and_is_undoable()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 1);
            var cell = table.Rows[0].Cells[0];
            cell.Paragraphs.Clear();
            cell.Paragraphs.Add(new Paragraph("AB"));
            cell.Paragraphs.Add(new Paragraph("CD"));

            view.PlaceCaretInCell(tableBlock, 0, 0, 0, 1);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 0, 1, 1);
            view.BackspacePublic();

            cell.PlainText.Should().Be("AD");
            cell.Paragraphs.Should().ContainSingle();
            view.CellCaretInfo.Should().Be((tableBlock, 0, 0, 0, 1));
            view.CanUndo.Should().BeTrue();
            view.Undo();
            cell.Paragraphs.Should().HaveCount(2);
            cell.PlainText.Should().Be("AB\nCD");
            view.CanRedo.Should().BeTrue();
            view.Redo();
            cell.Paragraphs.Should().ContainSingle();
            cell.PlainText.Should().Be("AD");
        });
    }

    [Fact]
    public async Task AdjacentCellSelection_preserves_cell_boundaries_and_clears_wpf_normalized_cells()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 3);
            table.Rows[0].Cells[0] = new TableCell("Axx");
            table.Rows[0].Cells[1] = new TableCell("Byy");
            table.Rows[0].Cells[2] = new TableCell("Czz");

            // Reverse the visual direction: anchor is in cell 1, focus is in cell 0.
            view.PlaceCaretInCell(tableBlock, 0, 0, 0, 1);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 1, 0, 1);
            view.DeleteForwardPublic();

            table.Rows[0].Cells.Should().HaveCount(3);
            table.Rows[0].Cells.Take(2).Select(cell => cell.PlainText).Should()
                .OnlyContain(text => text == string.Empty);
            table.Rows[0].Cells[2].PlainText.Should().Be("Czz");
            view.CellCaretInfo.Should().Be((tableBlock, 0, 0, 0, 0));
        });
    }

    [Fact]
    public async Task RectangularSelection_Delete_preserves_rows_cells_and_merged_spans()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(2, 3);
            table.Rows[0].Cells.Clear();
            table.Rows[0].Cells.Add(new TableCell("Merged") { GridSpan = 2 });
            table.Rows[0].Cells.Add(new TableCell("R0C2"));
            view.SetCellBlockSelection(tableBlock, 0, 0, 1, 2);
            view.DeleteForwardPublic();

            table.Rows.Should().HaveCount(2);
            table.Rows[0].Cells.Should().HaveCount(2);
            table.Rows[0].Cells[0].GridSpan.Should().Be(2);
            table.Rows.SelectMany(row => row.Cells).Should().OnlyContain(cell => cell.Paragraphs.Count == 1);
            table.Rows.SelectMany(row => row.Cells).Should().OnlyContain(cell => cell.PlainText == string.Empty);
            view.CellCaretInfo.Should().NotBeNull();
            view.CellCaretInfo!.Value.Row.Should().Be(0);
            view.CellCaretInfo.Value.Col.Should().Be(0);
        });
    }

    [Fact]
    public async Task Typing_over_rectangular_selection_is_one_undoable_replacement()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(2, 2);
            var original = table.Rows.SelectMany(row => row.Cells).Select(cell => cell.PlainText).ToArray();
            view.SetCellBlockSelection(tableBlock, 0, 0, 1, 1);
            view.InsertText("Z");

            table.Rows[0].Cells[0].PlainText.Should().Be("Z");
            table.Rows.SelectMany(row => row.Cells).Skip(1).Should().OnlyContain(cell => cell.PlainText == string.Empty);
            view.CanUndo.Should().BeTrue();
            view.Undo();
            table.Rows.SelectMany(row => row.Cells).Select(cell => cell.PlainText).Should().Equal(original);
            view.CanRedo.Should().BeTrue();
            view.Redo();
            table.Rows[0].Cells[0].PlainText.Should().Be("Z");
        });
    }

    [Fact]
    public async Task Tracked_cross_cell_delete_keeps_text_as_deleted_revisions_and_undo_restores_marks()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 2);
            table.Rows[0].Cells[0] = new TableCell("ABC");
            table.Rows[0].Cells[1] = new TableCell("DEF");
            view.ToggleTrackChanges().Should().BeTrue();
            view.PlaceCaretInCell(tableBlock, 0, 1, 0, 1);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 0, 0, 1);
            view.DeleteForwardPublic();

            table.Rows[0].Cells[0].PlainText.Should().Be("ABC");
            table.Rows[0].Cells[1].PlainText.Should().Be("DEF");
            table.Rows[0].Cells.SelectMany(cell => cell.Paragraphs.SelectMany(paragraph => paragraph.Runs))
                .Should().Contain(run => run.Revision == RevisionKind.Deleted);
            view.CanUndo.Should().BeTrue();
            view.Undo();
            table.Rows[0].Cells.SelectMany(cell => cell.Paragraphs.SelectMany(paragraph => paragraph.Runs))
                .Should().OnlyContain(run => run.Revision == RevisionKind.None);
        });
    }

    [Fact]
    public async Task Linear_three_cell_selection_clears_every_interior_cell_and_undo_redo_restores_it()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 4);
            table.Rows[0].Cells[0] = new TableCell("Axx");
            table.Rows[0].Cells[1] = new TableCell("Bmiddle");
            table.Rows[0].Cells[2] = new TableCell("Cmiddle");
            table.Rows[0].Cells[3] = new TableCell("Dyy");

            view.PlaceCaretInCell(tableBlock, 0, 3, 0, 1);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 0, 0, 1);
            view.DeleteForwardPublic();

            table.Rows[0].Cells.Should().OnlyContain(cell => cell.PlainText == string.Empty);
            table.Rows[0].Cells.Should().HaveCount(4);

            view.Undo();
            table.Rows[0].Cells.Select(cell => cell.PlainText).Should()
                .Equal("Axx", "Bmiddle", "Cmiddle", "Dyy");
            view.Redo();
            table.Rows[0].Cells.Select(cell => cell.PlainText).Should()
                .Equal(string.Empty, string.Empty, string.Empty, string.Empty);
        });
    }

    [Fact]
    public async Task Typing_over_linear_cross_cell_selection_is_one_undoable_replacement()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 3);
            table.Rows[0].Cells[0] = new TableCell("Axx");
            table.Rows[0].Cells[1] = new TableCell("Bmiddle");
            table.Rows[0].Cells[2] = new TableCell("Cyy");

            view.PlaceCaretInCell(tableBlock, 0, 2, 0, 1);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 0, 0, 1);
            view.InsertText("Z");

            table.Rows[0].Cells.Select(cell => cell.PlainText).Should()
                .Equal("Z", string.Empty, string.Empty);
            view.Undo();
            table.Rows[0].Cells.Select(cell => cell.PlainText).Should()
                .Equal("Axx", "Bmiddle", "Cyy");
            view.Redo();
            table.Rows[0].Cells.Select(cell => cell.PlainText).Should()
                .Equal("Z", string.Empty, string.Empty);
        });
    }

    [Fact]
    public async Task Cross_cell_selection_from_later_paragraph_collapses_to_valid_first_cell_origin()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 2);
            table.Rows[0].Cells[0].Paragraphs.Clear();
            table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("First"));
            table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("Second"));
            table.Rows[0].Cells[1] = new TableCell("Last");

            view.PlaceCaretInCell(tableBlock, 0, 1, 0, 1);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 0, 1, 2);
            view.InsertText("Z");

            table.Rows[0].Cells.Select(cell => cell.PlainText).Should()
                .Equal("Z", string.Empty);
            view.CellCaretInfo.Should().Be((tableBlock, 0, 0, 0, 1));
            view.Undo();
            table.Rows[0].Cells[0].PlainText.Should().Be("First\nSecond");
            table.Rows[0].Cells[1].PlainText.Should().Be("Last");
        });
    }

    [Fact]
    public async Task Typing_over_empty_cross_cell_selection_inserts_in_first_cell_without_empty_delete_undo()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 2);
            table.Rows[0].Cells[0] = new TableCell(string.Empty);
            table.Rows[0].Cells[1] = new TableCell(string.Empty);

            view.PlaceCaretInCell(tableBlock, 0, 1, 0, 0);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 0, 0, 0);
            view.InsertText("Z");

            table.Rows[0].Cells.Select(cell => cell.PlainText).Should()
                .Equal("Z", string.Empty);
            view.CellCaretInfo.Should().Be((tableBlock, 0, 0, 0, 1));
            view.Undo();
            table.Rows[0].Cells.Select(cell => cell.PlainText).Should()
                .Equal(string.Empty, string.Empty);
            view.CanUndo.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Typing_over_empty_rectangular_selection_inserts_in_canonical_first_cell()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(2, 2);
            foreach (var cell in table.Rows.SelectMany(row => row.Cells))
                cell.Paragraphs[0].Runs.Clear();

            view.SetCellBlockSelection(tableBlock, 1, 1, 0, 0);
            view.InsertText("Z");

            table.Rows[0].Cells[0].PlainText.Should().Be("Z");
            table.Rows.SelectMany(row => row.Cells).Skip(1)
                .Should().OnlyContain(cell => cell.PlainText == string.Empty);
            view.CellCaretInfo.Should().Be((tableBlock, 0, 0, 0, 1));
            view.Undo();
            table.Rows.SelectMany(row => row.Cells)
                .Should().OnlyContain(cell => cell.PlainText == string.Empty);
        });
    }

    [Fact]
    public async Task Reversed_rectangular_selection_preserves_merged_structure_through_delete_undo_redo()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(2, 3);
            table.Rows[0].Cells.Clear();
            table.Rows[0].Cells.Add(new TableCell("Merged") { GridSpan = 2 });
            table.Rows[0].Cells.Add(new TableCell("R0C2"));

            view.SetCellBlockSelection(tableBlock, 1, 2, 0, 0);
            view.DeleteForwardPublic();

            table.Rows.Should().HaveCount(2);
            table.Rows[0].Cells.Select(cell => (cell.GridSpan, cell.PlainText)).Should()
                .Equal((2, string.Empty), (1, string.Empty));
            table.Rows[1].Cells.Select(cell => cell.PlainText).Should()
                .Equal(string.Empty, string.Empty, string.Empty);

            view.Undo();
            table.Rows[0].Cells.Select(cell => (cell.GridSpan, cell.PlainText)).Should()
                .Equal((2, "Merged"), (1, "R0C2"));
            table.Rows[1].Cells.Select(cell => cell.PlainText).Should()
                .Equal("R1C0", "R1C1", "R1C2");
            view.Redo();
            table.Rows[0].Cells[0].GridSpan.Should().Be(2);
            table.Rows.SelectMany(row => row.Cells).Should()
                .OnlyContain(cell => cell.PlainText.Length == 0);
        });
    }

    [Fact]
    public async Task Enter_over_rectangular_selection_deletes_block_then_splits_canonical_cell()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 2);
            view.SetCellBlockSelection(tableBlock, 0, 0, 0, 1);
            view.InsertParagraphBreakPublic();

            table.Rows[0].Cells[0].Paragraphs.Should().HaveCount(2);
            table.Rows[0].Cells[0].PlainText.Should().Be("\n");
            table.Rows[0].Cells[1].PlainText.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Deleting_same_cell_text_preserves_unrelated_paragraph_metadata()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 1);
            var cell = table.Rows[0].Cells[0];
            var section = new Section(new PageSettings(), SectionBreakKind.NextPage);
            var first = new Paragraph("AB")
            {
                SectionBreak = section,
                PreservedNumbering = new PreservedNumbering(7, 2),
                ParagraphFormatRevision = new ParagraphFormatRevision(
                    ParagraphFormatting.Default, "Author", "2026-07-26T00:00:00Z")
            };
            first.BookmarkNames.Add("KeepMe");
            cell.Paragraphs.Clear();
            cell.Paragraphs.Add(first);
            cell.Paragraphs.Add(new Paragraph("CD"));

            view.PlaceCaretInCell(tableBlock, 0, 0, 1, 1);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 0, 0, 1);
            view.DeleteForwardPublic();

            var result = cell.Paragraphs.Should().ContainSingle().Subject;
            result.PlainText.Should().Be("AD");
            result.BookmarkNames.Should().Equal("KeepMe");
            result.SectionBreak.Should().BeSameAs(section);
            result.PreservedNumbering.Should().Be(new PreservedNumbering(7, 2));
            result.ParagraphFormatRevision.Should().NotBeNull();
            result.ParagraphFormatRevision!.Author.Should().Be("Author");
        });
    }

    [Fact]
    public async Task Protected_rectangular_selection_is_a_no_op_without_undo_history()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 2);
            view.SetProtection(ProtectionMode.ReadOnly);
            view.SetCellBlockSelection(tableBlock, 0, 0, 0, 1);
            view.DeleteForwardPublic();
            view.InsertText("Z");

            table.Rows[0].Cells.Select(cell => cell.PlainText).Should().Equal("R0C0", "R0C1");
            view.CanUndo.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Tracked_cross_cell_delete_marks_all_paragraphs_touched_by_the_linear_range()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 3);
            table.Rows[0].Cells[0].Paragraphs.Clear();
            table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("A0"));
            table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("A1"));
            table.Rows[0].Cells[1].Paragraphs.Clear();
            table.Rows[0].Cells[1].Paragraphs.Add(new Paragraph("B0"));
            table.Rows[0].Cells[1].Paragraphs.Add(new Paragraph("B1"));
            table.Rows[0].Cells[2].Paragraphs.Clear();
            table.Rows[0].Cells[2].Paragraphs.Add(new Paragraph("C0"));
            table.Rows[0].Cells[2].Paragraphs.Add(new Paragraph("C1"));

            view.ToggleTrackChanges().Should().BeTrue();
            view.PlaceCaretInCell(tableBlock, 0, 2, 1, 1);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 0, 0, 1);
            view.DeleteForwardPublic();

            foreach (var paragraph in table.Rows[0].Cells.SelectMany(cell => cell.Paragraphs))
                paragraph.Runs.Should().Contain(run => run.Revision == RevisionKind.Deleted);
            table.Rows[0].Cells.Select(cell => cell.Paragraphs.Count).Should().Equal(2, 2, 2);

            view.Undo();
            table.Rows[0].Cells.SelectMany(cell => cell.Paragraphs)
                .SelectMany(paragraph => paragraph.Runs)
                .Should().OnlyContain(run => run.Revision == RevisionKind.None);
        });
    }

    [Fact]
    public async Task Single_line_paste_over_linear_table_selection_is_one_undoable_replacement()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 3);
            table.Rows[0].Cells[0] = new TableCell("Axx");
            table.Rows[0].Cells[1] = new TableCell("Bmiddle");
            table.Rows[0].Cells[2] = new TableCell("Cyy");
            view.PlaceCaretInCell(tableBlock, 0, 2, 0, 1);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 0, 0, 1);

            view.PastePlainText("Z").Should().BeTrue();
            table.Rows[0].Cells.Select(cell => cell.PlainText).Should()
                .Equal("Z", string.Empty, string.Empty);
            view.Undo();
            table.Rows[0].Cells.Select(cell => cell.PlainText).Should().Equal("Axx", "Bmiddle", "Cyy");
            view.CanRedo.Should().BeTrue();
        });
    }

    [Fact]
    public async Task Multiline_paste_over_linear_table_selection_is_one_undoable_replacement()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(1, 3);
            table.Rows[0].Cells[0] = new TableCell("Axx");
            table.Rows[0].Cells[1] = new TableCell("Bmiddle");
            table.Rows[0].Cells[2] = new TableCell("Cyy");
            view.PlaceCaretInCell(tableBlock, 0, 2, 0, 1);
            view.SetCellSelectionAnchorForTest(tableBlock, 0, 0, 0, 1);

            view.PastePlainText("Z\nQ").Should().BeTrue();
            table.Rows[0].Cells.Select(cell => cell.PlainText).Should()
                .Equal("Z\nQ", string.Empty, string.Empty);
            view.Undo();
            table.Rows[0].Cells.Select(cell => cell.PlainText).Should().Equal("Axx", "Bmiddle", "Cyy");
        });
    }

    [Fact]
    public async Task Single_line_paste_over_rectangular_table_selection_is_one_undoable_replacement()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(2, 2);
            var original = table.Rows.SelectMany(row => row.Cells).Select(cell => cell.PlainText).ToArray();
            view.SetCellBlockSelection(tableBlock, 0, 0, 1, 1);

            view.PastePlainText("Z").Should().BeTrue();
            table.Rows[0].Cells[0].PlainText.Should().Be("Z");
            table.Rows.SelectMany(row => row.Cells).Skip(1).Should().OnlyContain(cell => cell.PlainText == string.Empty);
            view.Undo();
            table.Rows.SelectMany(row => row.Cells).Select(cell => cell.PlainText).Should().Equal(original);
        });
    }

    [Fact]
    public async Task Multiline_paste_over_rectangular_table_selection_is_one_undoable_replacement()
    {
        await OnUiThread(() =>
        {
            var (view, tableBlock, table) = MakeTable(2, 2);
            var original = table.Rows.SelectMany(row => row.Cells).Select(cell => cell.PlainText).ToArray();
            view.SetCellBlockSelection(tableBlock, 1, 1, 0, 0);

            view.PastePlainText("Z\nQ").Should().BeTrue();
            table.Rows[0].Cells[0].PlainText.Should().Be("Z\nQ");
            table.Rows.SelectMany(row => row.Cells).Skip(1).Should().OnlyContain(cell => cell.PlainText == string.Empty);
            table.Rows.Should().HaveCount(2);
            table.Rows.SelectMany(row => row.Cells).Should().HaveCount(4);
            view.Undo();
            table.Rows.SelectMany(row => row.Cells).Select(cell => cell.PlainText).Should().Equal(original);
        });
    }
}
