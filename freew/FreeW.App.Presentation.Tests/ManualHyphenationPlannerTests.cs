using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public class ManualHyphenationPlannerTests
{
    [Fact]
    public void Session_ReviewsBodyAndTableWordsInOrderWithoutMutatingUntilApplied()
    {
        var document = new TextDocument();
        var bodyRun = new Run("rabbit");
        document.Blocks.Add(new Paragraph { Runs = { bodyRun } });
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Clear();
        var tableRun = new Run("hyphenation");
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(tableRun);
        document.Blocks.Add(table);

        var session = ManualHyphenationPlanner.CreateSession(document);

        session.CandidateCount.Should().Be(2);
        session.Current!.Word.Should().Be("rabbit");
        session.Current.Options.Should().ContainSingle(option => option.DisplayText == "rab-bit");
        session.Accept(3);
        session.Current!.Word.Should().Be("hyphenation");
        session.Skip();
        session.IsComplete.Should().BeTrue();
        session.AcceptedCount.Should().Be(1);
        bodyRun.Text.Should().Be("rabbit");
        tableRun.Text.Should().Be("hyphenation");

        new ApplyManualHyphenationCommand(session.Edits).Apply(new Context(document));
        bodyRun.Text.Should().Be("rab" + Hyphenator.SoftHyphen + "bit");
        tableRun.Text.Should().Be("hyphenation");
    }

    [Fact]
    public void Session_HonorsCapsSuppressionParagraphSuppressionAndExistingManualHyphens()
    {
        var document = new TextDocument();
        document.Page.DoNotHyphenateCaps = true;
        document.Blocks.Add(new Paragraph("HYPHENATION rabbit"));
        document.Blocks.Add(new Paragraph("hyphenation")
        {
            Formatting = ParagraphFormatting.Default with { SuppressAutoHyphens = true }
        });
        document.Blocks.Add(new Paragraph("rab" + Hyphenator.SoftHyphen + "bit"));

        var session = ManualHyphenationPlanner.CreateSession(document);

        session.CandidateCount.Should().Be(1);
        session.Current!.Word.Should().Be("rabbit");
    }

    [Fact]
    public void Accept_RejectsBreakPointNotProposedForCurrentWord()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("rabbit"));
        var session = ManualHyphenationPlanner.CreateSession(document);

        var act = () => session.Accept(1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Session_ReviewsAWordSplitAcrossFormattingRuns()
    {
        var document = new TextDocument();
        var first = new Run("rab");
        var second = new Run("bit");
        document.Blocks.Add(new Paragraph { Runs = { first, second } });
        var session = ManualHyphenationPlanner.CreateSession(document);

        session.CandidateCount.Should().Be(1);
        session.Current!.Word.Should().Be("rabbit");
        session.Accept(3);
        new ApplyManualHyphenationCommand(session.Edits).Apply(new Context(document));

        first.Text.Should().Be("rab" + Hyphenator.SoftHyphen);
        second.Text.Should().Be("bit");
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
