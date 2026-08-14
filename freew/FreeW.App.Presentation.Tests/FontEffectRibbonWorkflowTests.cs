using Free.Shared.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FontEffectRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowRegistersEveryFontEffectWithoutReplacingNativeCommands()
    {
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();
        var ports = CreatePorts(events);

        FontEffectRibbonWorkflow.Register(bindings, ports);

        FontEffectRibbonWorkflow.Actions.Should().OnlyHaveUniqueItems().And.HaveCount(10);
        foreach (var action in FontEffectRibbonWorkflow.Actions)
        {
            var route = FreeWRibbonCommandWorkflow.Routes.First(candidate => candidate.Action == action);
            bindings.TryGet(route.CommandId, out var command).Should().BeTrue(action.ToString());
            command.Should().BeSameAs(CommandFor(ports, action));
        }
    }

    [Fact]
    public void StatefulNativeFontCommandsKeepTheirStateAndExecutionBehavior()
    {
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();
        FontEffectRibbonWorkflow.Register(bindings, CreatePorts(events));

        bindings.TryGet("freew.bold", out var bold).Should().BeTrue();
        bold.Should().BeAssignableTo<IRibbonStatefulCommand>();
        ((IRibbonStatefulCommand)bold!).GetState().Should().Be(
            new RibbonCommandState(IsEnabled: true, IsChecked: true, Value: "bold"));

        Execute(bindings, "freew.bold");
        Execute(bindings, "freew.strikethrough");
        Execute(bindings, "freew.grow-font");
        events.Should().Equal("bold", "strikethrough", "grow-font");
    }

    [Fact]
    public void SharedStatePlannerMapsEveryFontEffect()
    {
        var formatting = RunFormatting.Default with
        {
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            SmallCaps = true,
            AllCaps = true,
            VerticalAlign = VerticalAlign.Superscript,
        };
        var selection = new FontDialogSelectionState(formatting);

        foreach (var kind in new[]
                 {
                     FontEffectRibbonKind.Bold,
                     FontEffectRibbonKind.Italic,
                     FontEffectRibbonKind.Underline,
                     FontEffectRibbonKind.Strikethrough,
                     FontEffectRibbonKind.SmallCaps,
                     FontEffectRibbonKind.AllCaps,
                     FontEffectRibbonKind.Superscript,
                 })
        {
            FontEffectRibbonStatePlanner.GetState(kind, selection).IsChecked.Should().BeTrue(kind.ToString());
        }

        FontEffectRibbonStatePlanner.GetState(FontEffectRibbonKind.Subscript, selection).IsChecked.Should().BeFalse();
        FontEffectRibbonStatePlanner.GetState(
                FontEffectRibbonKind.Subscript,
                new FontDialogSelectionState(formatting with { VerticalAlign = VerticalAlign.Subscript }))
            .IsChecked.Should().BeTrue();
    }

    [Fact]
    public void SharedStatePlannerTreatsMixedSelectionAsUncheckedAndHonorsAvailability()
    {
        var selection = new FontDialogSelectionState(
            RunFormatting.Default with { Bold = true, SmallCaps = true },
            BoldIndeterminate: true,
            SmallCapsIndeterminate: true);

        FontEffectRibbonStatePlanner.GetState(FontEffectRibbonKind.Bold, selection, isEnabled: false)
            .Should().Be(new RibbonCommandState(IsEnabled: false, IsChecked: false));
        FontEffectRibbonStatePlanner.GetState(FontEffectRibbonKind.SmallCaps, selection)
            .Should().Be(new RibbonCommandState(IsEnabled: true, IsChecked: false));
    }

    [Fact]
    public void SharedStatefulCommandPreparesAndExecutesOnlyWhileEnabled()
    {
        var events = new List<string>();
        var isEnabled = true;
        var command = FontEffectRibbonStatePlanner.CreateCommand(
            FontEffectRibbonKind.Bold,
            () => events.Add("execute"),
            () => new FontDialogSelectionState(RunFormatting.Default with { Bold = true }),
            () => isEnabled,
            () => events.Add("prepare"));

        command.GetState().Should().Be(new RibbonCommandState(IsEnabled: true, IsChecked: true));
        command.Execute(RibbonCommandContext.Empty);
        events.Should().Equal("prepare", "execute");

        isEnabled = false;
        command.Execute(RibbonCommandContext.Empty);
        events.Should().Equal("prepare", "execute");
    }

    [Fact]
    public void BothRenderersDelegateFontEffectMappingToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));
        var avaloniaMain = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("FontEffectRibbonWorkflow.Register(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Bold");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Strikethrough");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Superscript");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.GrowFont");
        }

        avalonia.Should().Contain("FontEffectRibbonStatePlanner.CreateCommand(");
        avalonia.Should().NotContain("Bold: new ActionRibbonCommand");
        avalonia.Should().NotContain("Italic: new ActionRibbonCommand");
        avalonia.Should().NotContain("Underline: new ActionRibbonCommand");
        avaloniaMain.Should().Contain("_editor.CaretMoved += RefreshRibbonCommandStates;");
        avaloniaMain.Should().Contain("_editor.DocumentChanged += RefreshRibbonCommandStates;");
    }

    private static IRibbonCommand CommandFor(
        FontEffectRibbonPorts ports,
        FreeWRibbonCommandAction action) => action switch
        {
            FreeWRibbonCommandAction.Bold => ports.Bold,
            FreeWRibbonCommandAction.Italic => ports.Italic,
            FreeWRibbonCommandAction.Underline => ports.Underline,
            FreeWRibbonCommandAction.Strikethrough => ports.Strikethrough,
            FreeWRibbonCommandAction.Smallcaps => ports.SmallCaps,
            FreeWRibbonCommandAction.Allcaps => ports.AllCaps,
            FreeWRibbonCommandAction.Superscript => ports.Superscript,
            FreeWRibbonCommandAction.Subscript => ports.Subscript,
            FreeWRibbonCommandAction.GrowFont => ports.GrowFont,
            FreeWRibbonCommandAction.ShrinkFont => ports.ShrinkFont,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

    private static void Execute(FreeWRibbonCommandBindingPorts bindings, string commandId)
    {
        bindings.TryGet(commandId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static FontEffectRibbonPorts CreatePorts(ICollection<string> events) =>
        new(
            Bold: new RecordingStatefulCommand(events, "bold"),
            Italic: new RecordingCommand(events, "italic"),
            Underline: new RecordingCommand(events, "underline"),
            Strikethrough: new RecordingCommand(events, "strikethrough"),
            SmallCaps: new RecordingCommand(events, "small-caps"),
            AllCaps: new RecordingCommand(events, "all-caps"),
            Superscript: new RecordingCommand(events, "superscript"),
            Subscript: new RecordingCommand(events, "subscript"),
            GrowFont: new RecordingCommand(events, "grow-font"),
            ShrinkFont: new RecordingCommand(events, "shrink-font"));

    private class RecordingCommand(ICollection<string> events, string name) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => events.Add(name);
    }

    private sealed class RecordingStatefulCommand(ICollection<string> events, string name)
        : RecordingCommand(events, name), IRibbonStatefulCommand
    {
        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: true, Value: "bold");
    }
}
