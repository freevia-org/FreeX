using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// The shared planners behind interactive Find Next and single Replace: a match that runs across
/// paragraph marks is found (body and table cell) and located by where it starts and ends, and one
/// located match is replaced with exactly the edit Replace All makes for it, formatting included.
/// </summary>
public sealed class FindReplaceSingleMatchPlannerTests
{
    private static readonly FindReplaceSearchOptions NoOptions = new();
    private static readonly FindFormat Bold = new() { Bold = true };

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

    private static Table TableOf(params string[] cellParagraphs)
    {
        var cell = new TableCell();
        foreach (var text in cellParagraphs)
            cell.Paragraphs.Add(new Paragraph(text));
        var row = new TableRow();
        row.Cells.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        return table;
    }

    private static FindReplaceMatch? Next(TextDocument document, string term, int block = 0, int offset = 0) =>
        FindReplaceDialogPlanner.FindNextMatch(document, new FindReplaceSearchRequest(term, NoOptions), block, offset);

    private static List<string> Text(TextDocument document) =>
        document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText).ToList();

    [Fact]
    public void FindNext_FindsAMatchRunningAcrossAParagraphMark_AndLocatesItsEnd()
    {
        var document = Doc(new Paragraph("alpha"), new Paragraph("beta"), new Paragraph("gamma"));

        var match = Next(document, "a^pb");

        match.Should().NotBeNull();
        match!.Value.IsSpanning.Should().BeTrue();
        (match.Value.Block, match.Value.Start).Should().Be((0, 4));
        (match.Value.EndBlock, match.Value.EndOffset).Should().Be((1, 1));
    }

    [Fact]
    public void FindNext_ParagraphMarkAlone_EndsAtTheStartOfTheNextParagraph()
    {
        var document = Doc(new Paragraph("one"), new Paragraph("two"), new Paragraph("three"));

        var first = Next(document, "^p");
        var second = Next(document, "^p", 1, 0);

        (first!.Value.Block, first.Value.Start, first.Value.EndBlock, first.Value.EndOffset).Should().Be((0, 3, 1, 0));
        (second!.Value.Block, second.Value.Start, second.Value.EndBlock, second.Value.EndOffset).Should().Be((1, 3, 2, 0));
    }

    [Fact]
    public void FindNext_ContinuesPastASpanningMatchFromItsEnd()
    {
        var document = Doc(new Paragraph("end"), new Paragraph("end"), new Paragraph("end"));

        var first = Next(document, "end^p");
        var second = Next(document, "end^p", first!.Value.EndBlock!.Value, first.Value.EndOffset!.Value);

        first!.Value.Block.Should().Be(0);
        second!.Value.Block.Should().Be(1);
    }

    [Fact]
    public void FindNext_WithAPlainMatchEarlierInTheSameParagraph_StillReturnsTheEarlierOne()
    {
        var document = Doc(new Paragraph("end and end"), new Paragraph("tail"));

        var match = Next(document, "end");

        match!.Value.IsSpanning.Should().BeFalse();
        match.Value.Should().Be(new FindReplaceMatch(0, 0, 3));
    }

    [Fact]
    public void FindNext_FindsASpanningMatchInsideATableCell()
    {
        var document = Doc(new Paragraph("intro"), TableOf("alpha", "beta"));

        var match = Next(document, "a^pb");

        match.Should().NotBeNull();
        var hit = match!.Value;
        (hit.Block, hit.TableRow, hit.TableCol, hit.TableParagraphIndex, hit.Start).Should().Be((1, 0, 0, 0, 4));
        (hit.EndParagraphIndex, hit.EndOffset).Should().Be((1, 1));
    }

    [Fact]
    public void FindNext_NeverMatchesTheMarkAtTheEndOfTheLastParagraphOfAFlow()
    {
        Next(Doc(new Paragraph("alpha"), new Paragraph("beta")), "beta^p").Should().BeNull();
        Next(Doc(TableOf("alpha", "beta")), "beta^p").Should().BeNull();
    }

    [Fact]
    public void IsMatchAt_AcceptsExactlyTheSpanFindNextReported()
    {
        var document = Doc(new Paragraph("alpha"), new Paragraph("beta"));
        var request = new FindReplaceSearchRequest("a^pb", NoOptions);
        var match = Next(document, "a^pb")!.Value;

        FindReplaceDialogPlanner.IsMatchAt(document, request, match).Should().BeTrue();
        FindReplaceDialogPlanner.IsMatchAt(document, request, match with { EndOffset = 2 }).Should().BeFalse();
        FindReplaceDialogPlanner.IsMatchAt(document, request, match with { Start = 3 }).Should().BeFalse();
    }

    [Fact]
    public void PlanReplaceMatch_InATableCell_AppliesTheReplaceFormatToTheWrittenText()
    {
        var document = Doc(new Paragraph("intro"), TableOf("see cat here", "other cat"));
        var location = Next(document, "cat")!.Value;
        var request = new FindReplaceReplaceRequest("cat", "tiger", NoOptions, ReplaceFormat: Bold);

        var command = FindReplaceBodyPlanner.PlanReplaceMatch(document, request, location);
        command.ReplacedCount.Should().Be(1);
        command.Label.Should().Be("Replace");
        command.Apply(new Context(document));

        var paragraphs = ((Table)document.Blocks[1]).Rows[0].Cells[0].Paragraphs;
        paragraphs[0].PlainText.Should().Be("see tiger here");
        paragraphs[0].Runs.Single(run => run.Text == "tiger").Formatting.Bold.Should().BeTrue();
        paragraphs[0].Runs.Where(run => run.Text != "tiger").Should().OnlyContain(run => !run.Formatting.Bold);
        paragraphs[1].PlainText.Should().Be("other cat", "only the located match is replaced");

        command.Revert(new Context(document));
        ((Table)document.Blocks[1]).Rows[0].Cells[0].Paragraphs[0].PlainText.Should().Be("see cat here");
    }

    [Fact]
    public void PlanReplaceMatch_WithEmptyReplacement_ReformatsTheFoundText()
    {
        var document = Doc(TableOf("see cat here"));
        var location = Next(document, "cat")!.Value;
        var request = new FindReplaceReplaceRequest("cat", string.Empty, NoOptions, ReplaceFormat: Bold);

        FindReplaceBodyPlanner.PlanReplaceMatch(document, request, location).Apply(new Context(document));

        var paragraph = ((Table)document.Blocks[0]).Rows[0].Cells[0].Paragraphs[0];
        paragraph.PlainText.Should().Be("see cat here");
        paragraph.Runs.Single(run => run.Text == "cat").Formatting.Bold.Should().BeTrue();
    }

    [Fact]
    public void PlanReplaceMatch_OverASpanningMatch_JoinsTheParagraphsLikeReplaceAll()
    {
        var single = Doc(new Paragraph("end here end"), new Paragraph("next end"), new Paragraph("last"));
        var all = Doc(new Paragraph("end here end"), new Paragraph("next end"), new Paragraph("last"));
        var location = Next(single, "end^p")!.Value;
        var request = new FindReplaceReplaceRequest("end^p", "END. ", NoOptions);

        FindReplaceBodyPlanner.PlanReplaceMatch(single, request, location).Apply(new Context(single));
        FindReplaceBodyPlanner.PlanReplaceAll(all, request).Apply(new Context(all));

        Text(single).Should().Equal("end here END. next end", "last");
        Text(all).Should().Equal("end here END. next END. last");
    }

    [Fact]
    public void PlanReplaceMatch_WhenTheLocationIsNoLongerAMatch_ChangesNothing()
    {
        var document = Doc(TableOf("see cat here"));
        var stale = new FindReplaceMatch(0, 0, 3, 0, 0, 0);
        var request = new FindReplaceReplaceRequest("cat", "dog", NoOptions);

        var command = FindReplaceBodyPlanner.PlanReplaceMatch(document, request, stale);

        command.ReplacedCount.Should().Be(0);
        command.HasEffect(new Context(document)).Should().BeFalse();
    }

    [Fact]
    public void PlanReplacementAt_AppliesTheReplaceFormatInAHeaderParagraph()
    {
        var document = Doc(new Paragraph("body"));
        document.Header = new HeaderFooter("see cat here");
        var request = new FindReplaceReplaceRequest("cat", "tiger", NoOptions, ReplaceFormat: Bold);
        var header = document.Header.Paragraphs[0];

        var replacement = FindReplaceStoryPlanner.PlanReplacementAt(document, header, 4, 3, request);

        replacement.Should().NotBeNull();
        replacement!.Value.Command!.Apply(new Context(document));
        header.PlainText.Should().Be("see tiger here");
        header.Runs.Single(run => run.Text == "tiger").Formatting.Bold.Should().BeTrue();
    }

    [Fact]
    public void PlanReplacementAt_RefusesABodyParagraphAndAStaleSpan()
    {
        var document = Doc(new Paragraph("see cat here"));
        document.Header = new HeaderFooter("see cat here");
        var request = new FindReplaceReplaceRequest("cat", "tiger", NoOptions);

        FindReplaceStoryPlanner.PlanReplacementAt(document, (Paragraph)document.Blocks[0], 4, 3, request)
            .Should().BeNull("a body paragraph is not a story paragraph");
        FindReplaceStoryPlanner.PlanReplacementAt(document, document.Header.Paragraphs[0], 0, 3, request)
            .Should().BeNull("'see' is not a match of 'cat'");
    }
}
