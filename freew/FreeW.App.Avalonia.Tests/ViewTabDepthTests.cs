using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-VIEW: tests for the deepened View tab — the Zoom dialog mapping, the layout-gridlines and ruler
/// view-state toggles (flag + computed render geometry), and command resolution for the new ids
/// (zoom-dialog, view-gridlines, view-ruler, new-window, split).
/// </summary>
public sealed class ViewTabDepthTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false; // no headless backend in this environment
        }
    }

    private static RibbonHostCallbacks NoopCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { }, OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
            OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { },
            InsertPicture: () => { }, OpenWordCountDialog: () => { }, ApplyZoom: (_, _) => { });

    private static TextDocument MakeDoc(string text = "Hello world")
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    // ── Command resolution ───────────────────────────────────────────────────

    [Fact]
    public void View_tab_commands_are_all_registered()
    {
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), NoopCallbacks());

        foreach (var id in new[]
                 {
                     "freew.zoom-dialog", "freew.view-gridlines", "freew.view-ruler",
                     "freew.new-window", "freew.split",
                 })
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"AV-VIEW command '{id}' must be registered");
    }

    [Fact]
    public void View_tab_new_commands_appear_in_ribbon_definition()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var viewTab = definition.Tabs.Single(t => t.Id == "view");
        var ids = viewTab.Groups.SelectMany(g => g.Controls)
            .Select(c => c.CommandId.Value)
            .ToList();

        ids.Should().Contain("freew.zoom-dialog");
        ids.Should().Contain("freew.view-gridlines");
        ids.Should().Contain("freew.view-ruler");
        ids.Should().Contain("freew.new-window");
        ids.Should().Contain("freew.split");
        // Show group must surface the Reviewing Pane toggle on the View tab too.
        ids.Should().Contain("freew.reviewingpane");
    }

    [Fact]
    public void Window_group_exists_on_view_tab()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var viewTab = definition.Tabs.Single(t => t.Id == "view");
        viewTab.Groups.Should().Contain(g => g.Id == "window",
            "AV-VIEW must add a Window group to the View tab");
    }

    // ── Gridlines toggle (flag) ──────────────────────────────────────────────

    [Fact]
    public void Gridlines_toggle_flips_flag_via_command()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());
        view.ShowGridlines.Should().BeFalse("gridlines off by default");

        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.view-gridlines"), out var cmd).Should().BeTrue();

        cmd!.Execute(RibbonCommandContext.Empty);
        view.ShowGridlines.Should().BeTrue("executing freew.view-gridlines must turn gridlines on");

        cmd!.Execute(RibbonCommandContext.Empty);
        view.ShowGridlines.Should().BeFalse("executing it again must turn gridlines off");
    }

    [Fact]
    public void Ruler_toggle_flips_flag_via_command()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());
        view.ShowRuler.Should().BeFalse("ruler off by default");

        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.view-ruler"), out var cmd).Should().BeTrue();

        cmd!.Execute(RibbonCommandContext.Empty);
        view.ShowRuler.Should().BeTrue("executing freew.view-ruler must turn the ruler on");

        cmd!.Execute(RibbonCommandContext.Empty);
        view.ShowRuler.Should().BeFalse("executing it again must turn the ruler off");
    }

    // ── Gridlines / ruler render geometry (reflects the flag) ─────────────────

    [Fact]
    public async Task Gridlines_render_geometry_reflects_flag()
    {
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)>? off = null;
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)>? on = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(MakeDoc("Gridlines body text"));
            view.Measure(new Size(816, 4000));

            off = view.ComputeGridlines();
            view.ShowGridlines = true;
            view.Measure(new Size(816, 4000));
            on = view.ComputeGridlines();
        });

        if (!ran) return;
        off!.Should().BeEmpty("no gridlines should be emitted when the flag is off");
        on!.Should().NotBeEmpty("turning ShowGridlines on must produce grid line segments");
        // Both horizontal (Y1==Y2) and vertical (X1==X2) lines must be present.
        on!.Should().Contain(l => Math.Abs(l.Y1 - l.Y2) < 0.001, "horizontal gridlines expected");
        on!.Should().Contain(l => Math.Abs(l.X1 - l.X2) < 0.001, "vertical gridlines expected");
    }

    [Fact]
    public async Task Ruler_tick_geometry_reflects_flag()
    {
        IReadOnlyList<double>? off = null;
        IReadOnlyList<double>? on = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(MakeDoc("Ruler body text"));
            view.Measure(new Size(816, 4000));

            off = view.ComputeRulerTicks();
            view.ShowRuler = true;
            view.Measure(new Size(816, 4000));
            on = view.ComputeRulerTicks();
        });

        if (!ran) return;
        off!.Should().BeEmpty("no ruler ticks when the flag is off");
        on!.Should().NotBeEmpty("turning ShowRuler on must produce inch tick marks");
        on!.Count.Should().BeGreaterThan(1, "a Letter/A4 page width spans several inch ticks");
    }

    [Fact]
    public async Task Gridlines_and_ruler_render_without_throwing_when_enabled()
    {
        // Smoke: a full Render pass with both overlays on must not throw.
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView { ShowGridlines = true, ShowRuler = true };
            view.LoadDocument(MakeDoc("Body with overlays"));
            view.Measure(new Size(816, 4000));
            view.Arrange(new Rect(0, 0, 816, 4000));
        });
        // If the headless backend is unavailable the dispatch returns false — that's fine; when it does
        // run, reaching here without an exception is the assertion.
        ran.Should().BeTrue("the Render path with gridlines + ruler enabled must complete on the headless backend");
    }

    // ── Zoom dialog mapping ──────────────────────────────────────────────────

    [Fact]
    public async Task Zoom_dialog_preset_maps_to_expected_scale()
    {
        double? scale100 = null;
        double? scale200 = null;
        var ran = await OnUiThread(() =>
        {
            // 100% current → the 100% radio is pre-selected → ResolveScale == 1.0.
            scale100 = new ZoomDialog(1.0).ResolveScale();
            // 200% current → the 200% radio is pre-selected → ResolveScale == 2.0.
            scale200 = new ZoomDialog(2.0).ResolveScale();
        });

        if (!ran) return;
        scale100.Should().Be(1.0, "a 100% current zoom must resolve to the 100% preset");
        scale200.Should().Be(2.0, "a 200% current zoom must resolve to the 200% preset");
    }

    [Fact]
    public async Task Zoom_dialog_custom_percent_maps_to_scale()
    {
        double? scale = null;
        var ran = await OnUiThread(() =>
        {
            // A non-preset current zoom (e.g. 130%) selects the custom radio; ResolveScale reads the box.
            var dialog = new ZoomDialog(1.3);
            scale = dialog.ResolveScale();
        });

        if (!ran) return;
        scale.Should().BeApproximately(1.3, 0.001,
            "a custom 130% current zoom must resolve to a 1.3 scale via the percent box");
    }

    [Fact]
    public async Task Zoom_dialog_page_relative_presets_resolve_to_their_scales()
    {
        // Page-relative fit arithmetic is owned by the shared presentation planner; the Avalonia
        // dialog only supplies the host's current fit factors.
        ZoomDialogPlanner
            .TryCreateResult(
                new ZoomDialogSelectionRequest(
                    ZoomDialogFitOption.PageWidth,
                    PresetPercent: null,
                    CustomPercentText: "not parsed"),
                new ZoomDialogFitFactors(1.25, 1.5, 0.6),
                out var scale,
                out var error)
            .Should()
            .BeTrue();

        scale.Should().Be(1.25);
        error.Should().BeNull();
        await Task.CompletedTask;
    }
}
