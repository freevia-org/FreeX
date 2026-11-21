using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class PrintPageReferencePlannerTests
{
    private static TextDocument DocumentWith(params Block[] blocks)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.AddRange(blocks);
        return document;
    }

    [Fact]
    public void Build_shows_the_laid_out_page_of_the_bookmark_not_the_cached_number()
    {
        var document = DocumentWith(
            new Paragraph("Intro"),
            new Paragraph("Chapter") { BookmarkName = "_Toc1" },
            new Paragraph("Later") { BookmarkName = "_Toc2" });

        var resolve = PrintPageReferencePlanner.Build(document, [0, 2, 4], pageCount: 5);

        resolve(" PAGEREF _Toc1 \\h ", "40").Should().Be("3");
        resolve(" PAGEREF _Toc2 \\h ", "40").Should().Be("5");
    }

    [Fact]
    public void Build_keeps_the_cached_text_for_an_unknown_bookmark()
    {
        var document = DocumentWith(new Paragraph("Body"));
        var resolve = PrintPageReferencePlanner.Build(document, [0], pageCount: 1);

        resolve(" PAGEREF missing \\h ", "7").Should().Be("7");
    }

    [Fact]
    public void Marker_is_only_given_to_unlocked_page_references()
    {
        PrintPageReferencePlanner.MarkerFor(new ComplexField(" PAGEREF a \\h ")).Should().NotBeNull();
        PrintPageReferencePlanner.MarkerFor(new ComplexField(" REF a ")).Should().BeNull();
        PrintPageReferencePlanner.MarkerFor(new ComplexField(" PAGEREF a ").WithLock(true)).Should().BeNull();
        PrintPageReferencePlanner.InstructionOf(PrintPageReferencePlanner.MarkerFor(new ComplexField(" PAGEREF a ")))
            .Should().Be(" PAGEREF a ");
        PrintPageReferencePlanner.InstructionOf("freew-fn-3").Should().BeNull();
    }
}
