using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWRibbonFloatingObjectCommandFactoryTests
{
    [Fact]
    public void PositionCommandOwnsGatingParsingAndDialogFallback()
    {
        var selected = false;
        FreeWRibbonObjectPositionInput? applied = null;
        var dialogs = 0;
        var preparations = 0;
        var command = FreeWRibbonFloatingObjectCommandFactory.CreatePosition(new(
            () => selected,
            position => applied = position,
            (_, _) => { },
            () => dialogs++,
            PrepareExecution: () => preparations++));

        command.GetState().IsEnabled.Should().BeFalse();
        command.Execute(RibbonCommandContext.ForSelectedValue("12,24,Page,Paragraph"));
        applied.Should().BeNull();
        dialogs.Should().Be(0);
        preparations.Should().Be(0);

        selected = true;
        command.GetState().IsEnabled.Should().BeTrue();
        command.Execute(RibbonCommandContext.ForSelectedValue("12,24,Page,Paragraph"));
        applied.Should().Be(new FreeWRibbonObjectPositionInput(
            12,
            24,
            HorizontalAnchor.Page,
            VerticalAnchor.Paragraph));

        command.Execute(RibbonCommandContext.Empty);
        dialogs.Should().Be(1);
        preparations.Should().Be(2);
    }

    [Fact]
    public void SizeCommandOwnsParsingAndOnlyOpensDialogForBlankInput()
    {
        (double Width, double Height)? applied = null;
        var dialogs = 0;
        var command = FreeWRibbonFloatingObjectCommandFactory.CreateSize(new(
            () => true,
            _ => { },
            (width, height) => applied = (width, height),
            OpenSizeDialog: () => dialogs++));

        command.Execute(RibbonCommandContext.ForSelectedValue("144,72"));
        applied.Should().Be((144d, 72d));

        command.Execute(RibbonCommandContext.ForSelectedValue("invalid"));
        dialogs.Should().Be(0);

        command.Execute(RibbonCommandContext.Empty);
        dialogs.Should().Be(1);
    }

    [Fact]
    public void PresetCommandsShareSelectionPolicy()
    {
        var selected = false;
        var positions = new List<FreeWRibbonObjectPositionInput>();
        var sizes = new List<(double Width, double Height)>();
        var ports = new FreeWRibbonFloatingObjectCommandPorts(
            () => selected,
            positions.Add,
            (width, height) => sizes.Add((width, height)));
        var position = new FreeWRibbonObjectPositionInput(
            0,
            0,
            HorizontalAnchor.Margin,
            VerticalAnchor.Page);
        var positionCommand = FreeWRibbonFloatingObjectCommandFactory.CreatePositionPreset(ports, position);
        var sizeCommand = FreeWRibbonFloatingObjectCommandFactory.CreateSizePreset(ports, 216, 108);

        positionCommand.Execute(RibbonCommandContext.Empty);
        sizeCommand.Execute(RibbonCommandContext.Empty);
        positions.Should().BeEmpty();
        sizes.Should().BeEmpty();

        selected = true;
        positionCommand.Execute(RibbonCommandContext.Empty);
        sizeCommand.Execute(RibbonCommandContext.Empty);
        positions.Should().Equal(position);
        sizes.Should().Equal((216d, 108d));
    }

    [Fact]
    public void EditorProfileRegistersTheFloatingPositionFamily()
    {
        var registry = new RibbonCommandRegistry();
        var positions = new List<FreeWRibbonObjectPositionInput>();
        var ports = new FreeWRibbonFloatingObjectCommandPorts(
            () => true,
            positions.Add,
            (_, _) => { });
        IFreeWRibbonFloatingPositionPreset[] presets =
        [
            new TestPositionPreset(
                "page-top",
                12,
                24,
                HorizontalAnchor.Page,
                VerticalAnchor.Page),
        ];

        FreeWRibbonEditorExecutionProfile.RegisterFloatingPositionCommands(
            registry,
            "image",
            ports,
            presets);

        registry.TryGet("freew.image-position", out var positionCommand).Should().BeTrue();
        positionCommand.Should().BeAssignableTo<IRibbonStatefulCommand>();
        registry.TryGet("freew.image-position-page-top", out var presetCommand).Should().BeTrue();

        presetCommand!.Execute(RibbonCommandContext.Empty);

        positions.Should().Equal(new FreeWRibbonObjectPositionInput(
            12,
            24,
            HorizontalAnchor.Page,
            VerticalAnchor.Page));
    }

    private sealed record TestPositionPreset(
        string Suffix,
        double HorizontalOffsetPt,
        double VerticalOffsetPt,
        HorizontalAnchor HorizontalAnchor,
        VerticalAnchor VerticalAnchor) : IFreeWRibbonFloatingPositionPreset;
}
