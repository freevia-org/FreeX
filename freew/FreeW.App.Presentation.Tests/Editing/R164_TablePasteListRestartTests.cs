using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

/// <summary>
/// freew-lists-numbering-restart F1: <see cref="DocumentEditingSession.RestartUnrelatedNumberListRuns"/>
/// (the private helper both <see cref="DocumentEditingSession.InsertDocumentAfter"/> and
/// <see cref="DocumentEditingSession.TryInsertDocumentAtBodyCaret"/> call to fix
/// <see cref="R161_PasteListRestartTests"/>'s top-level case) only ever walked the top-level pasted
/// blocks: for any block that wasn't itself a <see cref="Paragraph"/> -- in particular a pasted
/// <see cref="Table"/> -- it just reset its "was this a Number list" tracking and moved on, never
/// descending into the table's rows/cells. <see cref="DocumentMerge.CloneBlocksForInsertion"/> clones a
/// table cell paragraph's <see cref="ParagraphFormatting.ListStartOverride"/> verbatim just like a body
/// paragraph, so a Number-kind paragraph inside a pasted table's cell kept its source "continue"
/// (<c>ListStartOverride == null</c>) and silently continued whatever unrelated Number list preceded the
/// paste point instead of restarting at 1, unlike an equivalent top-level pasted paragraph.
/// </summary>
public sealed class R164_TablePasteListRestartTests
{
    [Fact]
    public void InsertDocumentAfter_PastingATableWithANumberListCell_RestartsAtOneInsteadOfContinuing()
    {
        // Destination: a 3-item Number list ("1.", "2.", "3.") followed by ordinary body text -- the
        // finding's exact repro shape.
        var target = new TextDocument();
        target.Blocks.Add(NumberItem("List A item 1", startOverride: 1));
        target.Blocks.Add(NumberItem("List A item 2", startOverride: null));
        target.Blocks.Add(NumberItem("List A item 3", startOverride: null));
        target.Blocks.Add(new Paragraph("Some unrelated body text after the list."));

        // Clipboard fragment: a single table whose one cell holds one paragraph copied out of the MIDDLE
        // of some other Number list, so it carries no explicit ListStartOverride -- exactly what a real
        // paste of a mid-list table cell hands back.
        var source = new TextDocument();
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(NumberItem("Pasted table item", startOverride: null));
        row.Cells.Add(cell);
        table.Rows.Add(row);
        source.Blocks.Add(table);

        var session = new DocumentEditingSession();
        session.LoadDocument(target);

        // Paste after the unrelated body-text paragraph (block index 3).
        session.InsertDocumentAfter(3, source).Should().Be(4);

        target.Blocks.Should().HaveCount(5);
        var pastedTable = target.Blocks[4].Should().BeOfType<Table>().Subject;
        var pastedCellParagraph = pastedTable.Rows[0].Cells[0].Paragraphs[0];
        pastedCellParagraph.PlainText.Should().Be("Pasted table item");

        // The pasted table-cell list paragraph must now carry an explicit restart.
        pastedCellParagraph.Formatting.ListStartOverride.Should().Be(1);

        // Running the exact shared planner both renderers/writer use for markers must show the pasted
        // cell paragraph restarting at 1, not continuing the earlier list's count as 4.
        var plans = TableCellListMarkerPlanner.Build(target);
        plans[pastedCellParagraph].NumberValue.Should().Be(1);

        session.Commands.Undo().Should().BeTrue();
        target.Blocks.Should().HaveCount(4);
    }

    /// <summary>
    /// Sibling/no-regression: pasting a table whose cell continues the SAME (still-open) Number list --
    /// i.e. lands immediately after it with no intervening unrelated content -- must keep continuing it,
    /// exactly like <see cref="R161_PasteListRestartTests.InsertDocumentAfter_PastingListItemsRightAfterAnOpenNumberList_ContinuesItInstead"/>
    /// already proves for a top-level pasted paragraph. This checks the fix's table-descending walk didn't
    /// widen the restart trigger into legitimate continuations.
    /// </summary>
    [Fact]
    public void InsertDocumentAfter_PastingATableRightAfterAnOpenNumberList_ContinuesItInstead()
    {
        var target = new TextDocument();
        target.Blocks.Add(NumberItem("List A item 1", startOverride: 1));
        target.Blocks.Add(NumberItem("List A item 2", startOverride: null));

        var source = new TextDocument();
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(NumberItem("Pasted table item", startOverride: null));
        row.Cells.Add(cell);
        table.Rows.Add(row);
        source.Blocks.Add(table);

        var session = new DocumentEditingSession();
        session.LoadDocument(target);

        // Paste immediately after the still-open list (block index 1), no unrelated content in between.
        session.InsertDocumentAfter(1, source).Should().Be(2);

        var pastedTable = target.Blocks[2].Should().BeOfType<Table>().Subject;
        var pastedCellParagraph = pastedTable.Rows[0].Cells[0].Paragraphs[0];

        // No forced restart: this table cell continues the destination's still-open list.
        pastedCellParagraph.Formatting.ListStartOverride.Should().BeNull();

        var plans = TableCellListMarkerPlanner.Build(target);
        plans[pastedCellParagraph].NumberValue.Should().Be(3);
    }

    private static Paragraph NumberItem(string text, int? startOverride) => new(text)
    {
        Formatting = ParagraphFormatting.Default with
        {
            ListKind = ListKind.Number,
            ListStartOverride = startOverride,
        },
    };
}
