using System.IO;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentTableEditingCoordinatorTests
{
    [Fact]
    public void SetCellTextPreservesFirstRunFormattingAndUsesPortableUndo()
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("original")
        {
            Formatting = RunFormatting.Default with { Italic = true, ColorHex = "#4472C4" },
        });
        var document = new TextDocument { Blocks = { table } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var address = session.Tables.AddressFromCellIndex(0, 0, 0)!.Value;

        session.Tables.SetCellText(address, "updated").Applied.Should().BeTrue();

        var run = table.Rows[0].Cells[0].Paragraphs.Single().Runs.Single();
        run.Text.Should().Be("updated");
        run.Formatting.Italic.Should().BeTrue();
        run.Formatting.ColorHex.Should().Be("#4472C4");
        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells[0].PlainText.Should().Be("original");
        session.Commands.Redo().Should().BeTrue();
        table.Rows[0].Cells[0].PlainText.Should().Be("updated");

        session.Tables.SetCellText(
                new DocumentTableCellAddress(0, 9, 9),
                "ignored")
            .Applied.Should().BeFalse();
        table.Rows[0].Cells[0].PlainText.Should().Be("updated");
    }

    [Fact]
    public void AddressesNormalizeCellAndGridCoordinatesAcrossMergedCells()
    {
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells.RemoveAt(1);
        var session = SessionWith(table);

        session.Tables.AddressFromCellIndex(0, 0, 1)
            .Should().Be(new DocumentTableCellAddress(0, 0, 2));
        session.Tables.AddressFromGridColumn(0, 0, 1)
            .Should().Be(new DocumentTableCellAddress(0, 0, 1));
        session.Tables.AddressFromGridColumn(0, 0, 3).Should().BeNull();
    }

    [Fact]
    public void AddressesInRangeNormalizesReversedEndpointsAndDeduplicatesGridSpans()
    {
        var table = Table.Create(2, 3);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells.RemoveAt(1);
        var session = SessionWith(table);
        var expected = new[]
        {
            new DocumentTableCellAddress(0, 0, 0),
            new DocumentTableCellAddress(0, 0, 2),
            new DocumentTableCellAddress(0, 1, 0),
            new DocumentTableCellAddress(0, 1, 1),
            new DocumentTableCellAddress(0, 1, 2),
        };

        session.Tables.AddressesInRange(
                new DocumentTableCellAddress(0, 0, 0),
                new DocumentTableCellAddress(0, 1, 2))
            .Should().Equal(expected);
        session.Tables.AddressesInRange(
                new DocumentTableCellAddress(0, 1, 2),
                new DocumentTableCellAddress(0, 0, 0))
            .Should().Equal(expected);
        session.Tables.AddressesInRange(
                new DocumentTableCellAddress(0, 0, 1),
                new DocumentTableCellAddress(0, 0, 2))
            .Should().Equal(expected.Take(2));
    }

    [Fact]
    public void AddressesInRangeRejectsCrossTableAndInvalidEndpoints()
    {
        var document = new TextDocument
        {
            Blocks =
            {
                Table.Create(2, 2),
                Table.Create(2, 2),
            },
        };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.Tables.AddressesInRange(
                new DocumentTableCellAddress(0, 0, 0),
                new DocumentTableCellAddress(1, 0, 0))
            .Should().BeEmpty();
        session.Tables.AddressesInRange(
                new DocumentTableCellAddress(0, 0, 0),
                new DocumentTableCellAddress(0, 9, 0))
            .Should().BeEmpty();
    }

    [Fact]
    public void BorderEditsInRangeDistinguishesAllOutsideAndInsidePresets()
    {
        var session = SessionWith(Table.Create(2, 2));
        var anchor = new DocumentTableCellAddress(0, 0, 0);
        var active = new DocumentTableCellAddress(0, 1, 1);

        CellBorderEdges.Outside.Should().NotBe(CellBorderEdges.All);
        session.Tables.BorderEditsInRange(anchor, active, CellBorderEdges.All)
            .Should().OnlyContain(edit => edit.Edges == CellBorderEdges.All);
        session.Tables.BorderEditsInRange(anchor, active, CellBorderEdges.Outside)
            .Should().Equal(
                new DocumentTableCellBorderEdit(anchor, CellBorderEdges.Top | CellBorderEdges.Left),
                new DocumentTableCellBorderEdit(anchor with { GridColumn = 1 }, CellBorderEdges.Top | CellBorderEdges.Right),
                new DocumentTableCellBorderEdit(anchor with { RowIndex = 1 }, CellBorderEdges.Bottom | CellBorderEdges.Left),
                new DocumentTableCellBorderEdit(active, CellBorderEdges.Bottom | CellBorderEdges.Right));
        session.Tables.BorderEditsInRange(anchor, active, CellBorderEdges.Inside)
            .Should().Equal(
                new DocumentTableCellBorderEdit(anchor, CellBorderEdges.Bottom | CellBorderEdges.Right),
                new DocumentTableCellBorderEdit(anchor with { GridColumn = 1 }, CellBorderEdges.Bottom),
                new DocumentTableCellBorderEdit(anchor with { RowIndex = 1 }, CellBorderEdges.Right));
    }

    [Fact]
    public void BorderEditsInRangeNormalizesReversedMergedCellEndpoints()
    {
        var table = Table.Create(2, 3);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells.RemoveAt(1);
        var session = SessionWith(table);

        var edits = session.Tables.BorderEditsInRange(
            new DocumentTableCellAddress(0, 1, 2),
            new DocumentTableCellAddress(0, 0, 1),
            CellBorderEdges.Outside);

        edits.Select(edit => edit.Address).Should().Equal(
            new DocumentTableCellAddress(0, 0, 0),
            new DocumentTableCellAddress(0, 0, 2),
            new DocumentTableCellAddress(0, 1, 0),
            new DocumentTableCellAddress(0, 1, 1),
            new DocumentTableCellAddress(0, 1, 2));
        edits[0].Edges.Should().Be(CellBorderEdges.Top | CellBorderEdges.Left);
        edits[1].Edges.Should().Be(CellBorderEdges.Top | CellBorderEdges.Right);
        edits[^1].Edges.Should().Be(CellBorderEdges.Bottom | CellBorderEdges.Right);
    }

    [Fact]
    public void RowAndColumnStructureEditsReportPortableCaretAndUndo()
    {
        var table = Table.Create(2, 2);
        var session = SessionWith(table);
        var address = new DocumentTableCellAddress(0, 0, 0);

        var rowResult = session.Tables.InsertRow(address, after: true);

        rowResult.Applied.Should().BeTrue();
        rowResult.InvalidatesNativeSelection.Should().BeTrue();
        rowResult.Caret.RowIndex.Should().Be(1);
        table.Rows.Should().HaveCount(3);
        session.Commands.Undo().Should().BeTrue();
        table.Rows.Should().HaveCount(2);

        var columnResult = session.Tables.InsertColumn(address, after: true);

        columnResult.Caret.GridColumn.Should().Be(1);
        table.Rows.Should().OnlyContain(row => row.Cells.Count == 3);
        session.Commands.Undo().Should().BeTrue();
        table.Rows.Should().OnlyContain(row => row.Cells.Count == 2);
    }

    [Fact]
    public void MergeSplitAndEraseUseGridCoordinatesWithSingleUndoEntries()
    {
        var table = Table.Create(1, 3);
        var session = SessionWith(table);

        session.Tables.MergeCells(
                new DocumentTableCellAddress(0, 0, 0),
                new DocumentTableCellAddress(0, 0, 2))
            .Applied.Should().BeTrue();
        table.Rows[0].Cells.Should().ContainSingle();
        table.Rows[0].Cells[0].GridSpan.Should().Be(3);

        session.Tables.SplitCell(new DocumentTableCellAddress(0, 0, 0))
            .Applied.Should().BeTrue();
        table.Rows[0].Cells.Should().HaveCount(3);
        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells.Should().ContainSingle();
        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells.Should().HaveCount(3);

        session.Tables.EraseBorderAt(new DocumentTableCellAddress(0, 0, 0))
            .Applied.Should().BeTrue();
        table.Rows[0].Cells.Should().HaveCount(2);
    }

    [Fact]
    public void MergeCellsOnARectangularSelectionMergesEveryRowNotJustTheFirst()
    {
        var table = Table.Create(3, 3);
        var session = SessionWith(table);

        // Select a 2x2 block: rows 0-1, columns 0-1. Row 2 (untouched) and column 2 (untouched)
        // are the control group proving the merge stayed inside the selection.
        var result = session.Tables.MergeCells(
            new DocumentTableCellAddress(0, 0, 0),
            new DocumentTableCellAddress(0, 1, 1));

        result.Applied.Should().BeTrue();

        // Every selected row must have collapsed its first two columns into one cell -- not just
        // row 0. This is the crux of the bug: the old code merged row 0 only and left row 1 as two
        // separate, unmerged cells.
        table.Rows[0].Cells.Should().HaveCount(2, "row 0 must be merged across the selected columns");
        table.Rows[1].Cells.Should().HaveCount(2, "row 1 must be merged across the selected columns too");
        table.Rows[0].Cells[0].GridSpan.Should().Be(2);
        table.Rows[1].Cells[0].GridSpan.Should().Be(2);

        // The two merged row-cells must also be merged vertically into a single block.
        table.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        table.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);

        // Column 2 (outside the selection) is untouched in every selected row.
        table.Rows[0].Cells[1].GridSpan.Should().Be(1);
        table.Rows[1].Cells[1].GridSpan.Should().Be(1);
        table.Rows[0].Cells[1].VerticalMerge.Should().Be(VerticalMergeState.None);
        table.Rows[1].Cells[1].VerticalMerge.Should().Be(VerticalMergeState.None);

        // Row 2 (outside the selection) is completely untouched.
        table.Rows[2].Cells.Should().HaveCount(3);
        table.Rows[2].Cells.Should().OnlyContain(cell => cell.GridSpan == 1);

        // The whole rectangular merge undoes in a single step.
        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells.Should().HaveCount(3);
        table.Rows[1].Cells.Should().HaveCount(3);
        table.Rows.Should().OnlyContain(row => row.Cells.All(cell => cell.GridSpan == 1));
        table.Rows.Should().OnlyContain(
            row => row.Cells.All(cell => cell.VerticalMerge == VerticalMergeState.None));

        session.Commands.Redo().Should().BeTrue();
        table.Rows[0].Cells.Should().HaveCount(2);
        table.Rows[1].Cells.Should().HaveCount(2);
        table.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        table.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);
    }

    [Fact]
    public void MergeCellsOnASingleColumnSelectionStillOnlyMergesVertically()
    {
        // Sibling no-regression test: a selection confined to one column (multiple rows, single
        // grid column) must take the vertical-only merge path and must NOT touch the neighboring
        // column, before or after the rectangular-merge fix above.
        var table = Table.Create(3, 2);
        var session = SessionWith(table);

        var result = session.Tables.MergeCells(
            new DocumentTableCellAddress(0, 0, 0),
            new DocumentTableCellAddress(0, 2, 0));

        result.Applied.Should().BeTrue();
        table.Rows.Should().OnlyContain(row => row.Cells.Count == 2, "no horizontal merge occurred");
        table.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        table.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);
        table.Rows[2].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);
        table.Rows.Should().OnlyContain(
            row => row.Cells[1].VerticalMerge == VerticalMergeState.None,
            "the untouched column must stay unmerged");

        session.Commands.Undo().Should().BeTrue();
        table.Rows.Should().OnlyContain(
            row => row.Cells[0].VerticalMerge == VerticalMergeState.None);
    }

    [Fact]
    public void MultiCellFormattingIsOneUndoableOperation()
    {
        var table = Table.Create(1, 2);
        var session = SessionWith(table);
        var addresses = new[]
        {
            new DocumentTableCellAddress(0, 0, 0),
            new DocumentTableCellAddress(0, 0, 1),
        };

        session.Tables.SetCellShading(addresses, "#ABCDEF").Applied.Should().BeTrue();

        table.Rows[0].Cells.Should().OnlyContain(cell => cell.ShadingColorHex == "#ABCDEF");
        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells.Should().OnlyContain(cell => cell.ShadingColorHex == null);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SelectedRangeShadingUsesCanonicalGridExpansionAndOneUndoStep()
    {
        var table = Table.Create(2, 3);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells.RemoveAt(1);
        var session = SessionWith(table);
        var addresses = session.Tables.AddressesInRange(
            new DocumentTableCellAddress(0, 1, 2),
            new DocumentTableCellAddress(0, 0, 0));

        session.Tables.SetCellShading(addresses, "#123456").Applied.Should().BeTrue();

        addresses.Should().HaveCount(5);
        table.Rows.SelectMany(row => row.Cells)
            .Should().OnlyContain(cell => cell.ShadingColorHex == "#123456");
        session.Commands.Undo().Should().BeTrue();
        table.Rows.SelectMany(row => row.Cells)
            .Should().OnlyContain(cell => cell.ShadingColorHex == null);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SelectedRangeTextDirectionUsesCanonicalGridExpansionAndOneUndoStep()
    {
        var table = Table.Create(2, 3);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells.RemoveAt(1);
        var session = SessionWith(table);
        var addresses = session.Tables.AddressesInRange(
            new DocumentTableCellAddress(0, 1, 2),
            new DocumentTableCellAddress(0, 0, 0));

        session.Tables.SetCellTextDirection(addresses, CellTextDirection.Rotate270)
            .Applied.Should().BeTrue();

        addresses.Should().HaveCount(5);
        table.Rows.SelectMany(row => row.Cells)
            .Should().OnlyContain(cell => cell.TextDirection == CellTextDirection.Rotate270);
        session.Commands.Undo().Should().BeTrue();
        table.Rows.SelectMany(row => row.Cells)
            .Should().OnlyContain(cell => cell.TextDirection == CellTextDirection.Horizontal);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void MergedCellGridAddressesAreDeduplicatedAndBorderEditsAreGrouped()
    {
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells.RemoveAt(1);
        var session = SessionWith(table);

        session.Tables.SetCellShading(
            [
                new DocumentTableCellAddress(0, 0, 0),
                new DocumentTableCellAddress(0, 0, 1),
            ],
            "#ABCDEF");

        session.Commands.Undo().Should().BeTrue();
        session.Commands.CanUndo.Should().BeFalse();

        session.Tables.SetCellBorderEdges(
            [
                new DocumentTableCellBorderEdit(
                    new DocumentTableCellAddress(0, 0, 0),
                    CellBorderEdges.Top),
                new DocumentTableCellBorderEdit(
                    new DocumentTableCellAddress(0, 0, 2),
                    CellBorderEdges.Bottom),
            ],
            BorderLineStyle.Single,
            "#123456",
            1,
            clearEdges: false);

        table.Rows[0].Cells[0].Borders!.Top!.ColorHex.Should().Be("#123456");
        table.Rows[0].Cells[1].Borders!.Bottom!.ColorHex.Should().Be("#123456");
        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells.Should().OnlyContain(cell => cell.Borders == null);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SortAndConvertTableReturnPortablePostEditTargets()
    {
        var table = Table.Create(2, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("b"));
        table.Rows[1].Cells[0].Paragraphs[0].Runs.Add(new Run("a"));
        var session = SessionWith(table);
        var address = new DocumentTableCellAddress(0, 0, 0);

        session.Tables.SortRows(
                address,
                SortKind.Text,
                ascending: true,
                caseSensitive: false,
                hasHeaderRow: false)
            .Applied.Should().BeTrue();
        table = (Table)session.Document.Blocks[0];
        table.Rows.Select(row => row.Cells[0].PlainText).Should().Equal("a", "b");

        var result = session.Tables.ConvertToText(address, ',');

        result.Applied.Should().BeTrue();
        result.InvalidatesNativeSelection.Should().BeTrue();
        session.Document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("a", "b");
    }

    [Fact]
    public void TableStyleAndFormulaConstructionAreCoordinatorOwned()
    {
        var table = Table.Create(1, 1);
        var session = SessionWith(table);
        var address = new DocumentTableCellAddress(0, 0, 0);

        session.Tables.ApplyStyle(address, DocumentTableStyle.Catalog[0]).Applied.Should().BeTrue();
        table.TableStyleId.Should().Be(DocumentTableStyle.Catalog[0].WordStyleId);

        var result = session.Tables.InsertFormula(
            address,
            paragraphIndex: 0,
            textOffset: 0,
            new TableFormulaField("=SUM(ABOVE)"));

        result.Applied.Should().BeTrue();
        result.TextOffset.Should().BeGreaterThan(0);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Should()
            .Contain(run => run.TableFormula != null);
    }

    [Fact]
    public void InsertFormulaRejectsAStaleParagraphWithoutMovingTheCaretOrCreatingUndo()
    {
        var table = Table.Create(1, 1);
        var session = SessionWith(table);
        var address = new DocumentTableCellAddress(0, 0, 0);

        var result = session.Tables.InsertFormula(
            address,
            paragraphIndex: 7,
            textOffset: 4,
            new TableFormulaField("=SUM(ABOVE)"));

        result.Applied.Should().BeFalse();
        result.TextOffset.Should().Be(4);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Should().BeEmpty();
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void InsertNoteRejectsAStaleParagraphWithoutMovingTheCaretOrCreatingUndo()
    {
        var table = Table.Create(1, 1);
        var session = SessionWith(table);
        var address = new DocumentTableCellAddress(0, 0, 0);

        var result = session.Tables.InsertNote(
            address,
            paragraphIndex: -1,
            textOffset: 5,
            text: "note",
            footnote: true);

        result.Applied.Should().BeFalse();
        result.TextOffset.Should().Be(5);
        session.Document.Footnotes.Should().BeEmpty();
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Should().BeEmpty();
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void TableStylePreviewSwitchAndCancelRestoreTheCompleteBaselineWithoutUndo()
    {
        var table = Table.Create(1, 1);
        table.TableStyleId = "TableGrid";
        table.Formatting = new TableFormatting
        {
            Borders = false,
            HeaderRow = true,
            BandedRows = true,
            RepeatHeaderRow = true,
            LastRow = true,
            FirstColumn = true,
            LastColumn = true,
            BandedColumns = true,
        };
        var baseline = table.Formatting;
        var session = SessionWith(table);
        var address = new DocumentTableCellAddress(0, 0, 0);
        var first = DocumentTableStyle.Catalog[0];
        var second = DocumentTableStyle.Catalog[1];

        session.TableStylePreview.Preview(address, first).Should().BeTrue();
        table.TableStyleId.Should().Be(first.WordStyleId);
        session.Commands.CanUndo.Should().BeFalse();

        session.TableStylePreview.Preview(address, second).Should().BeTrue();
        table.TableStyleId.Should().Be(second.WordStyleId);
        table.Formatting.HeaderRow.Should().BeTrue("each preview starts from the captured baseline");

        session.TableStylePreview.Cancel().Should().Be(address);
        table.TableStyleId.Should().Be("TableGrid");
        table.Formatting.Should().Be(baseline);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void TableStylePreviewFreezesFirstTargetAndCommitsExactlyOneUndoEntry()
    {
        var document = new TextDocument();
        var firstTable = Table.Create(1, 1);
        var secondTable = Table.Create(1, 1);
        firstTable.Formatting = firstTable.Formatting with { HeaderRow = true, BandedColumns = true };
        var baseline = firstTable.Formatting;
        document.Blocks.Add(firstTable);
        document.Blocks.Add(secondTable);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var firstAddress = new DocumentTableCellAddress(0, 0, 0);
        var secondAddress = new DocumentTableCellAddress(1, 0, 0);
        var preview = DocumentTableStyle.Catalog[0];
        var committed = DocumentTableStyle.Catalog[1];

        session.TableStylePreview.Preview(firstAddress, preview).Should().BeTrue();
        session.TableStylePreview.Preview(secondAddress, committed).Should().BeTrue();
        session.TableStylePreview.ActiveTarget.Should().Be(firstAddress);

        session.TableStylePreview.Commit(secondAddress, committed).Applied.Should().BeTrue();
        firstTable.TableStyleId.Should().Be(committed.WordStyleId);
        secondTable.TableStyleId.Should().BeNull();
        session.Commands.CanUndo.Should().BeTrue();

        session.Commands.Undo().Should().BeTrue();
        firstTable.TableStyleId.Should().BeNull();
        firstTable.Formatting.Should().Be(baseline);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void LoadingAnotherDocumentCancelsAnActiveTableStylePreview()
    {
        var originalTable = Table.Create(1, 1);
        originalTable.Formatting = originalTable.Formatting with { LastRow = true };
        var baseline = originalTable.Formatting;
        var session = SessionWith(originalTable);
        session.TableStylePreview.Preview(
            new DocumentTableCellAddress(0, 0, 0),
            DocumentTableStyle.Catalog[0]).Should().BeTrue();

        session.LoadDocument(TextDocument.CreateEmpty());

        originalTable.TableStyleId.Should().BeNull();
        originalTable.Formatting.Should().Be(baseline);
        session.TableStylePreview.HasActivePreview.Should().BeFalse();
    }

    /// <summary>
    /// r180. Merge Cells kept only the first cell and removed the rest outright, destroying their
    /// text, per-run formatting, hyperlinks and nested tables -- and for a rectangular selection, in
    /// every touched row. Word stacks the merged cells' content as consecutive paragraphs instead.
    /// The structural command's drop-the-cells contract is deliberately pinned by
    /// DocumentCommandBusTests, so the content carry is a separate command in front of it.
    /// </summary>
    [Fact]
    public void MergeCellsKeepsTheTextOfEveryMergedCellNotJustTheFirst()
    {
        var table = Table.Create(1, 3);
        SetCellText(table, 0, 0, "alpha");
        SetCellText(table, 0, 1, "beta");
        SetCellText(table, 0, 2, "gamma");
        var session = SessionWith(table);

        session.Tables.MergeCells(
                new DocumentTableCellAddress(0, 0, 0),
                new DocumentTableCellAddress(0, 0, 2))
            .Applied.Should().BeTrue();

        var merged = table.Rows[0].Cells.Should().ContainSingle().Subject;
        var text = string.Join("\n", merged.Paragraphs.Select(p => p.PlainText));
        text.Should().Contain("alpha").And.Contain("beta").And.Contain("gamma",
            "merging must stack the cells' content, not delete all but the first");
    }

    [Fact]
    public void UndoingAMergeRemovesTheCarriedContentAgain()
    {
        var table = Table.Create(1, 2);
        SetCellText(table, 0, 0, "alpha");
        SetCellText(table, 0, 1, "beta");
        var session = SessionWith(table);

        session.Tables.MergeCells(
            new DocumentTableCellAddress(0, 0, 0),
            new DocumentTableCellAddress(0, 0, 1));
        session.Commands.Undo().Should().BeTrue();

        table.Rows[0].Cells.Should().HaveCount(2);
        string.Join("\n", table.Rows[0].Cells[0].Paragraphs.Select(p => p.PlainText))
            .Should().Be("alpha", "one undo must restore the split AND drop the carried copy of beta");
        string.Join("\n", table.Rows[0].Cells[1].Paragraphs.Select(p => p.PlainText))
            .Should().Be("beta");
    }

    private static void SetCellText(Table table, int row, int column, string text)
    {
        var cell = table.Rows[row].Cells[column];
        cell.Paragraphs.Clear();
        cell.Paragraphs.Add(new Paragraph(text));
    }

    private static DocumentEditingSession SessionWith(Block block)
    {
        var document = new TextDocument();
        document.Blocks.Add(block);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }
}

public sealed class DocumentEditingSessionWorkflowTests
{
    [Fact]
    public void ParagraphFormattingAndStylesAreGroupedAcrossTargets()
    {
        var document = DocumentWith("one", "two");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.FormatParagraphs(
                [0, 1],
                formatting => formatting with { KeepWithNext = true })
            .Should().BeTrue();
        document.Blocks.Cast<Paragraph>()
            .Should().OnlyContain(paragraph => paragraph.Formatting.KeepWithNext);
        session.Commands.Undo().Should().BeTrue();
        session.Commands.CanUndo.Should().BeFalse();

        session.SetParagraphStyles([0, 1], "Heading1").Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Should().OnlyContain(paragraph => paragraph.StyleId == "Heading1");
        session.Commands.Undo().Should().BeTrue();
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SortParagraphSpanPreservesInterleavedTableSlots()
    {
        var document = DocumentWith("b", "a");
        var table = Table.Create(1, 1);
        document.Blocks.Insert(1, table);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.SortParagraphSpan(
                0,
                2,
                SortKind.Text,
                ascending: true,
                caseSensitive: false,
                hasHeaderRow: false)
            .Should().BeTrue();

        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("a");
        document.Blocks[1].Should().BeSameAs(table);
        ((Paragraph)document.Blocks[2]).PlainText.Should().Be("b");
        session.Commands.Undo().Should().BeTrue();
        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("b");
    }

    [Fact]
    public void ParagraphConversionAndSourcePasteAreSessionOwned()
    {
        var document = DocumentWith("a,b", "c,d");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.ConvertParagraphsToTable([0, 1], ',', showBorders: true).Should().Be(0);
        document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Table>();
        ((Table)document.Blocks[0]).Formatting.Borders.Should().BeTrue();
        session.Commands.Undo().Should().BeTrue();

        var target = DocumentWith(string.Empty);
        var source = DocumentWith("source one", "source two");
        session.LoadDocument(target);
        session.ReplaceEmptyParagraphWithDocument(0, source).Should().BeTrue();
        target.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("source one", "source two");
    }

    [Fact]
    public void StyleCatalogCreationAppliesTargetsAndUndoesAtomically()
    {
        var document = DocumentWith("one", "two");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var created = session.CreateParagraphStyleAndApply(
            [0, 1],
            "Custom",
            "Normal",
            RunFormatting.Default with { Bold = true },
            ParagraphFormatting.Default,
            "Normal");

        created.Should().NotBeNull();
        document.Blocks.Cast<Paragraph>().Should().OnlyContain(paragraph => paragraph.StyleId == created!.Id);
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Should().OnlyContain(paragraph => paragraph.StyleId == null);
    }

    [Fact]
    public void CharacterFormattingHyphenationAndOutlineMovesAreSessionOwned()
    {
        var document = DocumentWith("Heading", "body", "Next");
        ((Paragraph)document.Blocks[0]).StyleId = "Heading1";
        ((Paragraph)document.Blocks[2]).StyleId = "Heading1";
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.FormatParagraphRuns([0, 1], formatting => formatting with { Bold = true });
        document.Blocks.OfType<Paragraph>().Take(2)
            .Should().OnlyContain(paragraph => paragraph.Runs.All(run => run.Formatting.Bold));
        session.Commands.Undo().Should().BeTrue();
        session.Commands.CanUndo.Should().BeFalse();

        var bodyRun = ((Paragraph)document.Blocks[1]).Runs[0];
        session.ApplyManualHyphenation([new ManualHyphenationEdit(bodyRun, 2)]).Should().BeTrue();
        bodyRun.Text.Should().Contain(Hyphenator.SoftHyphen.ToString());
        session.Commands.Undo().Should().BeTrue();

        session.MoveHeadingSubtree(2, moveUp: true).Should().Be(0);
        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("Next");

        session.ApplyDropCap(
            0,
            DropCapPosition.Dropped,
            DropCap.DefaultSizePt,
            DropCap.DefaultLineSpan,
            DropCap.DefaultDistanceFromTextPt).Should().BeTrue();
        ((Paragraph)document.Blocks[0]).DropCap.Should().NotBeNull();
        session.ClearDropCap(0).Should().BeTrue();
        ((Paragraph)document.Blocks[0]).Runs.Should()
            .OnlyContain(run => run.Formatting == RunFormatting.Default);
    }

    private static TextDocument DocumentWith(params string[] paragraphs)
    {
        var document = new TextDocument();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }
}

public sealed class DocumentReferenceEditingCoordinatorTests
{
    [Fact]
    public void NoteNumberingOptionsUseOnePortableUndoableCommand()
    {
        var document = TextDocument.CreateEmpty();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.ApplyNoteNumberingOptions(new FootnoteEndnoteOptionsDialogResult(
            NoteNumberFormat.UpperRoman,
            4,
            NoteNumberRestart.EachPage,
            NoteNumberFormat.LowerLetter,
            9,
            NoteNumberRestart.EachSection));

        document.FootnoteNumbering.StartAt.Should().Be(4);
        document.EndnoteNumbering.StartAt.Should().Be(9);
        session.Commands.Undo().Should().BeTrue();
        document.FootnoteNumbering.StartAt.Should().Be(1);
        document.EndnoteNumbering.StartAt.Should().Be(1);
    }

    [Fact]
    public void FieldCodeToggleUsesOnePortableDocumentWideMajorityDecision()
    {
        var hiddenOne = Run.ComplexFieldRun(" PAGE ", "1");
        var shown = Run.ComplexFieldRun(" AUTHOR ", "Ada", showCode: true);
        var hiddenTwo = Run.ComplexFieldRun(" TITLE ", "Notes");
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { hiddenOne, shown, hiddenTwo } });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var showResult = session.References.ToggleFieldCodes();

        showResult.Should().Be(new DocumentFieldCodeToggleResult(true, true, 3));
        document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => run.ComplexField!.ShowCode);

        var hideResult = session.References.ToggleFieldCodes();

        hideResult.Should().Be(new DocumentFieldCodeToggleResult(true, false, 3));
        document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => !run.ComplexField!.ShowCode);
    }

    /// <summary>
    /// Round 167 correction: sibling of <see cref="FieldCodeToggleUsesOnePortableDocumentWideMajorityDecision"/>
    /// for a document mixing a <see cref="ComplexField"/> run with a <see cref="RunFieldKind"/> simple field
    /// (the form Insert &gt; Header &amp; Footer &gt; Page Number produces). Real Word's Alt+F9 flips BOTH
    /// kinds of field to one shared state, so the document-wide toggle must count and flip the simple field
    /// too -- not leave it behind the way the round-166/167 carryover wrongly recorded as deliberate. The
    /// simple field's kind and lock are untouched by the toggle (only its new
    /// <see cref="Run.FieldCodeVisible"/> presentation flag changes) -- its cached <see cref="Run.Text"/>
    /// result also stays put, mirroring how flipping <see cref="ComplexField.ShowCode"/> never rewrites
    /// <see cref="ComplexField.Instruction"/> or the run's cached result either.
    /// </summary>
    [Fact]
    public void FieldCodeToggleAppliesToBothComplexAndSimpleRunFieldKindFields()
    {
        var complex = Run.ComplexFieldRun(" PAGE ", "1");
        var simplePageNumber = new Run("3") { FieldKind = RunFieldKind.PageNumber, FieldLocked = false };
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { complex, simplePageNumber } });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var result = session.References.ToggleFieldCodes();

        result.Should().Be(new DocumentFieldCodeToggleResult(true, true, 2));
        complex.ComplexField!.ShowCode.Should().BeTrue();
        simplePageNumber.FieldKind.Should().Be(RunFieldKind.PageNumber);
        simplePageNumber.FieldCodeVisible.Should().BeTrue(
            "Alt+F9 must show a simple field's code together with a complex field's, not skip it");
        simplePageNumber.Text.Should().Be("3", "the cached result must survive a display-mode toggle");
        simplePageNumber.FieldLocked.Should().BeFalse();

        var toggledBack = session.References.ToggleFieldCodes();

        toggledBack.Should().Be(new DocumentFieldCodeToggleResult(true, false, 2));
        complex.ComplexField!.ShowCode.Should().BeFalse();
        simplePageNumber.FieldCodeVisible.Should().BeFalse();
        simplePageNumber.Text.Should().Be("3");
    }

    [Fact]
    public void SelectedComplexFieldTransitionsAreCoordinatorOwned()
    {
        var title = Run.ComplexFieldRun(" TITLE ", "stale title");
        var author = Run.ComplexFieldRun(" AUTHOR ", "stale author");
        var document = new TextDocument();
        document.Properties.Title = "Current title";
        document.Properties.Author = "Ada Lovelace";
        document.Blocks.Add(new Paragraph { Runs = { title, author } });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var toggled = session.References.ToggleComplexFieldCodes([title.ComplexField!]);

        toggled.Should().Be(new DocumentComplexFieldEditResult(true, 1, 1));
        title.ComplexField!.ShowCode.Should().BeTrue();
        author.ComplexField!.ShowCode.Should().BeFalse();

        session.References.SetComplexFieldsLocked([title.ComplexField!], true).Applied
            .Should().BeTrue();
        title.ComplexField!.IsLocked.Should().BeTrue();
        session.References.SetComplexFieldsLocked([title.ComplexField!], false);

        var updated = session.References.UpdateComplexFields(
            [title.ComplexField!],
            evaluatedAt: new DateTime(2026, 8, 10));

        updated.Should().Be(new DocumentComplexFieldEditResult(true, 1, 1));
        title.Text.Should().Be("Current title");
        author.Text.Should().Be("stale author");

        var unlinked = session.References.UnlinkComplexFields(
            [new DocumentComplexFieldTarget(title.ComplexField!, "visible title")]);

        unlinked.Should().Be(new DocumentComplexFieldEditResult(true, 1, 1));
        title.Text.Should().Be("visible title");
        title.ComplexField.Should().BeNull();
        author.ComplexField.Should().NotBeNull();
    }

    [Fact]
    public void ComplexFieldInsertionBuildsCanonicalPortableRuns()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Target") { BookmarkName = "destination" });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var liveResolutionCount = 0;

        var formula = session.References.BuildComplexFieldInsertionRun(
            "=2*(3+4)",
            cachedResult: null,
            _ =>
            {
                liveResolutionCount++;
                return "live";
            });
        formula.ComplexField!.Instruction.Should().Be(" =2*(3+4) ");
        formula.Text.Should().Be("14");

        var reference = session.References.BuildComplexFieldInsertionRun(
            new ComplexField(" REF destination "),
            cachedResult: null,
            _ =>
            {
                liveResolutionCount++;
                return "live";
            });
        reference.Text.Should().Be("Target");

        var cached = session.References.BuildComplexFieldInsertionRun(
            new ComplexField(" AUTHOR "),
            "Cached author",
            _ =>
            {
                liveResolutionCount++;
                return "live";
            });
        cached.Text.Should().Be("Cached author");

        var live = session.References.BuildComplexFieldInsertionRun(
            new ComplexField(" AUTHOR "),
            cachedResult: null,
            _ =>
            {
                liveResolutionCount++;
                return "Live author";
            });
        live.Text.Should().Be("Live author");
        liveResolutionCount.Should().Be(1);
    }

    // F3: a freshly inserted position-sensitive field (STYLEREF) must resolve against the caller-supplied
    // insertion block index -- the caret's real position -- not always against block 0 (the document top).
    // With three "Heading1" paragraphs, inserting STYLEREF after the third heading must show "Chapter
    // Three" (the nearest preceding match), not "Chapter One" (the first match from the top).
    [Fact]
    public void ComplexFieldInsertionResolvesStyleRefAgainstTheSuppliedInsertionBlockIndexNotDocumentTop()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("Some body text"));
        document.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("More body text"));
        document.Blocks.Add(new Paragraph("Chapter Three") { StyleId = "Heading1" });
        // Caret sits in a new, still-empty paragraph placed right after "Chapter Three".
        var insertionBlockIndex = document.Blocks.Count;
        document.Blocks.Add(new Paragraph());
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var field = session.References.BuildComplexFieldInsertionRun(
            new ComplexField(" STYLEREF 1 "),
            cachedResult: null,
            _ => "live",
            evaluationDocument: null,
            insertionBlockIndex: insertionBlockIndex);

        field.Text.Should().Be("Chapter Three");
    }

    // Sibling/no-regression: inserting the same STYLEREF field at the very top of the document (before any
    // heading exists yet) must still fall forward to the first following heading, exactly as it did before
    // this change -- the insertion-block-index plumbing must not disturb the existing forward-fallback path.
    [Fact]
    public void ComplexFieldInsertionStyleRefStillFallsForwardWhenNoHeadingPrecedesInsertionPoint()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph()); // caret paragraph, to be inserted into at index 0
        document.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var field = session.References.BuildComplexFieldInsertionRun(
            new ComplexField(" STYLEREF 1 "),
            cachedResult: null,
            _ => "live",
            evaluationDocument: null,
            insertionBlockIndex: 0);

        field.Text.Should().Be("Chapter One");
    }

    [Fact]
    public void FieldUpdateOwnsLiveReferenceLockAndPageResultPolicy()
    {
        var evaluatedAt = new DateTime(2026, 8, 6, 14, 5, 0, DateTimeKind.Local);
        var simpleDate = new Run("old date") { FieldKind = RunFieldKind.Date };
        var complexTime = Run.ComplexFieldRun(" TIME ", "old time");
        var author = new Run("old author") { FieldKind = RunFieldKind.Author };
        var fileName = Run.ComplexFieldRun(" FILENAME ", "old.docx");
        var page = new Run("9") { FieldKind = RunFieldKind.PageNumber };
        var pageCount = Run.ComplexFieldRun(" NUMPAGES ", "9");
        var pageReference = Run.CrossReferenceFieldRun(
            new CrossReferenceField(
                CrossRefFieldKind.PageRef,
                "target",
                CrossRefInsertAs.PageNumber,
                Hyperlink: false),
            "9");
        var importedPageReference = Run.ComplexFieldRun(" PAGEREF target ", "9");
        var styleReference = Run.ComplexFieldRun(" STYLEREF 1 ", "Old heading");
        var lockedStyleReference = new Run("Locked heading")
        {
            ComplexField = new ComplexField(
                " STYLEREF 1 ",
                SimpleField: new SimpleFieldMetadata(IsLocked: true, IsDirty: true))
        };
        // C3 remediation: a locked RunFieldKind run -- the simple-field form the ribbon's Insert >
        // Header & Footer > Page Number / Quick Parts > Document Property routes actually produce,
        // distinct from the ComplexField form lockedStyleReference above -- must be honored the same way.
        var lockedAuthor = new Run("Locked author") { FieldKind = RunFieldKind.Author, FieldLocked = true };
        var document = new TextDocument();
        document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        document.Page.PageNumberStartAt = 4;
        document.Properties.Author = "Ada Lovelace";
        document.Blocks.Add(new Paragraph("Current heading")
        {
            StyleId = "Heading1",
            BookmarkName = "target"
        });
        document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                simpleDate,
                complexTime,
                author,
                fileName,
                page,
                pageCount,
                pageReference,
                importedPageReference,
                styleReference,
                lockedStyleReference,
                lockedAuthor
            }
        });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var layoutCalls = 0;

        var result = session.References.UpdateFields(
            blockPageResolutionFactory: () =>
            {
                layoutCalls++;
                return new DocumentReferenceBlockPageResolution(_ => 2, PageCount: 7);
            },
            fileName: "current.docx",
            evaluatedAt: evaluatedAt);

        result.UpdatedFieldCount.Should().Be(9);
        result.RefreshedGeneratedRegionCount.Should().Be(0);
        layoutCalls.Should().Be(1);
        simpleDate.Text.Should().Be("8/6/2026");
        complexTime.Text.Should().Be("2:05 PM");
        author.Text.Should().Be("Ada Lovelace");
        fileName.Text.Should().Be("current.docx");
        page.Text.Should().Be("V");
        pageCount.Text.Should().Be("7");
        pageReference.Text.Should().Be("V");
        importedPageReference.Text.Should().Be("V");
        styleReference.Text.Should().Be("Current heading");
        lockedStyleReference.Text.Should().Be("Locked heading");
        lockedStyleReference.ComplexField!.SimpleField.Should()
            .Be(new SimpleFieldMetadata(IsLocked: true, IsDirty: true));
        lockedAuthor.Text.Should().Be("Locked author");
        lockedAuthor.FieldLocked.Should().BeTrue();
    }

    [Fact]
    public void FieldUpdateCoversTableRunsAndUsesSourceRunPositionForNoteReferences()
    {
        var noteReference = Run.CrossReferenceFieldRun(
            new CrossReferenceField(
                CrossRefFieldKind.NoteRef,
                "_Ref1",
                CrossRefInsertAs.AboveBelow,
                Hyperlink: true),
            "stale");
        var noteMarker = Run.FootnoteReference(1);
        var sequence = Run.ComplexFieldRun(" SEQ Figure \\h ", "stale");
        var cellParagraph = new Paragraph { Runs = { noteReference, noteMarker, sequence } };
        cellParagraph.BookmarkNames.Add("_Ref1");
        cellParagraph.BookmarkBoundaries.Add(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.Start, 1, "_Ref1"));
        cellParagraph.BookmarkBoundaries.Add(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.End, 2));
        var cell = new TableCell();
        cell.Paragraphs.Add(cellParagraph);
        var row = new TableRow();
        row.Cells.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        var document = new TextDocument();
        document.Blocks.Add(table);
        document.Footnotes[1] = new Footnote(1, "note");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var result = session.References.UpdateFields();

        result.UpdatedFieldCount.Should().Be(2);
        noteReference.Text.Should().Be("1 below");
        sequence.Text.Should().BeEmpty();
    }

    [Fact]
    public void FieldUpdateCanEvaluateSubEditorFieldsAgainstOwningDocument()
    {
        var owner = TextDocument.CreateEmpty();
        owner.Properties.Author = "Owning author";
        var subEditor = TextDocument.CreateEmpty();
        var author = Run.AuthorField("stale");
        ((Paragraph)subEditor.Blocks[0]).Runs.Add(author);
        var session = new DocumentEditingSession();
        session.LoadDocument(subEditor);

        var result = session.References.UpdateFields(evaluationDocument: owner);

        result.UpdatedFieldCount.Should().Be(1);
        author.Text.Should().Be("Owning author");
    }

    [Fact]
    public void CrossReferenceInsertionPreservesPlannedCaptionBookmarkScope()
    {
        var caption = Captions.BuildCaption(CaptionLabel.Figure, 1, "Sample caption text");
        var host = new Paragraph("See ");
        var document = new TextDocument();
        document.Blocks.Add(caption);
        document.Blocks.Add(host);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var target = CrossReferences.Targets(document, CrossRefType.Figure).Single();

        session.References.InsertCrossReference(
            sourceBlockIndex: 1,
            preferredHostBlockIndex: 1,
            CrossRefType.Figure,
            target,
            CrossRefInsertAs.CaptionText,
            hyperlink: true);

        caption.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.Start, 3, "_Ref1"));
        caption.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.End, 4));
        host.Runs.Single(run => run.CrossReference is not null).Text
            .Should().Be("Sample caption text");
    }

    [Fact]
    public void FieldUpdateRefreshesEveryGeneratedReferenceRegionInOnePass()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph(TableOfContents.HeadingText)
        {
            StyleId = TableOfContents.HeadingStyleId
        });
        document.Blocks.Add(new Paragraph("Old heading\t9")
        {
            StyleId = TableOfContents.EntryStyleId(1)
        });
        document.Blocks.Add(new Paragraph("Fresh heading") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph(Citations.HeadingText)
        {
            StyleId = Citations.HeadingStyleId
        });
        document.Blocks.Add(new Paragraph("Old bibliography")
        {
            StyleId = Citations.EntryStyleId
        });
        document.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 1, "Fresh diagram"));
        document.Blocks.Add(new Paragraph("Table of Figures")
        {
            StyleId = TableOfFigures.HeadingStyleId
        });
        document.Blocks.Add(new Paragraph("Old figure\t9")
        {
            StyleId = TableOfFigures.EntryStyleId
        });
        document.Blocks.Add(new Paragraph
        {
            Runs = { Run.CitationMark(new Citation("Fresh Case", CitationCategory.Cases)) }
        });
        document.Blocks.AddRange(TableOfAuthorities.Build(
            new[] { new Citation("Old Case", CitationCategory.Cases) }));
        document.Sources.Add(new Source
        {
            Tag = "Fresh2026",
            Author = "Fresh Author",
            Title = "Current Source",
            Year = "2026"
        });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var blockPageLayoutCalls = 0;
        var authorityLayoutCalls = 0;

        var result = session.References.UpdateFields(
            blockPageResolutionFactory: () =>
            {
                blockPageLayoutCalls++;
                return new DocumentReferenceBlockPageResolution(_ => 1, PageCount: 1);
            },
            authorityPageResolverFactory: () =>
            {
                authorityLayoutCalls++;
                return (_, _, _, _) => TableOfAuthorities.CreatePageReference(3);
            });

        result.RefreshedGeneratedRegionCount.Should().Be(4);
        blockPageLayoutCalls.Should().Be(2, "TOC and figure pages are resolved after prior region edits");
        authorityLayoutCalls.Should().Be(1);
        document.Blocks.Where(TableOfContents.IsTocParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh heading\t1").And.NotContain("Old heading\t9");
        document.Blocks.Where(Citations.IsBibliographyParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain(text => text.Contains("Current Source", StringComparison.Ordinal))
            .And.NotContain("Old bibliography");
        document.Blocks.Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Figure 1: Fresh diagram\t1").And.NotContain("Old figure\t9");
        document.Blocks.Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh Case\t3").And.NotContain(text => text.Contains("Old Case", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedReferencePlannersExpandTableSpanAndFormatTableParagraphPages()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var tableIndex = document.Blocks
            .Select((block, index) => (block, index))
            .Single(item => item.block is Table)
            .index;
        var table = (Table)document.Blocks[tableIndex];

        int? PhysicalPageOfBlock(int blockIndex) => blockIndex == tableIndex ? 3 : 1;
        var figurePages = TableOfFiguresPageTextResolverPlanner.Build(
            document,
            PhysicalPageOfBlock,
            minimumPageCount: 2);
        var authorityPages = TableOfAuthoritiesPageResolverPlanner.Build(
            document,
            PhysicalPageOfBlock,
            observedPhysicalPageOfBlockOffset: null,
            minimumPageCount: 2);
        var lastParagraph = new TableParagraphAddress(
            table.Rows.Count - 1,
            CellIndex: 0,
            ParagraphIndex: 0);

        figurePages.Should().NotBeNull();
        figurePages!(tableIndex, lastParagraph).Should().Be("4");
        authorityPages(document, tableIndex, lastParagraph, 0, new Citation("Case"))
            .Should().Be(new ToaCitationPageReference(4, "4"));
    }

    [Fact]
    public void BibliographyInsertAndRefreshOwnInsertionCaretAndAtomicUndo()
    {
        var lead = new Paragraph("Lead");
        var caretParagraph = new Paragraph("Caret");
        var document = new TextDocument { Blocks = { lead, caretParagraph } };
        document.Sources.Add(new Source
        {
            Tag = "Ada2026",
            Author = "Ada",
            Title = "First source",
            Year = "2026"
        });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var inserted = session.References.InsertBibliography(new DocumentTextPosition(1, 3));

        inserted.Region.InsertIndex.Should().Be(1);
        inserted.Region.DeletedCount.Should().Be(0);
        inserted.Region.InsertedCount.Should().BeGreaterThan(0);
        inserted.Caret.Should().Be(new DocumentTextPosition(1 + inserted.Region.InsertedCount, 3));
        document.Blocks[inserted.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().Equal(lead, caretParagraph);

        inserted = session.References.InsertBibliography(new DocumentTextPosition(1, 3));
        var oldRegionText = document.Blocks
            .Where(Citations.IsBibliographyParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .ToArray();
        document.Sources.Add(new Source
        {
            Tag = "Grace2026",
            Author = "Grace",
            Title = "Second source",
            Year = "2026"
        });

        var refreshed = session.References.RefreshBibliography(inserted.Caret);

        refreshed.Region.InsertIndex.Should().Be(1);
        refreshed.Region.DeletedCount.Should().Be(oldRegionText.Length);
        document.Blocks[refreshed.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        document.Blocks.Where(Citations.IsBibliographyParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain(text => text.Contains("Second source", StringComparison.Ordinal));
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Where(Citations.IsBibliographyParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal(oldRegionText);
    }

    [Fact]
    public void IndexInsertAndRefreshOwnSelectiveRegionLookupAndCaretTransition()
    {
        var marked = new Paragraph
        {
            Runs = { new Run("People"), DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")) }
        };
        var caretParagraph = new Paragraph("Caret");
        var document = new TextDocument { Blocks = { marked, caretParagraph } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var inserted = session.References.InsertIndex(
            new DocumentTextPosition(1, 2),
            "People",
            pageReferenceOf: null);

        inserted.Region.InsertIndex.Should().Be(1);
        inserted.Caret.Should().Be(new DocumentTextPosition(1 + inserted.Region.InsertedCount, 2));
        document.Blocks[inserted.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().Equal(marked, caretParagraph);

        inserted = session.References.InsertIndex(
            new DocumentTextPosition(1, 2),
            "People",
            pageReferenceOf: null);
        marked.Runs.Add(DocumentIndex.MarkRun(new IndexMark("Grace", Identifier: "People")));

        var refreshed = session.References.RefreshIndex(
            inserted.Caret,
            "People",
            pageReferenceOf: null);

        refreshed.Region.InsertIndex.Should().Be(1);
        document.Blocks[refreshed.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        document.Blocks.Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Grace, 1");
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().NotContain("Grace, 1");
    }

    [Fact]
    public void IndexRefreshRemapsCaretsAcrossSparseGeneratedRegion()
    {
        static (DocumentEditingSession Session, TextDocument Document, Paragraph Before, Paragraph After)
            CreateScenario()
        {
            var before = new Paragraph("Before");
            var marked = new Paragraph
            {
                Runs = { DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")) }
            };
            var after = new Paragraph("After");
            var document = new TextDocument
            {
                Blocks =
                {
                    before,
                    new Paragraph(DocumentIndex.HeadingText)
                    {
                        StyleId = DocumentIndex.HeadingStyleIdFor("People")
                    },
                    marked,
                    new Paragraph("Old Person, 9")
                    {
                        StyleId = DocumentIndex.EntryStyleIdFor("People")
                    },
                    after
                }
            };
            var session = new DocumentEditingSession();
            session.LoadDocument(document);
            return (session, document, before, after);
        }

        var beforeScenario = CreateScenario();
        var before = beforeScenario.Session.References.RefreshIndex(
            new DocumentTextPosition(0, 2),
            "People",
            pageReferenceOf: null);
        before.Caret.Should().Be(new DocumentTextPosition(0, 2));
        beforeScenario.Document.Blocks[before.Caret.BlockIndex]
            .Should().BeSameAs(beforeScenario.Before);

        var insideScenario = CreateScenario();
        var inside = insideScenario.Session.References.RefreshIndex(
            new DocumentTextPosition(3, 7),
            "People",
            pageReferenceOf: null);
        inside.Caret.Should().Be(new DocumentTextPosition(1, 0));
        insideScenario.Document.Blocks[inside.Caret.BlockIndex]
            .Should().Match<Block>(block => DocumentIndex.IsIndexParagraph(block, "People"));

        var afterScenario = CreateScenario();
        var after = afterScenario.Session.References.RefreshIndex(
            new DocumentTextPosition(4, 3),
            "People",
            pageReferenceOf: null);
        after.Caret.Should().Be(new DocumentTextPosition(2 + after.Region.InsertedCount, 3));
        afterScenario.Document.Blocks[after.Caret.BlockIndex]
            .Should().BeSameAs(afterScenario.After);
    }

    [Fact]
    public void IndexRefreshWithoutExistingRegionAppendsAndPreservesCaret()
    {
        var marked = new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")) }
        };
        var document = new TextDocument { Blocks = { marked } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var refreshed = session.References.RefreshIndex(
            new DocumentTextPosition(0, 0),
            "People",
            pageReferenceOf: null);

        refreshed.Region.InsertIndex.Should().Be(1);
        refreshed.Caret.Should().Be(new DocumentTextPosition(0, 0));
        document.Blocks[0].Should().BeSameAs(marked);
        document.Blocks.Skip(1)
            .Should().OnlyContain(block => DocumentIndex.IsIndexParagraph(block, "People"));
    }

    [Fact]
    public void TableOfFiguresInsertAndRefreshOwnNormalizationRegionLookupAndCaretTransition()
    {
        var caption = Captions.BuildCaption(CaptionLabel.Figure, 1, "First");
        var caretParagraph = new Paragraph("Caret");
        var document = new TextDocument { Blocks = { caption, caretParagraph } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var inserted = session.References.InsertTableOfFigures(
            new DocumentTextPosition(1, 4),
            " Figure ",
            (_, _) => "2");

        inserted.Region.InsertIndex.Should().Be(1);
        inserted.Caret.Should().Be(new DocumentTextPosition(1 + inserted.Region.InsertedCount, 4));
        document.Blocks[inserted.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().Equal(caption, caretParagraph);

        inserted = session.References.InsertTableOfFigures(
            new DocumentTextPosition(1, 4),
            Captions.FigureLabelText,
            (_, _) => "2");
        caption.Runs[^1].Text = ": Updated";

        var refreshed = session.References.RefreshTableOfFigures(
            inserted.Caret,
            Captions.FigureLabelText,
            (_, _) => "3");

        refreshed.Region.InsertIndex.Should().Be(1);
        document.Blocks[refreshed.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        document.Blocks.Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Figure 1: Updated\t3");
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Figure 1: First\t2");
    }

    // The coordinator must hand the model the block index it is actually inserting at, so the generated
    // entries' page-number tab stop is sized from the front (portrait) section the table lands in rather
    // than from document.Page -- the document's FINAL section, here a wide landscape appendix. Without
    // the insertion index the tab stop would be 648pt, past the front section's own right margin.
    [Fact]
    public void InsertTableOfFiguresSizesTheGeneratedTabStopFromTheInsertionSection_NotTheFinalSection()
    {
        var document = SectionedFrontMatterCaptionDocument();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.InsertTableOfFigures(
            new DocumentTextPosition(0, 0),
            Captions.FigureLabelText,
            pageTextResolver: null).Region.InsertedCount.Should().BeGreaterThan(1);

        FirstGeneratedEntryTabStop(document, TableOfFigures.IsTableOfFiguresParagraph, TableOfFigures.EntryStyleId)
            .Should().Be(new TabStop(468, TabStopAlignment.Right, TabLeader.Dots));
    }

    // No-regression sibling: a table of figures genuinely inserted into the final (landscape) section
    // must still be sized from that section.
    [Fact]
    public void InsertTableOfFiguresIntoTheFinalSectionStillSizesTheGeneratedTabStopFromIt()
    {
        var document = SectionedFrontMatterCaptionDocument();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.InsertTableOfFigures(
            new DocumentTextPosition(document.Blocks.Count, 0),
            Captions.FigureLabelText,
            pageTextResolver: null).Region.InsertedCount.Should().BeGreaterThan(1);

        FirstGeneratedEntryTabStop(document, TableOfFigures.IsTableOfFiguresParagraph, TableOfFigures.EntryStyleId)
            .Should().Be(new TabStop(648, TabStopAlignment.Right, TabLeader.Dots));
    }

    [Fact]
    public void InsertTableOfAuthoritiesSizesTheGeneratedTabStopFromTheInsertionSection_NotTheFinalSection()
    {
        var document = SectionedFrontMatterCitationDocument();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.InsertTableOfAuthorities(
            new DocumentTextPosition(0, 0),
            ToaOptions.Default,
            () => null).Region.InsertedCount.Should().BeGreaterThan(1);

        FirstGeneratedEntryTabStop(
                document,
                TableOfAuthorities.IsTableOfAuthoritiesParagraph,
                TableOfAuthorities.EntryStyleId)
            .Should().Be(new TabStop(468, TabStopAlignment.Right, TabLeader.Dots));
    }

    // No-regression sibling: a table of authorities genuinely inserted into the final (landscape)
    // section must still be sized from that section -- and a later refresh, which re-derives the
    // insertion index from the region's own position, must not resize it.
    [Fact]
    public void InsertAndRefreshTableOfAuthoritiesInTheFinalSectionStillSizeTheTabStopFromIt()
    {
        var document = SectionedFrontMatterCitationDocument();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.InsertTableOfAuthorities(
            new DocumentTextPosition(document.Blocks.Count, 0),
            ToaOptions.Default,
            () => null).Region.InsertedCount.Should().BeGreaterThan(1);

        FirstGeneratedEntryTabStop(
                document,
                TableOfAuthorities.IsTableOfAuthoritiesParagraph,
                TableOfAuthorities.EntryStyleId)
            .Should().Be(new TabStop(648, TabStopAlignment.Right, TabLeader.Dots));

        session.References.RefreshTableOfAuthorities(
            new DocumentTextPosition(0, 0),
            ToaOptions.Default,
            () => null);

        FirstGeneratedEntryTabStop(
                document,
                TableOfAuthorities.IsTableOfAuthoritiesParagraph,
                TableOfAuthorities.EntryStyleId)
            .Should().Be(new TabStop(648, TabStopAlignment.Right, TabLeader.Dots));
    }

    private static TabStop FirstGeneratedEntryTabStop(
        TextDocument document,
        Func<Block, bool> isGeneratedBlock,
        string entryStyleId) =>
        document.Blocks
            .Where(isGeneratedBlock)
            .Cast<Paragraph>()
            .First(paragraph => paragraph.StyleId == entryStyleId)
            .Formatting.TabStops
            .Single();

    // First (front-matter) section: default portrait Letter -> 612 - 72 - 72 = 468pt usable.
    // Final section: landscape Letter -> 792 - 72 - 72 = 648pt usable, wider than the front section.
    private static TextDocument SectionedFrontMatterCaptionDocument() =>
        SectionedFrontMatterDocument(Captions.BuildCaption(CaptionLabel.Figure, 1, "Front matter"));

    private static TextDocument SectionedFrontMatterCitationDocument() =>
        SectionedFrontMatterDocument(
            new Paragraph { Runs = { Run.CitationMark(new Citation("Case A", CitationCategory.Cases)) } });

    private static TextDocument SectionedFrontMatterDocument(Paragraph frontMatter)
    {
        var document = new TextDocument();
        document.Blocks.Add(frontMatter);
        document.Blocks.Add(new Paragraph("End of front matter")
        {
            SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage)
        });
        document.Blocks.Add(new Paragraph("Appendix body"));

        document.Page.WidthPt = 792;
        document.Page.HeightPt = 612;
        return document;
    }

    // r142 tof-refresh-cross-label-deletion: RefreshTableOfFigures for one caption label (e.g. "Table",
    // via References > Update Table on a Table of Tables) must not delete a *different* label's own
    // caption-table region (e.g. a pre-existing Table of Figures). Before the fix,
    // TableOfFigures.IsTableOfFiguresParagraph had no label parameter, so
    // DocumentReferenceEditingCoordinator.RefreshTableOfFigures's `GeneratedRegionIndices` call matched
    // -- and deleted -- every caption-table region in the document regardless of label.
    [Fact]
    public void RefreshTableOfFiguresDoesNotDeleteADifferentlyLabelledCaptionTable()
    {
        var figureCaption = Captions.BuildCaption(CaptionLabel.Figure, 1, "Diagram");
        var tableCaption = Captions.BuildCaption(CaptionLabel.Table, 1, "Budget");
        var document = new TextDocument { Blocks = { figureCaption, tableCaption } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        // Insert a Table of Figures near the top, then a separate Table of Tables further down --
        // mirroring the failure scenario (two distinct caption-table regions in one document).
        var insertedFigures = session.References.InsertTableOfFigures(
            new DocumentTextPosition(0, 0),
            Captions.FigureLabelText,
            (_, _) => "1");
        var insertedTables = session.References.InsertTableOfFigures(
            insertedFigures.Caret,
            Captions.TableLabelText,
            (_, _) => "1");

        document.Blocks.Where(block => TableOfFigures.IsTableOfFiguresParagraph(block, Captions.FigureLabelText))
            .Cast<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Table of Figures", "Figure 1: Diagram\t1");
        document.Blocks.Where(block => TableOfFigures.IsTableOfFiguresParagraph(block, Captions.TableLabelText))
            .Cast<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Table of Tables", "Table 1: Budget\t1");

        // Now edit the Table caption and refresh only the Table of Tables (e.g. ribbon "Update Table"
        // invoked on that region) -- the Table of Figures region must survive untouched.
        tableCaption.Runs[^1].Text = ": Updated";
        session.References.RefreshTableOfFigures(
            insertedTables.Caret,
            Captions.TableLabelText,
            (_, _) => "2");

        document.Blocks.Where(block => TableOfFigures.IsTableOfFiguresParagraph(block, Captions.FigureLabelText))
            .Cast<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Table of Figures", "Figure 1: Diagram\t1");
        document.Blocks.Where(block => TableOfFigures.IsTableOfFiguresParagraph(block, Captions.TableLabelText))
            .Cast<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Table of Tables", "Table 1: Updated\t2");
    }

    // Sibling/neighbouring-behaviour check: refreshing a label that already has its own region must
    // still replace only that region in place (the pre-fix, single-label behaviour must be unchanged).
    [Fact]
    public void RefreshTableOfFiguresStillReplacesItsOwnRegionInPlaceWhenNoOtherLabelExists()
    {
        var figureCaption = Captions.BuildCaption(CaptionLabel.Figure, 1, "Diagram");
        var document = new TextDocument { Blocks = { figureCaption } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var inserted = session.References.InsertTableOfFigures(
            new DocumentTextPosition(0, 0),
            Captions.FigureLabelText,
            (_, _) => "1");
        figureCaption.Runs[^1].Text = ": Updated";

        var refreshed = session.References.RefreshTableOfFigures(
            inserted.Caret,
            Captions.FigureLabelText,
            (_, _) => "2");

        refreshed.Region.InsertIndex.Should().Be(0);
        document.Blocks.Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Cast<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Table of Figures", "Figure 1: Updated\t2");
    }

    // Same-label sibling of RefreshTableOfContentsPreservesASecondIndependentTocRegion (r161): a
    // document can legitimately hold two independent Table of Figures regions for the *same* caption
    // label (e.g. one per volume). GeneratedRegionIndices matched every one of them, so "Update Table
    // of Figures" deleted both and reinserted a single merged region at the first. The r142 label
    // parameter only separates *differently* labelled regions; it cannot separate these.
    [Fact]
    public void RefreshTableOfFiguresPreservesASecondIndependentSameLabelRegion()
    {
        var caption = Captions.BuildCaption(CaptionLabel.Figure, 1, "Diagram");
        var document = new TextDocument { Blocks = { caption } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.InsertTableOfFigures(
            new DocumentTextPosition(0, 0),
            Captions.FigureLabelText,
            (_, _) => "1").Applied.Should().BeTrue();
        session.References.InsertTableOfFigures(
            new DocumentTextPosition(document.Blocks.Count, 0),
            Captions.FigureLabelText,
            (_, _) => "1").Applied.Should().BeTrue();

        // Refresh the first region only -- the second must keep its own (now stale) page text.
        session.References.RefreshTableOfFigures(
            new DocumentTextPosition(0, 0),
            Captions.FigureLabelText,
            (_, _) => "2").Applied.Should().BeTrue();

        document.Blocks
            .Where(block => TableOfFigures.IsTableOfFiguresParagraph(block, Captions.FigureLabelText))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal(
                "Table of Figures",
                "Figure 1: Diagram\t2",
                "Table of Figures",
                "Figure 1: Diagram\t1");
    }

    // r175 freew-captions-figures F1: UpdateFields (F9 / "Update entire document") must refresh EVERY
    // distinct caption-table label's region, not just the first one encountered in document order.
    // Before the fix, RefreshGeneratedReferenceRegions computed a single `labelText` via
    // TableOfFigures.ExistingLabelText -- which stops at the first match -- and refreshed only that one
    // region, silently leaving a second, differently-labelled table (e.g. a Table of Tables alongside a
    // Table of Figures) stale.
    [Fact]
    public void UpdateFieldsRefreshesEveryDistinctCaptionTableLabelRegion()
    {
        var figureCaption = Captions.BuildCaption(CaptionLabel.Figure, 1, "First diagram");
        var tableCaption = Captions.BuildCaption(CaptionLabel.Table, 1, "First table");
        var document = new TextDocument { Blocks = { figureCaption, tableCaption } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var insertedFigures = session.References.InsertTableOfFigures(
            new DocumentTextPosition(0, 0),
            Captions.FigureLabelText,
            (_, _) => "1");
        session.References.InsertTableOfFigures(
            insertedFigures.Caret,
            Captions.TableLabelText,
            (_, _) => "1");

        // A second caption of EACH label, added after both tables already exist.
        document.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 2, "Second diagram"));
        document.Blocks.Add(Captions.BuildCaption(CaptionLabel.Table, 2, "Second table"));

        var result = session.References.UpdateFields();

        result.RefreshedGeneratedRegionCount.Should().Be(2);
        document.Blocks.Where(block => TableOfFigures.IsTableOfFiguresParagraph(block, Captions.FigureLabelText))
            .Cast<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Table of Figures", "Figure 1: First diagram\t1", "Figure 2: Second diagram\t1");
        document.Blocks.Where(block => TableOfFigures.IsTableOfFiguresParagraph(block, Captions.TableLabelText))
            .Cast<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Table of Tables", "Table 1: First table\t1", "Table 2: Second table\t1");
    }

    // Sibling/no-regression check: a document with only ONE distinct caption-table label must still be
    // refreshed exactly as before UpdateFieldsRefreshesEveryDistinctCaptionTableLabelRegion's fix -- one
    // region found, one region refreshed.
    [Fact]
    public void UpdateFieldsStillRefreshesASingleCaptionTableLabelRegion()
    {
        var figureCaption = Captions.BuildCaption(CaptionLabel.Figure, 1, "First diagram");
        var document = new TextDocument { Blocks = { figureCaption } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.InsertTableOfFigures(
            new DocumentTextPosition(0, 0),
            Captions.FigureLabelText,
            (_, _) => "1");
        document.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 2, "Second diagram"));

        var result = session.References.UpdateFields();

        result.RefreshedGeneratedRegionCount.Should().Be(1);
        document.Blocks.Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Cast<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Table of Figures", "Figure 1: First diagram\t1", "Figure 2: Second diagram\t1");
    }

    // Index counterpart of the above. A generated index has no title paragraph to cut at
    // (DocumentIndex.Build emits one only under IndexOptions.IncludeTitle, default false and off on
    // this path) and reuses its heading style for every letter-group heading, so neither contiguity
    // nor "next heading" can find the boundary. GeneratedRegionIndices uses the spanning field that
    // owns each region instead: DocumentIndex.Build stamps each build's paragraphs with one freshly
    // constructed ComplexField, so two inserts leave two distinct owners.
    [Fact]
    public void RefreshIndexPreservesASecondIndependentSameIdentifierRegion()
    {
        // Two terms so the first region spans several paragraphs, including more than one
        // letter-group heading -- those must not be mistaken for a region boundary.
        var marked = new Paragraph
        {
            Runs =
            {
                DocumentIndex.MarkRun(new IndexMark("Alpha")),
                DocumentIndex.MarkRun(new IndexMark("Beta")),
            }
        };
        var document = new TextDocument { Blocks = { marked } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References
            .InsertIndex(new DocumentTextPosition(0, 0), identifier: null, pageReferenceOf: null)
            .Applied.Should().BeTrue();
        var oneRegion = IndexRegionParagraphTexts(document);
        oneRegion.Should().HaveCountGreaterThan(
            2,
            "the region needs several paragraphs, with a letter-group heading inside it, to be a " +
            "meaningful test of the boundary rule");
        session.References.InsertIndex(
            new DocumentTextPosition(document.Blocks.Count, 0),
            identifier: null,
            pageReferenceOf: null).Applied.Should().BeTrue();

        IndexRegionParagraphTexts(document).Should().HaveCount(
            oneRegion.Count * 2,
            "two independently inserted indexes should be two distinct regions before any refresh");
        IndexRegionOwners(document).Should().HaveCount(
            2,
            "each insert stamps its own paragraphs with a distinct spanning field owner");

        session.References
            .RefreshIndex(new DocumentTextPosition(0, 0), identifier: null, pageReferenceOf: null)
            .Applied.Should().BeTrue();

        IndexRegionParagraphTexts(document).Should().HaveCount(
            oneRegion.Count * 2,
            "Update Index must rebuild only the region it targets -- a second, separately-located " +
            "index region with the same identifier must survive intact");
        IndexRegionOwners(document).Should().HaveCount(2);
    }

    private static IReadOnlyList<string> IndexRegionParagraphTexts(TextDocument document) =>
        document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, null))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .ToArray();

    private static IReadOnlyList<ComplexField> IndexRegionOwners(TextDocument document) =>
        document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, null))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.SpanningFieldOwner)
            .OfType<ComplexField>()
            .Distinct(ReferenceEqualityComparer.Instance)
            .Cast<ComplexField>()
            .ToArray();

    [Fact]
    public void TableOfAuthoritiesInsertAndRefreshOwnPlansCaretAndStabilization()
    {
        var citation = new Citation("Fresh Case", CitationCategory.Cases);
        var marked = new Paragraph { Runs = { Run.CitationMark(citation) } };
        var caretParagraph = new Paragraph("Caret");
        var document = new TextDocument { Blocks = { marked, caretParagraph } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var inserted = session.References.InsertTableOfAuthorities(
            new DocumentTextPosition(1, 2),
            ToaOptions.Default,
            () => (_, _, _, _, _) => TableOfAuthorities.CreatePageReference(1));

        inserted.Region.InsertIndex.Should().Be(1);
        inserted.Caret.Should().Be(new DocumentTextPosition(1 + inserted.Region.InsertedCount, 2));
        document.Blocks[inserted.Caret.BlockIndex].Should().BeSameAs(caretParagraph);

        var physicalPage = 1;
        var layoutRefreshes = 0;
        var refreshed = session.References.RefreshTableOfAuthorities(
            inserted.Caret,
            options: null,
            pageResolverFactory: () => (_, _, _, _, _) =>
                TableOfAuthorities.CreatePageReference(physicalPage),
            refreshLayout: () =>
            {
                layoutRefreshes++;
                physicalPage = 3;
            });

        refreshed.Region.InsertIndex.Should().Be(1);
        refreshed.Region.DeletedCount.Should().Be(inserted.Region.InsertedCount);
        refreshed.Caret.BlockIndex.Should().Be(
            inserted.Caret.BlockIndex - refreshed.Region.DeletedCount + refreshed.Region.InsertedCount);
        document.Blocks[refreshed.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        layoutRefreshes.Should().Be(2);
        document.Blocks.Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh Case\t3");

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh Case\t1");
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().Equal(marked, caretParagraph);
    }

    [Fact]
    public void TableOfAuthoritiesStabilizationIsOnePortableUndoTransaction()
    {
        var citation = new Citation("Fresh Case", CitationCategory.Cases);
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { Run.CitationMark(citation) } });
        document.Blocks.AddRange(TableOfAuthorities.Build(
            document,
            ToaOptions.Default,
            (_, _, _, _) => TableOfAuthorities.CreatePageReference(1)));
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var initialPlan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlanWithTableAddresses(
            document,
            pageResolver: (_, _, _, _, _) => TableOfAuthorities.CreatePageReference(1));
        var physicalPage = 1;
        var layoutRefreshes = 0;

        var result = session.References.ApplyStabilizedTableOfAuthoritiesRegion(
            initialPlan,
            pageResolverFactory: () => (_, _, _, _, _) =>
                TableOfAuthorities.CreatePageReference(physicalPage),
            undoLabel: "Update Table of Authorities",
            refreshLayout: () =>
            {
                layoutRefreshes++;
                physicalPage = 3;
            });

        result.Applied.Should().BeTrue();
        layoutRefreshes.Should().Be(2);
        document.Blocks.Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh Case\t3");

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh Case\t1");
    }

    [Fact]
    public void TocInsertAndRefreshAreAtomicGeneratedRegionEdits()
    {
        var heading = new Paragraph("Old heading") { StyleId = "Heading1" };
        var document = new TextDocument();
        document.Blocks.Add(heading);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.InsertTableOfContents(0, pageTextResolver: null).Applied.Should().BeTrue();
        document.Blocks.Any(TableOfContents.IsTocParagraph).Should().BeTrue();
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(heading);

        session.References.InsertTableOfContents(0, pageTextResolver: null);
        heading.Runs.Clear();
        heading.Runs.Add(new Run("New heading"));
        session.References.RefreshTableOfContents(pageTextResolver: null).Applied.Should().BeTrue();
        document.Blocks.OfType<Paragraph>()
            .Where(TableOfContents.IsTocParagraph)
            .Should().Contain(paragraph => paragraph.PlainText.Contains("New heading", StringComparison.Ordinal));
    }

    [Fact]
    public void RefreshTableOfContentsPreservesASecondIndependentTocRegion()
    {
        // Two independently inserted TOC fields (e.g. a main TOC plus a second TOC ahead of an
        // appendix) must both survive "Update Table of Contents" -- only the region the refresh
        // targets (the first one) should be replaced; a second, separately-located TOC region must
        // not be silently deleted.
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Chapter 1") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("Appendix A") { StyleId = "Heading1" });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.InsertTableOfContents(0, pageTextResolver: null).Applied.Should().BeTrue();
        var appendixHeadingIndex = document.Blocks
            .OfType<Paragraph>()
            .ToList()
            .FindIndex(paragraph => paragraph.PlainText == "Appendix A");
        session.References.InsertTableOfContents(appendixHeadingIndex, pageTextResolver: null)
            .Applied.Should().BeTrue();

        CountContiguousTocRegions(document).Should().Be(
            2,
            "two independently inserted TOC fields should be distinct regions before any refresh");

        session.References.RefreshTableOfContents(pageTextResolver: null).Applied.Should().BeTrue();

        CountContiguousTocRegions(document).Should().Be(
            2,
            "Update Table of Contents must not delete a second, independent TOC region -- " +
            "only the first region it targets should be replaced");
        document.Blocks.OfType<Paragraph>()
            .Count(paragraph => paragraph.StyleId == TableOfContents.HeadingStyleId)
            .Should().Be(2);
        document.Blocks.OfType<Paragraph>()
            .Should().Contain(paragraph => paragraph.PlainText == "Appendix A");
    }

    // The gain from moving the TOC path off contiguity onto spanning-field owner identity. A user can
    // type an ordinary paragraph into the middle of a generated TOC; the region is then sparse, and
    // "first contiguous run" saw only the entries before the gap -- so a refresh replaced those and
    // left every entry after the gap behind as stale duplicates. Owner identity spans the gap, because
    // TableOfContents.Build stamped all of those paragraphs with the same field.
    [Fact]
    public void RefreshTableOfContentsReplacesTheWholeRegionWhenTheUserTypedIntoTheMiddleOfIt()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("Chapter Three") { StyleId = "Heading1" });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.InsertTableOfContents(0, pageTextResolver: null).Applied.Should().BeTrue();
        var regionSize = document.Blocks.Count(TableOfContents.IsTocParagraph);
        regionSize.Should().BeGreaterThan(
            2,
            "the region needs enough paragraphs to have an interior for the user to type into");

        // Type an ordinary paragraph into the middle of the generated region, splitting it in two.
        var typed = new Paragraph("Typed by the user");
        document.Blocks.Insert(regionSize - 1, typed);
        CountContiguousTocRegions(document).Should().Be(
            2,
            "the typed paragraph splits one region into two contiguous runs -- which is exactly what " +
            "made contiguity the wrong boundary rule");

        session.References.RefreshTableOfContents(pageTextResolver: null).Applied.Should().BeTrue();

        document.Blocks.Count(TableOfContents.IsTocParagraph).Should().Be(
            regionSize,
            "the whole region is replaced -- no entry from beyond the gap survives as a stale duplicate");
        document.Blocks.OfType<Paragraph>()
            .Count(paragraph => paragraph.StyleId == TableOfContents.HeadingStyleId)
            .Should().Be(1, "one region means one generated heading, not two");
        document.Blocks.Should().Contain(typed, "the user's own paragraph must not be deleted");
    }

    private static int CountContiguousTocRegions(TextDocument document)
    {
        var regionCount = 0;
        var inRegion = false;
        foreach (var block in document.Blocks)
        {
            var isTocParagraph = TableOfContents.IsTocParagraph(block);
            if (isTocParagraph && !inRegion)
                regionCount++;
            inRegion = isTocParagraph;
        }

        return regionCount;
    }

    // freew-toc-fields F2, coordinator wiring: TableOfContents.Build sizes its entries' right tab stop
    // from the section the TOC is inserted into (see TableOfContentsTests.
    // Build_EntryRightTabStopUsesInsertionSectionWidth_NotFinalSectionWidth), but that model-level test
    // calls Build directly with an explicit insertionBlockIndex. Nothing exercised the coordinator itself
    // computing that index from the caller's insertion point and threading it through
    // ApplyStabilizedTableOfContentsRegion's three TableOfContents.Build calls -- so a refactor that
    // dropped the third argument at those call sites reintroduced the original bug with zero test
    // failures. This goes through DocumentReferenceEditingCoordinator.InsertTableOfContents (the shipping
    // path) instead of calling TableOfContents.Build directly.
    [Fact]
    public void InsertTableOfContentsSizesTheGeneratedTabStopFromTheInsertionSection_NotTheFinalSection()
    {
        var document = new TextDocument();
        // Front (front-matter) section: default portrait Letter -> 612 - 72 - 72 = 468pt usable.
        document.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("End of front matter")
        {
            SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage)
        });
        document.Blocks.Add(new Paragraph("Appendix heading") { StyleId = "Heading1" });

        // Final section: landscape Letter -> 792 - 72 - 72 = 648pt usable, wider than the front section.
        document.Page.WidthPt = 792;
        document.Page.HeightPt = 612;
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        // Inserted at block 0 -- front matter, inside the narrower front (portrait) section, not the
        // wider final (landscape) one.
        session.References.InsertTableOfContents(0, pageTextResolver: null).Applied.Should().BeTrue();

        var entry = document.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfContents.EntryStyleId(1)
                && paragraph.PlainText.StartsWith("Chapter One", StringComparison.Ordinal));
        entry.Formatting.TabStops.Should().ContainSingle()
            .Which.Should().Be(new TabStop(468, TabStopAlignment.Right, TabLeader.Dots));
    }

    // Sibling/no-regression case: when the TOC genuinely is inserted into the final (here, landscape)
    // section, the coordinator must still size the tab stop from that section's own (wider) width --
    // proving the fix threads the *actual* insertion point through, not a hardcoded front-section index.
    [Fact]
    public void InsertTableOfContentsSizesTheGeneratedTabStopFromTheFinalSection_WhenInsertedThere()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("End of front matter")
        {
            SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage)
        });
        document.Blocks.Add(new Paragraph("Appendix heading") { StyleId = "Heading1" });
        document.Page.WidthPt = 792;
        document.Page.HeightPt = 612;
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        // Inserted at block 2 -- the appendix heading's own index, inside the final (landscape) section.
        session.References.InsertTableOfContents(2, pageTextResolver: null).Applied.Should().BeTrue();

        var entry = document.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfContents.EntryStyleId(1)
                && paragraph.PlainText.StartsWith("Chapter One", StringComparison.Ordinal));
        entry.Formatting.TabStops.Should().ContainSingle()
            .Which.Should().Be(new TabStop(648, TabStopAlignment.Right, TabLeader.Dots));
    }

    [Fact]
    public void TocInsertStabilizesPageTextInsideOnePortableUndoGroup()
    {
        var heading = new Paragraph("Paged heading") { StyleId = "Heading1" };
        var document = new TextDocument();
        document.Blocks.Add(heading);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var page = "1";
        var layoutRefreshes = 0;

        var result = session.References.InsertTableOfContents(
            0,
            pageTextResolverFactory: () => _ => page,
            refreshLayout: () =>
            {
                layoutRefreshes++;
                page = "3";
            });

        result.Applied.Should().BeTrue();
        layoutRefreshes.Should().Be(2);
        document.Blocks.OfType<Paragraph>()
            .Where(TableOfContents.IsTocParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Paged heading\t3");

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(heading);
    }

    [Fact]
    public void CaptionAndCrossReferenceConstructionArePortable()
    {
        var target = new Paragraph("Chapter") { StyleId = "Heading1" };
        var host = new Paragraph("See ");
        var document = new TextDocument();
        document.Blocks.Add(target);
        document.Blocks.Add(host);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var crossReference = session.References.InsertCrossReference(
            sourceBlockIndex: 1,
            preferredHostBlockIndex: 1,
            CrossRefType.Heading,
            new CrossRefTarget("Chapter", Anchor: null, BlockIndex: 0),
            CrossRefInsertAs.Text,
            hyperlink: true);

        crossReference.HostBlockIndex.Should().Be(1);
        host.Runs.Should().Contain(run => run.CrossReference != null);

        var caption = session.References.InsertCaption(1, "Figure", "Diagram");
        caption.InsertedBlockIndex.Should().Be(2);
        ((Paragraph)document.Blocks[2]).PlainText.Should().Contain("Figure 1");
    }

    [Fact]
    public void NotesBookmarksAndCitationSettingsAreCoordinatorOwned()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Host"));
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var note = session.References.InsertNote(0, 2, "note", footnote: true);

        note.Applied.Should().BeTrue();
        document.Footnotes.Should().ContainKey(note.NoteId);
        session.Commands.Undo().Should().BeTrue();
        document.Footnotes.Should().NotContainKey(note.NoteId);

        session.References.SetBookmark(0, " Target ").Should().BeTrue();
        ((Paragraph)document.Blocks[0]).BookmarkNames.Should().Contain("Target");
        session.References.ApplyCitationStyle(CitationStyle.Ieee).Should().BeTrue();
        document.BibliographyStyle.Should().Be(CitationStyle.Ieee);

        session.References.InsertIndexEntry(0, 0, new IndexMark("Host"))
            .Applied.Should().BeTrue();
        ((Paragraph)document.Blocks[0]).Runs.Should()
            .Contain(run => DocumentIndex.MarkedEntry(run) != null);
        session.References.InsertAuthorityCitation(0, 0, new Citation("Case"))
            .Applied.Should().BeTrue();
        ((Paragraph)document.Blocks[0]).Runs.Should()
            .Contain(run => run.Citation != null);
    }

    // Confirmed HIGH finding: Insert Bookmark allowed a duplicate name, which then made the Bookmark
    // Manager's Delete (name-keyed) remove every instance at once. Word enforces unique bookmark names —
    // TrySetBookmark must reject a name already used by a different paragraph.
    [Fact]
    public void TrySetBookmark_RejectsANameAlreadyUsedByADifferentParagraph()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("First"));
        document.Blocks.Add(new Paragraph("Second"));
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.TrySetBookmark(0, "Shared").Should().Be(BookmarkInsertOutcome.Applied);
        session.References.TrySetBookmark(1, "Shared").Should().Be(BookmarkInsertOutcome.DuplicateName);

        ((Paragraph)document.Blocks[0]).BookmarkNames.Should().Equal("Shared");
        ((Paragraph)document.Blocks[1]).BookmarkNames.Should().BeEmpty();
    }

    // Sibling no-regression: re-applying a paragraph's own current name is not a duplicate.
    [Fact]
    public void TrySetBookmark_AllowsReapplyingTheSameNameToItsOwnParagraph()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("First"));
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.TrySetBookmark(0, "Target").Should().Be(BookmarkInsertOutcome.Applied);
        session.References.TrySetBookmark(0, "Target").Should().Be(BookmarkInsertOutcome.Applied);

        ((Paragraph)document.Blocks[0]).BookmarkNames.Should().Equal("Target");
    }

    // Confirmed HIGH finding, other half: Bookmark Manager Delete must target only the selected instance.
    [Fact]
    public void RemoveBookmarkAt_RemovesOnlyTheSelectedInstance_NotEveryParagraphSharingTheName()
    {
        var first = new Paragraph("First");
        first.BookmarkNames.Add("Dup");
        var second = new Paragraph("Second");
        second.BookmarkNames.Add("Dup");
        var document = new TextDocument();
        document.Blocks.Add(first);
        document.Blocks.Add(second);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.RemoveBookmarkAt(new BookmarkLocation("Dup", 1)).Should().BeTrue();

        first.BookmarkNames.Should().Equal("Dup");
        second.BookmarkNames.Should().BeEmpty();

        session.Commands.Undo().Should().BeTrue();
        second.BookmarkNames.Should().Equal("Dup");
    }

    [Fact]
    public void IndexEntryInsertion_RejectsEquivalentMarksAndRoundTripsHistory()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Host"));
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.InsertIndexEntry(0, 2, new IndexMark("Host"))
            .Applied.Should().BeTrue();
        session.References.InsertIndexEntry(0, 2, new IndexMark(" host "))
            .Applied.Should().BeFalse();
        ((Paragraph)document.Blocks[0]).Runs
            .Count(run => DocumentIndex.MarkedEntry(run) is not null).Should().Be(1);

        session.Commands.Undo().Should().BeTrue();
        ((Paragraph)document.Blocks[0]).Runs.Should()
            .NotContain(run => DocumentIndex.MarkedEntry(run) != null);
        session.Commands.Redo().Should().BeTrue();
        ((Paragraph)document.Blocks[0]).Runs.Should()
            .ContainSingle(run => DocumentIndex.MarkedEntry(run) != null);
    }
}

