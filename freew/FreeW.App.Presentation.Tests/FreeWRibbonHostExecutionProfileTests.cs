using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWRibbonHostExecutionProfileTests
{
    [Theory]
    [InlineData(FreeWRibbonCommandAction.PastePlain)]
    [InlineData(FreeWRibbonCommandAction.ChangeCase)]
    [InlineData(FreeWRibbonCommandAction.Field)]
    [InlineData(FreeWRibbonCommandAction.LineNumbersOptions)]
    [InlineData(FreeWRibbonCommandAction.PreviousChange)]
    [InlineData(FreeWRibbonCommandAction.CheckAccessibility)]
    [InlineData(FreeWRibbonCommandAction.Combine)]
    public void MissingOptionalHostEndpointIsExplicitlyUnavailable(
        FreeWRibbonCommandAction action)
    {
        var bindings = new FreeWRibbonCommandBindingPorts();

        FreeWRibbonHostExecutionProfile.Register(
            bindings,
            FreeWRibbonHostExecutionPorts.Empty,
            registerFileAdapterCommands: false);

        var command = Command(bindings, action);
        command.Should().BeAssignableTo<IRibbonStatefulCommand>();
        ((IRibbonStatefulCommand)command).GetState().IsEnabled.Should().BeFalse();
        command.Should().NotBeSameAs(EmptyRibbonCommand.Instance);
    }

    [Fact]
    public void MissingPdfImportEndpointIsExplicitlyUnavailable()
    {
        var bindings = new FreeWRibbonCommandBindingPorts();

        FreeWRibbonHostExecutionProfile.Register(
            bindings,
            FreeWRibbonHostExecutionPorts.Empty,
            registerFileAdapterCommands: true);

        bindings.TryGet(new RibbonCommandId("freew.import-pdf-text"), out var command)
            .Should().BeTrue();
        command.Should().BeSameAs(FreeWRibbonExecutionProfile.UnavailableCommand);
    }

    [Fact]
    public void SuppliedOptionalEndpointRemainsExecutable()
    {
        var calls = 0;
        var bindings = new FreeWRibbonCommandBindingPorts();
        var ports = FreeWRibbonHostExecutionPorts.Empty with
        {
            CheckAccessibility = () => calls++,
        };

        FreeWRibbonHostExecutionProfile.Register(
            bindings,
            ports,
            registerFileAdapterCommands: false);

        var command = Command(bindings, FreeWRibbonCommandAction.CheckAccessibility);
        command.Execute(RibbonCommandContext.Empty);

        calls.Should().Be(1);
    }

    [Fact]
    public void SuppliedChangeCaseDialogEndpointIsOwnedByTheSharedHostProfile()
    {
        var calls = 0;
        var bindings = new FreeWRibbonCommandBindingPorts();

        FreeWRibbonHostExecutionProfile.Register(
            bindings,
            FreeWRibbonHostExecutionPorts.Empty with
            {
                OpenChangeCaseDialog = () => calls++,
            },
            registerFileAdapterCommands: false);

        Command(bindings, FreeWRibbonCommandAction.ChangeCase)
            .Execute(RibbonCommandContext.Empty);

        calls.Should().Be(1);
    }

    [Fact]
    public void ReviewingPaneCommandAndAliasShareTheReturnedStatefulInstance()
    {
        var visible = false;
        var bindings = new FreeWRibbonCommandBindingPorts();
        var commands = FreeWRibbonHostExecutionProfile.Register(
            bindings,
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleReviewingPane = () => visible = !visible,
                IsReviewingPaneVisible = () => visible,
            },
            registerFileAdapterCommands: false);

        Command(bindings, FreeWRibbonCommandAction.ReviewingPane)
            .Should().BeSameAs(commands.ReviewingPane);
        bindings.TryGet("freew.reviewingpane", out var alias).Should().BeTrue();
        alias.Should().BeSameAs(commands.ReviewingPane);
        commands.ReviewingPane.GetState().IsChecked.Should().BeFalse();
        commands.ReviewingPane.Execute(RibbonCommandContext.Empty);
        commands.ReviewingPane.GetState().IsChecked.Should().BeTrue();
    }

    [Fact]
    public void RenderersDoNotRebindSharedReviewingPanePolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWAvaloniaRibbonCommands.cs"));
        var profile = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "FreeWRibbonHostExecutionProfile.cs"));

        profile.Should().Contain("bindings.Register(\"freew.reviewingpane\", reviewingPane)");
        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().NotContain("BindToggle(FreeWRibbonCommandAction.ReviewingPane");
            renderer.Should().NotContain("Register(\"freew.reviewingpane\"");
        }
        wpf.Should().Contain("hostCommands.ReviewingPane");
    }

    [Fact]
    public void AvaloniaOptionalDialogAdaptersUseTheSharedUnavailableCommand()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWAvaloniaRibbonCommands.cs"));

        source.Should().Contain("private static IRibbonCommand OptionalHostCommand(Action? callback)");
        source.Should().Contain("? FreeWRibbonExecutionProfile.UnavailableCommand");
        source.Should().NotContain("callbacks.OpenCaptionDialog ?? (() => { })");
        source.Should().NotContain("callbacks.OpenColumnsDialog ?? (() => { })");
        source.Should().NotContain("callbacks.OpenWatermarkDialog ?? (() => { })");
        source.Should().NotContain("callbacks.ToggleReadAloud ?? (() => { })");
    }

    private static IRibbonCommand Command(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        bindings.TryGet(route.CommandId, out var command).Should().BeTrue();
        return command!;
    }
}
