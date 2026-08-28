using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class Wave196Deck17Slide02ResolvedModelTests
{
    [Fact]
    public void Deck17Slide02_BodyResolvesToTheGeneralFixedSizeAptosFallbackRoute()
    {
        var corpusPath = TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "tools", "FreeP.RenderCompare", "corpus", "17-bullets-autofit.pptx");
        var presentation = FreeP.Core.IO.PptxPackageReader.Read(corpusPath);

        var body = SlideCompositor.Compose(presentation, presentation.Slides[1])
            .OfType<DrawOp.Shape>()
            .Select(shape => shape.Text)
            .Single(text => text is { Paragraphs.Count: 8 })!;

        body.AutoFitKind.Should().Be(TextAutoFitKind.None);
        body.ColumnCount.Should().Be(1);
        body.HasStoredFontScale.Should().BeFalse();
        body.Paragraphs.Should().OnlyContain(paragraph =>
            paragraph.BulletKind == BulletKind.None
            && paragraph.Runs.Count == 1
            && string.Equals(paragraph.Runs[0].FontFamily, "Aptos", StringComparison.OrdinalIgnoreCase)
            && Math.Abs(paragraph.Runs[0].FontSizePt - 18.0) < 0.01
            && !paragraph.Runs[0].Bold
            && !paragraph.Runs[0].Italic);
    }
}
