using System.Linq;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// R134: the slide editing canvas previously exposed zero UI Automation peer/properties --
/// a screen reader got nothing at all for the primary editing surface (no shape names, no
/// selection, no structure). These tests cover the custom automation peer added to
/// <see cref="SlideCanvas"/> (mirrors FreeX.App.UI.GridView's pattern -- see
/// src/FreeX.App.UI/GridView.cs and its GridViewAutomationPeerTests/
/// GridViewSelectionAutomationNotificationTests, which this test class parallels): the canvas
/// itself with a meaningful name/role, each shape as a child element with its name/alt-text and
/// selection state, and selection-change notifications routed from
/// <see cref="EditingSession.SelectionChanged"/>.
/// </summary>
public sealed class SlideCanvasAutomationPeerTests
{
    private static (SlideCanvas Canvas, EditingSession Editor, SlideShape ShapeA, SlideShape ShapeB) BuildCanvas()
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
            // No Name set -- exercises the AlternativeText fallback (Wave 26/R134 alt-text
            // announcement, analogous to GridView's comment/hyperlink cue fallbacks).
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
        // AttachEditing is the real production wiring path (see MainWindow.AttachCanvasEditing)
        // that points the canvas's automation peer at the live EditingSession.
        canvas.AttachEditing(editor, new Canvas());

        return (canvas, editor, shapeA, shapeB);
    }

    [StaFact]
    public void SlideCanvasAutomationPeer_ExposesCanvasNameRoleAndSelectionPattern()
    {
        var (canvas, _, _, _) = BuildCanvas();

        var peer = UIElementAutomationPeer.CreatePeerForElement(canvas);
        peer.Should().NotBeNull();
        peer.GetAutomationControlType().Should().Be(AutomationControlType.Pane);
        peer.GetName().Should().Be("Slide 1 canvas");

        var selectionProvider = peer.GetPattern(PatternInterface.Selection)
            .Should().BeAssignableTo<ISelectionProvider>().Subject;
        selectionProvider.CanSelectMultiple.Should().BeTrue();
        selectionProvider.GetSelection().Should().BeEmpty();
    }

    [StaFact]
    public void SlideCanvasAutomationPeer_ExposesEachShapeAsAChildWithNameAndSelectionItemPattern()
    {
        var (canvas, _, shapeA, shapeB) = BuildCanvas();

        var peer = UIElementAutomationPeer.CreatePeerForElement(canvas);
        var children = peer.GetChildren();
        children.Should().HaveCount(2);

        var shapeAPeer = children.Single(c => c.GetName() == shapeA.Name);
        var shapeAItem = shapeAPeer.GetPattern(PatternInterface.SelectionItem)
            .Should().BeAssignableTo<ISelectionItemProvider>().Subject;
        shapeAItem.IsSelected.Should().BeFalse();
        shapeAPeer.GetAutomationId().Should().Be("Shape_1");
        shapeAPeer.GetClassName().Should().Be("SlideShape");
        shapeAPeer.GetAutomationControlType().Should().Be(AutomationControlType.Image);
        Action selectFromAutomation = shapeAItem.Select;
        selectFromAutomation.Should().Throw<InvalidOperationException>()
            .WithMessage(PresentationCanvasAutomationSession.SelectionMutationNotSupportedMessage);

        // Shape B has a blank Name; its announced name must fall back to AlternativeText
        // rather than being blank/unannounced.
        var shapeBPeer = children.Single(c => c.GetName() == shapeB.AlternativeText);
        shapeBPeer.GetAutomationId().Should().Be("Shape_2");
        shapeBPeer.GetAutomationControlType().Should().Be(AutomationControlType.DataGrid);
    }

    [StaFact]
    public void EditingSessionSelectionChanged_UpdatesShapePeerIsSelectedAndKeyboardFocus()
    {
        var (canvas, editor, shapeA, shapeB) = BuildCanvas();

        var peer = UIElementAutomationPeer.CreatePeerForElement(canvas);
        var children = peer.GetChildren();
        var shapeAPeer = children.Single(c => c.GetName() == shapeA.Name);
        var shapeBPeer = children.Single(c => c.GetName() == shapeB.AlternativeText);
        var shapeAItem = (ISelectionItemProvider)shapeAPeer.GetPattern(PatternInterface.SelectionItem)!;
        var shapeBItem = (ISelectionItemProvider)shapeBPeer.GetPattern(PatternInterface.SelectionItem)!;
        var liveSelection = editor.SelectedShapeIds;

        shapeAItem.IsSelected.Should().BeFalse();
        shapeAPeer.HasKeyboardFocus().Should().BeFalse();

        // Real production selection path (CanvasGestureHandler click-to-select and the ribbon's
        // Selection Pane both funnel through EditingSession.Select).
        editor.Select(shapeA.Id);

        editor.SelectedShapeIds.Should().BeSameAs(liveSelection);
        shapeAItem.IsSelected.Should().BeTrue();
        shapeAPeer.HasKeyboardFocus().Should().BeTrue();

        var selectionProvider = (ISelectionProvider)peer.GetPattern(PatternInterface.Selection)!;
        selectionProvider.GetSelection().Should().ContainSingle();

        // EditingSession mutates the same selected-id list before raising SelectionChanged.
        // The shared automation session must retain a detached baseline and move focus to the
        // last selected shape rather than treating every selected peer as focused.
        editor.Select(shapeB.Id, addToSelection: true);

        editor.SelectedShapeIds.Should().BeSameAs(liveSelection);
        shapeAItem.IsSelected.Should().BeTrue();
        shapeAPeer.HasKeyboardFocus().Should().BeFalse();
        shapeBItem.IsSelected.Should().BeTrue();
        shapeBPeer.HasKeyboardFocus().Should().BeTrue();
        selectionProvider.GetSelection().Should().HaveCount(2);

        editor.ClearSelection();

        shapeAItem.IsSelected.Should().BeFalse();
        shapeAPeer.HasKeyboardFocus().Should().BeFalse();
        shapeBItem.IsSelected.Should().BeFalse();
        shapeBPeer.HasKeyboardFocus().Should().BeFalse();
        selectionProvider.GetSelection().Should().BeEmpty();
    }
}
