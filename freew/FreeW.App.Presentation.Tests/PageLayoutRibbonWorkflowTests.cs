using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class PageLayoutRibbonWorkflowTests
{
    [Fact]
    public void RegistersEveryOwnedActionAndSharedPresetAliases()
    {
        var page = new PageSettings();
        var registry = new FreeWRibbonCommandBindingPorts();

        var result = PageLayoutRibbonWorkflow.Register(registry, Ports(page));

        PageLayoutRibbonWorkflow.Actions.Should().OnlyHaveUniqueItems().And.HaveCount(18);
        foreach (var action in PageLayoutRibbonWorkflow.Actions)
        {
            var id = FreeWRibbonCommandWorkflow.GetPrimaryCommandId(action);
            registry.TryGet(id, out var command).Should().BeTrue(action.ToString());
            command.Should().BeAssignableTo<IRibbonStatefulCommand>();
        }

        registry.TryGet("freew.orientation", out var orientation).Should().BeTrue();
        registry.TryGet("freew.page-orientation", out var orientationAlias).Should().BeTrue();
        orientationAlias.Should().BeSameAs(orientation);

        foreach (var id in new[]
                 {
                     "freew.page-margins-normal",
                     "freew.page-margins-narrow",
                     "freew.page-margins-wide",
                     "freew.page-size-letter",
                     "freew.page-size-a4",
                 })
        {
            registry.TryGet(id, out var command).Should().BeTrue(id);
            command.Should().BeAssignableTo<IRibbonStatefulCommand>();
        }

        result.StatefulCommands.Should().HaveCount(23);
        result.StatefulCommands.Select(entry => entry.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SharedCommandsOwnMutationAndCheckedStatePolicy()
    {
        var page = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            LineNumberMode = LineNumberMode.None,
            AutoHyphenation = false,
            DifferentFirstPage = false,
        };
        var events = new List<string>();
        var registry = new FreeWRibbonCommandBindingPorts();
        PageLayoutRibbonWorkflow.Register(
            registry,
            Ports(page, events: events));

        Execute(registry, "freew.page-orientation");
        page.Landscape.Should().BeTrue();
        page.WidthPt.Should().Be(792);
        page.HeightPt.Should().Be(612);

        Execute(registry, "freew.page-margins-wide");
        page.MarginTopPt.Should().Be(72);
        page.MarginBottomPt.Should().Be(72);
        page.MarginLeftPt.Should().Be(108);
        page.MarginRightPt.Should().Be(108);

        Execute(registry, "freew.page-size-a4");
        page.WidthPt.Should().BeApproximately(841.9, 0.01);
        page.HeightPt.Should().BeApproximately(595.3, 0.01);

        Execute(registry, "freew.columns-three");
        page.ColumnCount.Should().Be(3);
        Stateful(registry, "freew.columns-three").GetState().IsChecked.Should().BeTrue();

        Execute(registry, "freew.line-numbers-continuous");
        page.LineNumberMode.Should().Be(LineNumberMode.Continuous);
        Stateful(registry, "freew.line-numbers-continuous").GetState().IsChecked.Should().BeTrue();

        Execute(registry, "freew.hyphenation-auto");
        page.AutoHyphenation.Should().BeTrue();
        Stateful(registry, "freew.hyphenation-auto").GetState().IsChecked.Should().BeTrue();

        Execute(registry, "freew.different-first-page");
        page.DifferentFirstPage.Should().BeTrue();
        Stateful(registry, "freew.different-first-page").GetState().IsChecked.Should().BeTrue();

        events.Except(["prepare", "apply"]).Should().BeEmpty();
        events.Count(entry => entry == "prepare").Should().Be(7);
        events.Count(entry => entry == "apply").Should().Be(7);
    }

    [Fact]
    public void DisabledCommandsDoNotPrepareOrMutate()
    {
        var page = new PageSettings { AutoHyphenation = true };
        var events = new List<string>();
        var registry = new FreeWRibbonCommandBindingPorts();
        PageLayoutRibbonWorkflow.Register(
            registry,
            Ports(page, enabled: () => false, events: events));

        var command = Stateful(registry, "freew.hyphenation-auto");
        command.GetState().Should().Be(new RibbonCommandState(IsEnabled: false, IsChecked: true));

        command.Execute(RibbonCommandContext.Empty);

        page.AutoHyphenation.Should().BeTrue();
        events.Should().BeEmpty();
    }

    [Fact]
    public void BothRenderersDelegatePageLayoutQuickActionsToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("PageLayoutRibbonWorkflow.Register(");
            source.Should().Contain("new PageLayoutRibbonPorts(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.ColumnsOne");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.LineNumbersContinuous");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.HyphenationAuto");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.PageValign");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.DifferentFirstPage");
        }

        wpf.Should().NotContain(".Bind(FreeWRibbonCommandAction.Orientation");
        avalonia.Should().Contain("var orientationCommand = new HostPageSettingCommand(");
        avalonia.Should().Contain("r.Bind(FreeWRibbonCommandAction.Orientation, orientationCommand)");
    }

    private static PageLayoutRibbonPorts Ports(
        PageSettings page,
        Func<bool>? enabled = null,
        ICollection<string>? events = null) =>
        new(
            GetPageSettings: () => page,
            ApplyPageSettings: apply =>
            {
                events?.Add("apply");
                apply(page);
            },
            IsEnabled: enabled ?? (() => true),
            PrepareExecution: () => events?.Add("prepare"));

    private static void Execute(FreeWRibbonCommandBindingPorts registry, string id)
    {
        registry.TryGet(id, out var command).Should().BeTrue(id);
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static IRibbonStatefulCommand Stateful(
        FreeWRibbonCommandBindingPorts registry,
        string id)
    {
        registry.TryGet(id, out var command).Should().BeTrue(id);
        return command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
    }
}
