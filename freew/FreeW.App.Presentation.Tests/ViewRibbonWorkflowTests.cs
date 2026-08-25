using System.IO;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class ViewRibbonWorkflowTests
{
    [Fact]
    public void Register_drives_view_modes_zoom_show_and_window_workflows()
    {
        var registry = new RibbonCommandRegistry();
        var mode = "print";
        var focus = false;
        var readMode = false;
        var navigationPane = false;
        var revealFormatting = false;
        var gridlines = false;
        var ruler = false;
        var multiplePages = false;
        var sideToSide = false;
        var split = false;
        var readModeColumns = new List<string>();
        var readModeColors = new List<string>();
        var zooms = new List<(double? Absolute, double Delta)>();
        var actions = new List<string>();

        var commands = ViewRibbonWorkflow.Register(
            registry,
            new ViewRibbonCommandBindings(
                PrintPreview: Action("print-preview"),
                ReadMode: new ViewRibbonReadModeBindings(
                    Toggle: Toggle(() => readMode = !readMode, () => readMode),
                    ColumnWidth: new ViewRibbonChoiceBinding(readModeColumns.Add),
                    PageColor: new ViewRibbonChoiceBinding(readModeColors.Add)),
                Modes: new ViewRibbonModeBindings(
                    Focus: Toggle(() => focus = !focus, () => focus),
                    PrintLayout: Mode("print"),
                    WebLayout: Mode("web"),
                    Draft: Mode("draft"),
                    Outline: Mode("outline"),
                    PagedEdit: Mode("paged-edit")),
                Show: new ViewRibbonShowBindings(
                    NavigationPane: Toggle(
                        () => navigationPane = !navigationPane,
                        () => navigationPane),
                    RevealFormatting: Toggle(
                        () => revealFormatting = !revealFormatting,
                        () => revealFormatting),
                    Gridlines: Toggle(() => gridlines = !gridlines, () => gridlines),
                    Ruler: Toggle(() => ruler = !ruler, () => ruler)),
                Zoom: new ViewRibbonZoomBindings(
                    Dialog: Action("zoom-dialog"),
                    ZoomIn: new ViewRibbonActionBinding(() => zooms.Add((null, +0.1))),
                    ZoomOut: new ViewRibbonActionBinding(() => zooms.Add((null, -0.1))),
                    Reset100: new ViewRibbonActionBinding(() => zooms.Add((1.0, 0))),
                    OnePage: Action("one-page"),
                    PageWidth: Action("page-width"),
                    MultiplePages: Toggle(
                        () => multiplePages = !multiplePages,
                        () => multiplePages),
                    SideToSide: Toggle(
                        () => sideToSide = !sideToSide,
                        () => sideToSide)),
                Window: new ViewRibbonWindowBindings(
                    NewWindow: Action("new-window"),
                    ArrangeAll: Action("arrange-all"),
                    Split: Toggle(() => split = !split, () => split)),
                RegisterCompatibilityAliases: true));

        Execute(registry, "freew.web-layout");
        Stateful(registry, "freew.print-layout").GetState().IsChecked.Should().BeFalse();
        Stateful(registry, "freew.web-layout").GetState().IsChecked.Should().BeTrue();
        Execute(registry, "freew.draftview");
        mode.Should().Be("draft");
        Stateful(registry, "freew.draft-view").GetState().IsChecked.Should().BeTrue();

        Execute(registry, "freew.focus");
        Stateful(registry, "freew.focus").GetState().IsChecked.Should().BeTrue();
        focus.Should().BeTrue();

        Execute(registry, "freew.read-mode");
        Execute(registry, "freew.read-mode-column-wide");
        Execute(registry, "freew.read-mode-color-sepia");
        readMode.Should().BeTrue();
        readModeColumns.Should().Equal("wide");
        readModeColors.Should().Equal("sepia");

        Execute(registry, "freew.navigationpane");
        Execute(registry, "freew.reveal-formatting");
        Execute(registry, "freew.view-gridlines");
        Execute(registry, "freew.view-ruler");
        navigationPane.Should().BeTrue();
        revealFormatting.Should().BeTrue();
        gridlines.Should().BeTrue();
        ruler.Should().BeTrue();
        commands.Gridlines.Should().BeSameAs(Stateful(registry, "freew.gridlines"));

        Execute(registry, "freew.zoom-in");
        Execute(registry, "freew.zoom-out");
        Execute(registry, "freew.zoom-100");
        zooms.Should().Equal((null, +0.1), (null, -0.1), (1.0, 0));
        Execute(registry, "freew.zoom-multiple-pages");
        Execute(registry, "freew.zoom-side-to-side");
        multiplePages.Should().BeTrue();
        sideToSide.Should().BeTrue();

        Execute(registry, "freew.split");
        split.Should().BeTrue();
        Command(registry, "freew.split").Should().BeSameAs(Command(registry, "freew.split-window"));
        Command(registry, "freew.printlayout").Should().BeSameAs(Command(registry, "freew.print-layout"));
        Command(registry, "freew.navigationpane").Should().BeSameAs(Command(registry, "freew.nav-pane"));

        Execute(registry, "freew.print-preview");
        Execute(registry, "freew.zoom-dialog");
        Execute(registry, "freew.zoom-one-page");
        Execute(registry, "freew.zoom-page-width");
        Execute(registry, "freew.new-window");
        Execute(registry, "freew.arrange-all");
        actions.Should().Equal(
            "print-preview",
            "zoom-dialog",
            "one-page",
            "page-width",
            "new-window",
            "arrange-all");

        ViewRibbonActionBinding Action(string name) =>
            new(() => actions.Add(name));
        ViewRibbonToggleBinding Toggle(Action toggle, Func<bool> isChecked) =>
            new(toggle, isChecked);
        ViewRibbonToggleBinding Mode(string target) =>
            new(() => mode = target, () => mode == target);
    }

    [Fact]
    public void Register_preserves_optional_absence_and_fails_closed_for_unbound_commands()
    {
        var registry = new RibbonCommandRegistry();

        ViewRibbonWorkflow.Register(
            registry,
            new ViewRibbonCommandBindings(
                ReadMode: new ViewRibbonReadModeBindings(
                    Toggle: new ViewRibbonToggleBinding(
                        AvailabilityWhenUnbound: ViewRibbonBindingAvailability.Disabled),
                    ColumnWidth: new ViewRibbonChoiceBinding(
                        AvailabilityWhenUnbound: ViewRibbonBindingAvailability.Disabled),
                    PageColor: new ViewRibbonChoiceBinding(
                        AvailabilityWhenUnbound: ViewRibbonBindingAvailability.Disabled)),
                Modes: new ViewRibbonModeBindings(
                    PrintLayout: new ViewRibbonToggleBinding(static () => { }, IsChecked: null)),
                Window: new ViewRibbonWindowBindings(
                    NewWindow: new ViewRibbonActionBinding(
                        AvailabilityWhenUnbound: ViewRibbonBindingAvailability.Disabled))));

        Stateful(registry, "freew.read-mode").GetState().IsEnabled.Should().BeFalse();
        Stateful(registry, "freew.read-mode-column-default").GetState().IsEnabled.Should().BeFalse();
        Stateful(registry, "freew.read-mode-color-inverse").GetState().IsEnabled.Should().BeFalse();
        Stateful(registry, "freew.new-window").GetState().IsEnabled.Should().BeFalse();

        registry.TryGet("freew.print-preview", out _).Should().BeFalse();
        registry.TryGet("freew.print-layout", out _).Should().BeFalse(
            "a toggle requires both execution and checked-state delegates");
        registry.TryGet("freew.printlayout", out _).Should().BeFalse();
        registry.TryGet("freew.arrange-all", out _).Should().BeFalse();
    }

    [Fact]
    public void Host_ports_project_all_document_view_checks_from_one_shared_snapshot()
    {
        var projections = 0;
        var ports = FreeWRibbonHostExecutionPorts.Empty with
        {
            GetDocumentViewChecks = () =>
            {
                projections++;
                return new FreeWDocumentViewCheckPlan(
                    PrintLayout: false,
                    WebLayout: false,
                    Draft: false,
                    PagedEdit: true);
            },
        };

        ports.ResolvePrintLayoutActive()!().Should().BeFalse();
        ports.ResolveWebLayoutActive()!().Should().BeFalse();
        ports.ResolveDraftViewActive()!().Should().BeFalse();
        ports.ResolvePagedEditViewActive()!().Should().BeTrue();
        projections.Should().Be(4);

        var explicitOverride = ports with { IsPrintLayoutActive = static () => true };
        explicitOverride.ResolvePrintLayoutActive()!().Should().BeTrue();
        projections.Should().Be(4, "legacy explicit checks remain valid compatibility overrides");

        FreeWRibbonHostExecutionPorts.Empty
            .ResolveDraftViewActive(static () => true)!()
            .Should().BeTrue("renderer fallback state is used only without a shared projection");
    }

    [Fact]
    public void Wpf_and_avalonia_adapters_delegate_view_registration_to_presentation()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var workflow = ReadSource("freew", "FreeW.App.Presentation", "Ribbon", "ViewRibbonWorkflow.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ViewRibbonWorkflow.Register(");
            source.Should().Contain("new ViewRibbonCommandBindings(");
            source.Should().NotContain(".Register(\"freew.print-layout\"");
            source.Should().NotContain(".Register(\"freew.zoom-dialog\"");
            source.Should().NotContain(".Register(\"freew.gridlines\"");
            source.Should().NotContain(".Register(\"freew.new-window\"");
            source.Should().NotContain(".Register(\"freew.split-window\"");
            source.Should().NotContain("EnabledNoOp");
        }

        avalonia.Should().NotContain("RegisterReadModeChoice(");
        workflow.Should().NotContain("EnabledNoOp",
            "the shared View contract must not permit an enabled command without an execution endpoint");
    }

    [Fact]
    public void Hosts_and_adapters_use_the_shared_document_view_check_projection()
    {
        var wpfHost = ReadSource("freew", "FreeW.App.Host", "MainWindow.cs");
        var avaloniaHost = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var wpfAdapter = ReadSource(
            "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaAdapter = ReadSource(
            "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");

        foreach (var source in new[] { wpfHost, avaloniaHost })
        {
            source.Should().Contain("CurrentDocumentViewChecks");
            source.Should().Contain("_viewSession.BuildDocumentViewChecks(");
            source.Should().NotContain("IsPrintLayoutActive = () =>");
            source.Should().NotContain("IsPrintLayoutActive: () =>");
            source.Should().NotContain("IsPagedEditViewActive = () =>");
            source.Should().NotContain("IsPagedEditViewActive: () =>");
        }

        foreach (var source in new[] { wpfAdapter, avaloniaAdapter })
        {
            source.Should().Contain("ResolvePrintLayoutActive(");
            source.Should().Contain("ResolveWebLayoutActive(");
            source.Should().Contain("ResolveDraftViewActive(");
            source.Should().Contain("ResolvePagedEditViewActive(");
        }
    }

    private static void Execute(IRibbonCommandRegistry registry, string commandId) =>
        Command(registry, commandId).Execute(RibbonCommandContext.Empty);

    private static IRibbonCommand Command(IRibbonCommandRegistry registry, string commandId)
    {
        registry.TryGet(commandId, out var command).Should().BeTrue();
        return command!;
    }

    private static IRibbonStatefulCommand Stateful(IRibbonCommandRegistry registry, string commandId) =>
        Command(registry, commandId).Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;

    private static string ReadSource(params string[] relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(relativePath.Aggregate(root, Path.Combine));
    }

}