public sealed class DocumentPortableEditingOwnershipTests
{
    /// <summary>
    /// Round 167 correction: the C3-remediation doc comment this guarded ("no field-code state to toggle
    /// for these runs") was wrong -- real Word toggles field-code display for a simple
    /// <see cref="RunFieldKind"/> field exactly like a <see cref="ComplexField"/> -- and pinning it with a
    /// source-contract test turned a fixable gap into a guarded "this is correct" regression trap. This now
    /// guards the corrected explanation instead: <see cref="DocumentReferenceEditingCoordinator.ToggleFieldCodes"/>
    /// including both field forms must stay documented, not silently revert to looking unexplained.
    /// </summary>
    [Fact]
    public void ToggleFieldCodesDocumentsThatItIncludesSimpleFields()
    {
        var source = ReadSource("freew", "FreeW.App.Presentation", "Editing", "DocumentReferenceEditingCoordinator.cs");

        source.Should().Contain("for a simple field exactly like it shows");
        source.Should().NotContain("no field-code state to toggle for these runs");
    }

    [Fact]
    public void RenderersDelegateMigratedTableParagraphAndReferenceCommands()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var forbidden = new[]
        {
            "new InsertTableRowCommand(",
            "new DeleteTableRowCommand(",
            "new InsertTableColumnCommand(",
            "new DeleteTableColumnCommand(",
            "new MergeCellsHorizontalCommand(",
            "new MergeCellsVerticalCommand(",
            "new SplitCellCommand(",
            "new SetCellShadingCommand(",
            "new SetCellAlignmentCommand(",
            "new SetCellBordersCommand(",
            "new SetTableFormattingCommand(",
            "new SetTableAutoFitCommand(",
            "new ApplyTableStyleCommand(",
            "new ApplyTablePropertiesCommand(",
            "new InsertTableCellFormulaCommand(",
            "new InsertTableCellNoteCommand(",
            "new SetParagraphStyleCommand(",
            "new DeleteParagraphCommand(",
            "new InsertCrossReferenceCommand(",
            "new ApplyManualHyphenationCommand(",
            "new FormatParagraphRunsCommand(",
            "new ReorderBlocksCommand(",
            "new InsertNoteCommand(",
            "new DeleteNoteCommand(",
            "new ReplaceNoteContentCommand(",
            "new SetNoteNumberingOptionsCommand(",
            "new ApplyCitationStyleCommand(",
            "new ReplaceSourcesCommand(",
            "new SetParagraphBookmarkNameCommand(",
            "new SetBookmarkNameCommand(",
            "new CellTextCommand(",
            "new ReplaceContentControlRunCommand(",
        };

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("DocumentTableEditingCoordinator TableEdits");
            source.Should().Contain("TableEdits.AddressesInRange(");
            source.Should().Contain("DocumentReferenceEditingCoordinator ReferenceEdits");
            source.Should().Contain("_editingSession.FormatParagraphs(");
            source.Should().Contain("_editingSession.SetParagraphStyles(");
            source.Should().Contain("_editingSession.ApplyDropCap(");
            source.Should().Contain("ReferenceEdits.InsertIndexEntry(");
            source.Should().Contain("ReferenceEdits.MarkAllIndexEntries(");
            source.Should().Contain("ReferenceEdits.ToggleFieldCodes()");
            source.Should().Contain("ReferenceEdits.UpdateFields(");
            source.Should().Contain("ReferenceEdits.ApplyNoteNumberingOptions(result)");
            source.Should().Contain("DocumentReferenceBlockPageResolution BuildReferenceBlockPageResolution()");
            source.Should().NotContain("CrossReferences.ResolveField(");
            source.Should().NotContain("ComplexFieldEngine.Recompute(");
            source.Should().NotContain("refreshedGeneratedRegion");
            source.Should().NotContain("with { ShowCode = show");
            foreach (var constructor in forbidden)
                source.Should().NotContain(constructor);
        }

        avalonia.Should().Contain("public void PlaceCaretInCell(");
        avalonia.Should().NotContain("SetCellAlignmentCommand expects");
        avalonia.Should().NotContain("CellEditRequested");
        avalonia.Should().NotContain("CellEditRequest");
        avalonia.Should().NotContain("public string GetCellText(");
        avalonia.Should().NotContain("public void SetCellText(");

        File.Exists(Path.Combine(
                TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
                "freew", "FreeW.App.Avalonia", "Editing", "ReferenceCommands.cs"))
            .Should().BeFalse("renderer-neutral reference commands belong in Core or Presentation");
    }

    [Fact]
    public void BothRenderersExpandSelectedCellFormattingThroughPresentationCoordinator()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        static string Slice(string source, string signature, string nextSignature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            var end = source.IndexOf(nextSignature, start, StringComparison.Ordinal);
            start.Should().BeGreaterThanOrEqualTo(0);
            end.Should().BeGreaterThan(start);
            return source[start..end];
        }

        var wpfShading = Slice(
            wpf,
            "public void SetCaretCellShading(string? colorHex)",
            "public void SetCaretCellBorders(");
        var avaloniaShading = Slice(
            avalonia,
            "public void SetCellShading(string? hexColor)",
            "public void SetCellBorders(");

        foreach (var method in new[] { wpfShading, avaloniaShading })
        {
            method.Should().Contain("TableEdits.AddressesInRange(");
            method.Should().Contain("TableEdits.SetCellShading(");
        }

        wpfShading.Should().Contain("TableAddressOf(Selection.Start.Parent as TextElement)");
        wpfShading.Should().Contain("TableAddressOf(Selection.End.Parent as TextElement)");
        avaloniaShading.Should().NotContain("for (var gridCol");
        avaloniaShading.Should().NotContain("new List<DocumentTableCellAddress>");

        var wpfDirection = Slice(
            wpf,
            "public void SetCaretCellTextDirection(CellTextDirection direction)",
            "public void ToggleTableHeaderRow(");
        var avaloniaDirection = Slice(
            avalonia,
            "public void SetCaretCellTextDirection(CellTextDirection direction)",
            "public (Table Table, int RowIndex, int ColumnIndex)? CaretTableCell(");

        foreach (var method in new[] { wpfDirection, avaloniaDirection })
        {
            method.Should().Contain("TableEdits.AddressesInRange(");
            method.Should().Contain("TableEdits.SetCellTextDirection(");
        }

        wpfDirection.Should().Contain("TableAddressOf(Selection.Start.Parent as TextElement)");
        wpfDirection.Should().Contain("TableAddressOf(Selection.End.Parent as TextElement)");
        avaloniaDirection.Should().Contain("SelectedCellRange is { } selection");
        avaloniaDirection.Should().NotContain("_cellCaret is not");
    }

    [Fact]
    public void GeneratedReferenceOrchestrationAndPaginationStayRendererNeutral()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var coordinator = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Editing",
            "DocumentReferenceEditingCoordinator.cs");
        var operations = new[]
        {
            "InsertBibliography",
            "RefreshBibliography",
            "InsertIndex",
            "RefreshIndex",
            "InsertTableOfFigures",
            "RefreshTableOfFigures",
            "InsertTableOfAuthorities",
            "RefreshTableOfAuthorities",
        };
        var rendererForbidden = new[]
        {
            "BibliographyRegionPlanner.Build",
            "DocumentIndex.EnsureStyles(",
            "DocumentIndex.Build(",
            "DocumentIndex.IsIndexParagraph(",
            "TableOfFigures.EnsureStyles(",
            "TableOfFigures.BuildWithTableAddresses(",
            "TableOfFigures.IsTableOfFiguresParagraph(",
            "TableOfAuthoritiesRegionPlanner.Build",
            "ReferenceEdits.InsertGeneratedRegion(",
            "ReferenceEdits.RefreshGeneratedRegion(",
            "ReferenceEdits.ApplyGeneratedRegion(",
            "ReferenceEdits.ApplyStabilizedTableOfAuthoritiesRegion(",
            "\"Insert Bibliography\"",
            "\"Update Bibliography\"",
            "\"Insert Index\"",
            "\"Update Index\"",
            "\"Insert Table of Figures\"",
            "\"Update Table of Figures\"",
            "\"Insert Table of Authorities\"",
            "\"Update Table of Authorities\"",
        };

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("TableOfFiguresPageTextResolverPlanner.Build(");
            source.Should().Contain("TableOfAuthoritiesPageResolverPlanner.Build(");
            source.Should().NotContain("GeneratedReferencePaginationContext.Create(");
            source.Should().NotContain("ResolveTableParagraphPageOffset(");
            source.Should().NotContain("private static ToaCitationPageReference CreateTableOfAuthoritiesPageReference(");
            source.Should().NotContain("ApplyTableOfAuthoritiesPlanCommands(");
            source.Should().NotContain("maxStabilizationPasses");
            foreach (var operation in operations)
                source.Should().Contain($"ReferenceEdits.{operation}(");
            foreach (var forbidden in rendererForbidden)
                source.Should().NotContain(forbidden);
        }

        foreach (var label in rendererForbidden.Where(value => value.StartsWith("\"", StringComparison.Ordinal)))
            coordinator.Should().Contain(label);
        coordinator.Should().Contain("BibliographyRegionPlanner.BuildInsertPlan(");
        coordinator.Should().Contain("GeneratedRegionIndices(");
        coordinator.Should().Contain("CompleteGeneratedReferenceEdit(");
        coordinator.Should().Contain("ApplyStabilizedTableOfAuthoritiesRegion(");
    }

    [Fact]
    public void PortableCoordinatorsHaveNoRendererDependencies()
    {
        foreach (var file in new[]
        {
            "DocumentTableEditingCoordinator.cs",
            "DocumentReferenceEditingCoordinator.cs",
        })
        {
            var source = ReadSource("freew", "FreeW.App.Presentation", "Editing", file);
            source.Should().NotContain("using Avalonia");
            source.Should().NotContain("using System.Windows");
            source.Should().NotContain("FreeW.App.Host");
            source.Should().NotContain("FreeW.App.Avalonia");
            source.Should().NotContain("TextPointer");
            source.Should().NotContain("DocPosition");
        }
    }

    [Fact]
    public void AvaloniaEditingFolderContainsNoDocumentCommandImplementations()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var editingDirectory = Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing");
        var sharedCommands = ReadSource(
            "freew", "FreeW.Core.Model", "TableCellParagraphCommands.cs");
        var offenders = Directory.EnumerateFiles(editingDirectory, "*.cs")
            .Where(file => File.ReadAllText(file).Contains(": IDocumentCommand", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        offenders.Should().BeEmpty("renderer-neutral undo commands belong in Core or Presentation");
        sharedCommands.Should().Contain("public sealed class SetCellParagraphMarkRevisionCommand(");
        sharedCommands.Should().Contain("TableCellCommandAddress.TryGetParagraph(");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
