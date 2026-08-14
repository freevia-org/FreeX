using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class TableOfAuthoritiesRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowRoutesMarkInsertAndPreparedRefresh()
    {
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        TableOfAuthoritiesRibbonWorkflow.Register(
            bindings,
            new TableOfAuthoritiesRibbonPorts(
                () => events.Add("mark"),
                () => events.Add("insert"),
                () => events.Add("refresh"),
                () => events.Add("prepare-refresh")));

        Command(bindings, FreeWRibbonCommandAction.MarkCitation).Execute(RibbonCommandContext.Empty);
        Command(bindings, FreeWRibbonCommandAction.TableOfAuthorities).Execute(RibbonCommandContext.Empty);
        Command(bindings, FreeWRibbonCommandAction.TableOfAuthoritiesRefresh).Execute(RibbonCommandContext.Empty);

        events.Should().Equal("mark", "insert", "prepare-refresh", "refresh");
    }

    [Fact]
    public void MissingNativeDialogsFailClosedWithoutDisablingBackedRefresh()
    {
        var refreshCalls = 0;
        var bindings = new FreeWRibbonCommandBindingPorts();
        TableOfAuthoritiesRibbonWorkflow.Register(
            bindings,
            new TableOfAuthoritiesRibbonPorts(
                MarkCitation: null,
                InsertTableOfAuthorities: null,
                RefreshTableOfAuthorities: () => refreshCalls++));

        Unavailable(bindings, FreeWRibbonCommandAction.MarkCitation);
        Unavailable(bindings, FreeWRibbonCommandAction.TableOfAuthorities);
        Command(bindings, FreeWRibbonCommandAction.TableOfAuthoritiesRefresh)
            .Execute(RibbonCommandContext.Empty);
        refreshCalls.Should().Be(1);
    }

    [Fact]
    public void EditorFamilyBuilderReceivesAllSharedAuthorityCommands()
    {
        var family = new FreeWRibbonEditorCommandFamilyBuilder();
        TableOfAuthoritiesRibbonWorkflow.Register(
            family,
            new TableOfAuthoritiesRibbonPorts(() => { }, () => { }, () => { }));

        family.Build().Commands.Should().ContainKeys(
            FreeWRibbonCommandAction.MarkCitation,
            FreeWRibbonCommandAction.TableOfAuthorities,
            FreeWRibbonCommandAction.TableOfAuthoritiesRefresh);
    }

    [Fact]
    public void BothRenderersDelegateAuthorityPolicyAndAvaloniaHasNoDefaultInsertionPath()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("TableOfAuthoritiesRibbonWorkflow.Register(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.MarkCitation,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.TableOfAuthorities,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.TableOfAuthoritiesRefresh,");
        }

        avalonia.Should().NotContain("useDefaultsWhenUnavailable:");
        avalonia.Should().NotContain("ToaOptions.Default");
        avalonia.Should().Contain("callbacks.ShowTableOfAuthoritiesDialog");
    }

    private static void Unavailable(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action)
    {
        var command = Command(bindings, action);
        command.Should().BeSameAs(FreeWRibbonExecutionProfile.UnavailableCommand);
        command.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeFalse();
    }

    private static IRibbonCommand Command(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        bindings.TryGet(route.CommandId, out var command).Should().BeTrue(action.ToString());
        return command!;
    }
}
