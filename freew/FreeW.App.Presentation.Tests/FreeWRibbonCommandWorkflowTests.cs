using System.IO;
using System.Text.RegularExpressions;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWRibbonCommandWorkflowTests
{
    [Fact]
    public void Routes_are_unique_and_cover_the_shared_renderer_groups()
    {
        var routes = FreeWRibbonCommandWorkflow.Routes;

        routes.Should().HaveCount(405);
        routes.Select(route => route.CommandId).Should().OnlyHaveUniqueItems();
        routes.Select(route => route.Action).Should().OnlyHaveUniqueItems();
        routes.Select(route => route.Action)
            .Should().BeEquivalentTo(Enum.GetValues<FreeWRibbonCommandAction>());

        routes.Should().Contain(new FreeWRibbonCommandRoute(
            "freew.bold",
            FreeWRibbonCommandAction.Bold));
        routes.Should().Contain(new FreeWRibbonCommandRoute(
            "freew.table-properties",
            FreeWRibbonCommandAction.TableProperties));
        routes.Should().Contain(new FreeWRibbonCommandRoute(
            "freew.image-brightness-plus20",
            FreeWRibbonCommandAction.ImageBrightnessPlus20));
        routes.Should().Contain(new FreeWRibbonCommandRoute(
            "freew.merge-finish",
            FreeWRibbonCommandAction.MergeFinish));
        routes.Should().Contain(new FreeWRibbonCommandRoute(
            "freew.smartart-add-shape",
            FreeWRibbonCommandAction.SmartartAddShape));
    }

    [Fact]
    public void Register_routes_a_native_command_without_renderer_owned_ids()
    {
        var registry = new RibbonCommandRegistry();
        var command = new RecordingCommand();

        FreeWRibbonCommandWorkflow.Register(
            registry,
            FreeWRibbonCommandAction.TableProperties,
            command);

        registry.TryGet("freew.table-properties", out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(command);
        FreeWRibbonCommandWorkflow.GetPrimaryCommandId(FreeWRibbonCommandAction.TableProperties)
            .Should().Be(new RibbonCommandId("freew.table-properties"));
    }

    [Fact]
    public void Shared_bindings_own_enabled_checked_value_and_prepare_policies()
    {
        var registry = new RibbonCommandRegistry();
        var enabled = false;
        var checkedState = false;
        var actionCount = 0;
        var prepareCount = 0;
        string? selectedValue = null;

        var action = FreeWRibbonCommandWorkflow.RegisterAction(
            registry,
            FreeWRibbonCommandAction.Find,
            () => actionCount++,
            () => enabled,
            () => prepareCount++);
        var toggle = FreeWRibbonCommandWorkflow.RegisterToggle(
            registry,
            FreeWRibbonCommandAction.Bold,
            () => checkedState = !checkedState,
            () => checkedState,
            prepareExecution: () => prepareCount++);
        var value = FreeWRibbonCommandWorkflow.RegisterValue(
            registry,
            FreeWRibbonCommandAction.FontFamily,
            selected => selectedValue = selected,
            getValue: () => "Aptos",
            prepareExecution: () => prepareCount++);

        action.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeFalse();
        action.Execute(RibbonCommandContext.Empty);
        actionCount.Should().Be(0);
        prepareCount.Should().Be(0);

        enabled = true;
        action.Execute(RibbonCommandContext.Empty);
        toggle.Execute(RibbonCommandContext.Empty);
        value.Execute(RibbonCommandContext.ForSelectedValue("Consolas"));

        actionCount.Should().Be(1);
        checkedState.Should().BeTrue();
        toggle.GetState().IsChecked.Should().BeTrue();
        value.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().Value.Should().Be("Aptos");
        selectedValue.Should().Be("Consolas");
        prepareCount.Should().Be(3);
    }

    [Fact]
    public void Binding_ports_build_the_complete_grouped_registry_and_keep_adapter_ids_separate()
    {
        var bindings = new FreeWRibbonCommandBindingPorts();
        var commands = Enum.GetValues<FreeWRibbonCommandAction>()
            .ToDictionary(action => action, _ => (IRibbonCommand)new RecordingCommand());

        foreach (var (action, command) in commands)
            bindings.Bind(action, command);

        var adapterId = new RibbonCommandId("freew.adapter-only");
        var adapterCommand = new RecordingCommand();
        bindings.Register(adapterId, adapterCommand);

        var result = bindings.Build();

        result.CanonicalCommandIds.Should().HaveCount(405).And.OnlyHaveUniqueItems();
        result.CanonicalCommandIds.Should().BeEquivalentTo(
            FreeWRibbonCommandWorkflow.Routes.Select(route => new RibbonCommandId(route.CommandId)));
        result.CommandGroups.Keys.Should().BeEquivalentTo(Enum.GetValues<FreeWRibbonCommandGroup>());
        result.AdapterCommandIds.Should().Equal(adapterId);

        foreach (var route in FreeWRibbonCommandWorkflow.Routes)
        {
            result.Registry.TryGet(route.CommandId, out var command).Should().BeTrue();
            command.Should().BeSameAs(commands[route.Action]);
        }

        result.Registry.TryGet(adapterId, out var resolvedAdapter).Should().BeTrue();
        resolvedAdapter.Should().BeSameAs(adapterCommand);
    }

    [Fact]
    public void Execution_profile_completes_missing_native_ports_as_disabled_commands()
    {
        var bindings = new FreeWRibbonCommandBindingPorts();
        var native = new RecordingCommand();
        bindings.Bind(FreeWRibbonCommandAction.Bold, native);
        bindings.Register("freew.adapter-only", native);

        var result = FreeWRibbonExecutionProfile.Build(bindings);

        result.CanonicalCommandIds.Should().HaveCount(405).And.OnlyHaveUniqueItems();
        result.Registry.TryGet("freew.bold", out var bold).Should().BeTrue();
        bold.Should().BeSameAs(native);

        result.Registry.TryGet("freew.about", out var unavailable).Should().BeTrue();
        unavailable.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeFalse();
        unavailable.Execute(RibbonCommandContext.Empty);

        result.Registry.TryGet("freew.adapter-only", out var adapter).Should().BeTrue();
        adapter.Should().BeSameAs(native);
    }

    [Fact]
    public void Host_execution_profile_owns_direct_shell_routes_and_preserves_fallback_state()
    {
        var cutCount = 0;
        var findCount = 0;
        var aboutCount = 0;
        var acceptCount = 0;
        var reviewingPaneVisible = false;
        var notesPaneVisible = false;
        var balloonsVisible = false;
        var ports = FreeWRibbonHostExecutionPorts.Empty with
        {
            Cut = () => cutCount++,
            OpenFindReplaceDialog = () => findCount++,
            OpenAbout = () => aboutCount++,
            AcceptThisChange = () => acceptCount++,
            ToggleReviewingPane = () => reviewingPaneVisible = !reviewingPaneVisible,
            IsReviewingPaneVisible = () => reviewingPaneVisible,
            ToggleNotesPane = () => notesPaneVisible = !notesPaneVisible,
            IsNotesPaneVisible = () => notesPaneVisible,
            ToggleReviewBalloons = () => balloonsVisible = !balloonsVisible,
            IsReviewBalloonsActive = () => balloonsVisible,
        };
        var bindings = new FreeWRibbonCommandBindingPorts();

        var hostCommands = FreeWRibbonHostExecutionProfile.Register(
            bindings,
            ports,
            registerFileAdapterCommands: true);
        var registry = FreeWRibbonExecutionProfile.Build(bindings).Registry;

        registry.TryGet("freew.cut", out var cut).Should().BeTrue();
        registry.TryGet("freew.find", out var find).Should().BeTrue();
        registry.TryGet("freew.replace", out var replace).Should().BeTrue();
        registry.TryGet("freew.about", out var about).Should().BeTrue();
        registry.TryGet("freew.open", out var open).Should().BeTrue();
        registry.TryGet("freew.accept-this", out var accept).Should().BeTrue();
        registry.TryGet("freew.reviewing-pane", out var reviewingPane).Should().BeTrue();
        registry.TryGet("freew.show-notes", out var notesPane).Should().BeTrue();
        registry.TryGet("freew.show-markup-balloons", out var balloons).Should().BeTrue();
        hostCommands.ShowMarkupBalloons.Should().BeSameAs(balloons);

        cut!.Execute(RibbonCommandContext.Empty);
        find!.Execute(RibbonCommandContext.Empty);
        replace!.Execute(RibbonCommandContext.Empty);
        about!.Execute(RibbonCommandContext.Empty);
        open!.Execute(RibbonCommandContext.Empty);
        accept!.Execute(RibbonCommandContext.Empty);
        reviewingPane!.Execute(RibbonCommandContext.Empty);
        notesPane!.Execute(RibbonCommandContext.Empty);
        balloons!.Execute(RibbonCommandContext.Empty);

        cutCount.Should().Be(1);
        findCount.Should().Be(2);
        aboutCount.Should().Be(1);
        acceptCount.Should().Be(1);
        reviewingPaneVisible.Should().BeTrue();
        notesPaneVisible.Should().BeTrue();
        balloonsVisible.Should().BeTrue();
        ((IRibbonStatefulCommand)reviewingPane).GetState().IsChecked.Should().BeTrue();
        ((IRibbonStatefulCommand)notesPane).GetState().IsChecked.Should().BeTrue();
        ((IRibbonStatefulCommand)balloons).GetState().IsChecked.Should().BeTrue();

        registry.TryGet("freew.screen-clipping", out var unavailable).Should().BeTrue();
        unavailable.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeFalse();
        registry.TryGet("freew.screenshot", out var screenshot).Should().BeTrue();
        screenshot.Should().BeSameAs(unavailable);
    }

    [Fact]
    public void Callback_ports_preserve_action_toggle_and_value_state_contracts()
    {
        var bindings = new FreeWRibbonCommandBindingPorts();
        var prepared = 0;
        var invoked = 0;
        var isChecked = false;
        string? selectedValue = null;

        var action = bindings.BindAction(
            FreeWRibbonCommandAction.Find,
            () => invoked++,
            prepareExecution: () => prepared++);
        var toggle = bindings.BindToggle(
            FreeWRibbonCommandAction.Bold,
            () => isChecked = !isChecked,
            () => isChecked,
            prepareExecution: () => prepared++);
        var value = bindings.BindValue(
            FreeWRibbonCommandAction.FontFamily,
            selected => selectedValue = selected,
            getValue: () => "Aptos",
            prepareExecution: () => prepared++);

        action.Execute(RibbonCommandContext.Empty);
        toggle.Execute(RibbonCommandContext.Empty);
        value.Execute(RibbonCommandContext.ForSelectedValue("Consolas"));

        invoked.Should().Be(1);
        isChecked.Should().BeTrue();
        toggle.GetState().IsChecked.Should().BeTrue();
        value.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().Value.Should().Be("Aptos");
        selectedValue.Should().Be("Consolas");
        prepared.Should().Be(3);
    }

    [Fact]
    public void Editor_execution_profile_owns_chart_and_smartart_state_and_catalog_expansion()
    {
        Chart? chart = null;
        SmartArt? smartArt = null;
        ChartKind? appliedKind = null;
        ChartStyle? appliedChartStyle = null;
        ChartColorScheme? appliedChartColor = null;
        SmartArtStructureOperation? structureOperation = null;
        SmartArtStyle? appliedSmartArtStyle = null;
        var prepared = 0;
        var bindings = new FreeWRibbonCommandBindingPorts();

        var commands = FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(
            bindings,
            new FreeWRibbonChartSmartArtExecutionPorts(
                PrepareExecution: () => prepared++,
                CompleteExecution: () => { },
                SelectedChart: () => chart,
                SetChartKind: kind => appliedKind = kind,
                ApplyChartStyle: style => appliedChartStyle = style,
                ApplyChartColorScheme: scheme => appliedChartColor = scheme,
                ApplyChartQuickLayout: _ => { },
                ToggleChartLegend: () => { },
                ShowChartTitleDialogAsync: _ => ValueTask.FromResult<ChartTitleDialogResult?>(null),
                ApplyChartTitleOutcome: _ => { },
                ToggleChartTitleFallback: null,
                ShowChartAxisTitlesDialogAsync: _ => ValueTask.FromResult<ChartAxisTitlesDialogResult?>(null),
                ApplyChartAxisTitlesOutcome: _ => { },
                ToggleChartAxisTitlesFallback: null,
                ShowChartDataDialogAsync: _ => ValueTask.FromResult<Chart?>(null),
                ApplyChartDataOutcome: _ => { },
                ShowChartSizeDialogAsync: _ => ValueTask.FromResult<ChartSizeDialogResult?>(null),
                ApplyChartSizeOutcome: _ => { },
                SelectedSmartArt: () => smartArt,
                MutateSmartArt: operation => structureOperation = operation,
                ApplySmartArtLayout: _ => { },
                ApplySmartArtColorScheme: _ => { },
                ApplySmartArtStyle: style => appliedSmartArtStyle = style,
                ShowSmartArtEditDialogAsync: _ => ValueTask.FromResult<SmartArt?>(null),
                ApplySmartArtEditOutcome: _ => { }));

        commands.ChartLegend.GetState().Should().Be(
            new RibbonCommandState(IsEnabled: false, IsChecked: false));

        bindings.TryGet("freew.chart-type-column", out var chartType).Should().BeTrue();
        chartType.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeFalse();
        chartType!.Execute(RibbonCommandContext.Empty);
        appliedKind.Should().BeNull();

        chart = Chart.Create(ChartKind.Line, ["A"], [1d]);
        chart.ShowLegend = false;
        ((IRibbonStatefulCommand)chartType!).GetState().IsEnabled.Should().BeTrue();
        commands.ChartLegend.GetState().Should().Be(
            new RibbonCommandState(IsEnabled: true, IsChecked: false));
        chart.ShowLegend = true;
        commands.ChartLegend.GetState().Should().Be(
            new RibbonCommandState(IsEnabled: true, IsChecked: true));
        chartType.Execute(RibbonCommandContext.Empty);
        appliedKind.Should().Be(ChartKind.Column);

        var firstChartStyle = ChartStyle.Catalog[0];
        bindings.TryGet($"freew.chart-style-{firstChartStyle.Id}", out var chartStyle).Should().BeTrue();
        chartStyle!.Execute(RibbonCommandContext.Empty);
        appliedChartStyle.Should().BeSameAs(firstChartStyle);

        var firstChartColor = ChartColorScheme.Catalog[0];
        bindings.TryGet(ChartColorRibbonCommandCatalog.CommandId(firstChartColor), out var chartColor).Should().BeTrue();
        chartColor!.Execute(RibbonCommandContext.Empty);
        appliedChartColor.Should().BeSameAs(firstChartColor);

        smartArt = SmartArt.Create(SmartArtKind.Process, ["One", "Two"]);
        bindings.TryGet("freew.smartart-add-shape", out var addShape).Should().BeTrue();
        addShape!.Execute(RibbonCommandContext.Empty);
        structureOperation.Should().Be(SmartArtStructureOperation.AddShape);

        bindings.TryGet("freew.smartart-change-style", out var smartArtStyle).Should().BeTrue();
        smartArtStyle!.Execute(RibbonCommandContext.ForSelectedValue(SmartArtStyle.Catalog[0].Name));
        appliedSmartArtStyle.Should().BeSameAs(SmartArtStyle.Catalog[0]);
        prepared.Should().Be(5);
    }

    [Fact]
    public void Chart_gallery_commands_share_preview_cancel_and_single_commit_lifecycle()
    {
        var chart = Chart.Create(ChartKind.Column, ["A"], [1d]);
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(
            bindings,
            new FreeWRibbonChartSmartArtExecutionPorts(
                PrepareExecution: () => events.Add("prepare"),
                CompleteExecution: () => { },
                SelectedChart: () => chart,
                SetChartKind: _ => { },
                ApplyChartStyle: _ => events.Add("legacy-style"),
                ApplyChartColorScheme: _ => events.Add("legacy-color"),
                ApplyChartQuickLayout: _ => events.Add("legacy-layout"),
                ToggleChartLegend: () => { },
                ShowChartTitleDialogAsync: null,
                ApplyChartTitleOutcome: _ => { },
                ToggleChartTitleFallback: null,
                ShowChartAxisTitlesDialogAsync: null,
                ApplyChartAxisTitlesOutcome: _ => { },
                ToggleChartAxisTitlesFallback: null,
                ShowChartDataDialogAsync: null,
                ApplyChartDataOutcome: _ => { },
                ShowChartSizeDialogAsync: null,
                ApplyChartSizeOutcome: _ => { },
                SelectedSmartArt: () => null,
                MutateSmartArt: _ => { },
                ApplySmartArtLayout: _ => { },
                ApplySmartArtColorScheme: _ => { },
                ApplySmartArtStyle: _ => { },
                ShowSmartArtEditDialogAsync: null,
                ApplySmartArtEditOutcome: _ => { },
                PreviewChartStyle: style => events.Add($"preview-style:{style.Id}"),
                PreviewChartColorScheme: scheme => events.Add($"preview-color:{scheme.Id}"),
                PreviewChartQuickLayout: layout => events.Add($"preview-layout:{layout.Id}"),
                CancelChartDesignPreview: () => events.Add("cancel"),
                CommitChartStyle: style => events.Add($"commit-style:{style.Id}"),
                CommitChartColorScheme: scheme => events.Add($"commit-color:{scheme.Id}"),
                CommitChartQuickLayout: layout => events.Add($"commit-layout:{layout.Id}")));

        var style = ChartStyle.Catalog[1];
        AssertPreviewLifecycle(
            bindings,
            $"freew.chart-style-{style.Id}",
            events,
            $"preview-style:{style.Id}",
            $"commit-style:{style.Id}");

        var scheme = ChartColorScheme.Catalog[1];
        AssertPreviewLifecycle(
            bindings,
            ChartColorRibbonCommandCatalog.CommandId(scheme),
            events,
            $"preview-color:{scheme.Id}",
            $"commit-color:{scheme.Id}");

        var layout = ChartQuickLayout.Catalog[1];
        AssertPreviewLifecycle(
            bindings,
            $"freew.chart-quick-layout-{layout.Id}",
            events,
            $"preview-layout:{layout.Id}",
            $"commit-layout:{layout.Id}");

        events.Should().NotContain(item => item.StartsWith("legacy-", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_renderers_adapt_chart_galleries_to_the_shared_preview_transaction()
    {
        var wpfCommands = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaCommands = ReadSource(
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWAvaloniaRibbonCommands.cs");
        var wpfEditor = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaEditor = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var wpfGallery = ReadSource("freew", "FreeW.App.Host", "Ribbon", "ChartDesignGallery.cs");

        foreach (var commands in new[] { wpfCommands, avaloniaCommands })
        {
            commands.Should().Contain("PreviewChartStyle:")
                .And.Contain("PreviewChartColorScheme:")
                .And.Contain("PreviewChartQuickLayout:")
                .And.Contain("CancelChartDesignPreview:")
                .And.Contain("CommitChartStyle:")
                .And.Contain("CommitChartColorScheme:")
                .And.Contain("CommitChartQuickLayout:");
        }

        foreach (var editor in new[] { wpfEditor, avaloniaEditor })
        {
            editor.Should().Contain("_editingSession.ChartDesignPreview")
                .And.Contain("ChartDesignPreviews.PreviewStyle(")
                .And.Contain("ChartDesignPreviews.PreviewColorScheme(")
                .And.Contain("ChartDesignPreviews.PreviewQuickLayout(")
                .And.Contain("ChartDesignPreviews.Cancel()")
                .And.Contain("ChartDesignPreviews.CommitStyle(")
                .And.Contain("ChartDesignPreviews.CommitColorScheme(")
                .And.Contain("ChartDesignPreviews.CommitQuickLayout(");
        }

        wpfGallery.Should().Contain("IRibbonPreviewCommand")
            .And.Contain("preview.BeginPreview(RibbonCommandContext.Empty)")
            .And.Contain("preview.CancelPreview()")
            .And.Contain("command.Execute(RibbonCommandContext.Empty)")
            .And.NotContain("DocumentView")
            .And.NotContain("PreviewSelectedChart")
            .And.NotContain("CommitChart")
            .And.NotContain("_savedQuickLayoutId")
            .And.NotContain("_savedStyleId")
            .And.NotContain("_savedColorSchemeId")
            .And.NotContain("RevertChart(");
    }

    [Fact]
    public void SmartArt_gallery_commands_share_preview_cancel_and_single_commit_lifecycle()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["One"]);
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(
            bindings,
            new FreeWRibbonChartSmartArtExecutionPorts(
                PrepareExecution: () => events.Add("prepare"),
                CompleteExecution: () => { },
                SelectedChart: () => null,
                SetChartKind: _ => { },
                ApplyChartStyle: _ => { },
                ApplyChartColorScheme: _ => { },
                ApplyChartQuickLayout: _ => { },
                ToggleChartLegend: () => { },
                ShowChartTitleDialogAsync: null,
                ApplyChartTitleOutcome: _ => { },
                ToggleChartTitleFallback: null,
                ShowChartAxisTitlesDialogAsync: null,
                ApplyChartAxisTitlesOutcome: _ => { },
                ToggleChartAxisTitlesFallback: null,
                ShowChartDataDialogAsync: null,
                ApplyChartDataOutcome: _ => { },
                ShowChartSizeDialogAsync: null,
                ApplyChartSizeOutcome: _ => { },
                SelectedSmartArt: () => smartArt,
                MutateSmartArt: _ => { },
                ApplySmartArtLayout: _ => events.Add("legacy-layout"),
                ApplySmartArtColorScheme: _ => events.Add("legacy-color"),
                ApplySmartArtStyle: _ => events.Add("legacy-style"),
                ShowSmartArtEditDialogAsync: null,
                ApplySmartArtEditOutcome: _ => { },
                PreviewSmartArtLayout: layout => events.Add($"preview-layout:{layout.Id}"),
                PreviewSmartArtColorScheme: scheme => events.Add($"preview-color:{scheme.Id}"),
                PreviewSmartArtStyle: style => events.Add($"preview-style:{style.Id}"),
                CancelSmartArtDesignPreview: () => events.Add("cancel"),
                CommitSmartArtLayout: layout => events.Add($"commit-layout:{layout.Id}"),
                CommitSmartArtColorScheme: scheme => events.Add($"commit-color:{scheme.Id}"),
                CommitSmartArtStyle: style => events.Add($"commit-style:{style.Id}")));

        var layout = SmartArtLayoutPreset.Catalog[1];
        AssertPreviewLifecycle(
            bindings,
            $"freew.smartart-layout-{layout.Id}",
            events,
            $"preview-layout:{layout.Id}",
            $"commit-layout:{layout.Id}");

        var scheme = SmartArtColorScheme.Catalog[1];
        AssertPreviewLifecycle(
            bindings,
            $"freew.smartart-colors-{scheme.Id}",
            events,
            $"preview-color:{scheme.Id}",
            $"commit-color:{scheme.Id}");

        var style = SmartArtStyle.Catalog[1];
        AssertPreviewLifecycle(
            bindings,
            SmartArtCommandPlanner.StyleCommandId(style),
            events,
            $"preview-style:{style.Id}",
            $"commit-style:{style.Id}");

        events.Clear();
        bindings.TryGet("freew.smartart-change-style", out var parent).Should().BeTrue();
        parent!.Execute(RibbonCommandContext.ForSelectedValue(style.Name));
        events.Should().Equal("prepare", $"commit-style:{style.Id}");
        events.Should().NotContain(item => item.StartsWith("legacy-", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_renderers_adapt_SmartArt_galleries_to_the_shared_preview_transaction()
    {
        var wpfCommands = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaCommands = ReadSource(
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWAvaloniaRibbonCommands.cs");
        var wpfEditor = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaEditor = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var wpfGallery = ReadSource("freew", "FreeW.App.Host", "Ribbon", "SmartArtGallery.cs");
        var canonical = ReadSource(
            "freew",
            "FreeW.Ribbon.Definitions",
            "FreeWCanonicalRibbonTabs.Contextual.cs");

        foreach (var commands in new[] { wpfCommands, avaloniaCommands })
        {
            commands.Should().Contain("PreviewSmartArtLayout:")
                .And.Contain("PreviewSmartArtColorScheme:")
                .And.Contain("PreviewSmartArtStyle:")
                .And.Contain("CancelSmartArtDesignPreview:")
                .And.Contain("CommitSmartArtLayout:")
                .And.Contain("CommitSmartArtColorScheme:")
                .And.Contain("CommitSmartArtStyle:");
        }

        foreach (var editor in new[] { wpfEditor, avaloniaEditor })
        {
            editor.Should().Contain("_editingSession.SmartArtDesignPreview")
                .And.Contain("SmartArtDesignPreviews.PreviewLayout(")
                .And.Contain("SmartArtDesignPreviews.PreviewColorScheme(")
                .And.Contain("SmartArtDesignPreviews.PreviewStyle(")
                .And.Contain("SmartArtDesignPreviews.Cancel()")
                .And.Contain("SmartArtDesignPreviews.CommitLayout(")
                .And.Contain("SmartArtDesignPreviews.CommitColorScheme(")
                .And.Contain("SmartArtDesignPreviews.CommitStyle(");
        }

        wpfGallery.Should().Contain("IRibbonPreviewCommand")
            .And.Contain("preview.BeginPreview(RibbonCommandContext.Empty)")
            .And.Contain("preview.CancelPreview()")
            .And.Contain("command.Execute(RibbonCommandContext.Empty)")
            .And.NotContain("DocumentView")
            .And.NotContain("_previewSnapshot")
            .And.NotContain("PreviewSnapshot")
            .And.NotContain("RestorePreviewFields");
        canonical.Should().Contain(
                "group.Dropdown(\"freew.smartart-change-style\", \"Styles\", BuildSmartArtStylesMenu())")
            .And.NotContain("group.ComboBox(\"freew.smartart-change-style\"");
    }

    [Fact]
    public void Editor_execution_profile_applies_typed_chart_and_smartart_dialog_outcomes()
    {
        var chart = Chart.Create(ChartKind.Column, ["A"], [1d]);
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["One"]);
        var replacementChart = Chart.Create(ChartKind.Line, ["B"], [2d]);
        var replacementSmartArt = SmartArt.Create(SmartArtKind.Hierarchy, ["Two"]);
        ChartTitleDialogResult? titleOutcome = null;
        ChartAxisTitlesDialogResult? axisOutcome = null;
        Chart? dataOutcome = null;
        ChartSizeDialogResult? sizeOutcome = null;
        SmartArt? smartArtOutcome = null;
        var completed = 0;
        var bindings = new FreeWRibbonCommandBindingPorts();

        FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(
            bindings,
            new FreeWRibbonChartSmartArtExecutionPorts(
                PrepareExecution: () => { },
                CompleteExecution: () => completed++,
                SelectedChart: () => chart,
                SetChartKind: _ => { },
                ApplyChartStyle: _ => { },
                ApplyChartColorScheme: _ => { },
                ApplyChartQuickLayout: _ => { },
                ToggleChartLegend: () => { },
                ShowChartTitleDialogAsync: selected =>
                {
                    selected.Should().BeSameAs(chart);
                    return ValueTask.FromResult<ChartTitleDialogResult?>(new(true, "Revenue"));
                },
                ApplyChartTitleOutcome: result => titleOutcome = result,
                ToggleChartTitleFallback: null,
                ShowChartAxisTitlesDialogAsync: _ => ValueTask.FromResult<ChartAxisTitlesDialogResult?>(
                    new("Quarter", "Amount")),
                ApplyChartAxisTitlesOutcome: result => axisOutcome = result,
                ToggleChartAxisTitlesFallback: null,
                ShowChartDataDialogAsync: _ => ValueTask.FromResult<Chart?>(replacementChart),
                ApplyChartDataOutcome: result => dataOutcome = result,
                ShowChartSizeDialogAsync: _ => ValueTask.FromResult<ChartSizeDialogResult?>(new(400, 300)),
                ApplyChartSizeOutcome: result => sizeOutcome = result,
                SelectedSmartArt: () => smartArt,
                MutateSmartArt: _ => { },
                ApplySmartArtLayout: _ => { },
                ApplySmartArtColorScheme: _ => { },
                ApplySmartArtStyle: _ => { },
                ShowSmartArtEditDialogAsync: selected =>
                {
                    selected.Should().BeSameAs(smartArt);
                    return ValueTask.FromResult<SmartArt?>(replacementSmartArt);
                },
                ApplySmartArtEditOutcome: result => smartArtOutcome = result));

        Execute(FreeWRibbonCommandAction.ChartTitle);
        Execute(FreeWRibbonCommandAction.ChartAxisTitles);
        Execute(FreeWRibbonCommandAction.ChartEditData);
        Execute(FreeWRibbonCommandAction.ChartSize);
        Execute(FreeWRibbonCommandAction.SmartartEditText);

        titleOutcome.Should().Be(new ChartTitleDialogResult(true, "Revenue"));
        axisOutcome.Should().Be(new ChartAxisTitlesDialogResult("Quarter", "Amount"));
        dataOutcome.Should().BeSameAs(replacementChart);
        sizeOutcome.Should().Be(new ChartSizeDialogResult(400, 300));
        smartArtOutcome.Should().BeSameAs(replacementSmartArt);
        completed.Should().Be(5);

        void Execute(FreeWRibbonCommandAction action)
        {
            var commandId = FreeWRibbonCommandWorkflow.Routes.Single(route => route.Action == action).CommandId;
            bindings.TryGet(commandId, out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        }
    }

    [Fact]
    public void Editor_execution_profile_owns_image_and_table_selection_dialog_and_outcome_workflows()
    {
        var image = new InlineImage([0], 1, 1, ImageFormat.Png);
        var table = Table.Create(1, 1);
        var context = new ModelTableContext(table, table.Rows[0], table.Rows[0].Cells[0]);
        ImageCropDialogResult? cropOutcome = null;
        TableFormulaDialogInitialState? formulaRequest = null;
        TableFormulaField? formulaOutcome = null;
        TablePropertiesValues? propertiesOutcome = null;
        char? delimiterOutcome = null;
        var reset = 0;
        var completed = 0;
        var bindings = new FreeWRibbonCommandBindingPorts();
        var properties = new TablePropertiesValues(
            null,
            TableAlignment.Left,
            false,
            null,
            null,
            null,
            null,
            TableRowHeightRule.Auto,
            true,
            false,
            null,
            null,
            TableCellVerticalAlignment.Top,
            null,
            true,
            false);

        FreeWRibbonEditorExecutionProfile.RegisterImageTableWorkflows(
            bindings,
            new FreeWRibbonImageExecutionPorts(
                PrepareExecution: () => { },
                CompleteExecution: () => completed++,
                SelectedImage: () => image,
                ShowCropDialogAsync: selected =>
                {
                    selected.Should().BeSameAs(image);
                    return ValueTask.FromResult<ImageCropDialogResult?>(new(0.1, 0.2, 0.3, 0.1));
                },
                ApplyCropOutcome: result => cropOutcome = result,
                ResetImage: () => reset++),
            new FreeWRibbonTableExecutionPorts(
                PrepareExecution: () => { },
                CompleteExecution: () => completed++,
                SelectedCell: () => new FreeWRibbonTableCellSelection(table, 0, 0),
                SelectedContext: () => context,
                CanConvertToText: () => true,
                ShowFormulaDialogAsync: request =>
                {
                    formulaRequest = request;
                    return ValueTask.FromResult<TableFormulaField?>(new("=SUM(ABOVE)"));
                },
                ApplyFormulaOutcome: result => formulaOutcome = result,
                ShowPropertiesDialogAsync: selected =>
                {
                    selected.Should().BeSameAs(context);
                    return ValueTask.FromResult<TablePropertiesValues?>(properties);
                },
                ApplyPropertiesOutcome: result => propertiesOutcome = result,
                ShowTableToTextDialogAsync: () => ValueTask.FromResult<char?>(';'),
                ApplyTableToTextOutcome: result => delimiterOutcome = result));

        Execute(FreeWRibbonCommandAction.ImageCrop);
        Execute(FreeWRibbonCommandAction.ImageReset);
        Execute(FreeWRibbonCommandAction.TableFormula);
        Execute(FreeWRibbonCommandAction.TableProperties);
        Execute(FreeWRibbonCommandAction.TableToText);

        cropOutcome.Should().Be(new ImageCropDialogResult(0.1, 0.2, 0.3, 0.1));
        reset.Should().Be(1);
        formulaRequest.Should().NotBeNull();
        formulaOutcome.Should().Be(new TableFormulaField("=SUM(ABOVE)"));
        propertiesOutcome.Should().BeSameAs(properties);
        delimiterOutcome.Should().Be(';');
        completed.Should().Be(4, "reset is synchronous and has no dialog completion phase");

        void Execute(FreeWRibbonCommandAction action)
        {
            var commandId = FreeWRibbonCommandWorkflow.Routes.Single(route => route.Action == action).CommandId;
            bindings.TryGet(commandId, out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        }
    }

    [Fact]
    public async Task Async_stateful_port_command_resumes_an_incomplete_native_dialog_operation()
    {
        var dialog = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new FreeWRibbonAsyncStatefulPortCommand(
            async _ => applied.SetResult(await dialog.Task),
            () => new RibbonCommandState(IsEnabled: true));

        command.Execute(RibbonCommandContext.Empty);
        applied.Task.IsCompleted.Should().BeFalse();

        dialog.SetResult("accepted");
        (await applied.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("accepted");
    }

    [Fact]
    public void Editor_execution_profile_declares_disjoint_table_reference_and_header_footer_families()
    {
        var families = new[]
        {
            FreeWRibbonEditorExecutionProfile.TableActions,
            FreeWRibbonEditorExecutionProfile.ReferenceActions,
            FreeWRibbonEditorExecutionProfile.HeaderFooterActions,
        };

        families.SelectMany(static family => family).Should().OnlyHaveUniqueItems();
        FreeWRibbonEditorExecutionProfile.TableActions.Should().Contain(
            FreeWRibbonCommandAction.TableFormula);
        FreeWRibbonEditorExecutionProfile.ReferenceActions.Should().Contain(
            FreeWRibbonCommandAction.TableOfAuthorities);
        FreeWRibbonEditorExecutionProfile.HeaderFooterActions.Should().Contain(
            FreeWRibbonCommandAction.HfEditEvenFooter);
    }

    [Fact]
    public void Editor_family_builder_defers_all_registration_to_the_shared_profile()
    {
        var bindings = new FreeWRibbonCommandBindingPorts();
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();
        var canonical = new RecordingCommand();
        var adapter = new RecordingCommand();

        builder.Bind(FreeWRibbonCommandAction.TableProperties, canonical);
        builder.Register("freew.table-native-adapter", adapter);

        bindings.TryGet("freew.table-properties", out _).Should().BeFalse();
        bindings.TryGet("freew.table-native-adapter", out _).Should().BeFalse();

        FreeWRibbonEditorExecutionProfile.RegisterFamily(bindings, builder.Build());

        bindings.TryGet("freew.table-properties", out var registeredCanonical).Should().BeTrue();
        registeredCanonical.Should().BeSameAs(canonical);
        bindings.TryGet("freew.table-native-adapter", out var registeredAdapter).Should().BeTrue();
        registeredAdapter.Should().BeSameAs(adapter);
    }

    [Fact]
    public void Editor_execution_profile_owns_floating_planners_and_shape_state()
    {
        Shape? shape = null;
        ShapeFill? fill = null;
        ObjectFormatSizeDimension? sizeDimension = null;
        double? sizePoints = null;
        ObjectFormatTarget? wrappedTarget = null;
        ImageWrapping? wrapping = null;
        ObjectFormatTarget? zOrderTarget = null;
        var hasTransformSelection = true;
        var transformed = false;
        var feedback = new List<FreeWRibbonFloatingFeedback>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        FreeWRibbonEditorExecutionProfile.RegisterFloating(
            bindings,
            new FreeWRibbonFloatingExecutionPorts(
                PrepareExecution: () => { },
                HasSelection: target => target == ObjectFormatTarget.Shape && shape is not null,
                HasTransformSelection: () => hasTransformSelection,
                ApplyWrap: (target, value) =>
                {
                    wrappedTarget = target;
                    wrapping = value;
                },
                ApplyTransform: (_, _) => transformed = true,
                ApplyZOrder: (target, _) =>
                {
                    zOrderTarget = target;
                    return true;
                },
                ApplySize: (_, dimension, points) =>
                {
                    sizeDimension = dimension;
                    sizePoints = points;
                },
                ApplyParagraphAlignment: (_, _) => { },
                CanArrange: _ => false,
                Arrange: _ => { },
                SelectedShape: () => shape,
                SetShapeKind: _ => { },
                ConvertShapeToFreeform: () => { },
                BeginShapeEditPoints: () => { },
                SetShapeTextDirection: _ => { },
                SetShapeExtendedFill: value => fill = value,
                SetShapeFill: _ => { },
                SetShapeOutline: (_, _, _) => { },
                SetShapeEffects: _ => { },
                ApplyShapeStyle: _ => { },
                CanGroup: () => false,
                Group: () => { },
                CanUngroup: () => false,
                Ungroup: () => { },
                ShowFeedback: feedback.Add));

        bindings.TryGet("freew.shape-edit-shape", out var editShape).Should().BeTrue();
        editShape!.Execute(RibbonCommandContext.Empty);
        feedback.Should().ContainSingle().Which.Should().Be(FreeWRibbonFloatingFeedbackCatalog.EditShape);

        bindings.TryGet("freew.object-group", out var group).Should().BeTrue();
        group!.Execute(RibbonCommandContext.Empty);
        feedback.Should().Contain(FreeWRibbonFloatingFeedbackCatalog.GroupSelectionRequired);

        bindings.TryGet("freew.shape-fill-gradient-blue", out var gradient).Should().BeTrue();
        gradient.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeFalse();

        bindings.TryGet("freew.shape-rotate-right90", out var rotate).Should().BeTrue();
        rotate.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeTrue();
        rotate!.Execute(RibbonCommandContext.Empty);
        transformed.Should().BeTrue();

        hasTransformSelection = false;
        ((IRibbonStatefulCommand)rotate).GetState().IsEnabled.Should().BeFalse();

        shape = Shape.Preset(ShapeKind.Rectangle, 100, 50, "#FFFFFF");
        ((IRibbonStatefulCommand)gradient!).GetState().IsEnabled.Should().BeTrue();
        gradient!.Execute(RibbonCommandContext.Empty);
        fill.Should().NotBeNull();

        bindings.TryGet("freew.layout-wrap-square", out var layoutWrap).Should().BeTrue();
        layoutWrap.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeTrue();
        layoutWrap!.Execute(RibbonCommandContext.Empty);
        wrappedTarget.Should().Be(ObjectFormatTarget.Shape);
        wrapping.Should().Be(ImageWrapping.Square);

        bindings.TryGet("freew.layout-bring-forward", out var layoutBringForward).Should().BeTrue();
        layoutBringForward!.Execute(RibbonCommandContext.Empty);
        zOrderTarget.Should().Be(ObjectFormatTarget.Shape);

        bindings.TryGet("freew.shape-width", out var width).Should().BeTrue();
        width!.Execute(RibbonCommandContext.ForSelectedValue("144"));
        sizeDimension.Should().Be(ObjectFormatSizeDimension.Width);
        sizePoints.Should().Be(144);

        width.Execute(RibbonCommandContext.ForSelectedValue("invalid"));
        sizePoints.Should().Be(144);
    }

    [Fact]
    public void Renderer_sources_cannot_reintroduce_catalog_owned_registration_literals()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = ReadSource(
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWAvaloniaRibbonCommands.cs");
        var wpfMainWindow = ReadSource("freew", "FreeW.App.Host", "MainWindow.cs");
        var wpfNativePorts = ReadSource(
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWWpfRibbonNativeExecutionPorts.cs");
        var hostProfile = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "FreeWRibbonHostExecutionProfile.cs");
        var editorProfile = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "FreeWRibbonEditorExecutionProfile.cs");
        var citationWorkflow = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "CitationRibbonWorkflow.cs");
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var portableRibbonSources = string.Concat(
            Directory.GetFiles(
                    Path.Combine(root, "freew", "FreeW.App.Presentation", "Ribbon"),
                    "*.cs")
                .Select(File.ReadAllText));
        var directRegistration = new Regex(
            @"(?:registry|r)\.Register\(\s*""(?<id>freew\.[^""]+)""",
            RegexOptions.CultureInvariant);

        var copiedIds = new[] { wpf, avalonia }
            .SelectMany(source => directRegistration.Matches(source)
                .Select(match => match.Groups["id"].Value))
            .Intersect(
                FreeWRibbonCommandWorkflow.Routes.Select(route => route.CommandId),
                StringComparer.Ordinal)
            .ToArray();

        copiedIds.Should().BeEmpty();
        wpf.Should().Contain("new FreeWRibbonCommandBindingPorts()");
        avalonia.Should().Contain("new FreeWRibbonCommandBindingPorts()");
        wpf.Should().Contain("hostCommands?.ShowMarkupBalloons is { } showMarkupBalloons");
        wpf.Should().NotContain("new ActionRibbonCommand(onToggleBalloons)");
        wpf.Should().NotContain("var onToggleBalloons");
        wpf.Should().Contain("return FreeWRibbonExecutionProfile.Build(registry).Registry;");
        avalonia.Should().Contain("return FreeWRibbonExecutionProfile.Build(r).Registry;");
        wpf.Should().NotContain(".Build().Registry");
        avalonia.Should().NotContain(".Build().Registry");
        wpf.Should().NotContain("FreeWRibbonCommandWorkflow.Register(");
        avalonia.Should().NotContain("FreeWRibbonCommandWorkflow.Register(");
        File.Exists(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWRibbon.cs")).Should().BeFalse();
        File.Exists(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew", "FreeW.App.Host", "FreeWRibbon.cs")).Should().BeFalse();
        wpf.Should().NotContain("NativeCanonicalCommands");
        wpf.Should().NotContain("CreateNativeFloatingCommands");
        avalonia.Should().NotContain("RibbonHostCallbacks");
        avalonia.Should().Contain(
            "FreeWRibbonHostExecutionProfile.Register(r, callbacks, registerFileAdapterCommands: true);");
        wpf.Should().Contain("FreeWRibbonHostExecutionPorts hostPorts");
        wpf.Should().Contain("private static RibbonCommandRegistry BuildCore(");
        wpf.Should().Contain("FreeWWpfRibbonNativeExecutionPorts nativePorts");
        wpf.Should().NotContain("Action? onPrintPreview");
        wpf.Should().NotContain("Compatibility seam for focused WPF command tests");
        wpf.Should().Contain("FreeWRibbonHostExecutionProfile.Register(");
        wpf.Should().Contain("registerFileAdapterCommands: true");
        wpf.Should().Contain("Routed(FreeWRibbonCommandAction.Cut, ApplicationCommands.Cut);");
        wpf.Should().Contain("Routed(FreeWRibbonCommandAction.Copy, ApplicationCommands.Copy);");
        wpf.Should().Contain("Routed(FreeWRibbonCommandAction.Paste, ApplicationCommands.Paste);");
        wpfMainWindow.Should().Contain("CreateRibbonHostExecutionPorts()");
        wpfMainWindow.Should().Contain("new FreeWWpfRibbonNativeExecutionPorts(");
        wpfMainWindow.Should().NotContain("onPrintPreview:");
        wpfNativePorts.Should().Contain("AskHeaderFooterText");
        wpfNativePorts.Should().Contain("ResolveFieldEditor");
        wpfNativePorts.Should().Contain("AskFieldInstruction");
        wpfNativePorts.Should().NotContain("OpenFindReplaceDialog");
        wpfNativePorts.Should().NotContain("ToggleReviewingPane");
        wpf.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterFloating(");
        wpf.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(");
        wpf.Should().Contain("chartCommands.ChartLegend");
        avalonia.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterFloating(");
        avalonia.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(");
        wpf.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterImageTableWorkflows(");
        avalonia.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterImageTableWorkflows(");
        wpf.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterFamilies(");
        avalonia.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterFamilies(");
        editorProfile.Should().Contain("FreeWRibbonEditorCommandFamilyBuilder");
        editorProfile.Should().Contain("Func<Chart, ValueTask<ChartTitleDialogResult?>>?");
        editorProfile.Should().Contain("Func<ModelTableContext, ValueTask<TablePropertiesValues?>>?");
        editorProfile.Should().NotContain("IRibbonCommand ChartTitleCommand");
        editorProfile.Should().NotContain("IRibbonCommand SmartArtEditTextCommand");
        editorProfile.Should().NotContain("CaptureBoundFamily");
        wpf.Should().NotContain("RegisterSharedEditorCommandFamilies");
        avalonia.Should().NotContain("RegisterSharedEditorCommandFamilies");
        avalonia.Should().NotContain("RegisterFloatingFormatCommands");
        avalonia.Should().NotContain("RegisterChartSmartArtFormatCommands");
        avalonia.Should().NotContain("RegisterShapeFillOutlineCommands");
        avalonia.Should().NotContain("new ActionRibbonCommand(callbacks.OpenFindReplaceDialog)");
        avalonia.Should().NotContain("HostCommand(callbacks.OpenAbout)");
        avalonia.Should().NotContain("class UnavailableRibbonCommand");

        foreach (var retiredRendererCommand in new[]
                 {
                     "FloatingTransformCommand",
                     "FloatingZOrderCommand",
                     "ImageWrapCommand",
                     "ShapeWrapCommand",
                     "ShapeKindCommand",
                     "ShapeEffectsCommand",
                     "ShapeStylesGalleryCommand",
                     "FloatingObjectArrangeCommand",
                     "ChartSizeCommand",
                     "SmartArtEditTextRibbonCommand",
                     "ImageCropCommand",
                     "ImageResetCommand",
                     "TableFormulaCommand",
                     "TablePropertiesCommand",
                     "TableToTextCommand",
                 })
        {
            wpf.Should().NotContain(retiredRendererCommand);
            avalonia.Should().NotContain(retiredRendererCommand);
        }

        foreach (var sharedCatalogExpansion in new[]
                 {
                     "ObjectFormatCommandPlanner.WrapCommands",
                     "ObjectFormatCommandPlanner.TransformCommands",
                     "ObjectFormatCommandPlanner.ZOrderCommands",
                     "ObjectFormatCommandPlanner.SizeCommands",
                     "ObjectFormatCommandPlanner.ShapeFillCommands",
                     "ObjectFormatCommandPlanner.ShapeOutlineCommands",
                     "ChartStyle.Catalog",
                     "ChartColorScheme.Catalog",
                     "ChartQuickLayout.Catalog",
                     "SmartArtLayoutPreset.Catalog",
                     "SmartArtColorScheme.Catalog",
                     "ShapeStylePreset.Catalog",
                 })
        {
            wpf.Should().NotContain(sharedCatalogExpansion);
            avalonia.Should().NotContain(sharedCatalogExpansion);
            editorProfile.Should().Contain(sharedCatalogExpansion);
        }

        foreach (var portableCommand in new[]
                 {
                     "FreeWRibbonFormatPainterCommand",
                     "FreeWRibbonNumericValueCommand",
                     "FreeWRibbonStatefulPortCommand",
                 })
        {
            wpf.Should().Contain(portableCommand);
            avalonia.Should().Contain(portableCommand);
        }

        wpf.Should().NotContain("FreeWRibbonChoiceCommand");
        avalonia.Should().NotContain("FreeWRibbonChoiceCommand");
        citationWorkflow.Should().Contain("FreeWRibbonChoiceCommand");

        foreach (var rendererCommand in new[]
                 {
                     "class FormatPainterCommand",
                     "class LineSpacingCommand",
                     "class CitationStyleCommand",
                     "class ImageStylePresetCommand",
                     "class ChartQuickLayoutRibbonCommand",
                     "class SmartArtStructureRibbonCommand",
                     "class SmartArtStyleRibbonCommand",
                 })
        {
            wpf.Should().NotContain(rendererCommand);
            avalonia.Should().NotContain(rendererCommand);
        }

        var catalogOwnedActions = ImageAdjustmentCommandPlanner.AdjustmentPresets
            .Select(preset => preset.Action.ToString())
            .Concat(ImageAdjustmentCommandPlanner.RecolorPresets.Select(preset => preset.Action.ToString()))
            .Concat(ImageAdjustmentCommandPlanner.EffectPresets
                .Where(preset => preset.Action.HasValue)
                .Select(preset => preset.Action!.Value.ToString()))
            .ToHashSet(StringComparer.Ordinal);
        var explicitlyOwnedActions = Enum.GetNames<FreeWRibbonCommandAction>()
            .Where(action => !catalogOwnedActions.Contains(action))
            .ToArray();

        RibbonActions(wpf).Should().HaveCountLessThan(Enum.GetValues<FreeWRibbonCommandAction>().Length);
        RibbonActions(wpf + portableRibbonSources).Except(catalogOwnedActions)
            .Should().BeEquivalentTo(explicitlyOwnedActions);
        RibbonActions(avalonia).Should().HaveCountLessThan(Enum.GetValues<FreeWRibbonCommandAction>().Length);
        RibbonActions(avalonia + portableRibbonSources).Except(catalogOwnedActions)
            .Should().BeEquivalentTo(explicitlyOwnedActions);

        foreach (var helperPrefix in new[]
                 {
                     "Routed(\"freew.",
                     "Toggle(\"freew.",
                     "PageSetting(\"freew.",
                     "RegisterImageMutation(r, editor, \"freew.",
                     "RegisterSmartArtStructureCommand(r, editor, \"freew.",
                     "RegisterMailingsAlias(r, \"freew.",
                 })
        {
            wpf.Should().NotContain(helperPrefix);
            avalonia.Should().NotContain(helperPrefix);
        }
    }

    private static string ReadSource(params string[] relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(relativePath.Aggregate(root, Path.Combine));
    }

    private static string[] RibbonActions(string source) =>
        Regex.Matches(source, @"FreeWRibbonCommandAction\.(?<action>[A-Za-z0-9_]+)")
            .Select(match => match.Groups["action"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void AssertPreviewLifecycle(
        IRibbonCommandRegistry bindings,
        RibbonCommandId commandId,
        List<string> events,
        string previewEvent,
        string commitEvent)
    {
        bindings.TryGet(commandId, out var command).Should().BeTrue();
        command.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeTrue();
        var preview = command.Should().BeAssignableTo<IRibbonPreviewCommand>().Subject;

        events.Clear();
        preview.BeginPreview(RibbonCommandContext.Empty);
        preview.CancelPreview();
        preview.Execute(RibbonCommandContext.Empty);

        events.Should().Equal(previewEvent, "cancel", "prepare", commitEvent);
    }

    private sealed class RecordingCommand : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
        }
    }
}
