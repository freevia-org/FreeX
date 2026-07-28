using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.Ribbon;

namespace FreeP.App.Host.Tests;

public sealed class ShapeShadowRibbonTests
{
    [Fact]
    public void ShapeShadowPresets_AreDefinedAndRoutedByHost()
    {
        var definition = FreePRibbon.Build();
        var illustrations = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .Single(group => group.Id == "illustrations");

        foreach (var commandId in new[]
        {
            ShapeEffectAuthoringPlanner.NoneCommandId,
            ShapeEffectAuthoringPlanner.SubtleCommandId,
            ShapeEffectAuthoringPlanner.OffsetCommandId,
        })
        {
            illustrations.Controls.Should().Contain(control => control.CommandId.Value == commandId);
        }

        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape { Id = 501, Kind = SlideShapeKind.AutoShape };
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        var editor = new EditingSession(presentation, bus);
        editor.Select(shape.Id);

        var registry = FreePRibbonCommands.Build(new RibbonStateStore(), editor);
        registry.TryGet(ShapeEffectAuthoringPlanner.SubtleCommandId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);

        shape.Effects.Should().NotBeNull();
        shape.Effects!.HasOuterShadow.Should().BeTrue();
        shape.Effects.OuterShadowAlpha.Should().Be(0x55);
    }
}
