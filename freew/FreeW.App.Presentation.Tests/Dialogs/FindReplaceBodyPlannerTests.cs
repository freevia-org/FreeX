using System.Diagnostics;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// Body Replace All is planned in one pass over the model (one projection per flow, matches applied
/// back to front) and lands as a single block-list command.
/// </summary>
public sealed class FindReplaceBodyPlannerTests
{
    private static readonly FindReplaceSearchOptions NoOptions = new();
    private static readonly RunFormatting Bold = new() { Bold = true };

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    private static TextDocument Doc(params Block[] blocks)
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        document.Blocks.AddRange(blocks);
        return document;
    }

    private static Paragraph Runs(params Run[] runs)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange(runs);
        return paragraph;
    }

    private static Table TableOf(params string[][] cellParagraphs)
    {
        var table = new Table();
        var row = new TableRow();
        foreach (var texts in cellParagraphs)
        {
            var cell = new TableCell();
            foreach (var text in texts)
                cell.Paragraphs.Add(new Paragraph(text));
            row.Cells.Add(cell);
        }

        table.Rows.Add(row);
        return table;
    }

    private static List<string> Text(TextDocument document) =>
        document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText).ToList();

    private static FindReplaceBodyReplaceAllCommand Replace(
        TextDocument document,
        FindReplaceReplaceRequest request,
        FindReplaceRevisionContext? tracked = null,
        DocumentTextRange? restrictTo = null)
    {
        var command = FindReplaceBodyPlanner.PlanReplaceAll(document, request, tracked, restrictTo);
        if (command.HasEffect(new Context(document)))
            command.Apply(new Context(document));
        return command;
    }

    private static FindReplaceReplaceRequest Request(string find, string replace) => new(find, replace, NoOptions);

    [Theory]
    [InlineData("aaaa", "aa", "b", "bb", 2)]
    [InlineData("aaaaa", "aa", "b", "bba", 2)]
    [InlineData("abab", "ab", "x", "xx", 2)]
    [InlineData("a a", "a", "aa", "aa aa", 2)]
    [InlineData("aaa", "a", "", "", 3)]
    [InlineData("cat Cat", "cat", "[^&]", "[cat] [Cat]", 2)]
    [InlineData("xyz", "q", "r", "xyz", 0)]
    public void AdjacentAndOverlappingMatches_AreReplacedLeftToRightWithoutReFinding(
        string text, string find, string replace, string expected, int count)
    {
        var document = Doc(new Paragraph(text));

        var command = Replace(document, Request(find, replace));

        command.ReplacedCount.Should().Be(count);
        Text(document).Should().Equal(expected);
    }

    // ---- object marks: notes, comments, graphics, fields, section breaks --------------------------

    private static Paragraph WithObjects(Run before, Run mark, Run after) => Runs(before, mark, after);

    private static int Count(TextDocument document, string find) =>
        FindReplaceBodyPlanner.CountMatches(document, new FindReplaceSearchRequest(find, NoOptions));

    private static Run Graphic() => Run.FromImage(new InlineImage([1, 2, 3], 10, 10));

    [Theory]
    [InlineData("^f")]
    [InlineData("^e")]
    [InlineData("^a")]
    [InlineData("^5")]
    [InlineData("^g")]
    public void ObjectCodes_FindTheirOwnObjectOnly(string code)
    {
        var document = Doc(Runs(
            new Run("A"),
            Run.FootnoteReference(1),
            new Run("B"),
            Run.EndnoteReference(2),
            new Run("C"),
            Run.CommentReference(3),
            new Run("D"),
            Graphic(),
            new Run("E")));

        Count(document, code).Should().Be(1);
    }

    private static string RunsWithBreaks(Paragraph paragraph) =>
        string.Concat(paragraph.Runs.Select(run =>
            run.IsPageBreak ? "<page>" : run.IsColumnBreak ? "<column>" : run.Text));

    [Fact]
    public void PageAndColumnBreakCodes_InReplaceWith_InsertBreakRunsNotText()
    {
        var document = Doc(new Paragraph("one; two; three"));

        var page = Replace(document, Request("; two", "^mX^nY"));

        page.ReplacedCount.Should().Be(1);
        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.PlainText.Should().Be("oneXY; three", "a break contributes no text");
        RunsWithBreaks(paragraph).Should().Be("one<page>X<column>Y; three");
        paragraph.Runs.Where(run => run.IsPageBreak || run.IsColumnBreak).Should().OnlyContain(run => run.Text.Length == 0);
    }

    [Fact]
    public void PageBreakCode_InReplaceWith_AfterAParagraphMark_StaysWithinItsOwnParagraph()
    {
        var document = Doc(new Paragraph("a|b"));

        Replace(document, Request("|", "^p^m"));

        Text(document).Should().Equal("a", "b");
        RunsWithBreaks((Paragraph)document.Blocks[1]).Should().Be("<page>b");
    }

    [Fact]
    public void BreakCode_InReplaceWith_IsMarkedInsertedWhenTracked()
    {
        var document = Doc(new Paragraph("one; two"));

        Replace(document, Request("; ", "^n"), new FindReplaceRevisionContext("Ann", null));

        var inserted = ((Paragraph)document.Blocks[0]).Runs.Single(run => run.IsColumnBreak);
        inserted.Revision.Should().Be(RevisionKind.Inserted);
        inserted.RevisionAuthor.Should().Be("Ann");
    }

    [Fact]
    public void NumericCodes_FindAndReplaceThroughTheBody()
    {
        var document = Doc(new Paragraph("Terms (c) apply — always"), new Paragraph("Last (c)"));

        Replace(document, Request("(c)", "^0169")).ReplacedCount.Should().Be(2);
        Text(document).Should().Equal("Terms © apply — always", "Last ©");

        Replace(document, Request("^u8212", "^0150")).ReplacedCount.Should().Be(1);
        Replace(document, Request("^0169", "(C)")).ReplacedCount.Should().Be(2);
        Text(document).Should().Equal("Terms (C) apply – always", "Last (C)");
    }

    [Fact]
    public void FindNext_LocatesANoteMarkByItsPlainTextExtent_AndNeverByItsNumber()
    {
        var document = Doc(Runs(new Run("see"), Run.FootnoteReference(1), new Run(" here 1")));

        var mark = FindReplaceDialogPlanner.FindNextMatch(document, "see^f", NoOptions, 0, 0);
        mark.Should().NotBeNull();
        (mark!.Value.Start, mark.Value.Length).Should().Be((0, 4));

        var number = FindReplaceDialogPlanner.FindNextMatch(document, "1", NoOptions, 0, 0);
        (number!.Value.Start, number.Value.Length).Should().Be((10, 1));
    }

    [Fact]
    public void FindNext_LocatesAGraphicByItsRunIndex_AndStepsPastEachOne()
    {
        // A graphic has no PlainText extent, so the match has length zero and is addressed by its run.
        var document = Doc(Runs(new Run("a"), Graphic(), new Run("b"), Graphic(), Graphic(), new Run("c")));
        var request = new FindReplaceSearchRequest("^g", NoOptions);

        Count(document, "^g").Should().Be(3);
        var first = FindReplaceDialogPlanner.FindNextMatch(document, request, 0, 0)!.Value;
        (first.Block, first.Start, first.Length, first.MarkRunIndex ?? -1, first.MarkEndRunIndex ?? -1)
            .Should().Be((0, 1, 0, 1, 1));
        first.IsMarkOnly.Should().BeTrue();

        // Two graphics at one offset: the offset alone cannot say which was visited.
        var second = FindReplaceDialogPlanner.FindNextMatch(document, request, 0, 1, resumeAfterMarkRun: 1)!.Value;
        (second.Start, second.MarkRunIndex ?? -1).Should().Be((2, 3));
        var third = FindReplaceDialogPlanner.FindNextMatch(document, request, 0, 2, resumeAfterMarkRun: 3)!.Value;
        (third.Start, third.MarkRunIndex ?? -1).Should().Be((2, 4));

        // Past the last one the walk wraps to the first.
        var wrapped = FindReplaceDialogPlanner.FindNextMatch(document, request, 0, 2, resumeAfterMarkRun: 4)!.Value;
        (wrapped.Start, wrapped.MarkRunIndex ?? -1).Should().Be((1, 1));
    }

    [Fact]
    public void FindNext_LocatesACommentMarkAndOneInATableCell()
    {
        var cell = new TableCell();
        var inCell = new Paragraph();
        inCell.Runs.Add(new Run("x"));
        inCell.Runs.Add(Run.CommentReference(9));
        cell.Paragraphs.Add(inCell);
        var row = new TableRow();
        row.Cells.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        var document = Doc(Runs(new Run("a"), Run.CommentReference(2), new Run("b")), table);

        var body = FindReplaceDialogPlanner.FindNextMatch(document, "^a", NoOptions, 0, 0)!.Value;
        (body.Block, body.Start, body.Length, body.MarkRunIndex ?? -1).Should().Be((0, 1, 0, 1));

        var inTable = FindReplaceDialogPlanner.FindNextMatch(
            document, new FindReplaceSearchRequest("^a", NoOptions), 0, 1, resumeAfterMarkRun: 1)!.Value;
        (inTable.Block, inTable.TableRow ?? -1, inTable.TableCol ?? -1, inTable.TableParagraphIndex ?? -1,
                inTable.Start, inTable.MarkRunIndex ?? -1)
            .Should().Be((1, 0, 0, 0, 1, 1));
    }

    [Fact]
    public void FindNext_LocatesAGraphicInTheDefaultHeader_AndIsMatchAtKnowsTheObject()
    {
        var document = Doc(new Paragraph("nothing here"));
        var header = new HeaderFooter();
        header.Paragraphs.Add(Runs(new Run("H"), Graphic(), new Run("I")));
        document.Header = header;
        var request = new FindReplaceSearchRequest("^g", NoOptions);

        var hit = FindReplaceDialogPlanner.FindNextMatch(document, request, 0, 0)!.Value;

        hit.IsInHeaderFooter.Should().BeTrue();
        (hit.HeaderFooterIsFooter == true, hit.HeaderFooterParagraphIndex ?? -1, hit.Start, hit.Length, hit.MarkRunIndex ?? -1)
            .Should().Be((false, 0, 1, 0, 1));
        FindReplaceDialogPlanner.IsMatchAt(document, request, hit).Should().BeTrue();
        FindReplaceDialogPlanner.IsMatchAt(document, request, hit with { MarkRunIndex = 0 }).Should().BeFalse();
    }

    [Fact]
    public void IsMatchAt_OfAMarkOnlyLocation_NeedsTheRightObject_AndANonMatchingSearchFails()
    {
        var document = Doc(Runs(new Run("a"), Graphic(), Run.CommentReference(2), new Run("b")));
        var graphic = new FindReplaceMatch(0, 1, 0, MarkRunIndex: 1, MarkEndRunIndex: 1);
        var comment = new FindReplaceMatch(0, 1, 0, MarkRunIndex: 2, MarkEndRunIndex: 2);

        FindReplaceDialogPlanner.IsMatchAt(document, new FindReplaceSearchRequest("^g", NoOptions), graphic).Should().BeTrue();
        FindReplaceDialogPlanner.IsMatchAt(document, new FindReplaceSearchRequest("^g", NoOptions), comment).Should().BeFalse();
        FindReplaceDialogPlanner.IsMatchAt(document, new FindReplaceSearchRequest("^a", NoOptions), comment).Should().BeTrue();
        FindReplaceDialogPlanner.IsMatchAt(document, new FindReplaceSearchRequest("b", NoOptions), graphic).Should().BeFalse();
    }

    [Fact]
    public void PlanReplaceMatch_OfAMarkOnlyLocation_ConsumesThatObjectOnly()
    {
        static Run Image(byte id) => Run.FromImage(new InlineImage([id], 10, 10));
        static byte[] Remaining(TextDocument document) =>
            ((Paragraph)document.Blocks[0]).Runs.Where(run => run.Image != null).Select(run => run.Image!.Bytes[0]).ToArray();

        var second = Doc(Runs(new Run("a"), Image(1), Image(2), new Run("b")));
        var secondCommand = FindReplaceBodyPlanner.PlanReplaceMatch(
            second,
            new FindReplaceReplaceRequest("^g", "[x]", NoOptions),
            new FindReplaceMatch(0, 1, 0, MarkRunIndex: 2, MarkEndRunIndex: 2));
        secondCommand.Apply(new Context(second));
        secondCommand.ReplacedCount.Should().Be(1);
        ((Paragraph)second.Blocks[0]).PlainText.Should().Be("a[x]b");
        Remaining(second).Should().Equal(new byte[] { 1 }, "the location named the second graphic");

        var first = Doc(Runs(new Run("a"), Image(1), Image(2), new Run("b")));
        FindReplaceBodyPlanner.PlanReplaceMatch(
                first,
                new FindReplaceReplaceRequest("^g", "[y]", NoOptions),
                new FindReplaceMatch(0, 1, 0, MarkRunIndex: 1, MarkEndRunIndex: 1))
            .Apply(new Context(first));
        Remaining(first).Should().Equal(new byte[] { 2 }, "the location named the first graphic");
    }

    private static string[] RunLabels(Paragraph paragraph) =>
        paragraph.Runs
            .Select(run => run.Image is { } image
                ? "img" + image.Bytes[0]
                : run.IsCommentReference
                    ? "c" + run.CommentId
                    : run.Text)
            .ToArray();

    [Fact]
    public void PlanReplaceMatch_OfAMarkOnlyLocation_WritesTheReplacementWhereThatObjectWas()
    {
        static Run Image(byte id) => Run.FromImage(new InlineImage([id], 10, 10));

        // Consuming the SECOND of two objects at one offset: the text takes its place after the first,
        // not the front of both.
        var second = Doc(Runs(new Run("a"), Image(1), Image(2), new Run("b")));
        FindReplaceBodyPlanner.PlanReplaceMatch(
                second,
                new FindReplaceReplaceRequest("^g", "[x]", NoOptions),
                new FindReplaceMatch(0, 1, 0, MarkRunIndex: 2, MarkEndRunIndex: 2))
            .Apply(new Context(second));
        RunLabels((Paragraph)second.Blocks[0]).Should().Equal("a", "img1", "[x]", "b");

        // The first one: the text takes its place before the second.
        var first = Doc(Runs(new Run("a"), Image(1), Image(2), new Run("b")));
        FindReplaceBodyPlanner.PlanReplaceMatch(
                first,
                new FindReplaceReplaceRequest("^g", "[y]", NoOptions),
                new FindReplaceMatch(0, 1, 0, MarkRunIndex: 1, MarkEndRunIndex: 1))
            .Apply(new Context(first));
        RunLabels((Paragraph)first.Blocks[0]).Should().Equal("a", "[y]", "img2", "b");
    }

    [Fact]
    public void ReplaceAllOfAdjacentObjects_WritesEachReplacementWhereItsObjectWas()
    {
        static Run Image(byte id) => Run.FromImage(new InlineImage([id], 10, 10));

        var document = Doc(Runs(new Run("a"), Image(1), Run.CommentReference(7), Image(2), new Run("b")));

        Replace(document, Request("^g", "x")).ReplacedCount.Should().Be(2);

        // The comment mark between them is not a match and stays between the two written texts.
        RunLabels((Paragraph)document.Blocks[0]).Should().Equal("a", "x", "c7", "x", "b");
    }

    [Fact]
    public void WithMarkRun_NamesTheObjectByItsPlaceAmongTheTextlessMatchesAtOneOffset()
    {
        var document = Doc(Runs(new Run("a"), Run.CommentReference(1), Run.CommentReference(2), new Run("b")));
        var request = new FindReplaceSearchRequest("^a", NoOptions);
        var location = new FindReplaceMatch(0, 1, 0);

        var firstObject = FindReplaceBodyPlanner.WithMarkRun(document, request, location, 0);
        var secondObject = FindReplaceBodyPlanner.WithMarkRun(document, request, location, 1);

        (firstObject.MarkRunIndex, firstObject.MarkEndRunIndex).Should().Be((1, 1));
        (secondObject.MarkRunIndex, secondObject.MarkEndRunIndex).Should().Be((2, 2));
        FindReplaceBodyPlanner.WithMarkRun(document, request, location, 2).Should().Be(location, "no match answers it");
        FindReplaceBodyPlanner.WithMarkRun(document, request, location with { Length = 1 }, 1)
            .Should().Be(location with { Length = 1 }, "a match with text is not an object");

        FindReplaceBodyPlanner.PlanReplaceMatch(document, new FindReplaceReplaceRequest("^a", "", NoOptions), secondObject)
            .Apply(new Context(document));
        RunLabels((Paragraph)document.Blocks[0]).Should().Equal("a", "c1", "b");
    }

    private static Table WithNested(Table outer, params Table[] nested)
    {
        foreach (var table in nested)
            outer.Rows[0].Cells[0].NestedTables.Add(table);
        return outer;
    }

    private static string NestedCellText(TextDocument document, int nested = 0, int paragraph = 0) =>
        ((Table)document.Blocks.OfType<Table>().Single()).Rows[0].Cells[0].NestedTables[nested]
            .Rows[0].Cells[0].Paragraphs[paragraph].PlainText;

    [Fact]
    public void FindNextNested_WalksTheMatchesOfNestedTablesOnly_InDocumentOrder()
    {
        var document = Doc(
            new Paragraph("body cat"),
            WithNested(TableOf(["outer cat"]), TableOf(["nested cat one", "cat two"]), TableOf(["third cat"])));
        var request = new FindReplaceSearchRequest("cat", NoOptions);

        var first = FindReplaceBodyPlanner.FindNextNested(document, request)!;
        (first.Block, first.FirstParagraph, first.Start, first.MatchedText).Should().Be((1, 0, 7, "cat"));

        var second = FindReplaceBodyPlanner.FindNextNested(document, request, first.End)!;
        (second.FirstParagraph, second.Start).Should().Be((1, 0), "the next paragraph of the same nested cell");

        var third = FindReplaceBodyPlanner.FindNextNested(document, request, second.End)!;
        third.Context.Should().Contain("third cat");

        FindReplaceBodyPlanner.FindNextNested(document, request, third.End).Should().BeNull();
        FindReplaceBodyPlanner.FindNextNested(
            Doc(new Paragraph("cat"), TableOf(["outer cat"])), request).Should().BeNull("there is no nested table");
    }

    [Fact]
    public void PlanReplaceNested_MakesReplaceAllsEditForThatMatchOnly()
    {
        var document = Doc(WithNested(TableOf(["outer cat"]), TableOf(["cat and cat"])));
        var request = new FindReplaceReplaceRequest("cat", "tiger", NoOptions);
        var match = FindReplaceBodyPlanner.FindNextNested(document, request.ToSearchRequest())!;
        var second = FindReplaceBodyPlanner.FindNextNested(document, request.ToSearchRequest(), match.End)!;

        var command = FindReplaceBodyPlanner.PlanReplaceNested(document, request, second);
        command.ReplacedCount.Should().Be(1);
        NestedCellText(document).Should().Be("cat and cat", "planning touches nothing");

        command.Apply(new Context(document));
        NestedCellText(document).Should().Be("cat and tiger");

        command.Revert(new Context(document));
        NestedCellText(document).Should().Be("cat and cat");

        // A match that is no longer one is declined.
        FindReplaceBodyPlanner.PlanReplaceNested(document, request with { Term = "dog" }, second).ReplacedCount.Should().Be(0);
    }

    [Fact]
    public void NoteMarkNumber_IsNotSearchableText()
    {
        // The reference run mirrors its id as text, but in Word the mark is one character that no text finds.
        var document = Doc(Runs(new Run("a"), Run.FootnoteReference(7), new Run("b 7")));

        Count(document, "7").Should().Be(1);
        Replace(document, Request("7", "x")).ReplacedCount.Should().Be(1);
        Text(document).Should().Equal("a7b x");
    }

    [Fact]
    public void ReplacingANoteMark_RemovesTheReferenceAndLeavesTheNeighbours()
    {
        var document = Doc(WithObjects(new Run("one"), Run.FootnoteReference(1), new Run("two")));

        Replace(document, Request("^f", "|")).ReplacedCount.Should().Be(1);

        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Runs.Any(run => run.FootnoteId is not null).Should().BeFalse();
        paragraph.PlainText.Should().Be("one|two");
    }

    [Fact]
    public void ReplacingGraphicAndCommentMarks_WithNothing_DeletesThem()
    {
        var document = Doc(Runs(new Run("a"), Graphic(), new Run("b"), Run.CommentReference(4), new Run("c")));

        Replace(document, Request("^g", "")).ReplacedCount.Should().Be(1);
        Replace(document, Request("^a", "")).ReplacedCount.Should().Be(1);

        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Runs.Should().OnlyContain(run => run.Image == null && !run.IsCommentReference);
        paragraph.PlainText.Should().Be("abc");
    }

    [Fact]
    public void ReplaceAllOfMarks_ComesBackToFrontWithoutDisturbingEarlierOnes()
    {
        var document = Doc(Runs(
            new Run("a"), Run.FootnoteReference(1), new Run("b"), Run.FootnoteReference(2), new Run("c")));

        Replace(document, Request("^f", "")).ReplacedCount.Should().Be(2);

        ((Paragraph)document.Blocks[0]).Runs.Select(run => run.Text).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void ObjectMarks_UnderTrackChanges_AreLeftAlone()
    {
        var document = Doc(WithObjects(new Run("one"), Run.FootnoteReference(1), new Run("two")));

        Replace(document, Request("^f", ""), new FindReplaceRevisionContext("Ann", null)).ReplacedCount.Should().Be(0);

        ((Paragraph)document.Blocks[0]).Runs.Any(run => run.FootnoteId == 1).Should().BeTrue();
    }

    [Fact]
    public void FieldCode_FindsTheWholeField_AndOrdinarySearchStillReadsThroughItsResult()
    {
        var document = Doc(Runs(new Run("Page "), Run.PageNumberField(), new Run(" of 9")));

        Count(document, "^d").Should().Be(1);
        Count(document, "Page ^d of").Should().Be(1);
        // Without ^d no field marks are projected, so text searches straight through the cached result.
        Count(document, "Page 1 of 9").Should().Be(1);
    }

    [Fact]
    public void ReplacingAField_RemovesTheFieldAndWritesTheReplacement()
    {
        var document = Doc(Runs(new Run("Page "), Run.PageNumberField(), new Run(" of 9")));

        Replace(document, Request("^d", "N")).ReplacedCount.Should().Be(1);

        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Runs.Any(run => run.FieldKind != RunFieldKind.None).Should().BeFalse();
        paragraph.PlainText.Should().Be("Page N of 9");
    }

    [Fact]
    public void SectionBreak_EndsItsParagraph_AndIsNotAParagraphMark()
    {
        var document = Doc(
            new Paragraph("first") { SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage) },
            new Paragraph("second"),
            new Paragraph("third"));

        Count(document, "^b").Should().Be(1);
        Count(document, "^p").Should().Be(1);
        Count(document, "first^b").Should().Be(1);
    }

    [Fact]
    public void ReplacingASectionBreak_JoinsTheParagraphsAndMergesTheSections()
    {
        var document = Doc(
            new Paragraph("first") { SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage) },
            new Paragraph("second"));

        Replace(document, Request("^b", " ")).ReplacedCount.Should().Be(1);

        document.Blocks.Should().ContainSingle();
        var merged = (Paragraph)document.Blocks[0];
        merged.PlainText.Should().Be("first second");
        merged.SectionBreak.Should().BeNull();
    }

    [Fact]
    public void ASectionBreakThatEndsItsFlow_CannotBeConsumed()
    {
        var document = Doc(
            new Paragraph("only") { SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage) });

        Count(document, "^b").Should().Be(1);
        Replace(document, Request("^b", "")).ReplacedCount.Should().Be(0);

        ((Paragraph)document.Blocks[0]).SectionBreak.Should().NotBeNull();
    }

    [Fact]
    public void ReplaceWithClipboardContents_UsesTheInjectedProvider()
    {
        var document = Doc(new Paragraph("a cat sat"));
        var request = new FindReplaceReplaceRequest("cat", "[^c]", NoOptions, ClipboardText: () => "dog");

        Replace(document, request).ReplacedCount.Should().Be(1);

        Text(document).Should().Equal("a [dog] sat");
    }

    [Fact]
    public void ReplaceWithFoundText_KeepsTheTextOfWhatWasFound()
    {
        var document = Doc(new Paragraph("a cat sat"));

        Replace(document, Request("cat", "<^&^&>")).ReplacedCount.Should().Be(1);

        Text(document).Should().Equal("a <catcat> sat");
    }

    [Fact]
    public void ParagraphMarkMatches_JoinParagraphs_IncludingAdjacentOnes()
    {
        var document = Doc(new Paragraph("alpha"), new Paragraph("beta"), new Paragraph("gamma"));
        Replace(document, Request("^p", " / ")).ReplacedCount.Should().Be(2);
        Text(document).Should().Equal("alpha / beta / gamma");

        var empties = Doc(new Paragraph("one"), new Paragraph(), new Paragraph("two"), new Paragraph(), new Paragraph(), new Paragraph("three"));
        Replace(empties, Request("^p^p", "^p")).ReplacedCount.Should().Be(2);
        Text(empties).Should().Equal("one", "two", "", "three");

        var tails = Doc(new Paragraph("end here end"), new Paragraph("next end"), new Paragraph("last"));
        Replace(tails, Request("end^p", "END. "));
        Text(tails).Should().Equal("end here END. next END. last");
    }

    [Fact]
    public void ParagraphMarkInTheReplacement_SplitsTheParagraph_KeepingItsStyle()
    {
        var document = Doc(new Paragraph("one; two; three") { StyleId = "Quote" }, new Paragraph("tail"));

        Replace(document, Request("; ", "^p")).ReplacedCount.Should().Be(2);

        Text(document).Should().Equal("one", "two", "three", "tail");
        document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.StyleId)
            .Should().Equal("Quote", "Quote", "Quote", null);
    }

    [Fact]
    public void AMatchNeverCrossesFromTheBodyIntoATable_OrBetweenCells()
    {
        var document = Doc(new Paragraph("a"), TableOf(["b", "c"], ["d"]), new Paragraph("e"));

        Replace(document, Request("^p", "+")).ReplacedCount.Should().Be(1);

        var table = document.Blocks.OfType<Table>().Single();
        table.Rows[0].Cells[0].Paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal("b+c");
        table.Rows[0].Cells[1].Paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal("d");
        Text(document).Should().Equal("a", "e");
    }

    [Fact]
    public void ReplacementText_TakesTheFormattingOfTheTextItReplaces()
    {
        var document = Doc(Runs(new Run("plain "), new Run("strong", Bold), new Run(" hel"), new Run("lo", Bold)));

        Replace(document, Request("strong", "X"));
        Replace(document, Request("hello", "bye"));

        var runs = ((Paragraph)document.Blocks[0]).Runs;
        runs.Single(run => run.Text == "X").Formatting.Bold.Should().BeTrue();
        runs.Single(run => run.Text.Contains("bye")).Formatting.Bold.Should().BeFalse();
        Text(document).Should().Equal("plain X bye");
    }

    [Fact]
    public void FormattingReplacements_FindByFormat_ReformatInPlace_AndFormatOnlyNewText()
    {
        var document = Doc(
            Runs(new Run("plain "), new Run("strong", Bold), new Run(" plain "), new Run("also", Bold)),
            new Paragraph("see cat here"));

        Replace(
                document,
                new FindReplaceReplaceRequest(
                    string.Empty,
                    string.Empty,
                    NoOptions,
                    new FindFormat { Bold = true },
                    new FindFormat { Bold = false, Italic = true, ParagraphStyleId = "Quote" }))
            .ReplacedCount.Should().Be(2);

        var first = (Paragraph)document.Blocks[0];
        first.PlainText.Should().Be("plain strong plain also");
        first.StyleId.Should().Be("Quote");
        first.Runs.Where(run => run.Text is "strong" or "also")
            .Should().OnlyContain(run => run.Formatting.Italic && !run.Formatting.Bold);
        first.Runs.Where(run => run.Text.Contains("plain")).Should().OnlyContain(run => !run.Formatting.Italic);

        Replace(document, new FindReplaceReplaceRequest("cat", "tiger", NoOptions, ReplaceFormat: new FindFormat { Bold = true }));
        ((Paragraph)document.Blocks[1]).Runs.Select(run => (run.Text, run.Formatting.Bold))
            .Should().Equal(("see ", false), ("tiger", true), (" here", false));
    }

    [Fact]
    public void TextWithFindFormatting_ReplacesOnlyTheFormattedOccurrence()
    {
        var document = Doc(Runs(new Run("word "), new Run("word", Bold), new Run(" word")));

        Replace(document, new FindReplaceReplaceRequest("word", "X", NoOptions, new FindFormat { Bold = true }))
            .ReplacedCount.Should().Be(1);

        Text(document).Should().Equal("word X word");
    }

    [Fact]
    public void ManualPageBreak_IsFoundAndRemoved_AndFieldsAreLeftAlone()
    {
        var document = Doc(
            Runs(new Run("before"), Run.PageBreak(), new Run("after 1 "), new Run("1") { FieldKind = RunFieldKind.PageNumber }));

        Replace(document, Request("^m", " | ")).ReplacedCount.Should().Be(1);
        Replace(document, Request("1", "one")).ReplacedCount.Should().Be(1);

        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Runs.Should().NotContain(run => run.IsPageBreak);
        paragraph.PlainText.Should().Be("before | after one 1");
        paragraph.Runs.Should().Contain(run => run.FieldKind == RunFieldKind.PageNumber && run.Text == "1");
    }

    [Fact]
    public void TrackedReplaceAll_KeepsOriginalsStruckThrough_AndCannotReFindThem()
    {
        var document = Doc(new Paragraph("Confidential a"), new Paragraph("b Confidential"));
        var tracked = new FindReplaceRevisionContext("Ann", "2026-01-01T00:00:00Z");

        Replace(document, Request("Confidential", "Strictly Confidential"), tracked).ReplacedCount.Should().Be(2);

        var runs = document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs).ToList();
        runs.Count(run => run.Revision == RevisionKind.Inserted && run.Text == "Strictly Confidential").Should().Be(2);
        runs.Count(run => run.Revision == RevisionKind.Deleted && run.Text == "Confidential").Should().Be(2);
        Text(document)[0].Should().Be("Strictly ConfidentialConfidential a");

        // The struck-through originals are masked: a second pass finds only the new text.
        FindReplaceBodyPlanner.CountMatches(document, new FindReplaceSearchRequest("Confidential", NoOptions))
            .Should().Be(2);
    }

    [Fact]
    public void TrackedParagraphMarkMatch_MarksTheMarkDeletedInsteadOfMerging()
    {
        var document = Doc(new Paragraph("a"), new Paragraph("b"));

        Replace(document, Request("^p", "-"), new FindReplaceRevisionContext("Ann", null)).ReplacedCount.Should().Be(1);

        document.Blocks.Should().HaveCount(2);
        ((Paragraph)document.Blocks[0]).MarkRevision.Should().Be(RevisionKind.Deleted);
        ((Paragraph)document.Blocks[0]).Runs.Should().Contain(run => run.Revision == RevisionKind.Inserted && run.Text == "-");
    }

    [Fact]
    public void TheCommand_LeavesTheOriginalBlocksUntouched_SoUndoRestoresThemExactly()
    {
        var first = new Paragraph("foo one");
        var untouched = new Paragraph("nothing");
        var table = TableOf(["foo cell"]);
        var document = Doc(first, table, untouched);
        var context = new Context(document);

        var command = FindReplaceBodyPlanner.PlanReplaceAll(document, Request("foo", "bar"));
        Text(document).Should().Equal(["foo one", "nothing"], "planning must not touch the document");

        command.Apply(context);
        Text(document).Should().Equal("bar one", "nothing");
        document.Blocks[2].Should().BeSameAs(untouched, "blocks without a match are not cloned");
        document.Blocks.OfType<Table>().Single().Rows[0].Cells[0].Paragraphs[0].PlainText.Should().Be("bar cell");
        command.ReplacedCount.Should().Be(2);

        command.Revert(context);
        document.Blocks.Should().Equal(first, table, untouched);
        first.PlainText.Should().Be("foo one");
        table.Rows[0].Cells[0].Paragraphs[0].PlainText.Should().Be("foo cell");

        command.Apply(context);
        Text(document).Should().Equal("bar one", "nothing");
    }

    [Fact]
    public void NoMatch_HasNoEffect()
    {
        var document = Doc(new Paragraph("text"));

        var command = FindReplaceBodyPlanner.PlanReplaceAll(document, Request("zzz", "y"));

        command.HasEffect(new Context(document)).Should().BeFalse();
        command.ReplacedCount.Should().Be(0);
    }

    [Fact]
    public void RestrictedToARange_ReplacesOnlyMatchesThatStartInsideIt()
    {
        var document = Doc(new Paragraph("x x x"), TableOf(["x"]), new Paragraph("x x"), new Paragraph("x"));
        var range = new DocumentTextRange(new DocumentTextPosition(0, 2), new DocumentTextPosition(2, 1));

        Replace(document, Request("x", "Y"), restrictTo: range).ReplacedCount.Should().Be(4);

        Text(document).Should().Equal("x Y Y", "Y x", "x");
        document.Blocks.OfType<Table>().Single().Rows[0].Cells[0].Paragraphs[0].PlainText.Should().Be("Y");
    }

    [Fact]
    public void CountMatches_AgreesWithWhatReplaceAllReplaces()
    {
        var document = Doc(new Paragraph("a"), new Paragraph("b"), TableOf(["a", "a"]), new Paragraph("a a"));

        FindReplaceBodyPlanner.CountMatches(document, new FindReplaceSearchRequest("a", NoOptions)).Should().Be(5);
        FindReplaceBodyPlanner.CountMatches(document, new FindReplaceSearchRequest("^p", NoOptions)).Should().Be(2);
        FindReplaceDialogPlanner.CountMatches(document, new FindReplaceSearchRequest("^p", NoOptions)).Should().Be(2);
        Replace(document, Request("a", "z")).ReplacedCount.Should().Be(5);
    }

    // Same shape check as FiveThousandParagraphJoins_AreOnePass below: a flat 2 s wall-clock ceiling
    // flaked under machine load, so the guarantee (one linear pass over the document, not a rescan of
    // the document per replacement) is asserted on the bytes the planning thread allocates, which load
    // and scheduling do not change. 10 replacements per paragraph at N and 4N paragraphs: linear work
    // allocates ~4x as much, a per-replacement rescan (quadratic) ~16x. Wall clock is only a hang guard.
    private static long AllocatedByReplacing(int paragraphCount, out TimeSpan elapsed)
    {
        var paragraphs = Enumerable.Range(0, paragraphCount)
            .Select(_ => (Block)new Paragraph(string.Join(' ', Enumerable.Repeat("lorem needle ipsum", 10))))
            .ToArray();
        var document = Doc(paragraphs);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var command = Replace(document, Request("needle", "pin"));
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        command.ReplacedCount.Should().Be(paragraphCount * 10);
        Text(document).Should().OnlyContain(text => !text.Contains("needle") && text.Contains("lorem pin ipsum"));
        elapsed = watch.Elapsed;
        return allocated;
    }

    [Fact]
    public void FiveThousandReplacements_AreOnePass_NotOneScanPerReplacement()
    {
        const int Small = 125;
        const int Large = 500;

        AllocatedByReplacing(40, out _); // warm-up: JIT and first-use allocations out of the measurement.
        var small = AllocatedByReplacing(Small, out _);
        var large = AllocatedByReplacing(Large, out var elapsed);

        small.Should().BeGreaterThan(0);
        var ratio = (double)large / small;
        ratio.Should().BeLessThan(
            6d,
            "4x the replacements should allocate ~4x (one pass); a per-replacement rescan of the document is quadratic (~16x)");
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20), "this is only a hang guard; the allocation ratio above is the real check");
    }

    // A flat wall-clock ceiling for this operation failed under machine load (the same replace
    // measured ~1 s alone and 2-4 s while other sessions were building), and a wall-clock growth
    // ratio proved just as noisy at these millisecond scales. What this test must prove is the
    // SHAPE of the cost: one linear pass, not a per-match loop. The deterministic signal for that is
    // the bytes the planning thread allocates (unaffected by load or scheduling): the same Replace
    // All is measured at N and 4N paragraphs after a warm-up, and linear work allocates ~4x as much
    // while a per-replacement re-projection of the flow (quadratic) allocates ~16x. The wall-clock
    // ceiling is kept only as a generous hang guard.
    private static long AllocatedByJoining(int paragraphCount, out TimeSpan elapsed)
    {
        var joined = Doc(Enumerable.Range(0, paragraphCount).Select(index => (Block)new Paragraph($"p{index}")).ToArray());
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var command = Replace(joined, Request("^p", " "));
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        command.ReplacedCount.Should().Be(paragraphCount - 1);
        joined.Blocks.Should().HaveCount(1);
        elapsed = watch.Elapsed;
        return allocated;
    }

    [Fact]
    public void FiveThousandParagraphJoins_AreOnePass()
    {
        const int Small = 1_251;
        const int Large = 5_001;

        AllocatedByJoining(300, out _); // warm-up: JIT and first-use allocations out of the measurement.
        var small = AllocatedByJoining(Small, out _);
        var large = AllocatedByJoining(Large, out var elapsed);

        small.Should().BeGreaterThan(0);
        var ratio = (double)large / small;
        ratio.Should().BeLessThan(
            6d,
            "4x the paragraphs should allocate ~4x (one pass); a per-replacement re-projection is quadratic (~16x)");
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20), "this is only a hang guard; the allocation ratio above is the real check");
    }
}
