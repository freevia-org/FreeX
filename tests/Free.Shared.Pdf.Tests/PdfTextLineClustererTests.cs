using FluentAssertions;
using Free.Shared.Pdf.Import;

namespace Free.Shared.Pdf.Tests;

public sealed class PdfTextLineClustererTests
{
    [Fact]
    public void CalculateModalFontSize_UsesHalfPointBucketsAndIgnoresNonPositiveSizes()
    {
        var glyphs = new[]
        {
            new TestGlyph("A", 100, 0, 12.26),
            new TestGlyph("B", 100, 1, 12.28),
            new TestGlyph("C", 100, 2, 12.24),
            new TestGlyph("D", 100, 3, 0),
            new TestGlyph("E", 100, 4, -4),
        };

        var modalSize = PdfTextLineClusterer.CalculateModalFontSize(glyphs, GetMetrics);

        modalSize.Should().Be(12.5);
    }

    [Fact]
    public void Cluster_UsesRunningMeanBaselineSoGradualDriftRemainsOneLine()
    {
        // FreeX used a running mean while FreeW anchored to the first glyph. The shared policy keeps the
        // running mean so gradual baseline drift cannot split one visual line.
        var glyphs = new List<TestGlyph> { new("A", 100, 0, 12) };
        glyphs.AddRange(Enumerable.Range(1, 10).Select(index => new TestGlyph("B", 94, index, 12)));
        glyphs.Add(new TestGlyph("C", 89, 11, 12));

        var result = PdfTextLineClusterer.Cluster(glyphs, GetMetrics);

        result.BaselineTolerance.Should().Be(6);
        var line = result.Lines.Should().ContainSingle().Subject;
        line.BaselineY.Should().BeApproximately((100 + (10 * 94) + 89) / 12.0, 0.0001);
        line.Glyphs.Should().HaveCount(12);
    }

    [Fact]
    public void Cluster_OrdersLinesTopToBottomAndGlyphsLeftToRight()
    {
        var glyphs = new[]
        {
            new TestGlyph("D", 80, 20, 12.26),
            new TestGlyph("B", 100, 20, 12.28),
            new TestGlyph("C", 80, 10, 12.27),
            new TestGlyph("A", 100, 10, 12.27),
        };

        var result = PdfTextLineClusterer.Cluster(glyphs, GetMetrics);

        result.Lines.Select(line => string.Concat(line.Glyphs.Select(glyph => glyph.Text)))
            .Should().Equal("AB", "CD");
        result.Lines.Should().OnlyContain(line => line.ModalFontSize == 12.5);
    }

    [Fact]
    public void Cluster_FiltersEmptyTextWithoutDroppingPageFontMeasurement()
    {
        var glyphs = new[]
        {
            new TestGlyph(string.Empty, 100, 0, 18),
            new TestGlyph(string.Empty, 100, 1, 18),
            new TestGlyph("A", 100, 2, 12),
        };

        var result = PdfTextLineClusterer.Cluster(glyphs, GetMetrics);

        result.ModalFontSize.Should().Be(18);
        result.BaselineTolerance.Should().Be(9);
        result.Lines.Should().ContainSingle()
            .Which.Glyphs.Should().ContainSingle()
            .Which.Text.Should().Be("A");
    }

    private static PdfTextGlyphMetrics GetMetrics(TestGlyph glyph) =>
        new(glyph.Text, glyph.BaselineY, glyph.Left, glyph.FontSize);

    private sealed record TestGlyph(string Text, double BaselineY, double Left, double FontSize);
}
