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
            .BeEquivalentTo(Enum.GetValues<DrawingShapeEffectPreset>());
        options.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.LabelKey));
        options.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.DescriptionKey));
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
