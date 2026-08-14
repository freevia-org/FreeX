using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewChangeRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowRoutesNativeSelectionCommandsAndAliases()
    {
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();
        ReviewChangeRibbonWorkflow.Register(
            bindings,
            new ReviewChangeRibbonPorts(
                () => events.Add("previous"),
                () => events.Add("next"),
                () => events.Add("accept"),
                () => events.Add("reject")));

        Command(bindings, FreeWRibbonCommandAction.PreviousChange).Execute(RibbonCommandContext.Empty);
        Command(bindings, FreeWRibbonCommandAction.NextChange).Execute(RibbonCommandContext.Empty);
        var accept = Command(bindings, FreeWRibbonCommandAction.AcceptThis);
        var reject = Command(bindings, FreeWRibbonCommandAction.RejectThis);
        accept.Execute(RibbonCommandContext.Empty);
        reject.Execute(RibbonCommandContext.Empty);

        events.Should().Equal("previous", "next", "accept", "reject");
        bindings.TryGet("freew.accept-change", out var acceptAlias).Should().BeTrue();
        acceptAlias.Should().BeSameAs(accept);
        bindings.TryGet("freew.reject-change", out var rejectAlias).Should().BeTrue();
        rejectAlias.Should().BeSameAs(reject);
    }

    [Fact]
    public void MissingNativeSelectionEndpointsFailClosedIncludingAliases()
    {
        var bindings = new FreeWRibbonCommandBindingPorts();
        ReviewChangeRibbonWorkflow.Register(
            bindings,
            new ReviewChangeRibbonPorts(null, null, null, null));

        foreach (var action in new[]
                 {
                     FreeWRibbonCommandAction.PreviousChange,
                     FreeWRibbonCommandAction.NextChange,
                     FreeWRibbonCommandAction.AcceptThis,
                     FreeWRibbonCommandAction.RejectThis,
                 })
        {
            Command(bindings, action).Should().BeSameAs(FreeWRibbonExecutionProfile.UnavailableCommand);
        }

        bindings.TryGet("freew.accept-change", out var acceptAlias).Should().BeTrue();
        acceptAlias.Should().BeSameAs(FreeWRibbonExecutionProfile.UnavailableCommand);
        bindings.TryGet("freew.reject-change", out var rejectAlias).Should().BeTrue();
        rejectAlias.Should().BeSameAs(FreeWRibbonExecutionProfile.UnavailableCommand);
    }

    [Fact]
    public void HostProfileDelegatesPolicyAndAvaloniaHasNoCaretRelativeFallback()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var profile = Read(root, "FreeW.App.Presentation", "Ribbon", "FreeWRibbonHostExecutionProfile.cs");
        var avalonia = Read(root, "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");

        profile.Should().Contain("ReviewChangeRibbonWorkflow.Register(");
        avalonia.Should().NotContain("callbacks.AcceptThisChange ??");
        avalonia.Should().NotContain("callbacks.RejectThisChange ??");
        avalonia.Should().NotContain("editor.AcceptCurrentRevision()");
        avalonia.Should().NotContain("editor.RejectCurrentRevision()");
        avalonia.Should().NotContain("r.Register(\"freew.accept-change\"");
        avalonia.Should().NotContain("r.Register(\"freew.reject-change\"");
    }

    private static IRibbonCommand Command(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        bindings.TryGet(route.CommandId, out var command).Should().BeTrue(action.ToString());
        return command!;
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, "freew", .. parts]));
}
