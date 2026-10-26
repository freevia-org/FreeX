using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r525: found by the tripwire added in r523/r525 rather than by a sweep -- which is the point of
/// having written it. DeleteParagraphCommand captures a block index and, in Apply, both READS
/// <c>Blocks[index]</c> and calls <c>RemoveAt(index)</c> with no bounds check; Revert then calls
/// <c>Insert(index, ...)</c>, which throws once the document has fewer blocks than when the command
/// was created.
///
/// <para>r524's scan listed this very line in its first output and I moved on to the pattern-match
/// shape without coming back to it. The guard caught what the reviewer forgot.</para>
/// </summary>
public class R525_DeleteParagraphTolerateStaleIndexTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static (TextDocument doc, DocumentCommandBus bus) NewWith(int paragraphs)
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        for (var i = 0; i < paragraphs; i++)
            doc.Blocks.Add(new Paragraph($"p{i}"));
        return (doc, new DocumentCommandBus(new Context(doc)));
    }

    [Fact]
    public void DeletingPastTheEndDoesNotThrow()
    {
        var (doc, bus) = NewWith(2);

        var act = () => bus.Execute(new DeleteParagraphCommand(9));

        act.Should().NotThrow();
        doc.Blocks.Should().HaveCount(2, "nothing was in range to delete");
    }

    [Fact]
    public void ANegativeIndexDoesNotThrow()
    {
        var (doc, bus) = NewWith(2);

        var act = () => bus.Execute(new DeleteParagraphCommand(-1));

        act.Should().NotThrow();
        doc.Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void UndoAfterTheDocumentShrankDoesNotThrow()
    {
        // Apply at a valid index, then let the document shrink underneath the captured index before
        // Revert runs -- the capture-then-revert gap this whole class comes from.
        var (doc, bus) = NewWith(4);
        bus.Execute(new DeleteParagraphCommand(3));
        doc.Blocks.Should().HaveCount(3);

        doc.Blocks.Clear();

        var act = () => bus.Undo();

        act.Should().NotThrow();
    }

    [Fact]
    public void AValidDeleteStillWorksAndUndoRestoresIt()
    {
        var (doc, bus) = NewWith(3);

        bus.Execute(new DeleteParagraphCommand(1));
        doc.Blocks.Should().HaveCount(2);

        bus.Undo();
        doc.Blocks.Should().HaveCount(3);
        ((Paragraph)doc.Blocks[1]).PlainText.Should().Be("p1");
    }
}
