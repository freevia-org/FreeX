using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class PictureEffectVisualPlannerTests
{
    [Fact]
    public void ImportedReflection_UsesAuthoredStartAlphaAndDistance()
    {
        var image = new InlineImage([], 1, 1)
        {
            ReflectionPreset = 1,
            ImportedEffects = new ShapeEffectLst
            {
                HasReflection = true,
                ReflectionStartAlpha = 35000,
                ReflectionStartPosition = 20000,
                ReflectionEndAlpha = 10000,
                ReflectionEndPosition = 80000,
                ReflectionDist = 38100,
            },
        };

        var plan = PictureEffectVisualPlanner.BuildReflectionPlan(image);

        plan.Should().NotBeNull();
        plan!.Opacity.Should().Be(0.35);
        plan.StartPosition.Should().Be(0.2);
        plan.EndOpacity.Should().Be(0.1);
        plan.EndPosition.Should().Be(0.8);
        plan.DistanceDip.Should().Be(4);
    }

    [Fact]
    public void PresetReflectionPlan_PreservesExistingOpacityAndDistance()
    {
        var plan = PictureEffectVisualPlanner.BuildReflectionPlan(new InlineImage([], 1, 1)
        {
            ReflectionPreset = 2,
        });

        plan.Should().NotBeNull();
        plan!.Opacity.Should().Be(0.5);
        plan.StartPosition.Should().Be(0);
        plan.EndOpacity.Should().Be(0);
        plan.EndPosition.Should().Be(1);
        plan.DistanceDip.Should().BeApproximately(5.3333333333, 0.000001);
    }

    [Fact]
    public void ImportedShadow_BuildsExactDrawingMlGeometry()
    {
        var image = new InlineImage([], 1, 1)
        {
            ShadowPreset = 2,
            ImportedEffects = new ShapeEffectLst
            {
                HasShadow = true,
                ShadowBlurRad = 76200,
                ShadowDist = 63500,
                ShadowDir = 5400000,
                ShadowAlpha = 25000,
                ShadowColorHex = "102030",
            },
        };

        var plan = PictureEffectVisualPlanner.BuildShadowPlan(image);

        plan.BlurPoints.Should().Be(6);
        plan.DistancePoints.Should().Be(5);
        plan.DirectionDegrees.Should().Be(90);
        plan.OffsetXPoints.Should().BeApproximately(0, 0.000001);
        plan.OffsetYPoints.Should().Be(-5);
        plan.Opacity.Should().Be(0.25);
        plan.ColorHex.Should().Be("102030");
    }

    [Fact]
    public void PresetShadowPlan_PreservesExistingHostGeometry()
    {
        var plan = PictureEffectVisualPlanner.BuildShadowPlan(new InlineImage([], 1, 1)
        {
            ShadowPreset = 2,
        });

        plan.BlurPoints.Should().Be(6);
        plan.DistancePoints.Should().Be(5);
        plan.DirectionDegrees.Should().Be(315);
        plan.OffsetXPoints.Should().Be(5);
        plan.OffsetYPoints.Should().Be(5);
        plan.Opacity.Should().Be(0.55);
        plan.ColorHex.Should().Be("000000");
    }

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
