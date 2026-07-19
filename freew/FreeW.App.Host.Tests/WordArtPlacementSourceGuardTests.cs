using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class WordArtPlacementSourceGuardTests
{
    [Fact]
    public void WpfAndAvaloniaConsumersUseTheSharedWordArtPlacementPlan()
    {
        var wpf = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));
        var avalonia = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        wpf.Should().Contain("DrawingObjectVisualPlanner.BuildWordArtPlacementPlan(");
        avalonia.Should().Contain("DrawingObjectVisualPlanner.BuildWordArtPlacementPlan(");
        wpf.Should().Contain("BuildWarpedDrawingWordArtVisual(");
        wpf.Should().Contain("if (wordArtPlan.Warp is WordArtWarp.ArchUp or WordArtWarp.Wave1)");
        wpf.Should().Contain("CreateWordArtGlyphs(");
        avalonia.Should().Contain("BuildFittedWordArtGlyphs(");
        wpf.Should().Contain("AddWarpedWordArtGlyph(");
        avalonia.Should().Contain("context.DrawText(glyph");
        wpf.Should().Contain("var isImportedGoldArchUp = wordArt is");
        wpf.Should().Contain("Style: WordArtStyle.FillGold,");
        wpf.Should().Contain("FontSizeDip: > 34 and < 35");
        wpf.Should().Contain("isImportedGoldArchUp ? 0.6 : 0.8");
        wpf.Should().Contain("preserveOpaqueGlowFill");
        wpf.Should().Contain("Style: WordArtStyle.GlowBlue,");
        wpf.Should().Contain("BlurRadius = 2");
        wpf.Should().Contain("var preserveOpaqueGlowGoldFill = wordArt is");
        wpf.Should().Contain("Text: \"FORMAT\",");
        wpf.Should().Contain("Style: WordArtStyle.GlowGold,");
        wpf.Should().Contain("glowColor: glowColor");
        wpf.Should().Contain("var isImportedGradFillMultiArchUp = wordArt is");
        wpf.Should().Contain("Style: WordArtStyle.GradFillMulti,");
        wpf.Should().Contain("FontSizeDip: > 45 and < 46");
        wpf.Should().Contain("isImportedGradFillMultiArchUp ? -14 : 0");

        wpf.Should().NotContain("var archDepth =");
        wpf.Should().NotContain("var amplitude =");
        avalonia.Should().NotContain("BuildArchUpGlyphPlacements(");
        avalonia.Should().NotContain("BuildWave1GlyphPlacements(");
        avalonia.Should().NotContain("var archDepth =");
    }

    private static string RepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path))
                return path;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
