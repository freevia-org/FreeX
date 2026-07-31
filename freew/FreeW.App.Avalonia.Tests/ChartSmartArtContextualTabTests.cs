using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-CHARTTAB: Guard tests for the Chart Design/Format + SmartArt Design contextual tabs and the
/// <see cref="FloatingRibbonContextSource"/> routing that drives them.
/// <list type="bullet">
///   <item>Selecting a floating chart activates the Chart context; SmartArt activates SmartArt; image → Picture.</item>
///   <item>The chart/smartart Design-tab commands resolve and mutate the model (kind/style/colours/layout).</item>
///   <item>Undo reverts each chart/smartart edit.</item>
///   <item>Commands no-op safely when no chart/smartart is selected.</item>
/// </list>
/// </summary>
public sealed class ChartSmartArtContextualTabTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUi(Action action)
    {
        try { await Session.Dispatch(action, CancellationToken.None); return true; }
        catch (Exception) { return false; }
    }

    private static RibbonHostCallbacks NoopCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { },
            ToggleNavigationPane: () => { }, ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
            OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { },
            InsertPicture: () => { },
            OpenWordCountDialog: () => { },
            ApplyZoom: (_, _) => { });

    private static (TextDocument Doc, int BlockIdx, int RunIdx) DocWithFloatingChart()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text.", RunFormatting.Default));
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2", "Q3"], [1, 2, 3], "Sales", "Revenue");
        chart.Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = 36, VerticalOffsetPt = 36, ZOrderIndex = 1,
        };
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
        doc.Blocks.Add(para);
        return (doc, 0, 1);
    }

    private static (TextDocument Doc, int BlockIdx, int RunIdx) DocWithFloatingSmartArt()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text.", RunFormatting.Default));
        var sa = SmartArt.Create(SmartArtKind.List, ["Step A", "Step B", "Step C"]);
        sa.Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = 36, VerticalOffsetPt = 36, ZOrderIndex = 1,
        };
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });
        doc.Blocks.Add(para);
        return (doc, 0, 1);
    }

    // ── Ribbon definition shape ───────────────────────────────────────────────

    [Fact]
    public void Ribbon_definition_includes_chart_and_smartart_contextual_tabs()
    {
        var def = FreeWRibbon.BuildDefinition();
        var ctx = def.ContextualTabs.ToList();

        ctx.Any(t => t.Id == "chart-design").Should().BeTrue("chart-design tab must be defined");
        ctx.Any(t => t.Id == "chart-format").Should().BeTrue("chart-format tab must be defined");
        ctx.Any(t => t.Id == "smartart-design").Should().BeTrue("smartart-design tab must be defined");

        var cd = def.FindTab("chart-design")!;
        cd.Context!.ActivationKey.Should().Be(FloatingRibbonContextSource.ChartContextKey);
        cd.Context.Color.Should().Be(RibbonContextColor.Green);
        cd.Groups.Select(g => g.Id).Should().ContainInOrder(
            "chart-type", "chart-data", "chart-quick-layout", "chart-styles", "chart-elements");
        var quickLayouts = cd.Groups.Single(g => g.Id == "chart-quick-layout").Controls
            .Cast<RibbonButton>()
            .ToArray();
        quickLayouts.Select(control => control.CommandId.Value)
            .Should().Equal(ChartQuickLayout.Catalog.Select(layout => $"freew.chart-quick-layout-{layout.Id}"));
        quickLayouts.Select(control => control.Label)
            .Should().Equal(ChartQuickLayout.Catalog.Select(layout => layout.Name));
        quickLayouts.Should().OnlyContain(control =>
            control.Icon != null && control.Icon.Kind == RibbonCommandIconKind.Grid);
        cd.Groups.Single(g => g.Id == "chart-elements").Controls
            .Select(GetCommandId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Should()
            .Equal("freew.chart-toggle-legend", "freew.chart-title", "freew.chart-axis-titles");

        var cf = def.FindTab("chart-format")!;
        cf.Context!.ActivationKey.Should().Be(FloatingRibbonContextSource.ChartContextKey);
        AssertUsesSharedZOrderCommands(cf, "chart-arrange");

        var sd = def.FindTab("smartart-design")!;
        sd.Context!.ActivationKey.Should().Be(FloatingRibbonContextSource.SmartArtContextKey);
        sd.Context.Color.Should().Be(RibbonContextColor.Blue);
        AssertUsesSharedZOrderCommands(sd, "smartart-arrange");
    }

    [Fact]
    public void Registry_contains_all_chart_and_smartart_commands()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());

        var ids = new[]
        {
            // Chart Design
            "freew.chart-type", "freew.chart-type-column", "freew.chart-type-bar", "freew.chart-type-line",
            "freew.chart-type-pie", "freew.chart-type-scatter", "freew.chart-type-area", "freew.chart-type-doughnut",
            "freew.chart-style", "freew.chart-style-1", "freew.chart-colors", "freew.chart-colors-colorful1",
            "freew.chart-toggle-legend", "freew.chart-title", "freew.chart-axis-titles",
            "freew.chart-size", "freew.chart-size-dialog",
            // SmartArt Design
            "freew.smartart-layout", "freew.smartart-layout-list", "freew.smartart-layout-process",
            "freew.smartart-layout-cycle", "freew.smartart-layout-hierarchy",
            "freew.smartart-colors", "freew.smartart-colors-colorful1",
        };

        foreach (var id in ids)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"command '{id}' must be registered");

        foreach (var layout in ChartQuickLayout.Catalog)
            registry.TryGet(new RibbonCommandId($"freew.chart-quick-layout-{layout.Id}"), out _)
                .Should().BeTrue($"quick layout {layout.Id} must be registered");

        foreach (var layout in SmartArtLayoutPreset.Catalog)
            registry.TryGet(new RibbonCommandId($"freew.smartart-layout-{layout.Id}"), out _)
                .Should().BeTrue($"smartart layout {layout.Id} must be registered");
    }

    [Fact]
    public async Task Chart_size_dialog_command_routes_selected_chart_to_owner_modal_callback()
    {
        var ran = await OnUi(() =>
        {
            var (doc, blockIndex, runIndex) = DocWithFloatingChart();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(blockIndex, runIndex);
            var dialogOpened = false;
            var registry = FreeWAvaloniaRibbonCommands.Build(
                view,
                NoopCallbacks() with { OpenChartSizeDialog = () => dialogOpened = true });

            registry.TryGet(new RibbonCommandId("freew.chart-size-dialog"), out var command)
                .Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);

            dialogOpened.Should().BeTrue();
        });

        ran.Should().BeTrue();
    }

    [Fact]
    public void Every_contextual_ribbon_command_is_registered()
    {
        var def = FreeWRibbon.BuildDefinition();
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), NoopCallbacks());

        var ids = def.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .Select(GetCommandId)
            .Where(id => id is not null)
            .Select(id => id!.Value);

        foreach (var id in ids)
            registry.TryGet(id, out _)
                .Should().BeTrue($"Ribbon command '{id.Value}' must be registered");
    }

    // ── Context source activation ─────────────────────────────────────────────

    [Fact]
    public async Task FloatingContextSource_activates_chart_for_chart_selection()
    {
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingChart();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            var src = new FloatingRibbonContextSource(view);
            src.Current.IsActive(FloatingRibbonContextSource.ChartContextKey).Should().BeFalse("inactive before selection");

            view.SelectFloating(bi, ri);

            src.Current.IsActive(FloatingRibbonContextSource.ChartContextKey).Should().BeTrue("chart active for chart");
            src.Current.IsActive(FloatingRibbonContextSource.DrawingContextKey).Should().BeFalse("drawing inactive for chart");
            src.Current.IsActive(FloatingRibbonContextSource.PictureContextKey).Should().BeFalse("picture inactive for chart");
        });
        if (!ran) return;
    }

    [Fact]
    public async Task FloatingContextSource_activates_smartart_for_smartart_selection()
    {
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingSmartArt();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            var src = new FloatingRibbonContextSource(view);
            view.SelectFloating(bi, ri);

            src.Current.IsActive(FloatingRibbonContextSource.SmartArtContextKey).Should().BeTrue("smartart active for smartart");
            src.Current.IsActive(FloatingRibbonContextSource.ChartContextKey).Should().BeFalse("chart inactive for smartart");
            src.Current.IsActive(FloatingRibbonContextSource.DrawingContextKey).Should().BeFalse("drawing inactive for smartart");
        });
        if (!ran) return;
    }

    [Fact]
    public async Task FloatingContextSource_switches_chart_to_smartart_when_selection_changes()
    {
        var ran = await OnUi(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Body.", RunFormatting.Default));
            var chart = Chart.Create(ChartKind.Column, ["A"], [1]);
            chart.Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square, ZOrderIndex = 1 };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            var sa = SmartArt.Create(SmartArtKind.Process, ["X", "Y"]);
            sa.Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square, ZOrderIndex = 2 };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            var src = new FloatingRibbonContextSource(view);

            view.SelectFloating(0, 1); // chart
            src.Current.IsActive(FloatingRibbonContextSource.ChartContextKey).Should().BeTrue();

            view.SelectFloating(0, 2); // smartart
            src.Current.IsActive(FloatingRibbonContextSource.SmartArtContextKey).Should().BeTrue();
            src.Current.IsActive(FloatingRibbonContextSource.ChartContextKey).Should().BeFalse("chart must clear when smartart selected");
        });
        if (!ran) return;
    }

    // ── Execute-through-to-model + undo ───────────────────────────────────────

    [Fact]
    public async Task SetChartType_command_changes_chart_kind_and_reverts_on_undo()
    {
        ChartKind? before = null, after = null, undone = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingChart(); // Column
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            before = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!.Kind;

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.chart-type-bar"), out var cmd);
            cmd!.Execute(RibbonCommandContext.Empty);
            after = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!.Kind;

            view.Undo();
            undone = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!.Kind;
        });
        if (!ran) return;
        before.Should().Be(ChartKind.Column);
        after.Should().Be(ChartKind.Bar, "chart-type-bar must set Bar kind");
        undone.Should().Be(ChartKind.Column, "undo must revert the chart kind");
    }

    [Fact]
    public async Task SetChartStyle_command_changes_style_id_and_reverts_on_undo()
    {
        int? before = null, after = null, undone = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingChart();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            before = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!.StyleId;

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.chart-style-5"), out var cmd);
            cmd!.Execute(RibbonCommandContext.Empty);
            after = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!.StyleId;

            view.Undo();
            undone = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!.StyleId;
        });
        if (!ran) return;
        before.Should().Be(0);
        after.Should().Be(5, "chart-style-5 must set StyleId = 5");
        undone.Should().Be(0, "undo must revert the chart style");
    }

    [Fact]
    public async Task SetChartColorScheme_command_changes_scheme_and_reverts_on_undo()
    {
        string? before = null, after = null, undone = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingChart();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            before = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!.ColorSchemeId;

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.chart-colors-mono-blue"), out var cmd);
            cmd!.Execute(RibbonCommandContext.Empty);
            after = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!.ColorSchemeId;

            view.Undo();
            undone = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!.ColorSchemeId;
        });
        if (!ran) return;
        before.Should().BeNull();
        after.Should().Be("mono-blue", "chart-colors-mono-blue must set the scheme id");
        undone.Should().BeNull("undo must revert the colour scheme");
    }

    [Fact]
    public async Task ChartQuickLayoutCatalog_commands_apply_preserve_selection_and_support_undo_redo()
    {
        var ran = await OnUi(() =>
        {
            foreach (var layout in ChartQuickLayout.Catalog)
            {
                var (doc, bi, ri) = DocWithFloatingChart();
                var chart = ((Paragraph)doc.Blocks[bi]).Runs[ri].Chart!;
                chart.ShowLegend = true;
                chart.CategoryAxisTitle = "Quarter";
                chart.ValueAxisTitle = "USD";
                chart.StyleId = 7;
                chart.ColorSchemeId = "mono-blue";
                var categories = chart.Categories.ToArray();
                var values = chart.Series[0].Values.ToArray();

                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(800, 2000));
                view.SelectFloating(bi, ri);

                var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
                registry.TryGet(new RibbonCommandId($"freew.chart-quick-layout-{layout.Id}"), out var command)
                    .Should().BeTrue();
                var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
                stateful.GetState().IsEnabled.Should().BeTrue();

                command!.Execute(RibbonCommandContext.Empty);

                chart.QuickLayoutId.Should().Be(layout.Id);
                chart.StyleId.Should().Be(7);
                chart.ColorSchemeId.Should().Be("mono-blue");
                chart.Categories.Should().Equal(categories);
                chart.Series[0].Values.Should().Equal(values);
                view.GetSelectedChartInfo().Should().NotBeNull();

                view.Undo();
                chart.QuickLayoutId.Should().Be(0);
                view.Redo();
                chart.QuickLayoutId.Should().Be(layout.Id);
            }
        });
        ran.Should().BeTrue();
    }

    [Fact]
    public async Task ChartQuickLayoutCatalog_commands_are_disabled_without_a_chart_selection()
    {
        var ran = await OnUi(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(TextDocument.CreateEmpty());
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

            foreach (var layout in ChartQuickLayout.Catalog)
            {
                registry.TryGet(new RibbonCommandId($"freew.chart-quick-layout-{layout.Id}"), out var command)
                    .Should().BeTrue();
                command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject
                    .GetState().IsEnabled.Should().BeFalse();
            }
        });
        ran.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleChartLegend_command_clears_layout_override_and_reverts_on_undo()
    {
        bool? visibleBefore = null, visibleAfter = null, visibleUndone = null;
        int? quickLayoutAfter = null, quickLayoutUndone = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingChart();
            var chart = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!;
            chart.ShowLegend = false;
            chart.QuickLayoutId = 3;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            visibleBefore = ChartSmartArtVisualPlanner.BuildChartPlan(chart).ShowLegend;

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.chart-toggle-legend"), out var cmd);
            cmd!.Execute(RibbonCommandContext.Empty);
            visibleAfter = ChartSmartArtVisualPlanner.BuildChartPlan(chart).ShowLegend;
            quickLayoutAfter = chart.QuickLayoutId;

            view.Undo();
            visibleUndone = ChartSmartArtVisualPlanner.BuildChartPlan(chart).ShowLegend;
            quickLayoutUndone = chart.QuickLayoutId;
        });
        if (!ran) return;
        visibleBefore.Should().BeTrue("quick layout 3 shows the legend before the explicit command");
        visibleAfter.Should().BeFalse("the Legend command must be able to hide a layout-supplied legend");
        quickLayoutAfter.Should().Be(0, "explicit chart element commands clear quick-layout overrides");
        visibleUndone.Should().BeTrue("undo restores the layout-driven legend");
        quickLayoutUndone.Should().Be(3);
    }

    [Fact]
    public async Task ToggleChartTitle_command_sets_default_title_and_reverts_on_undo()
    {
        string? titleAfter = null, titleUndone = "not observed";
        int? quickLayoutAfter = null, quickLayoutUndone = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingChart();
            var chart = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!;
            chart.Title = null;
            chart.QuickLayoutId = 2;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.chart-title"), out var cmd);
            cmd!.Execute(RibbonCommandContext.Empty);
            titleAfter = chart.Title;
            quickLayoutAfter = chart.QuickLayoutId;

            view.Undo();
            titleUndone = chart.Title;
            quickLayoutUndone = chart.QuickLayoutId;
        });
        if (!ran) return;
        titleAfter.Should().Be("Chart Title");
        quickLayoutAfter.Should().Be(0, "explicit chart element commands clear quick-layout overrides");
        titleUndone.Should().BeNull();
        quickLayoutUndone.Should().Be(2);
    }

    [Fact]
    public async Task ToggleChartAxisTitles_command_sets_default_titles_and_reverts_on_undo()
    {
        string? categoryAfter = null, valueAfter = null, categoryUndone = "not observed", valueUndone = "not observed";
        int? quickLayoutAfter = null, quickLayoutUndone = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingChart();
            var chart = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!;
            chart.QuickLayoutId = 9;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.chart-axis-titles"), out var cmd);
            cmd!.Execute(RibbonCommandContext.Empty);
            categoryAfter = chart.CategoryAxisTitle;
            valueAfter = chart.ValueAxisTitle;
            quickLayoutAfter = chart.QuickLayoutId;

            view.Undo();
            categoryUndone = chart.CategoryAxisTitle;
            valueUndone = chart.ValueAxisTitle;
            quickLayoutUndone = chart.QuickLayoutId;
        });
        if (!ran) return;
        categoryAfter.Should().Be("Category Axis");
        valueAfter.Should().Be("Value Axis");
        quickLayoutAfter.Should().Be(0, "explicit chart element commands clear quick-layout overrides");
        categoryUndone.Should().BeNull();
        valueUndone.Should().BeNull();
        quickLayoutUndone.Should().Be(9);
    }

    [Fact]
    public async Task EditChartData_command_replaces_chart_data_and_reverts_on_undo()
    {
        string[]? categoriesAfter = null, categoriesUndone = null;
        double[]? valuesAfter = null, valuesUndone = null;
        ChartKind? kindAfter = null, kindUndone = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingChart();
            var chart = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.chart-edit-data"), out var cmd);
            cmd!.Execute(RibbonCommandContext.ForSelectedValue("Monthly Revenue"));
            kindAfter = chart.Kind;
            categoriesAfter = chart.Categories.ToArray();
            valuesAfter = chart.Series[0].Values.ToArray();

            view.Undo();
            kindUndone = chart.Kind;
            categoriesUndone = chart.Categories.ToArray();
            valuesUndone = chart.Series[0].Values.ToArray();
        });
        if (!ran) return;
        kindAfter.Should().Be(ChartKind.Line);
        categoriesAfter.Should().Equal("Jan", "Feb", "Mar");
        valuesAfter.Should().Equal(5.0, 6.0, 7.0);
        kindUndone.Should().Be(ChartKind.Column);
        categoriesUndone.Should().Equal("Q1", "Q2", "Q3");
        valuesUndone.Should().Equal(1.0, 2.0, 3.0);
    }

    [Fact]
    public Task EditChartData_primary_action_routes_selected_chart_and_reverts_accepted_data() =>
        Session.Dispatch(() =>
        {
            var (doc, bi, ri) = DocWithFloatingChart();
            var chart = ((Paragraph)doc.Blocks[bi]).Runs[ri].Chart!;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            Chart? seed = null;
            var replacement = Chart.Create(
                ChartKind.Line,
                ["Apr", "May", "Jun"],
                [9, 8, 7],
                "Forecast",
                "Updated");
            var callbacks = NoopCallbacks() with
            {
                OpenChartEditDataDialog = () =>
                {
                    seed = view.SelectedFloatingChart();
                    view.ReplaceSelectedChartData(replacement);
                },
            };
            var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

            registry.TryGet(new RibbonCommandId("freew.chart-edit-data"), out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);

            seed.Should().BeSameAs(chart);
            chart.Kind.Should().Be(ChartKind.Line);
            chart.Categories.Should().Equal("Apr", "May", "Jun");
            chart.Series[0].Values.Should().Equal(9.0, 8.0, 7.0);

            view.Undo();
            chart.Kind.Should().Be(ChartKind.Column);
            chart.Categories.Should().Equal("Q1", "Q2", "Q3");
            chart.Series[0].Values.Should().Equal(1.0, 2.0, 3.0);
        }, CancellationToken.None);

    [Fact]
    public Task EditChartData_cancelled_primary_action_leaves_selected_chart_unchanged() =>
        Session.Dispatch(() =>
        {
            var (doc, bi, ri) = DocWithFloatingChart();
            var chart = ((Paragraph)doc.Blocks[bi]).Runs[ri].Chart!;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            var invoked = 0;
            var callbacks = NoopCallbacks() with
            {
                OpenChartEditDataDialog = () =>
                {
                    invoked++;
                    view.SelectedFloatingChart().Should().BeSameAs(chart);
                },
            };
            var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

            registry.TryGet(new RibbonCommandId("freew.chart-edit-data"), out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);

            invoked.Should().Be(1);
            chart.Kind.Should().Be(ChartKind.Column);
            chart.Categories.Should().Equal("Q1", "Q2", "Q3");
            chart.Series[0].Values.Should().Equal(1.0, 2.0, 3.0);
        }, CancellationToken.None);

    [Fact]
    public async Task ChartSize_command_resizes_selected_chart_and_reverts_on_undo()
    {
        double? widthAfter = null, heightAfter = null, widthUndone = null, heightUndone = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingChart();
            var chart = ((Paragraph)doc.Blocks[0]).Runs[ri].Chart!;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.chart-size"), out var cmd);
            cmd!.Execute(RibbonCommandContext.ForSelectedValue("400 x 300"));
            widthAfter = chart.WidthPt;
            heightAfter = chart.HeightPt;

            view.Undo();
            widthUndone = chart.WidthPt;
            heightUndone = chart.HeightPt;
        });
        if (!ran) return;
        widthAfter.Should().Be(400);
        heightAfter.Should().Be(300);
        widthUndone.Should().Be(360);
        heightUndone.Should().Be(216);
    }

    [Fact]
    public async Task SetSmartArtLayout_command_changes_kind_and_reverts_on_undo()
    {
        SmartArtKind? before = null, after = null, undone = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingSmartArt(); // List
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            before = ((Paragraph)doc.Blocks[0]).Runs[ri].SmartArt!.Kind;

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.smartart-layout-hierarchy"), out var cmd);
            cmd!.Execute(RibbonCommandContext.Empty);
            after = ((Paragraph)doc.Blocks[0]).Runs[ri].SmartArt!.Kind;

            view.Undo();
            undone = ((Paragraph)doc.Blocks[0]).Runs[ri].SmartArt!.Kind;
        });
        if (!ran) return;
        before.Should().Be(SmartArtKind.List);
        after.Should().Be(SmartArtKind.Hierarchy, "smartart-layout-hierarchy must set Hierarchy");
        undone.Should().Be(SmartArtKind.List, "undo must revert the smartart kind");
    }

    [Fact]
    public async Task SetSmartArtContinuousBlockProcess_command_sets_layout_and_reverts_on_undo()
    {
        SmartArtKind? kindAfter = null, kindUndone = null;
        string? layoutBefore = null, layoutAfter = null, layoutUndone = null, geometryKind = null;
        int nodeCount = 0, connectorCount = 0;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingSmartArt(); // List
            var smartArt = ((Paragraph)doc.Blocks[0]).Runs[ri].SmartArt!;
            smartArt.LayoutId = "horizbullet1";
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            layoutBefore = smartArt.LayoutId;

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.smartart-layout-continuous-block-process"), out var cmd)
                .Should().BeTrue();
            cmd!.Execute(RibbonCommandContext.Empty);
            kindAfter = smartArt.Kind;
            layoutAfter = smartArt.LayoutId;
            view.Measure(new Size(800, 2000));
            var geometry = view.FloatingSmartArtLayoutGeometries.Single();
            geometryKind = geometry.GeometryKind;
            nodeCount = geometry.GeometryNodeCount;
            connectorCount = geometry.GeometryConnectorCount;

            view.Undo();
            kindUndone = smartArt.Kind;
            layoutUndone = smartArt.LayoutId;
        });
        if (!ran) return;
        layoutBefore.Should().Be("horizbullet1");
        kindAfter.Should().Be(SmartArtKind.Process);
        layoutAfter.Should().Be("continuousBlockProcess");
        geometryKind.Should().Be("ContinuousBlockProcess");
        nodeCount.Should().Be(3);
        connectorCount.Should().Be(2);
        kindUndone.Should().Be(SmartArtKind.List);
        layoutUndone.Should().Be("horizbullet1");
    }

    [Fact]
    public async Task SmartArt_layout_catalog_commands_apply_each_shared_preset()
    {
        var applied = new List<string>();
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingSmartArt();
            var smartArt = ((Paragraph)doc.Blocks[bi]).Runs[ri].SmartArt!;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

            foreach (var preset in SmartArtLayoutPreset.Catalog)
            {
                registry.TryGet(new RibbonCommandId($"freew.smartart-layout-{preset.Id}"), out var command)
                    .Should().BeTrue();
                command!.Execute(RibbonCommandContext.Empty);
                smartArt.LayoutId.Should().Be(preset.Id);
                smartArt.Kind.Should().Be(preset.Kind);
                applied.Add(preset.Id);
            }
        });

        if (!ran) return;
        applied.Should().Equal(SmartArtLayoutPreset.Catalog.Select(preset => preset.Id));
    }

    [Fact]
    public async Task SetSmartArtColor_command_changes_scheme()
    {
        string? after = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingSmartArt();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.smartart-colors-mono1"), out var cmd);
            cmd!.Execute(RibbonCommandContext.Empty);
            after = ((Paragraph)doc.Blocks[0]).Runs[ri].SmartArt!.ColorSchemeId;
        });
        if (!ran) return;
        after.Should().Be("mono1", "smartart-colors-mono1 must set the SmartArt scheme id");
    }

    [Fact]
    public async Task SmartArt_color_catalog_commands_apply_each_shared_scheme()
    {
        var applied = new List<string>();
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingSmartArt();
            var smartArt = ((Paragraph)doc.Blocks[bi]).Runs[ri].SmartArt!;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

            foreach (var scheme in SmartArtColorScheme.Catalog)
            {
                registry.TryGet(new RibbonCommandId($"freew.smartart-colors-{scheme.Id}"), out var command)
                    .Should().BeTrue();
                command!.Execute(RibbonCommandContext.Empty);
                smartArt.ColorSchemeId.Should().Be(scheme.Id);
                applied.Add(scheme.Id);
            }
        });

        if (!ran) return;
        applied.Should().Equal(SmartArtColorScheme.Catalog.Select(scheme => scheme.Id));
    }

    [Fact]
    public void SmartArt_contextual_tab_exposes_all_shared_commands_and_style_catalog()
    {
        var tab = FreeWRibbon.BuildDefinition().FindTab("smartart-design")!;
        var commandIds = tab.Groups.SelectMany(group => group.Controls)
            .Select(GetCommandId)
            .Where(id => id is not null)
            .Select(id => id!.Value.Value)
            .ToArray();

        commandIds.Should().Contain(new[]
        {
            "freew.smartart-add-shape",
            "freew.smartart-remove-shape",
            "freew.smartart-promote",
            "freew.smartart-demote",
            "freew.smartart-move-up",
            "freew.smartart-move-down",
            "freew.smartart-edit-text",
            "freew.smartart-change-style",
        });
        var styles = tab.FindGroup("smartart-styles")!.Controls.OfType<RibbonComboBox>().Single();
        styles.Items.Should().Equal(SmartArtStyle.Catalog.Select(style => style.Name));
        var layouts = tab.FindGroup("smartart-layouts")!.Controls.OfType<RibbonDropdown>().Single();
        layouts.Menu.Items.Select(item => item.CommandId!.Value)
            .Should().Equal(SmartArtLayoutPreset.Catalog.Select(preset =>
                new RibbonCommandId($"freew.smartart-layout-{preset.Id}")));
        var colors = tab.FindGroup("smartart-styles")!.Controls.OfType<RibbonDropdown>().Single();
        colors.Menu.Items.Select(item => item.CommandId!.Value)
            .Should().Equal(SmartArtColorScheme.Catalog.Select(scheme =>
                new RibbonCommandId($"freew.smartart-colors-{scheme.Id}")));
    }

    [Fact]
    public async Task SmartArt_structure_commands_mutate_preserve_selection_and_support_undo_redo()
    {
        var ran = await OnUi(() =>
        {
            foreach (var (commandId, operation) in new[]
            {
                ("freew.smartart-add-shape", SmartArtStructureOperation.AddShape),
                ("freew.smartart-remove-shape", SmartArtStructureOperation.RemoveShape),
                ("freew.smartart-move-up", SmartArtStructureOperation.MoveUp),
                ("freew.smartart-move-down", SmartArtStructureOperation.MoveDown),
            })
            {
                var (doc, bi, ri) = DocWithFloatingSmartArt();
                var smartArt = ((Paragraph)doc.Blocks[bi]).Runs[ri].SmartArt!;
                var before = smartArt.Nodes.Select(node => node.Text).ToArray();
                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(800, 2000));
                view.SelectFloating(bi, ri);
                var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
                registry.TryGet(new RibbonCommandId(commandId), out var command).Should().BeTrue();
                command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject.GetState().IsEnabled.Should().BeTrue();

                command!.Execute(RibbonCommandContext.Empty);

                smartArt.Nodes.Select(node => node.Text).Should().NotEqual(before);
                view.SelectedFloatingSmartArt().Should().BeSameAs(smartArt);
                view.Undo();
                smartArt.Nodes.Select(node => node.Text).Should().Equal(before);
                view.Redo();
                MutateSmartArtStructureCommand.CanApply(smartArt, operation).Should().BeTrue();
            }

            var (hierarchyDoc, hierarchyBi, hierarchyRi) = DocWithFloatingSmartArt();
            var hierarchy = ((Paragraph)hierarchyDoc.Blocks[hierarchyBi]).Runs[hierarchyRi].SmartArt!;
            hierarchy.Kind = SmartArtKind.Hierarchy;
            hierarchy.Nodes.Clear();
            hierarchy.Nodes.Add(new SmartArtNode("Root", [new SmartArtNode("Child")]));
            var hierarchyView = new DocumentView();
            hierarchyView.LoadDocument(hierarchyDoc);
            hierarchyView.Measure(new Size(800, 2000));
            hierarchyView.SelectFloating(hierarchyBi, hierarchyRi);
            var hierarchyRegistry = FreeWAvaloniaRibbonCommands.Build(hierarchyView, NoopCallbacks());

            hierarchyRegistry.TryGet(new RibbonCommandId("freew.smartart-promote"), out var promote).Should().BeTrue();
            promote.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject.GetState().IsEnabled.Should().BeTrue();
            promote!.Execute(RibbonCommandContext.Empty);
            hierarchy.Nodes.Select(node => node.Text).Should().Equal("Root", "Child");
            hierarchyView.Undo();
            hierarchy.Nodes.Should().ContainSingle();

            hierarchy.Nodes.Add(new SmartArtNode("Sibling"));
            hierarchyRegistry.TryGet(new RibbonCommandId("freew.smartart-demote"), out var demote).Should().BeTrue();
            demote.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject.GetState().IsEnabled.Should().BeTrue();
            demote!.Execute(RibbonCommandContext.Empty);
            hierarchy.Nodes.Should().ContainSingle();
            hierarchyView.Undo();
            hierarchy.Nodes.Select(node => node.Text).Should().Equal("Root", "Sibling");
        });
        ran.Should().BeTrue();
    }

    [Fact]
    public async Task SmartArt_edit_and_style_commands_mutate_preserve_metadata_and_support_undo()
    {
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingSmartArt();
            var smartArt = ((Paragraph)doc.Blocks[bi]).Runs[ri].SmartArt!;
            smartArt.LayoutId = "list1";
            smartArt.ColorSchemeId = "colorful2";
            smartArt.StyleId = "flat1";
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            var dialogOpened = false;
            var registry = FreeWAvaloniaRibbonCommands.Build(
                view,
                NoopCallbacks() with { OpenSmartArtEditDialog = () => dialogOpened = true });

            registry.TryGet(new RibbonCommandId("freew.smartart-edit-text"), out var edit).Should().BeTrue();
            edit!.Execute(RibbonCommandContext.Empty);
            dialogOpened.Should().BeTrue();
            edit!.Execute(RibbonCommandContext.ForSelectedValue("One\nTwo"));
            smartArt.Nodes.Select(node => node.Text).Should().Equal("One", "Two");
            smartArt.LayoutId.Should().Be("list1");
            smartArt.ColorSchemeId.Should().Be("colorful2");
            smartArt.StyleId.Should().Be("flat1");
            view.Undo();
            smartArt.Nodes.Select(node => node.Text).Should().Equal("Step A", "Step B", "Step C");

            registry.TryGet(new RibbonCommandId("freew.smartart-change-style"), out var styles).Should().BeTrue();
            styles!.Execute(RibbonCommandContext.ForSelectedValue(SmartArtStyle.Catalog[4].Name));
            smartArt.StyleId.Should().Be(SmartArtStyle.Catalog[4].Id);
            smartArt.Nodes.Select(node => node.Text).Should().Equal("Step A", "Step B", "Step C");
            view.Undo();
            smartArt.StyleId.Should().Be("flat1");
        });
        ran.Should().BeTrue();
    }

    [Fact]
    public async Task SmartArt_commands_are_disabled_without_a_smartart_selection()
    {
        var ran = await OnUi(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(TextDocument.CreateEmpty());
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            foreach (var id in new[]
            {
                "freew.smartart-add-shape", "freew.smartart-remove-shape",
                "freew.smartart-promote", "freew.smartart-demote",
                "freew.smartart-move-up", "freew.smartart-move-down",
                "freew.smartart-edit-text", "freew.smartart-change-style",
            })
            {
                registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
                command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject.GetState().IsEnabled.Should().BeFalse();
            }
        });
        ran.Should().BeTrue();
    }

    [Fact]
    public async Task ChartCommand_does_not_affect_smartart_and_vice_versa()
    {
        ChartKind? chartKind = null;
        SmartArtKind? saKind = null;
        var ran = await OnUi(() =>
        {
            // Chart selected: running a smartart-layout command must NOT touch the chart.
            var (cdoc, cbi, cri) = DocWithFloatingChart();
            var cview = new DocumentView();
            cview.LoadDocument(cdoc);
            cview.Measure(new Size(800, 2000));
            cview.SelectFloating(cbi, cri);
            var creg = FreeWAvaloniaRibbonCommands.Build(cview, NoopCallbacks());
            creg.TryGet(new RibbonCommandId("freew.smartart-layout-process"), out var smartCmd);
            smartCmd!.Execute(RibbonCommandContext.Empty); // must be a no-op (no smartart selected)
            chartKind = ((Paragraph)cdoc.Blocks[0]).Runs[cri].Chart!.Kind;

            // SmartArt selected: a chart-type command must NOT touch the smartart.
            var (sdoc, sbi, sri) = DocWithFloatingSmartArt();
            var sview = new DocumentView();
            sview.LoadDocument(sdoc);
            sview.Measure(new Size(800, 2000));
            sview.SelectFloating(sbi, sri);
            var sreg = FreeWAvaloniaRibbonCommands.Build(sview, NoopCallbacks());
            sreg.TryGet(new RibbonCommandId("freew.chart-type-pie"), out var chartCmd);
            chartCmd!.Execute(RibbonCommandContext.Empty); // must be a no-op (no chart selected)
            saKind = ((Paragraph)sdoc.Blocks[0]).Runs[sri].SmartArt!.Kind;
        });
        if (!ran) return;
        chartKind.Should().Be(ChartKind.Column, "smartart command must not change the selected chart");
        saKind.Should().Be(SmartArtKind.List, "chart command must not change the selected smartart");
    }

    [Fact]
    public async Task Commands_are_noops_when_no_float_selected()
    {
        var ran = await OnUi(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Add(new Paragraph("no float"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            foreach (var id in new[]
            {
                "freew.chart-type-bar", "freew.chart-style-3", "freew.chart-colors-colorful2",
                "freew.chart-toggle-legend", "freew.chart-edit-data", "freew.chart-size",
                "freew.smartart-layout-cycle", "freew.smartart-colors-mono1",
            })
            {
                registry.TryGet(new RibbonCommandId(id), out var cmd);
                cmd!.Execute(RibbonCommandContext.Empty); // must not throw
            }
        });
        ran.Should().BeTrue("chart/smartart commands must silently no-op when nothing is selected");
    }

    private static RibbonCommandId? GetCommandId(RibbonControl control) => control switch
    {
        RibbonButton b       => b.CommandId,
        RibbonToggleButton t => t.CommandId,
        RibbonComboBox c     => c.CommandId,
        RibbonCheckBox cb    => cb.CommandId,
        RibbonSplitButton sb => sb.CommandId,
        RibbonDropdown d     => d.CommandId,
        RibbonGallery g      => g.CommandId,
        _                    => (RibbonCommandId?)null,
    };

    private static void AssertUsesSharedZOrderCommands(RibbonTab tab, string groupId)
    {
        var commandIds = tab.Groups.Single(g => g.Id == groupId).Controls
            .Select(control => GetCommandId(control)?.Value)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToArray();

        commandIds.Should().Contain(new[]
        {
            "freew.image-bring-to-front",
            "freew.image-send-to-back",
        }, "Avalonia should share WPF's object z-order command ids for contextual object tabs");
        commandIds.Should().NotContain(new[]
        {
            "freew.shape-bring-to-front",
            "freew.shape-send-to-back",
            "freew.shape-bring-forward",
            "freew.shape-send-backward",
        }, "shape-prefixed z-order ids create duplicate generated inventory rows");
    }
}
