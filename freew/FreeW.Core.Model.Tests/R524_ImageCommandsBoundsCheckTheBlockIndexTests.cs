using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r524: applies the sharper signature r523 arrived at -- an index CAPTURED at construction and
/// dereferenced later -- instead of searching for casts. That finds a class no cast-based sweep could
/// see, because the code uses a pattern match rather than a cast:
/// <c>Blocks[paragraphIndex] is Paragraph p</c> type-checks safely but still INDEXES first, so an
/// out-of-range block index throws before <c>is</c> is ever evaluated.
///
/// <para>What makes these an oversight rather than an invariant is visible in the expression itself.
/// The image and shape accessors read
/// <c>Blocks[paragraphIndex] is Paragraph p &amp;&amp; runIndex >= 0 &amp;&amp; runIndex &lt; p.Runs.Count</c>:
/// the author was demonstrably thinking about bounds, checked the RUN index, and missed the BLOCK
/// index in the same line. 38 sites carried that half-guard.</para>
/// </summary>
public class R524_ImageCommandsBoundsCheckTheBlockIndexTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static (TextDocument doc, DocumentCommandBus bus) NewSingleParagraph()
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("text"));
        return (doc, new DocumentCommandBus(new Context(doc)));
    }

    [Fact]
    public void SetImageAltText_WithABlockIndexPastTheEnd_DoesNotThrow()
    {
        var (_, bus) = NewSingleParagraph();

        var act = () => bus.Execute(new SetImageAltTextCommand(42, 0, "alt"));

        act.Should().NotThrow();
    }

    [Fact]
    public void SetImageRotation_WithABlockIndexPastTheEnd_DoesNotThrow()
    {
        var (_, bus) = NewSingleParagraph();

        var act = () => bus.Execute(new SetImageRotationCommand(42, 0, 90, false, false));

        act.Should().NotThrow();
    }

    [Fact]
    public void SetImageSize_WithABlockIndexPastTheEnd_DoesNotThrow()
    {
        var (_, bus) = NewSingleParagraph();

        var act = () => bus.Execute(new SetImageSizeCommand(42, 0, 10, 10));

        act.Should().NotThrow();
    }

    [Fact]
    public void ANegativeBlockIndexIsRejectedToo()
    {
        // The half-guard checked runIndex >= 0 but never the lower bound of the block index.
        var (_, bus) = NewSingleParagraph();

        var act = () => bus.Execute(new SetImageAltTextCommand(-1, 0, "alt"));

        act.Should().NotThrow();
    }

    [Fact]
    public void AValidTargetIsUntouchedByTheGuard()
    {
        // A paragraph with no image: the command must still run and simply find nothing to change,
        // rather than being short-circuited by the new bounds check.
        var (doc, bus) = NewSingleParagraph();

        var act = () => bus.Execute(new SetImageAltTextCommand(0, 0, "alt"));

        act.Should().NotThrow();
        doc.Blocks.Should().HaveCount(1);
    }
}
