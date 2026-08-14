using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class IndexRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowRoutesMarkInsertAndRefresh()
    {
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        IndexRibbonWorkflow.Register(
            bindings,
            new IndexRibbonPorts(
                () => events.Add("mark"),
                () => events.Add("insert"),
                () => events.Add("refresh")));

        Command(bindings, FreeWRibbonCommandAction.IndexMark).Execute(RibbonCommandContext.Empty);
        Command(bindings, FreeWRibbonCommandAction.IndexInsert).Execute(RibbonCommandContext.Empty);
        Command(bindings, FreeWRibbonCommandAction.IndexRefresh).Execute(RibbonCommandContext.Empty);

        events.Should().Equal("mark", "insert", "refresh");
    }

    [Fact]
    public void MissingRendererPortsFailClosedIndependently()
    {
        var insertCalls = 0;
        var bindings = new FreeWRibbonCommandBindingPorts();
        IndexRibbonWorkflow.Register(
            bindings,
            new IndexRibbonPorts(
                MarkEntry: null,
                InsertIndex: () => insertCalls++,
                RefreshIndex: null));

        Unavailable(bindings, FreeWRibbonCommandAction.IndexMark);
        Unavailable(bindings, FreeWRibbonCommandAction.IndexRefresh);

        var insert = Command(bindings, FreeWRibbonCommandAction.IndexInsert);
        insert.Should().BeOfType<ActionRibbonCommand>();
        insert.Execute(RibbonCommandContext.Empty);
        insertCalls.Should().Be(1);
    }

    [Fact]
    public void EditorFamilyBuilderReceivesAllSharedIndexCommands()
    {
        var family = new FreeWRibbonEditorCommandFamilyBuilder();
        IndexRibbonWorkflow.Register(
            family,
            new IndexRibbonPorts(() => { }, () => { }, () => { }));

        family.Build().Commands.Should().ContainKeys(
            FreeWRibbonCommandAction.IndexMark,
            FreeWRibbonCommandAction.IndexInsert,
            FreeWRibbonCommandAction.IndexRefresh);
    }

    [Fact]
    public void BothRenderersDelegateIndexPolicyAndAvaloniaSuppliesEditorFallbacks()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("IndexRibbonWorkflow.Register(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.IndexMark,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.IndexInsert,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.IndexRefresh,");
        }

        avalonia.Should().Contain("callbacks.OpenMarkIndexEntryDialog ?? (() => editor.MarkIndexEntry())");
        avalonia.Should().Contain("callbacks.OpenInsertIndexDialog ?? (() => editor.InsertIndex())");
        avalonia.Should().Contain("callbacks.OpenUpdateIndexDialog ?? (() => editor.RefreshIndex())");
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
