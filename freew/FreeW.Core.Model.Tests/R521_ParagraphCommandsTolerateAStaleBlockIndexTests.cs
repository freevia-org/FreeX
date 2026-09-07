using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r521: the sibling of r520. Having guarded the table commands' block access, the paragraph
/// commands beside them still used the same unchecked cast,
/// <c>(Paragraph)context.Document.Blocks[index]</c>, duplicated privately in two command classes.
///
/// <para>This one sits in a worse place than the table version did. SetParagraphFormattingCommand and
/// SetParagraphStyleCommand call it from <c>HasEffect</c>, which the bus consults BEFORE Apply
/// precisely to decide whether the command is a no-op. So the gate that exists to answer "would this
/// change anything?" threw instead of answering, on a document where the honest answer is simply
/// no.</para>
///
/// <para>Fixing r520 and leaving this is the exact drift this review keeps finding; the sweep of my
/// own fix is what surfaced it.</para>
/// </summary>
public class R521_ParagraphCommandsTolerateAStaleBlockIndexTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static (TextDocument doc, DocumentCommandBus bus) New()
    {
        var doc = new TextDocument();
        return (doc, new DocumentCommandBus(new Context(doc)));
    }

    private static TextDocument WithSingleTable()
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(Table.Create(2, 2));
        return doc;
    }

    [Fact]
    public void HasEffect_OnATableBlock_AnswersFalseInsteadOfThrowing()
    {
        var doc = WithSingleTable();
        var context = new Context(doc);

        // The gate itself was the throw site: it asked the block for .Formatting after casting.
        var formatting = new Func<bool>(() =>
            new SetParagraphFormattingCommand(0, new ParagraphFormatting()).HasEffect(context));
        var style = new Func<bool>(() => new SetParagraphStyleCommand(0, "Heading1").HasEffect(context));

        formatting.Should().NotThrow();
        style.Should().NotThrow();
        formatting().Should().BeFalse("a block that is not a paragraph cannot be reformatted");
        style().Should().BeFalse();
    }

    [Fact]
    public void ExecutingAgainstANonParagraphBlockLeavesTheDocumentAlone()
    {
        var (doc, bus) = New();
        doc.Blocks.Clear();
        doc.Blocks.Add(Table.Create(2, 2));

        var act = () =>
        {
            bus.Execute(new SetParagraphFormattingCommand(0, new ParagraphFormatting()));
            bus.Execute(new SetParagraphStyleCommand(0, "Heading1"));
        };

        act.Should().NotThrow();
        doc.Blocks[0].Should().BeOfType<Table>();
    }

    [Fact]
    public void BlockIndexPastTheEndDoesNotThrow()
    {
        var (doc, bus) = New();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("only block"));

        var act = () => bus.Execute(new SetParagraphStyleCommand(9, "Heading1"));

        act.Should().NotThrow();
        doc.Blocks.Should().HaveCount(1);
    }

    [Fact]
    public void AValidParagraphStillFormatsAndUndoes()
    {
        // The guard must not have turned a working command into a silent no-op.
        var (doc, bus) = New();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("text"));

        bus.Execute(new SetParagraphStyleCommand(0, "Heading1"));
        ((Paragraph)doc.Blocks[0]).StyleId.Should().Be("Heading1");

        bus.Undo();
        ((Paragraph)doc.Blocks[0]).StyleId.Should().NotBe("Heading1");
    }
}
