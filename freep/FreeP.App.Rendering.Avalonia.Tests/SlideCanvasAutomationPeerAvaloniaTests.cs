using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// R134: the slide editing canvas previously exposed zero UI Automation peer/properties in
/// this shell either -- a screen reader got nothing at all for the primary editing surface.
/// These tests cover the custom automation peer added to <see cref="SlideCanvas"/> (mirrors
/// FreeX.App.UI.GridView's WPF pattern; the WPF twin FreeP.App.Rendering.Wpf.SlideCanvas
/// follows the same model directly and has the parallel
/// FreeP.App.Host.Tests.SlideCanvasAutomationPeerTests coverage): the canvas itself with a
/// meaningful name/role, each shape as a child element with its name/alt-text and selection
/// state, and selection-change notifications routed from
/// <see cref="EditingSession.SelectionChanged"/>.
/// </summary>
public sealed class SlideCanvasAutomationPeerAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    private static Task Run(System.Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static (SlideCanvas Canvas, EditingSession Editor, SlideShape ShapeA, SlideShape ShapeB)
        BuildCanvas()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();

        var shapeA = new SlideShape
        {
            Id = 1,
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
            // No Name set -- exercises the AlternativeText fallback.
            AlternativeText = "A decorative circle",
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
            Slide = slide
        };

        var adorner = new SelectionAdornerLayer();
        // The real production wiring path (FreeP.App.Avalonia.MainWindow.WireInteraction) that
        // points the canvas's automation peer at the live EditingSession.
        var handler = new AvaloniaCanvasGestureHandler(canvas, editor, adorner);
        canvas.AttachGestureHandler(handler);

        return (canvas, editor, shapeA, shapeB);
    }

    [Fact]
    public async Task SlideCanvasAutomationPeer_ExposesCanvasNameRoleAndSelectionPattern()
    {
        await Run(() =>
        {
            var (canvas, _, _, _) = BuildCanvas();

            var peer = ControlAutomationPeer.CreatePeerForElement(canvas);
            peer.Should().NotBeNull();
            peer.GetAutomationControlType().Should().Be(AutomationControlType.Pane);
            peer.GetName().Should().Be("Slide 1 canvas");

            var selectionProvider = peer.GetProvider<ISelectionProvider>();
            selectionProvider.Should().NotBeNull();
            selectionProvider!.CanSelectMultiple.Should().BeTrue();
            selectionProvider.GetSelection().Should().BeEmpty();
        });
    }

    [Fact]
    public async Task SlideCanvasAutomationPeer_ExposesEachShapeAsAChildWithNameAndSelectionItemPattern()
    {
        await Run(() =>
        {
            var (canvas, _, shapeA, shapeB) = BuildCanvas();

            var peer = ControlAutomationPeer.CreatePeerForElement(canvas);
            var children = peer.GetChildren();
            children.Should().HaveCount(2);

            var shapeAPeer = children.Single(c => c.GetName() == shapeA.Name);
            var shapeAItem = shapeAPeer.GetProvider<ISelectionItemProvider>();
            shapeAItem.Should().NotBeNull();
            shapeAItem!.IsSelected.Should().BeFalse();

            // Shape B has a blank Name; its announced name must fall back to AlternativeText
            // rather than being blank/unannounced.
            var shapeBPeer = children.Single(c => c.GetName() == shapeB.AlternativeText);
            shapeBPeer.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task EditingSessionSelectionChanged_UpdatesShapePeerIsSelected()
    {
        await Run(() =>
        {
            var (canvas, editor, shapeA, _) = BuildCanvas();

            var peer = ControlAutomationPeer.CreatePeerForElement(canvas);
            var children = peer.GetChildren();
            var shapeAPeer = children.Single(c => c.GetName() == shapeA.Name);
            var shapeAItem = shapeAPeer.GetProvider<ISelectionItemProvider>();
            shapeAItem.Should().NotBeNull();

            shapeAItem!.IsSelected.Should().BeFalse();

            // Real production selection path (AvaloniaCanvasGestureHandler click-to-select and
            // the ribbon's Selection Pane both funnel through EditingSession.Select).
            editor.Select(shapeA.Id);

            shapeAItem.IsSelected.Should().BeTrue();

            var selectionProvider = peer.GetProvider<ISelectionProvider>();
            selectionProvider!.GetSelection().Should().ContainSingle();

            editor.ClearSelection();

            shapeAItem.IsSelected.Should().BeFalse();
            selectionProvider.GetSelection().Should().BeEmpty();
        });
    }
}
