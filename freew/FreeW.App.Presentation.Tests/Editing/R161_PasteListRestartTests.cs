using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

/// <summary>
/// freew-list-restart F1: before this fix, neither <see cref="DocumentEditingSession.InsertDocumentAfter"/>
/// ("Insert Text from File") nor <see cref="DocumentEditingSession.TryInsertDocumentAtBodyCaret"/> (Ctrl+V /
/// the ribbon Paste button, via <c>PasteKeepSourceFormatting</c>) ever checked whether a pasted Number-kind
/// paragraph was adjacent to an already-rendering Number list. <see cref="DocumentMerge.CloneBlocksForInsertion"/>
/// clones every paragraph's <see cref="ParagraphFormatting.ListStartOverride"/> verbatim, so pasting a
/// Number-list paragraph copied from the middle of some OTHER list (carrying <c>ListStartOverride == null</c>,
/// "continue") anywhere in the destination document made the shared per-document counter in
/// <see cref="DocumentListMarkerSequencePlanner"/> simply keep counting from whatever unrelated list last
/// left off, instead of restarting at 1 the way the ribbon Numbering button already does for the same
/// "new, unrelated list" case (<see cref="DocumentParagraphFormattingCoordinator.ToggleListKind"/>).
/// </summary>
public sealed class R161_PasteListRestartTests
{
    [Fact]
    public void InsertDocumentAfter_PastingListItemsAfterAnUnrelatedList_RestartsAtOneInsteadOfContinuing()
    {
        // Destination: a 3-item Number list ("1.", "2.", "3.") followed by ordinary body text -- exactly
        // the finding's repro shape.
        var target = new TextDocument();
        target.Blocks.Add(NumberItem("List A item 1", startOverride: 1));
        target.Blocks.Add(NumberItem("List A item 2", startOverride: null));
        target.Blocks.Add(NumberItem("List A item 3", startOverride: null));
        target.Blocks.Add(new Paragraph("Some unrelated body text after the list."));

        // Clipboard fragment: two paragraphs copied out of the MIDDLE of some other Number list, so
        // neither carries an explicit ListStartOverride -- exactly what a real paste hands back for any
        // non-first list paragraph.
        var source = new TextDocument();
        source.Blocks.Add(NumberItem("Pasted item X", startOverride: null));
        source.Blocks.Add(NumberItem("Pasted item Y", startOverride: null));

        var session = new DocumentEditingSession();
        session.LoadDocument(target);

        // Paste after the unrelated body-text paragraph (block index 3).
        session.InsertDocumentAfter(3, source).Should().Be(4);

        target.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText).Should().Equal(
            "List A item 1",
            "List A item 2",
            "List A item 3",
            "Some unrelated body text after the list.",
            "Pasted item X",
            "Pasted item Y");

        // The first pasted list paragraph must now carry an explicit restart; the second continues from it.
        ((Paragraph)target.Blocks[4]).Formatting.ListStartOverride.Should().Be(1);
        ((Paragraph)target.Blocks[5]).Formatting.ListStartOverride.Should().BeNull();

        // Running the exact shared planner both renderers use for markers must show the pasted list
        // restarting at 1/2, not continuing the earlier list's count as 4/5.
        NumberMarkersFor(target).Should().Equal(new int?[] { 1, 2, 3, null, 1, 2 });

