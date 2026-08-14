using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class CitationRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowOwnsRoutesCompatibilityAliasAndNativeCommandIdentity()
    {
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();
        var insert = new RecordingCommand(events, "citation");
        var manage = new RecordingCommand(events, "sources");
        var bibliography = new RecordingCommand(events, "bibliography");

        CitationRibbonWorkflow.Register(
            bindings,
            new CitationRibbonPorts(insert, manage, bibliography, _ => { }, () => CitationStyle.Apa));

        Command(bindings, FreeWRibbonCommandAction.Citation).Should().BeSameAs(insert);
        Registered(bindings, CitationRibbonWorkflow.InsertCitationCompatibilityId).Should().BeSameAs(insert);
        Command(bindings, FreeWRibbonCommandAction.ManageSources).Should().BeSameAs(manage);
        Command(bindings, FreeWRibbonCommandAction.Bibliography).Should().BeSameAs(bibliography);

        Command(bindings, FreeWRibbonCommandAction.Citation).Execute(RibbonCommandContext.Empty);
        Command(bindings, FreeWRibbonCommandAction.ManageSources).Execute(RibbonCommandContext.Empty);
        Command(bindings, FreeWRibbonCommandAction.Bibliography).Execute(RibbonCommandContext.Empty);
        events.Should().Equal("citation", "sources", "bibliography");
    }

    [Fact]
    public void CitationStyleChoiceTracksLiveStateParsesValuesAndPublishesChanges()
    {
        var style = CitationStyle.Apa;
        var published = new List<RibbonCommandState>();
        var bindings = new FreeWRibbonCommandBindingPorts();
        var registration = CitationRibbonWorkflow.Register(
            bindings,
            new CitationRibbonPorts(
                new RecordingCommand([], "citation"),
                new RecordingCommand([], "sources"),
                new RecordingCommand([], "bibliography"),
                value => style = value,
                () => style,
                published.Add));

        registration.CitationStyleCommand.GetState().Value.Should().Be("APA");
        registration.CitationStyleCommand.Execute(RibbonCommandContext.ForSelectedValue("IEEE"));

        style.Should().Be(CitationStyle.Ieee);
        registration.CitationStyleCommand.GetState().Value.Should().Be("IEEE");
        published.Should().ContainSingle().Which.Value.Should().Be("IEEE");

        registration.CitationStyleCommand.Execute(RibbonCommandContext.ForSelectedValue("unknown"));
        style.Should().Be(CitationStyle.Ieee);
    }

    [Fact]
    public void BothRenderersDelegateCitationPolicyToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("CitationRibbonWorkflow.Register(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Citation,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.ManageSources,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.Bibliography,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.CitationStyle,");
            source.Should().NotContain("Citations.ParseStyle(");
        }
    }

    private static IRibbonCommand Command(FreeWRibbonCommandBindingPorts bindings, FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        return Registered(bindings, route.CommandId);
    }

    private static IRibbonCommand Registered(FreeWRibbonCommandBindingPorts bindings, RibbonCommandId commandId)
    {
        bindings.TryGet(commandId, out var command).Should().BeTrue(commandId.Value);
        return command!;
    }

    private sealed class RecordingCommand(ICollection<string> events, string value) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => events.Add(value);
    }
}
