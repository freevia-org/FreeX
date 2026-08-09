using System.IO;
using System.Text.RegularExpressions;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWRibbonCommandWorkflowTests
{
    [Fact]
    public void Routes_are_unique_and_cover_the_shared_renderer_groups()
    {
        var routes = FreeWRibbonCommandWorkflow.Routes;

        routes.Should().HaveCount(399);
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

        result.CanonicalCommandIds.Should().HaveCount(399).And.OnlyHaveUniqueItems();
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

        result.CanonicalCommandIds.Should().HaveCount(399).And.OnlyHaveUniqueItems();
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
        var ports = FreeWRibbonHostExecutionPorts.Empty with
        {
            Cut = () => cutCount++,
            OpenFindReplaceDialog = () => findCount++,
            OpenAbout = () => aboutCount++,
        };
        var bindings = new FreeWRibbonCommandBindingPorts();

        FreeWRibbonHostExecutionProfile.Register(
            bindings,
            ports,
            registerFileAdapterCommands: true);
        var registry = FreeWRibbonExecutionProfile.Build(bindings).Registry;

        registry.TryGet("freew.cut", out var cut).Should().BeTrue();
        registry.TryGet("freew.find", out var find).Should().BeTrue();
        registry.TryGet("freew.replace", out var replace).Should().BeTrue();
        registry.TryGet("freew.about", out var about).Should().BeTrue();
        registry.TryGet("freew.open", out var open).Should().BeTrue();

        cut!.Execute(RibbonCommandContext.Empty);
        find!.Execute(RibbonCommandContext.Empty);
        replace!.Execute(RibbonCommandContext.Empty);
        about!.Execute(RibbonCommandContext.Empty);
        open!.Execute(RibbonCommandContext.Empty);

        cutCount.Should().Be(1);
        findCount.Should().Be(2);
        aboutCount.Should().Be(1);

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
        SmartArtStructureOperation? structureOperation = null;
        SmartArtStyle? appliedSmartArtStyle = null;
        var prepared = 0;
        var bindings = new FreeWRibbonCommandBindingPorts();

        FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(
            bindings,
            new FreeWRibbonChartSmartArtExecutionPorts(
                PrepareExecution: () => prepared++,
                SelectedChart: () => chart,
                SetChartKind: kind => appliedKind = kind,
                ApplyChartStyle: style => appliedChartStyle = style,
                ApplyChartColorScheme: _ => { },
                ApplyChartQuickLayout: _ => { },
                ToggleChartLegend: () => { },
                ChartTitleCommand: new RecordingCommand(),
                ChartAxisTitlesCommand: new RecordingCommand(),
                ChartEditDataCommand: new RecordingCommand(),
                ChartSizeCommand: new RecordingCommand(),
                SelectedSmartArt: () => smartArt,
                MutateSmartArt: operation => structureOperation = operation,
                ApplySmartArtLayout: _ => { },
                ApplySmartArtColorScheme: _ => { },
                ApplySmartArtStyle: style => appliedSmartArtStyle = style,
                SmartArtEditTextCommand: new RecordingCommand()));

        bindings.TryGet("freew.chart-type-column", out var chartType).Should().BeTrue();
        chartType.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeFalse();
        chartType!.Execute(RibbonCommandContext.Empty);
        appliedKind.Should().BeNull();

        chart = Chart.Create(ChartKind.Line, ["A"], [1d]);
        ((IRibbonStatefulCommand)chartType!).GetState().IsEnabled.Should().BeTrue();
        chartType.Execute(RibbonCommandContext.Empty);
        appliedKind.Should().Be(ChartKind.Column);

        var firstChartStyle = ChartStyle.Catalog[0];
        bindings.TryGet($"freew.chart-style-{firstChartStyle.Id}", out var chartStyle).Should().BeTrue();
        chartStyle!.Execute(RibbonCommandContext.Empty);
        appliedChartStyle.Should().BeSameAs(firstChartStyle);

        smartArt = SmartArt.Create(SmartArtKind.Process, ["One", "Two"]);
        bindings.TryGet("freew.smartart-add-shape", out var addShape).Should().BeTrue();
        addShape!.Execute(RibbonCommandContext.Empty);
        structureOperation.Should().Be(SmartArtStructureOperation.AddShape);

        bindings.TryGet("freew.smartart-change-style", out var smartArtStyle).Should().BeTrue();
        smartArtStyle!.Execute(RibbonCommandContext.ForSelectedValue(SmartArtStyle.Catalog[0].Name));
        appliedSmartArtStyle.Should().BeSameAs(SmartArtStyle.Catalog[0]);
        prepared.Should().Be(4);
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
    public void Editor_execution_profile_owns_floating_planners_and_shape_state()
    {
        Shape? shape = null;
        ShapeFill? fill = null;
        ObjectFormatSizeDimension? sizeDimension = null;
        double? sizePoints = null;
        var bindings = new FreeWRibbonCommandBindingPorts();

        FreeWRibbonEditorExecutionProfile.RegisterFloating(
            bindings,
            new FreeWRibbonFloatingExecutionPorts(
                PrepareExecution: () => { },
                HasSelection: target => target == ObjectFormatTarget.Shape && shape is not null,
                ApplyWrap: (_, _) => { },
                ApplyTransform: (_, _) => true,
                ApplyZOrder: (_, _) => true,
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
                Ungroup: () => { }));

        bindings.TryGet("freew.shape-fill-gradient-blue", out var gradient).Should().BeTrue();
        gradient.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsEnabled.Should().BeFalse();

        shape = Shape.Preset(ShapeKind.Rectangle, 100, 50, "#FFFFFF");
        ((IRibbonStatefulCommand)gradient!).GetState().IsEnabled.Should().BeTrue();
        gradient!.Execute(RibbonCommandContext.Empty);
        fill.Should().NotBeNull();

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
        var avaloniaRibbon = ReadSource(
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWRibbon.cs");
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
        wpf.Should().Contain("return FreeWRibbonExecutionProfile.Build(registry).Registry;");
        avalonia.Should().Contain("return FreeWRibbonExecutionProfile.Build(r).Registry;");
        wpf.Should().NotContain(".Build().Registry");
        avalonia.Should().NotContain(".Build().Registry");
        wpf.Should().NotContain("FreeWRibbonCommandWorkflow.Register(");
        avalonia.Should().NotContain("FreeWRibbonCommandWorkflow.Register(");
        avaloniaRibbon.Should().Contain(
            "global using RibbonHostCallbacks = FreeW.App.Presentation.Ribbon.FreeWRibbonHostExecutionPorts;");
        avaloniaRibbon.Should().NotContain("record RibbonHostCallbacks");
        avalonia.Should().Contain(
            "FreeWRibbonHostExecutionProfile.Register(r, callbacks, registerFileAdapterCommands: true);");
        wpf.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterFloating(");
        wpf.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(");
        avalonia.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterFloating(");
        avalonia.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(");
        wpf.Should().Contain("RegisterSharedEditorCommandFamilies(registry);");
        avalonia.Should().Contain("RegisterSharedEditorCommandFamilies(r);");
        avalonia.Should().NotContain("RegisterChartSmartArtFormatCommands(r, editor, callbacks);");
        avalonia.Should().NotContain("new ActionRibbonCommand(callbacks.OpenFindReplaceDialog)");
        avalonia.Should().NotContain("HostCommand(callbacks.OpenAbout)");
        avalonia.Should().NotContain("class UnavailableRibbonCommand");

        foreach (var portableCommand in new[]
                 {
                     "FreeWRibbonFormatPainterCommand",
                     "FreeWRibbonNumericValueCommand",
                     "FreeWRibbonChoiceCommand",
                     "FreeWRibbonStatefulPortCommand",
                 })
        {
            wpf.Should().Contain(portableCommand);
            avalonia.Should().Contain(portableCommand);
        }

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
        RibbonActions(wpf + hostProfile + editorProfile)
            .Should().BeEquivalentTo(explicitlyOwnedActions);
        RibbonActions(avalonia).Should().HaveCountLessThan(Enum.GetValues<FreeWRibbonCommandAction>().Length);
        RibbonActions(avalonia + hostProfile + editorProfile)
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

    private sealed class RecordingCommand : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
        }
    }
}