        session.Commands.Undo().Should().BeTrue();
        target.Blocks.Should().HaveCount(4);
    }

    [Fact]
    public void TryInsertDocumentAtBodyCaret_PastingListItemsAfterAnUnrelatedList_RestartsAtOne()
    {
        var target = new TextDocument();
        target.Blocks.Add(NumberItem("List A item 1", startOverride: 1));
        target.Blocks.Add(NumberItem("List A item 2", startOverride: null));
        target.Blocks.Add(NumberItem("List A item 3", startOverride: null));
        target.Blocks.Add(new Paragraph("Some unrelated body text."));

        var source = new TextDocument();
        source.Blocks.Add(NumberItem("Pasted item X", startOverride: null));
        source.Blocks.Add(NumberItem("Pasted item Y", startOverride: null));

        var session = new DocumentEditingSession();
        session.LoadDocument(target);

        // Caret at the end of the unrelated body-text paragraph (block 3) -- the ordinary Ctrl+V gesture.
        var caret = new DocumentTextPosition(3, "Some unrelated body text.".Length);
        session.TryInsertDocumentAtBodyCaret(caret, source, out _).Should().BeTrue();

        // The destination paragraph (index 3) is replaced by 2 blocks (head + the standalone second
        // pasted paragraph), so the original 4 blocks become 5.
        target.Blocks.Should().HaveCount(5);
        // The destination's own body paragraph absorbs the first pasted paragraph's runs (splice), so the
        // first STANDALONE pasted block is "Pasted item Y" at index 4.
        ((Paragraph)target.Blocks[4]).PlainText.Should().Be("Pasted item Y");
        ((Paragraph)target.Blocks[4]).Formatting.ListKind.Should().Be(ListKind.Number);
        ((Paragraph)target.Blocks[4]).Formatting.ListStartOverride.Should().Be(1);

        NumberMarkersFor(target).Should().Equal(new int?[] { 1, 2, 3, null, 1 });
    }

    /// <summary>
    /// Sibling/no-regression: pasting Number-list content immediately after an EXISTING, still-open Number
    /// list must keep continuing it (no forced restart) -- proving the fix did not widen past the
    /// "unrelated list" case into legitimate continuations, matching
    /// <see cref="DocumentParagraphFormattingCoordinator.ToggleListKind"/>'s own adjacency rule.
    /// </summary>
    [Fact]
    public void InsertDocumentAfter_PastingListItemsRightAfterAnOpenNumberList_ContinuesItInstead()
    {
        var target = new TextDocument();
        target.Blocks.Add(NumberItem("List A item 1", startOverride: 1));
        target.Blocks.Add(NumberItem("List A item 2", startOverride: null));

        var source = new TextDocument();
        source.Blocks.Add(NumberItem("Pasted item X", startOverride: null));
        source.Blocks.Add(NumberItem("Pasted item Y", startOverride: null));

        var session = new DocumentEditingSession();
        session.LoadDocument(target);

        session.InsertDocumentAfter(1, source).Should().Be(2);

        ((Paragraph)target.Blocks[2]).Formatting.ListStartOverride.Should().BeNull();
        ((Paragraph)target.Blocks[3]).Formatting.ListStartOverride.Should().BeNull();
        NumberMarkersFor(target).Should().Equal(new int?[] { 1, 2, 3, 4 });
    }

    /// <summary>
    /// Sibling/no-regression: pasting at a caret positioned MID-TEXT of an existing Number-list paragraph
    /// must not disturb that paragraph's own (already-correct) restart marker.
    /// </summary>
    [Fact]
    public void TryInsertDocumentAtBodyCaret_PastingIntoAContinuingListParagraph_LeavesItsOwnMarkerAlone()
    {
        var target = new TextDocument();
        target.Blocks.Add(NumberItem("List A item 1", startOverride: 1));
        target.Blocks.Add(NumberItem("List A item 2", startOverride: null));

        var source = new TextDocument();
        source.Blocks.Add(new Paragraph("INSERTED"));

        var session = new DocumentEditingSession();
        session.LoadDocument(target);

        var caret = new DocumentTextPosition(1, "List A item ".Length);
        session.TryInsertDocumentAtBodyCaret(caret, source, out _).Should().BeTrue();

        var mergedParagraph = (Paragraph)target.Blocks[1];
        mergedParagraph.PlainText.Should().Be("List A item INSERTED2");
        mergedParagraph.Formatting.ListKind.Should().Be(ListKind.Number);
        mergedParagraph.Formatting.ListStartOverride.Should().BeNull();
        NumberMarkersFor(target).Should().Equal(new int?[] { 1, 2 });
    }

    private static Paragraph NumberItem(string text, int? startOverride) => new(text)
    {
        Formatting = ParagraphFormatting.Default with
        {
            ListKind = ListKind.Number,
            ListStartOverride = startOverride,
        },
    };

    /// <summary>Runs the exact shared marker planner the WPF/Avalonia renderers use, collecting each
    /// Number-kind paragraph's rendered value (null for a non-Number paragraph).</summary>
    private static IReadOnlyList<int?> NumberMarkersFor(TextDocument document)
    {
        var planner = new DocumentListMarkerSequencePlanner();
        var values = new List<int?>();
        foreach (var block in document.Blocks)
        {
            if (block is not Paragraph paragraph)
                continue;
            var plan = planner.Advance(paragraph);
            values.Add(paragraph.Formatting.ListKind == ListKind.Number ? plan.NumberValue : null);
        }
        return values;
    }
}
