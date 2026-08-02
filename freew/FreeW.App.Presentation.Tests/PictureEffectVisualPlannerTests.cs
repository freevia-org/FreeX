using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class PictureEffectVisualPlannerTests
{
    [Fact]
    public void ImportedShadow_UsesAuthoredColor()
    {
        var image = new InlineImage([], 1, 1)
        {
            ImportedEffects = new ShapeEffectLst
            {
                HasShadow = true,
                ShadowColorHex = "102030",
            },
        };

        PictureEffectVisualPlanner.ResolveShadowColorHex(image).Should().Be("102030");
    }

    [Fact]
    public void PresetShadow_KeepsBlackFallbackWhenNoImportedShadowExists()
    {
        var image = new InlineImage([], 1, 1)
        {
            ImportedEffects = new ShapeEffectLst
            {
                HasGlow = true,
                ShadowColorHex = "102030",
            },
        };

        PictureEffectVisualPlanner.ResolveShadowColorHex(image).Should().Be("000000");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25000, 0.25)]
    [InlineData(100000, 1)]
    [InlineData(-1, 0)]
    [InlineData(100001, 1)]
    public void ImportedShadow_UsesAuthoredAlpha(int alpha, double expected)
    {
        var image = new InlineImage([], 1, 1)
        {
            ImportedEffects = new ShapeEffectLst
            {
                HasShadow = true,
                ShadowAlpha = alpha,
            },
        };

        PictureEffectVisualPlanner.ResolveShadowOpacity(image, 0.55).Should().Be(expected);
    }

    [Fact]
    public void PresetShadow_KeepsExistingFallbackWhenNoImportedShadowExists()
    {
        var image = new InlineImage([], 1, 1)
        {
            ImportedEffects = new ShapeEffectLst { HasGlow = true },
        };

        PictureEffectVisualPlanner.ResolveShadowOpacity(image, 0.55).Should().Be(0.55);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25000, 0.25)]
    [InlineData(100000, 1)]
    [InlineData(-1, 0)]
    [InlineData(100001, 1)]
    public void ImportedGlow_UsesAuthoredAlpha(int alpha, double expected)
    {
        var image = new InlineImage([], 1, 1)
        {
            ImportedEffects = new ShapeEffectLst
            {
                HasGlow = true,
                GlowAlpha = alpha,
            },
        };

        PictureEffectVisualPlanner.ResolveGlowOpacity(image).Should().Be(expected);
    }

    [Fact]
    public void PresetGlow_KeepsExistingFallbackWhenNoImportedGlowExists()
    {
        var image = new InlineImage([], 1, 1)
        {
            ImportedEffects = new ShapeEffectLst
            {
                HasShadow = true,
                GlowAlpha = 25000,
            },
        };

        PictureEffectVisualPlanner.ResolveGlowOpacity(image)
            .Should().Be(PictureEffectVisualPlanner.PresetGlowOpacity);
    }
}
