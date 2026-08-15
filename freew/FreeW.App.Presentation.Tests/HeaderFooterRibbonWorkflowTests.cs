using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class HeaderFooterRibbonWorkflowTests
{
    [Fact]
    public void RegistersEveryPrimaryAndContextualActionWithFourStatefulHandles()
    {
        var events = new List<string>();
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();
        var bindings = CreateBindings(events);

        var result = HeaderFooterRibbonWorkflow.Register(builder, bindings);
        var commands = builder.Build().Commands;

        HeaderFooterRibbonWorkflow.Actions.Should().OnlyHaveUniqueItems().And.HaveCount(25);
        foreach (var action in HeaderFooterRibbonWorkflow.Actions)
        {
            commands.Should().ContainKey(action);
            commands[action].Should().NotBeNull();
        }

        result.StatefulCommands.Select(entry => entry.Id).Should().Equal(
            "freew.hf-different-first-page",
            "freew.hf-different-odd-even",
            "freew.hf-header-from-top",
            "freew.hf-footer-from-bottom");
        result.StatefulCommands.Select(entry => entry.Command).Should().Equal(
            bindings.DifferentFirstPage,
            bindings.DifferentOddEvenPages,
            bindings.HeaderFromTop,
            bindings.FooterFromBottom);
    }

    [Fact]
    public void SharedWorkflowOwnsCanonicalEditAndNavigationSlotMapping()
    {
        var events = new List<string>();
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();
        HeaderFooterRibbonWorkflow.Register(builder, CreateBindings(events));
        var commands = builder.Build().Commands;

        foreach (var binding in FreeWRibbonSemanticCatalog.HeaderFooterEditSlots)
            Execute(commands, binding.Action);
        foreach (var binding in FreeWRibbonSemanticCatalog.HeaderFooterNavigationSlots)
            Execute(commands, binding.Action);

        events.Should().Equal(
            "edit:Header",
            "edit:Footer",
            "edit:EvenHeader",
            "edit:EvenFooter",
            "edit:FirstHeader",
            "edit:FirstFooter",
            "navigate:Header",
            "navigate:Footer");
    }

    [Fact]
    public void BothRenderersDelegateHeaderFooterMappingToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("HeaderFooterRibbonWorkflow.Register(");
            source.Should().Contain("HeaderFooterRibbonWorkflow.CreatePageSettingCommands(");
            source.Should().Contain("new HeaderFooterRibbonBindings(");
            source.Should().NotContain("headerFooterCommands.Bind(");
            source.Should().NotContain("ConfigureHeaderFooterCommandFamily(");
            source.Should().NotContain("BindHeaderFooterSlot(");
            source.Should().NotContain("class PageSettingCommand");
            source.Should().NotContain("class HeaderFooterDistanceCommand");
        }
    }

    [Fact]
    public void PageSettingCommandsOwnToggleDistanceAndEnablementPolicy()
    {
        var page = new PageSettings
        {
            HeaderDistancePt = 18,
            FooterDistancePt = 24,
        };
        var enabled = true;
        var mutationCount = 0;
        var commands = HeaderFooterRibbonWorkflow.CreatePageSettingCommands(
            new HeaderFooterPageSettingsPorts(
                GetPageSettings: () => page,
                ApplyPageSettings: apply =>
                {
                    apply(page);
                    mutationCount++;
                },
                IsEnabled: () => enabled));

        commands.DifferentFirstPage.GetState().Should().BeEquivalentTo(
            new RibbonCommandState(IsEnabled: true, IsChecked: false));
        commands.DifferentFirstPage.Execute(RibbonCommandContext.Empty);
        page.DifferentFirstPage.Should().BeTrue();

        commands.DifferentOddEvenPages.Execute(RibbonCommandContext.Empty);
        page.DifferentOddEvenPages.Should().BeTrue();

        commands.HeaderFromTop.GetState().Value.Should().Be("18");
        commands.HeaderFromTop.Execute(RibbonCommandContext.ForSelectedValue("36.25"));
        page.HeaderDistancePt.Should().Be(36.25);

        commands.FooterFromBottom.GetState().Value.Should().Be("24");
        commands.FooterFromBottom.Execute(RibbonCommandContext.ForSelectedValue("bad"));
        page.FooterDistancePt.Should().Be(24);
        mutationCount.Should().Be(3);

        enabled = false;
        commands.DifferentFirstPage.Execute(RibbonCommandContext.Empty);
        commands.FooterFromBottom.Execute(RibbonCommandContext.ForSelectedValue("48"));
        page.DifferentFirstPage.Should().BeTrue();
        page.FooterDistancePt.Should().Be(24);
        commands.FooterFromBottom.GetState().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void PageSettingCommandsAllowRendererSpecificValueResolution()
    {
        var page = new PageSettings();
        var commands = HeaderFooterRibbonWorkflow.CreatePageSettingCommands(
            new HeaderFooterPageSettingsPorts(
                GetPageSettings: () => page,
                ApplyPageSettings: apply => apply(page),
                IsEnabled: static () => true,
                ResolveSelectedValue: context => context.Parameters.TryGetValue("legacy", out var value)
                    ? value as string
                    : null));

        commands.HeaderFromTop.Execute(new RibbonCommandContext(
            new Dictionary<string, object?> { ["legacy"] = "42" }));

        page.HeaderDistancePt.Should().Be(42);
    }

    private static HeaderFooterRibbonBindings CreateBindings(ICollection<string> events)
    {
        IRibbonCommand Command(string name) => new RecordingCommand(events, name);

        return new HeaderFooterRibbonBindings(
            Header: Command("header"),
            Footer: Command("footer"),
            PageNumber: Command("page-number"),
            PageNumberTop: Command("page-number-top"),
            PageNumberBottom: Command("page-number-bottom"),
            PageNumberCurrent: Command("page-number-current"),
            PageNumberFormat: Command("page-number-format"),
            DateTime: Command("date-time"),
            CreateEditSlotCommand: slot => Command($"edit:{slot}"),
            DifferentFirstPage: new RecordingStatefulCommand(events, "different-first"),
            DifferentOddEvenPages: new RecordingStatefulCommand(events, "different-odd-even"),
            HeaderFromTop: new RecordingStatefulCommand(events, "header-from-top"),
            FooterFromBottom: new RecordingStatefulCommand(events, "footer-from-bottom"),
            CreateNavigationCommand: slot => Command($"navigate:{slot}"),
            Close: Command("close"),
            InsertHeaderPageNumber: Command("insert-header-page-number"),
            InsertFooterPageNumber: Command("insert-footer-page-number"),
            InsertDateTime: Command("insert-date-time"),
            InsertDocumentInfo: Command("insert-document-info"));
    }

    private static void Execute(
        IReadOnlyDictionary<FreeWRibbonCommandAction, IRibbonCommand> commands,
        FreeWRibbonCommandAction action)
    {
        commands.Should().ContainKey(action);
        commands[action].Execute(RibbonCommandContext.Empty);
    }

    private class RecordingCommand(ICollection<string> events, string name) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => events.Add(name);
    }

    private sealed class RecordingStatefulCommand(ICollection<string> events, string name)
        : RecordingCommand(events, name), IRibbonStatefulCommand
    {
        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: true, Value: "stateful");
    }
}
