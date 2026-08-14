using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class CaptionRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowOwnsPrimaryAliasFixedLabelsAndCrossReference()
    {
        var events = new List<string>();
        var primary = new RecordingCommand(() => events.Add("primary"));
        var crossReference = new RecordingCommand(() => events.Add("cross-reference"));
        var bindings = new FreeWRibbonCommandBindingPorts();

        CaptionRibbonWorkflow.Register(
            bindings,
            new CaptionRibbonPorts(
                primary,
                label => events.Add($"label:{label}"),
                crossReference));

        Command(bindings, FreeWRibbonCommandAction.Caption).Should().BeSameAs(primary);
        bindings.TryGet("freew.insert-caption", out var alias).Should().BeTrue();
        alias.Should().BeSameAs(primary);
        Command(bindings, FreeWRibbonCommandAction.CrossReference).Should().BeSameAs(crossReference);

        Command(bindings, FreeWRibbonCommandAction.Caption).Execute(RibbonCommandContext.Empty);
        Command(bindings, FreeWRibbonCommandAction.InsertCaption_Figure).Execute(RibbonCommandContext.Empty);
        Command(bindings, FreeWRibbonCommandAction.InsertCaption_Table).Execute(RibbonCommandContext.Empty);
        Command(bindings, FreeWRibbonCommandAction.InsertCaption_Equation).Execute(RibbonCommandContext.Empty);
        Command(bindings, FreeWRibbonCommandAction.CrossReference).Execute(RibbonCommandContext.Empty);

        events.Should().Equal(
            "primary",
            "label:Figure",
            "label:Table",
            "label:Equation",
            "cross-reference");
    }

    [Fact]
    public void MissingFixedLabelDialogPortFailsClosed()
    {
        var bindings = new FreeWRibbonCommandBindingPorts();
        CaptionRibbonWorkflow.Register(
            bindings,
            new CaptionRibbonPorts(
                new RecordingCommand(() => { }),
                InsertCaptionWithLabel: null,
                new RecordingCommand(() => { })));

        foreach (var action in new[]
                 {
                     FreeWRibbonCommandAction.InsertCaption_Figure,
                     FreeWRibbonCommandAction.InsertCaption_Table,
                     FreeWRibbonCommandAction.InsertCaption_Equation,
                 })
        {
            var command = Command(bindings, action);
            command.Should().BeSameAs(FreeWRibbonExecutionProfile.UnavailableCommand);
            command.Should().BeAssignableTo<IRibbonStatefulCommand>()
                .Which.GetState().IsEnabled.Should().BeFalse();
        }
    }

    [Fact]
    public void EditorFamilyBuilderReceivesCanonicalCommandsAndCaptionAlias()
    {
        var family = new FreeWRibbonEditorCommandFamilyBuilder();
        var primary = new RecordingCommand(() => { });
        CaptionRibbonWorkflow.Register(
            family,
            new CaptionRibbonPorts(
                primary,
                _ => { },
                new RecordingCommand(() => { })));

        var built = family.Build();
        built.Commands.Should().ContainKeys(
            FreeWRibbonCommandAction.Caption,
            FreeWRibbonCommandAction.InsertCaption_Figure,
            FreeWRibbonCommandAction.InsertCaption_Table,
            FreeWRibbonCommandAction.InsertCaption_Equation,
            FreeWRibbonCommandAction.CrossReference);
        built.AdapterCommands.Should().ContainKey("freew.insert-caption")
            .WhoseValue.Should().BeSameAs(primary);
    }

    [Fact]
    public void BothRenderersDelegateCaptionPolicyAndAvaloniaUsesLabelSpecificDialogPort()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));
        var avaloniaWindow = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("CaptionRibbonWorkflow.Register(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Caption,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.InsertCaption_Figure,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.InsertCaption_Table,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.InsertCaption_Equation,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.CrossReference,");
        }

        avalonia.Should().Contain("callbacks.OpenCaptionDialogForLabel");
        avalonia.Should().NotContain("editor.InsertCaption(CaptionLabel.");
        avaloniaWindow.Should().Contain("OpenCaptionDialogForLabel: label => _ = OpenCaptionDialogAsync(label)");
    }

    private static IRibbonCommand Command(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        bindings.TryGet(route.CommandId, out var command).Should().BeTrue(action.ToString());
        return command!;
    }

    private sealed class RecordingCommand(Action execute) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => execute();
    }
}
