using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class HeaderFooterSemanticResolutionTests
{
    [Fact]
    public void ResolveLineTextOwnsSimpleComplexAndPlainRunProjection()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Author = "Ada";
        var header = new HeaderFooter();
        var first = new Paragraph();
        first.Runs.Add(new Run("cached page") { FieldKind = RunFieldKind.PageNumber });
        first.Runs.Add(new Run(" of "));
        first.Runs.Add(new Run("cached pages") { FieldKind = RunFieldKind.NumPages });
        var second = new Paragraph();
        second.Runs.Add(Run.ComplexFieldRun(" SECTION \\* ROMAN ", "cached section"));
        second.Runs.Add(new Run(" by "));
        second.Runs.Add(new Run("cached author") { FieldKind = RunFieldKind.Author });
        header.Paragraphs.Add(first);
        header.Paragraphs.Add(second);

        var text = HeaderFooterVisualPlanner.ResolveLineText(
            header,
            new HeaderFooterFieldResolutionContext(
                document,
                PageNumberText: "iv",
                PageCount: 12,
                SectionOrdinal: 4,
                SectionPageCount: 7),
            lineSeparator: " | ");

        text.Should().Be("iv of 12 | IV by Ada");
        HeaderFooterVisualPlanner.ResolveFieldText(
                new Run("plain"),
                new HeaderFooterFieldResolutionContext(document, "1", 1, 1, 1))
            .Should().BeNull();
    }
}
