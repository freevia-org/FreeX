using Free.Shared.Theme;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePVisualPaletteTests
{
    [Fact]
    public void DefaultPalettePreservesPresentationSpecificColors()
    {
        var palette = FreePVisualPalettes.Default;

        palette.SelectedCardSurface.ToHex().Should().Be("#FFF6F2");
        palette.SelectedCardSurface.ToHex().Should().NotBe(BrandThemes.FreeP.Colors.AccentSoft.ToHex());
        palette.SelectedCommentSurface.ToHex().Should().Be("#F4ECE8");
        palette.AnimationText.ToHex().Should().Be("#222222");
        palette.AnimationSelectedSurface.ToHex().Should().Be("#FFE0D6");
        palette.AnimationDanger.ToHex().Should().Be("#C02020");
        palette.PresenterSurface.ToHex().Should().Be("#1E222A");
        palette.PresenterPanelSurface.ToHex().Should().Be("#2D323D");
        palette.PresenterSecondarySurface.ToHex().Should().Be("#262B35");
        palette.PresenterBorder.ToHex().Should().Be("#505766");
        palette.PresenterMutedText.ToHex().Should().Be("#AAB2C2");
    }

    [Fact]
    public void RenderersConsumeSharedPaletteInsteadOfDuplicatingPresentationColors()
    {
        string[] rendererFiles =
        [
            "MainWindow.cs",
            "PresenterViewWindow.cs",
            "ChartDataDialog.cs",
            "HyperlinkDialog.cs",
            "CustomShowDialog.cs",
        ];

        foreach (var project in new[] { "FreeP.App.Host", "FreeP.App.Avalonia" })
        {
            foreach (var file in rendererFiles)
            {
                var source = TestWorkspaceFileLocator.ReadAllText("freep", project, file);
                source.Should().NotContain("Color.FromRgb(0xB7, 0x47, 0x2A)");
                source.Should().NotContain("Color.FromRgb(0xFF, 0xF6, 0xF2)");
                source.Should().NotContain("Color.FromRgb(30, 34, 42)");
                source.Should().NotContain("Color.FromRgb(45, 50, 61)");
                source.Should().NotContain("Color.FromRgb(38, 43, 53)");
                source.Should().NotContain("Color.FromRgb(80, 87, 102)");
                source.Should().NotContain("Color.FromRgb(170, 178, 194)");
            }
        }

        TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Host", "PresenterViewWindow.cs")
            .Should().Contain("FreePBrushes.PresenterSurface");
        TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Avalonia", "PresenterViewWindow.cs")
            .Should().Contain("FreePBrushes.PresenterSurface");
    }
}
