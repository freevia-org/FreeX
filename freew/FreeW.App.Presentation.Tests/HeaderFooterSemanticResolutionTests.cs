using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class HeaderFooterSemanticResolutionTests
{
    [Theory]
    [InlineData(32, 42)]
    [InlineData(28, 37)]
    [InlineData(36, 48)]
    public void InlineHeaderImageRasterHeightUsesWordsLowerPixelBound(double points, double expectedDip)
    {
        HeaderFooterVisualPlanner.ResolveInlineHeaderImageRasterHeightDip(
                PageLayout.PointsToDip(points))
            .Should().Be(expectedDip);
    }

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

    // Regression for freew-avalonia-fields F1: a locked header/footer field (Ctrl+F11) must stay frozen
    // at its cached text instead of recomputing to the live value on every re-render. Covers both lock
    // forms -- the simple RunFieldKind.FieldLocked flag and the ComplexField.IsLocked wrapper -- matching
    // the WPF host's BuildFieldRun/ResolveComplexFieldText guards (DocumentView.cs ~12773 and ~12933).
    [Fact]
    public void ResolveFieldTextFreezesLockedSimpleAndComplexFieldsAtCachedText()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Author = "Ada";
        var context = new HeaderFooterFieldResolutionContext(
            document,
            PageNumberText: "iv",
            PageCount: 12,
            SectionOrdinal: 4,
            SectionPageCount: 7,
            EvaluatedAt: new DateTime(2026, 8, 28, 9, 0, 0));

        var lockedSimple = new Run("cached author")
        {
            FieldKind = RunFieldKind.Author,
            FieldLocked = true
        };
        HeaderFooterVisualPlanner.ResolveFieldText(lockedSimple, context)
            .Should().Be("cached author", "a locked RunFieldKind field must not recompute from live document state");

        var lockedComplex = new Run("cached section")
        {
            ComplexField = new ComplexField(" SECTION \\* ROMAN ").WithLock(true)
        };
        HeaderFooterVisualPlanner.ResolveFieldText(lockedComplex, context)
            .Should().Be("cached section", "a locked ComplexField must not recompute (SECTION/temporal-picture) from live state");
    }

    // Sibling no-regression: unlocked fields of both forms must still resolve live, matching the
    // existing coverage in ResolveLineTextOwnsSimpleComplexAndPlainRunProjection.
    [Fact]
    public void ResolveFieldTextStillResolvesUnlockedSimpleAndComplexFieldsLive()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Author = "Ada";
        var context = new HeaderFooterFieldResolutionContext(
            document,
            PageNumberText: "iv",
            PageCount: 12,
            SectionOrdinal: 4,
            SectionPageCount: 7);

        var unlockedSimple = new Run("cached author") { FieldKind = RunFieldKind.Author };
        HeaderFooterVisualPlanner.ResolveFieldText(unlockedSimple, context).Should().Be("Ada");

        var unlockedComplex = new Run("cached section")
        {
            ComplexField = new ComplexField(" SECTION \\* ROMAN ")
        };
        HeaderFooterVisualPlanner.ResolveFieldText(unlockedComplex, context).Should().Be("IV");
    }
}
