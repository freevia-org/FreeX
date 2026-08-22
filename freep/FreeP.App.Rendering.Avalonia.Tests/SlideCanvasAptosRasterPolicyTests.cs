using FreeP.App.Rendering.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class SlideCanvasAptosRasterPolicyTests
{
    [Fact]
    public void UsesImportedAptosBodyFont_MatchesOnlyTheMeasuredNoAutofitBody()
    {
        var matching = CreateLayout(8);

        SlideCanvas.UsesImportedAptosBodyFont(matching).Should().BeTrue();
        SlideCanvas.UsesImportedAptosBodyFont(CreateLayout(7)).Should().BeFalse();
        SlideCanvas.UsesImportedAptosBodyFont(
            CreateLayout(8, TextAutoFitKind.Normal)).Should().BeFalse();
        SlideCanvas.UsesImportedAptosBodyFont(
            CreateLayout(8, TextAutoFitKind.None, BulletKind.Char)).Should().BeFalse();
    }

    private static ResolvedTextLayout CreateLayout(
        int paragraphCount,
        TextAutoFitKind autoFitKind = TextAutoFitKind.None,
        BulletKind bulletKind = BulletKind.None) =>
        new()
        {
            AutoFitKind = autoFitKind,
            Paragraphs = Enumerable.Range(0, paragraphCount)
                .Select(_ => new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun
                        {
                            Text = "Office body",
                            FontFamily = "Aptos",
                            FontSizePt = 18.0,
                            Color = SrgbColor.Black
                        }
                    },
                    BulletKind = bulletKind
                })
                .ToArray()
        };
}
