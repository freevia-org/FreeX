using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r522: the third pass over one class in one file, and the reason there was a third pass is the
/// interesting part. r520 fixed the sites reached through <c>TableAt</c>; r521 fixed the sites reached
/// through <c>ParagraphAt</c>. Both sweeps searched for a HELPER NAME. Six more casts were written
/// INLINE -- <c>((Paragraph)context.Document.Blocks[paragraphIndex]).Runs[runIndex]</c> -- so neither
/// name-based sweep saw them. Searching for the pattern instead of the helper found them immediately.
///
/// <para>Two of the six carried a second unguarded index on top of the cast: <c>.Runs[runIndex]</c>
/// with no bounds check, so a stale run index threw even when the block really was a paragraph.</para>
/// </summary>
public class R522_RunCommandsTolerateAStaleBlockIndexTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static (TextDocument doc, DocumentCommandBus bus) NewWithTable()
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(Table.Create(2, 2));
        return (doc, new DocumentCommandBus(new Context(doc)));
    }

    [Fact]
    public void SetRunFormatting_OnANonParagraphBlock_DoesNotThrow()
    {
        var (doc, bus) = NewWithTable();

        var act = () => bus.Execute(new SetRunFormattingCommand(0, 0, new RunFormatting()));

        act.Should().NotThrow();
        doc.Blocks[0].Should().BeOfType<Table>();
    }

    [Fact]
    public void SetRunFormatting_WithAStaleRunIndex_DoesNotThrow()
    {
        // The second unguarded index: the block IS a paragraph, but the run is long gone.
        var doc = new TextDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("one run"));
        var bus = new DocumentCommandBus(new Context(doc));

        var act = () => bus.Execute(new SetRunFormattingCommand(0, 99, new RunFormatting()));

        act.Should().NotThrow();
    }

    [Fact]
    public void ReplaceParagraphRuns_OnANonParagraphBlock_DoesNotThrow()
    {
        var (_, bus) = NewWithTable();

        var act = () => bus.Execute(new ReplaceParagraphRunsCommand(0, _ => { }));

        act.Should().NotThrow();
    }

    [Fact]
    public void FormatParagraphRuns_OnANonParagraphBlock_DoesNotThrow()
    {
        var (_, bus) = NewWithTable();

        var act = () => bus.Execute(new FormatParagraphRunsCommand(0, formatting => formatting));

        act.Should().NotThrow();
    }

    [Fact]
    public void AValidRunIsStillFormattedAndUndone()
    {
        // The guards must not have turned working commands into silent no-ops.
        var doc = new TextDocument();
        doc.Blocks.Clear();
        var paragraph = new Paragraph("text");
        doc.Blocks.Add(paragraph);
        var bus = new DocumentCommandBus(new Context(doc));

        var bold = new RunFormatting { Bold = true };
        bus.Execute(new SetRunFormattingCommand(0, 0, bold));
        paragraph.Runs[0].Formatting.Bold.Should().BeTrue();

        bus.Undo();
        paragraph.Runs[0].Formatting.Bold.Should().BeFalse();
    }
}
