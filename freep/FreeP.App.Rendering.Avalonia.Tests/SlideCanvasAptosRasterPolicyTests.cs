using FreeP.App.Rendering.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class SlideCanvasAptosRasterPolicyTests
{
    [Fact]
    public void UsesFixedSizeAptosBodyFallback_MatchesSemanticRenderingRoute()
    {
        SlideCanvas.UsesFixedSizeAptosBodyFallback(CreateLayout(8)).Should().BeTrue();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(2, bold: true)).Should().BeTrue();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, TextAutoFitKind.Normal)).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, bulletKind: BulletKind.Char)).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, fontFamily: "Calibri")).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, fontSizePt: 24.0)).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, columnCount: 2)).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(CreateLayout(0)).Should().BeFalse();
    }

    private static ResolvedTextLayout CreateLayout(
        int paragraphCount,
        TextAutoFitKind autoFitKind = TextAutoFitKind.None,
        BulletKind bulletKind = BulletKind.None,
        string fontFamily = "Aptos",
        double fontSizePt = 18.0,
        bool bold = false,
        int columnCount = 1) =>
        new()
        {
            AutoFitKind = autoFitKind,
            ColumnCount = columnCount,
            Paragraphs = Enumerable.Range(0, paragraphCount)
                .Select(_ => new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun
                        {
                            Text = "Office body",
                            FontFamily = fontFamily,
                            FontSizePt = fontSizePt,
                            Bold = bold,
                            Color = SrgbColor.Black
                        }
                    },
                    BulletKind = bulletKind
                })
                .ToArray()
        };
}
