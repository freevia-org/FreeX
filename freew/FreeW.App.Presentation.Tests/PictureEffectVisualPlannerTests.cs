using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class PictureEffectVisualPlannerTests
{
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
