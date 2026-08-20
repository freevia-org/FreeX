using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using FluentAssertions;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// shared-accessibility-tree F1: SlideCanvas's Avalonia automation peer raised an IsSelected
/// property-changed notification on shape selection but never moved real Avalonia keyboard
/// focus to represent the new selection. Avalonia's AutomationPeer has no
/// RaiseAutomationEvent/AutomationFocusChanged equivalent (unlike the WPF twin,
/// FreeP.App.Rendering.Wpf/SlideCanvas.cs); its AT-SPI/UIA bridge only reacts to real
/// FocusManager focus transitions (see src/FreeX.App.Avalonia/MainWindow.cs's
/// MoveFocusToActiveCellBorder, which exists for the identical reason on FreeX's worksheet
/// grid). SlideCanvas has a single Control for the whole slide with no per-shape backing
/// control, so the fix moves real focus onto the canvas itself whenever
/// PresentationCanvasAutomationFocusIntent.MoveToShape is produced -- this is most visible for
/// selection changes that never go through AvaloniaCanvasGestureHandler's PointerPressed
/// (e.g. the Selection Pane, or any other direct EditingSession.Select caller), since that
/// handler's own <c>_canvas.Focus()</c> call never runs for those paths.
/// </summary>
public sealed class SlideCanvasAutomationFocusAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    private static Task Run(System.Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private sealed record Fixture(Window Window, SlideCanvas Canvas, EditingSession Editor, TextBox OtherFocusTarget, SlideShape ShapeA, SlideShape ShapeB);

    /// <summary>
    /// Builds a slide canvas hosted in a real (headless) Window -- required for Avalonia's
    /// FocusManager to actually track/report focus -- alongside a sibling TextBox the test can
    /// focus first, so moving focus onto the canvas is a genuine, observable transition. Mirrors
    /// the production wiring path (FreeP.App.Avalonia.MainWindow.WireInteraction ->
    /// SlideCanvas.AttachGestureHandler) used by SlideCanvasAutomationPeerAvaloniaTests.BuildCanvas.
    /// </summary>
    private static Fixture BuildFixture()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();

        var shapeA = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Picture,
            Name = "Title 1",
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1143000,
        };
        var shapeB = new SlideShape
        {
            Id = 2,
            Kind = SlideShapeKind.Table,
            Name = "Circle 1",
            AutoShapeKind = DrawingShapeKind.Ellipse,
            OffsetXEmu = 1000000,
            OffsetYEmu = 500000,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1500000,
        };
        slide.Shapes.Add(shapeA);
        slide.Shapes.Add(shapeB);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var canvas = new SlideCanvas
        {
            Presentation = presentation,
            Slide = slide,
        };

        var adorner = new SelectionAdornerLayer();
        // Real production wiring path (FreeP.App.Avalonia.MainWindow.WireInteraction) that points
        // the canvas's automation peer at the live EditingSession.
        var handler = new AvaloniaCanvasGestureHandler(canvas, editor, adorner);
        canvas.AttachGestureHandler(handler);

        var otherFocusTarget = new TextBox { Focusable = true };
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = new StackPanel { Children = { otherFocusTarget, canvas } },
        };
        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        // OnEditingSessionSelectionChangedForAutomation only calls NotifySelectionChanged when a
        // peer has already been realized ("i.e. a screen reader or other automation client is
        // actually listening" -- see its doc comment). Realize it here, exactly as a running AT
        // client would, so selection changes actually reach the peer under test. Mirrors
        // SlideCanvasAutomationPeerAvaloniaTests.BuildCanvas's callers.
        ControlAutomationPeer.CreatePeerForElement(canvas);

        return new Fixture(window, canvas, editor, otherFocusTarget, shapeA, shapeB);
    }

    [Fact]
    public async Task Select_ThroughEditingSessionDirectly_MovesRealKeyboardFocusToTheCanvas()
    {
        await Run(() =>
        {
            var fixture = BuildFixture();

            // Focus starts away from the canvas (e.g. the ribbon, ribbon tab strip, or Selection
            // Pane list in the real host) -- exactly what makes a subsequent canvas focus a real,
            // observable transition and not a same-element no-op.
            fixture.OtherFocusTarget.Focus();
            fixture.OtherFocusTarget.IsFocused.Should().BeTrue();
            fixture.Canvas.IsFocused.Should().BeFalse();

            // Selection Pane / any direct EditingSession.Select caller -- deliberately NOT routed
            // through AvaloniaCanvasGestureHandler.OnPointerPressed, whose own _canvas.Focus() call
            // only fires for pointer-driven shape selection.
            fixture.Editor.Select(fixture.ShapeA.Id);

            fixture.Canvas.IsFocused.Should().BeTrue(
                "selecting a shape must move real Avalonia keyboard focus onto the canvas so its " +
                "AT-SPI/UIA bridge re-queries automation focus and a screen reader announces the " +
                "newly selected shape, matching the WPF twin's AutomationFocusChanged behavior");

            fixture.Window.Close();
        });
    }

    [Fact]
    public async Task ClearSelection_DoesNotThrowAndDoesNotStealFocusFromElsewhere()
    {
        await Run(() =>
        {
            var fixture = BuildFixture();

            // Select, then move focus away again (simulating the user tabbing to a panel after
            // selecting a shape), then clear the selection. ClearShapeFocus (no CurrentPeer) must
            // not attempt to steal focus back onto the canvas -- there is no specific shape to
            // represent, so real focus should simply stay wherever the user put it.
            fixture.Editor.Select(fixture.ShapeA.Id);
            fixture.Canvas.IsFocused.Should().BeTrue();
            fixture.OtherFocusTarget.Focus();
            fixture.OtherFocusTarget.IsFocused.Should().BeTrue();

            var act = () => fixture.Editor.ClearSelection();
            act.Should().NotThrow();

            fixture.Canvas.IsFocused.Should().BeFalse(
                "clearing the selection carries no specific shape to focus, so the fix must not " +
                "yank real keyboard focus back onto the canvas away from wherever the user is");
            fixture.OtherFocusTarget.IsFocused.Should().BeTrue();

            fixture.Window.Close();
        });
    }

    [Fact]
    public async Task Select_ADifferentShapeWhileCanvasAlreadyFocused_LeavesModelSelectionStateCorrect()
    {
        await Run(() =>
        {
            var fixture = BuildFixture();

            fixture.Editor.Select(fixture.ShapeA.Id);
            fixture.Canvas.IsFocused.Should().BeTrue();

            // Re-selecting a different shape while the canvas already holds real focus must not
            // throw, and the model-level selection/focus state (already covered by
            // SlideCanvasAutomationPeerAvaloniaTests.EditingSessionSelectionChanged_UpdatesShapePeerIsSelected)
            // must remain correct regardless of whether this particular reselection produces a
            // fresh native GotFocus transition.
            var act = () => fixture.Editor.Select(fixture.ShapeB.Id);
            act.Should().NotThrow();

            fixture.Canvas.IsFocused.Should().BeTrue();
            fixture.Editor.SelectedShapeIds.Should().ContainSingle(id => id == fixture.ShapeB.Id);

            fixture.Window.Close();
        });
    }
}
