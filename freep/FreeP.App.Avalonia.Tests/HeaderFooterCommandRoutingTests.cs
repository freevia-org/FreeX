using System.Threading;
using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class HeaderFooterCommandRoutingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static HeaderFooterCommandRoutingTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    [Fact]
    public async Task HeaderFooter_command_opens_testable_pane_with_current_state()
    {
        HeaderFooterState? state = null;
        var visible = false;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.Presentation.Slides[0].HfVisibility = new HfFlags
            {
                ShowDate = true,
                ShowFooter = false,
                ShowSlideNum = true,
            };

            Execute(window.BuildCommandRegistry(), HeaderFooterCommandPlanner.HeaderFooterCommandId);
            state = window.LastHeaderFooterState;
            visible = window.IsHeaderFooterPaneVisible;
        });

        if (!ran) return;
        visible.Should().BeTrue();
        state!.ShowDateTime.Should().BeTrue();
        state.ShowFooter.Should().BeFalse();
        state.ShowSlideNumber.Should().BeTrue();
    }

    [Fact]
    public async Task HeaderFooter_apply_uses_shared_planner()
    {
        HfFlags? flags = null;
        string? footer = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());

            Execute(window.BuildCommandRegistry(), HeaderFooterCommandPlanner.HeaderFooterCommandId);
            window.ApplyHeaderFooterForTests(
                showDateTime: true,
                showFooter: true,
                showSlideNumber: true,
                footerText: "Deck footer",
                scope: HeaderFooterApplyScope.CurrentSlide);

            var slide = window.Editor.Presentation.Slides[0];
            flags = slide.HfVisibility;
            footer = slide.Shapes
                .Where(shape => shape.Placeholder?.Type == PlaceholderType.Footer)
                .SelectMany(shape => shape.TextBody?.Paragraphs ?? [])
                .SelectMany(paragraph => paragraph.Runs)
                .Single(run => run.Field?.FieldType == "footer")
                .Field!.CachedText;
        });

        if (!ran) return;
        flags!.ShowDate.Should().BeTrue();
        flags.ShowFooter.Should().BeTrue();
        flags.ShowSlideNum.Should().BeTrue();
        footer.Should().Be("Deck footer");
    }

    [Fact]
    public async Task View_show_commands_toggle_shared_state_and_gesture_snap_flags()
    {
        PresentationViewShowState state = default;
        bool? snapToGrid = null;
        bool? snapToShapes = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();

            Execute(registry, PresentationViewShowPlanner.GridlinesCommandId);
            Execute(registry, PresentationViewShowPlanner.GuidesCommandId);

            state = window.ViewShowStateForTests;
            snapToGrid = window.GestureSnapToGridForTests;
            snapToShapes = window.GestureSnapToShapesForTests;
        });

        if (!ran) return;
        state.ShowGridlines.Should().BeFalse();
        state.ShowGuides.Should().BeFalse();
        snapToGrid.Should().BeFalse();
        snapToShapes.Should().BeFalse();
    }

    [Fact]
    public async Task View_zoom_commands_update_window_and_canvas_zoom_state()
    {
        PresentationViewZoomState windowStateAfterZoom = default;
        PresentationViewZoomState canvasStateAfterZoom = default;
        PresentationViewZoomState windowStateAfterFit = default;
        PresentationViewZoomState canvasStateAfterFit = default;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();

            Execute(
                registry,
                PresentationViewZoomPlanner.ZoomCommandId,
                RibbonCommandContext.ForSelectedValue("175%"));
            windowStateAfterZoom = window.ViewZoomStateForTests;
            canvasStateAfterZoom = window.SlideCanvasViewZoomStateForTests;

            Execute(registry, PresentationViewZoomPlanner.FitToWindowCommandId);
            windowStateAfterFit = window.ViewZoomStateForTests;
            canvasStateAfterFit = window.SlideCanvasViewZoomStateForTests;
        });

        if (!ran) return;
        windowStateAfterZoom.Mode.Should().Be(PresentationViewZoomMode.Percent);
        windowStateAfterZoom.ZoomPercent.Should().Be(175);
        canvasStateAfterZoom.Should().Be(windowStateAfterZoom);
        windowStateAfterFit.Mode.Should().Be(PresentationViewZoomMode.FitToWindow);
        windowStateAfterFit.ZoomPercent.Should().Be(175);
        canvasStateAfterFit.Should().Be(windowStateAfterFit);
    }

    private static Task<bool> OnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None)
            .ContinueWith(task => task.Exception is null, CancellationToken.None);

    private static void Execute(
        RibbonCommandRegistry registry,
        string commandId,
        RibbonCommandContext? context = null)
    {
        registry.TryGet(commandId, out var command).Should().BeTrue();
        command!.Execute(context ?? RibbonCommandContext.Empty);
    }

}
