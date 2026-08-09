using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ShapeEffectsPlannerTests
{
    [Fact]
    public void CreateOptions_CoversEveryDefinedPreset()
    {
        var options = ShapeEffectsPlanner.CreateOptions();

        options.Select(o => o.Preset)
            .Should()
            .Equal(Enum.GetValues<DrawingShapeEffectPreset>());
        options.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.LabelKey));
        options.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.DescriptionKey));
    }

    [Fact]
    public void CreateResolvedPlan_ResolvesEveryDescriptorAndSelectsTheRequestedOption()
    {
        var plan = ShapeEffectsPlanner.CreateResolvedPlan(
            DrawingShapeEffectPreset.Reflection,
            key => $"resolved:{key}");

        plan.Options.Select(option => option.Label).Should().Equal(
            "resolved:ShapeEffects_None",
            "resolved:ShapeEffects_Shadow",
            "resolved:ShapeEffects_InnerShadow",
            "resolved:ShapeEffects_Reflection",
            "resolved:ShapeEffects_Glow",
            "resolved:ShapeEffects_SoftEdges",
            "resolved:ShapeEffects_Bevel",
            "resolved:ShapeEffects_ThreeDRotation");
        plan.Options.Select(option => option.Description).Should().Equal(
            "resolved:ShapeEffects_NoneDescription",
            "resolved:ShapeEffects_ShadowDescription",
            "resolved:ShapeEffects_InnerShadowDescription",
            "resolved:ShapeEffects_ReflectionDescription",
            "resolved:ShapeEffects_GlowDescription",
            "resolved:ShapeEffects_SoftEdgesDescription",
            "resolved:ShapeEffects_BevelDescription",
            "resolved:ShapeEffects_ThreeDRotationDescription");
        plan.SelectedOption.Should().BeSameAs(plan.Options[3]);
        plan.DefaultOption.Should().BeSameAs(plan.Options[0]);
    }

    [Fact]
    public void CreateResolvedPlan_DefaultsUnknownAndMissingSelectionsToNone()
    {
        var plan = ShapeEffectsPlanner.CreateResolvedPlan(
            (DrawingShapeEffectPreset)999,
            key => key);

        ShapeEffectsPlanner.DefaultPreset.Should().Be(DrawingShapeEffectPreset.None);
        plan.SelectedOption.Preset.Should().Be(DrawingShapeEffectPreset.None);
        plan.DefaultOption.Preset.Should().Be(DrawingShapeEffectPreset.None);
        plan.ResolveSelection(null).Should().BeSameAs(plan.DefaultOption);
        plan.ResolveSelection(plan.Options[2]).Should().BeSameAs(plan.Options[2]);
    }

    [Fact]
    public void CreatePlan_NormalizesSelectedPreset()
    {
        var plan = ShapeEffectsPlanner.CreatePlan((DrawingShapeEffectPreset)999);
        plan.SelectedPreset.Should().Be(DrawingShapeEffectPreset.None);
        plan.Options.Should().HaveCount(Enum.GetValues<DrawingShapeEffectPreset>().Length);
    }

    [Fact]
    public void NormalizePreset_KeepsDefinedValues()
    {
        ShapeEffectsPlanner.NormalizePreset(DrawingShapeEffectPreset.Glow)
            .Should().Be(DrawingShapeEffectPreset.Glow);
    }

    [Fact]
    public void FindOptionIndex_ReturnsZeroForUnknown()
    {
        var options = ShapeEffectsPlanner.CreateOptions();
        ShapeEffectsPlanner.FindOptionIndex(options, (DrawingShapeEffectPreset)999).Should().Be(0);
    }

    [Fact]
    public void FindOptionIndex_FindsTheReflectionRow()
    {
        var options = ShapeEffectsPlanner.CreateOptions();
        var index = ShapeEffectsPlanner.FindOptionIndex(options, DrawingShapeEffectPreset.Reflection);
        options[index].Preset.Should().Be(DrawingShapeEffectPreset.Reflection);
    }

    [Fact]
    public void BuildCommand_MapsPortablePresetToCoreCommand()
    {
        ShapeEffectsPlanner.BuildCommand(SheetId.New(), Guid.NewGuid(), DrawingShapeEffectPreset.Glow)
            .Should().BeOfType<SetDrawingShapeEffectCommand>();
    }
}
