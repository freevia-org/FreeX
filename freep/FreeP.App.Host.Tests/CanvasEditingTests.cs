using System.IO;
using System.Windows;
using System.Windows.Input;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.App.Rendering.Wpf;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
// Disambiguate: this test file exercises the WPF ShapeHitTester compatibility facade.
// The implementation lives in FreeP.App.Compositor.
using ShapeHitTester = FreeP.App.Rendering.Wpf.ShapeHitTester;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Tests for Wave 3C canvas editing infrastructure:
/// hit-testing, transform, gesture-to-command mapping, adorner state.
///
/// Design: the interaction helpers (SlideTransform, ShapeHitTester, CanvasGestureHandler's
/// ComputeResizeBounds / ComputeRotationAngle) are unit-testable without a live window.
/// Tests that require STA use [StaFact]; pure-logic tests use [Fact].
/// </summary>
public sealed class CanvasEditingTests
{
    [Fact]
    public void DoubleClickPolicy_TextlessShapesContinueSelection_TextShapesDeferToEditor()
    {
        CanvasGestureHandler.ShouldContinueDoubleClickSelection(
            new SlideShape { Kind = SlideShapeKind.AutoShape })
            .Should().BeTrue();
        CanvasGestureHandler.ShouldContinueDoubleClickSelection(
            new SlideShape
            {
                Kind = SlideShapeKind.AutoShape,
                TextBody = new TextBody
                {
                    Paragraphs =
                    {
                        new Paragraph { Runs = { new Run { Text = "Edit me" } } }
                    }
                }
            })
            .Should().BeFalse();
    }

    [Fact]
    public void DoubleClickPolicy_ZoomNavigationIsTerminalBeforeSelection()
    {
        var source = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Wpf",
            "CanvasGestureHandler.cs").Replace("\r\n", "\n");
        var start = source.IndexOf(
            "if (shape?.Kind == SlideShapeKind.Zoom &&",
            StringComparison.Ordinal);
        var end = source.IndexOf("// Text editing", start, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        source[start..end].Should().Contain("e.Handled = true;\n                return;");
    }

    [StaFact]
    public void GestureHandler_CaptureLoss_CancelsPendingResize()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 914400L,
        };
        slide.Shapes.Add(shape);

        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        editor.Select(shape.Id);
        var canvas = new SlideCanvas();
        var handler = new CanvasGestureHandler(canvas, editor);

        handler.SeedResizeStateForTests(
            new Point(100, 100),
            shape,
            CanvasGestureHandleKind.ResizeSE);
        handler.IsGestureActiveForTests.Should().BeTrue();

        canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = UIElement.LostMouseCaptureEvent,
        });

        handler.IsGestureActiveForTests.Should().BeFalse();
    }

    [StaFact]
    public void GestureHandler_Escape_CancelsResizeAndIgnoresStaleMouseUp()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 914400L,
            OffsetYEmu = 457200L,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 914400L,
            RotationDeg = 12,
        };
        slide.Shapes.Add(shape);

        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        editor.Select(shape.Id);
        var canvas = new SlideCanvas();
        var handler = new CanvasGestureHandler(canvas, editor);

        handler.SeedResizeStateForTests(
            new Point(100, 100),
            shape,
            CanvasGestureHandleKind.ResizeSE);
        handler.SeedTransientInteractionVisualsForTests();
        handler.IsGestureActiveForTests.Should().BeTrue();
        handler.HasPendingGestureStateForTests.Should().BeTrue();
        handler.HasTransientInteractionVisualsForTests.Should().BeTrue();

        handler.HandleEscapeForTests().Should().BeTrue();
        handler.SimulateStaleMouseUpForTests();

        handler.IsGestureActiveForTests.Should().BeFalse();
        handler.HasPendingGestureStateForTests.Should().BeFalse();
        handler.HasTransientInteractionVisualsForTests.Should().BeFalse();
        editor.CanUndo.Should().BeFalse("Escape must cancel before a later mouse-up can commit");
        shape.OffsetXEmu.Should().Be(914400L);
        shape.OffsetYEmu.Should().Be(457200L);
        shape.ExtentCxEmu.Should().Be(914400L);
        shape.ExtentCyEmu.Should().Be(914400L);
        shape.RotationDeg.Should().Be(12);
    }
    // ── SlideTransform ────────────────────────────────────────────────────────────

    [StaFact]
    public void GestureHandler_MultiSelectionMove_BelowStartThresholdDoesNotCommit()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var first = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 914400L,
            OffsetYEmu = 457200L,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 914400L,
        };
        var second = new SlideShape
        {
            Id = 2,
            OffsetXEmu = 2743200L,
            OffsetYEmu = 457200L,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 914400L,
        };
        slide.Shapes.Add(first);
        slide.Shapes.Add(second);

        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        editor.Select(first.Id);
        editor.Select(second.Id, addToSelection: true);
        var canvas = new SlideCanvas();
        var handler = new CanvasGestureHandler(canvas, editor);

        handler.SeedMoveStateForTests(new Point(100, 100));
        handler.CompleteGestureForTests(new Point(102, 100));

        first.OffsetXEmu.Should().Be(914400L);
        second.OffsetXEmu.Should().Be(2743200L);
        editor.CanUndo.Should().BeFalse("a sub-threshold multi-selection move is not a user action");
    }

    [Fact]
    public void SlideTransform_Compute_CorrectScale_CenteredSlide()
    {
        // 10"×7.5" slide in DIP (960×720 DIP at 96 dpi)
        double slideDipW = 960, slideDipH = 720;
        // Render area also 960×720 → scale=1, no offset
        var xf = SlideTransform.Compute(960, 720, slideDipW, slideDipH);

        xf.Scale.Should().BeApproximately(1.0, 1e-9);
        xf.OffsetX.Should().BeApproximately(0.0, 1e-9);
        xf.OffsetY.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void SlideTransform_Compute_Letterbox_WideRenderArea()
    {
        // 10"×7.5" slide = 960×720 DIP, render area = 1920×720 → scale=1, offsetX=480
        var xf = SlideTransform.Compute(1920, 720, 960, 720);
        xf.Scale.Should().BeApproximately(1.0, 1e-9);
        xf.OffsetX.Should().BeApproximately(480.0, 1e-9);
        xf.OffsetY.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void SlideTransform_ScreenToSlide_RoundTrip()
    {
        var xf = SlideTransform.Compute(800, 600, 960, 720);
        var screenPt = new Point(400, 300);
        var slidePt  = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        var roundTrip = xf.SlideToScreen(slidePt.X, slidePt.Y);

        roundTrip.X.Should().BeApproximately(screenPt.X, 1e-6);
        roundTrip.Y.Should().BeApproximately(screenPt.Y, 1e-6);
    }

    [Fact]
    public void SlideTransform_DipToEmu_EmuToDip_Roundtrip()
    {
        long emu = 914400L; // 1 inch
        double dip = SlideTransform.EmuToDip(emu);
        long emuBack = SlideTransform.DipToEmu(dip);
        dip.Should().BeApproximately(96.0, 1e-9);   // 96 DIP = 1 inch
        emuBack.Should().Be(emu);
    }

    [Fact]
    public void SlideTransform_WpfFacadeDelegatesToCompositorCore()
    {
        var transform = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideTransform.cs");
        var canvas = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var gestures = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "CanvasGestureHandler.cs");

        transform.Should().Contain("internal SlideTransformCore Core");
        transform.Should().Contain("Core.SlideToScreen");
        transform.Should().Contain("Core.ScreenToSlide");
        transform.Should().Contain("SlideTransformCore.DipToEmu");
        transform.Should().Contain("SlideTransformCore.Compute");
        transform.Should().NotContain("private const double EmuPerDip");
        transform.Should().NotContain("Math.Min(renderW / slideWidthDip");

        canvas.Should().Contain("SlideTransform.Compute(renderW, renderH");
        gestures.Should().Contain("=> xf.Core;");
        gestures.Should().NotContain("new(xf.Scale, xf.OffsetX");
    }

    // ── ShapeHitTester ────────────────────────────────────────────────────────────

    [Fact]
    public void InCanvasTableCellEditor_ProjectsSharedInitialSelectionPlan()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "InCanvasTableCellEditor.cs");

        source.Should().Contain("TableCellEditPlanner.BeginEdit");
        source.Should().Contain("_cellEditPlan = editStart.EditPlanner");
        source.Should().Contain("TextBodyFlowDocumentConverter.ToFlowDocument");
        source.Should().Contain("TextBodyFlowDocumentConverter.FromFlowDocument");
        source.Should().Contain("TableCellEditPlanner.CommitRichText");
        source.Should().Contain("ApplyInitialSelection(_cellTextBox, editStart.InitialSelection)");
        source.Should().Contain("TableCellEditPlanner.PlanNavigation");
        source.Should().Contain("TryNavigateActiveTableCell");
        source.Should().Contain("RichTextBox");
        source.Should().Contain("TableCellEditPlanner.PlanKeyboard");
        source.Should().Contain("ToTableCellEditKeyboardModifiers");
        source.Should().Contain("ExecuteCellFormattingCommand(EditingCommands.ToggleBold)");
        source.Should().Contain("ApplyWithPreservedSelection");
        source.Should().Contain("_cellTextBox.Selection.Select(selectionStart, selectionEnd)");
    }

    private static (Presentation pres, Slide slide, SlideShape shape1, SlideShape shape2) MakeTestSlide()
    {
        var pres  = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Clear();

        // Shape 1 (bottom z-order): 100×100 DIP at (0,0) → EMU: offX=0, offY=0, cx=952500, cy=952500
        var shape1 = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 952500L,  // 100 DIP
            ExtentCyEmu = 952500L,
            Fill        = new ShapeFill.Solid(new SrgbColor(0xFF, 0, 0))
        };

        // Shape 2 (top z-order): 50×50 DIP at (50,50) DIP → EMU: offX=476250, offY=476250, cx=476250, cy=476250
        // Overlaps shape1 in the 50..100 range. Z-order: shape2 on top.
        var shape2 = new SlideShape
        {
            Id          = 2,
            OffsetXEmu  = 476250L,  // 50 DIP
            OffsetYEmu  = 476250L,
            ExtentCxEmu = 476250L,  // 50 DIP
            ExtentCyEmu = 476250L,
            Fill        = new ShapeFill.Solid(new SrgbColor(0, 0xFF, 0))
        };

        slide.Shapes.Add(shape1);
        slide.Shapes.Add(shape2);
        return (pres, slide, shape1, shape2);
    }

    [Fact]
    public void HitTest_PointOnTopShape_ReturnsTopShapeId()
    {
        var (pres, slide, _, shape2) = MakeTestSlide();
        // 75 DIP = 75*9525 = 714375 EMU → inside both shapes; topmost is shape2
        var hit = ShapeHitTester.HitTest(slide, pres, 75.0, 75.0);
        hit.Should().Be(shape2.Id);
    }

    [Fact]
    public void HitTest_PointOnBottomShapeOnly_ReturnsBottomShapeId()
    {
        var (pres, slide, shape1, _) = MakeTestSlide();
        // (25, 25) DIP: inside shape1 only (shape2 starts at 50 DIP)
        var hit = ShapeHitTester.HitTest(slide, pres, 25.0, 25.0);
        hit.Should().Be(shape1.Id);
    }

    [Fact]
    public void HitTest_PointOutsideAllShapes_ReturnsNull()
    {
        var (pres, slide, _, _) = MakeTestSlide();
        // (200, 200) DIP: outside both shapes (both are ≤100 DIP wide)
        var hit = ShapeHitTester.HitTest(slide, pres, 200.0, 200.0);
        hit.Should().BeNull();
    }

    [Fact]
    public void HitTest_PointOnGroupedChild_ReturnsChildId()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Clear();

        var child = new SlideShape
        {
            Id = 21,
            OffsetXEmu = 952500L,
            OffsetYEmu = 952500L,
            ExtentCxEmu = 476250L,
            ExtentCyEmu = 476250L,
            Fill = new ShapeFill.Solid(new SrgbColor(0x20, 0x40, 0x80))
        };
        var group = new SlideShape
        {
            Id = 20,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 952500L,
            OffsetYEmu = 952500L,
            ExtentCxEmu = 476250L,
            ExtentCyEmu = 476250L
        };
        group.Children.Add(child);
        slide.Shapes.Add(group);

        ShapeHitTester.HitTest(slide, pres, 120.0, 120.0).Should().Be(child.Id);
        ShapeHitTester.FindShape(slide, child.Id).Should().BeSameAs(child);
        ShapeHitTester.GetShapeBoundsDip(slide, pres, child.Id).Should().NotBeNull();

        var moveState = CanvasGesturePlanner.CaptureMoveState(slide, [child.Id]);
        moveState.Should().ContainSingle().Which.ShapeId.Should().Be(child.Id);
    }

    [Fact]
    public void MarqueeHitTest_OverlapsAll_ReturnsBothShapes()
    {
        var (pres, slide, shape1, shape2) = MakeTestSlide();
        var hits = ShapeHitTester.MarqueeHitTest(slide, pres, 0, 0, 200, 200);
        hits.Should().Contain(shape1.Id).And.Contain(shape2.Id);
        hits.Should().HaveCount(2);
    }

    [Fact]
    public void MarqueeHitTest_OverlapsOnlyBottom_ReturnsBottomOnly()
    {
        var (pres, slide, shape1, _) = MakeTestSlide();
        // Marquee (0..40) DIP — only shape1 overlaps
        var hits = ShapeHitTester.MarqueeHitTest(slide, pres, 0, 0, 40, 40);
        hits.Should().ContainSingle().Which.Should().Be(shape1.Id);
    }

    [Fact]
    public void GetShapeBoundsDip_NonPlaceholder_ReturnsShapeBounds()
    {
        var pres  = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Clear();

        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 914400L,   // 1 inch = 96 DIP
            OffsetYEmu  = 1828800L,  // 2 inch = 192 DIP
            ExtentCxEmu = 2743200L,  // 3 inch = 288 DIP
            ExtentCyEmu = 1828800L,  // 2 inch = 192 DIP
        };
        slide.Shapes.Add(shape);

        var b = ShapeHitTester.GetShapeBoundsDip(shape, pres);
        b.Left.Should().BeApproximately(96.0,   1e-6);
        b.Top.Should().BeApproximately(192.0,  1e-6);
        b.Width.Should().BeApproximately(288.0, 1e-6);
        b.Height.Should().BeApproximately(192.0, 1e-6);
    }

    [Fact]
    public void ShapeHitTester_WpfFacadeDelegatesToCompositorImplementation()
    {
        var source = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "ShapeHitTester.cs");

        source.Should().Contain("FreeP.App.Compositor.ShapeHitTester.HitTest");
        source.Should().Contain("FreeP.App.Compositor.ShapeHitTester.MarqueeHitTest");
        source.Should().Contain("FreeP.App.Compositor.ShapeHitTester.GetShapeBoundsDip");
        source.Should().NotContain("PlaceholderResolver.ResolveAnchor");
        source.Should().NotContain("SlideTransformCore.UnRotatePoint");
        source.Should().NotContain("private const double EmuPerDip");
        source.Should().NotContain("private static bool HitTestShape");
    }

    private static string ReadWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = directory.FullName;
            relativeParts.CopyTo(parts, 1);

            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate workspace file.",
            Path.Combine(relativeParts));
    }

    // ── SelectionAdorner handle positions ────────────────────────────────────────

    [StaFact]
    public void SelectionAdorner_GetHandleCenters_ReturnsCorrectPositions()
    {
        var rect    = new Rect(100, 50, 200, 100);
        var centers = SelectionAdorner.GetHandleCenters(rect);

        centers.Should().HaveCount(8);

        // N: top-center
        centers[0].X.Should().BeApproximately(200, 1e-9); // 100 + 200/2
        centers[0].Y.Should().BeApproximately(50,  1e-9);

        // SE: bottom-right
        centers[3].X.Should().BeApproximately(300, 1e-9); // right
        centers[3].Y.Should().BeApproximately(150, 1e-9); // bottom
    }

    [StaFact]
    public void SelectionAdorner_GetRotateHandleCenter_IsAboveTopCenter()
    {
        var rect   = new Rect(100, 50, 200, 100);
        var center = SelectionAdorner.GetRotateHandleCenter(rect);

        center.X.Should().BeApproximately(200, 1e-9); // top-center X
        center.Y.Should().BeLessThan(50.0);           // above the top edge
    }

    [StaFact]
    public void SelectionAdorner_HitTestHandle_Body_InsideRect()
    {
        var adorner = new SelectionAdorner(new System.Windows.Controls.Canvas());
        var rect    = new Rect(100, 100, 200, 100);
        var handle  = adorner.HitTestHandle(rect, new Point(200, 150)); // inside
        handle.Should().Be(CanvasGestureHandleKind.Body);
    }

    [StaFact]
    public void SelectionAdorner_HitTestHandle_None_OutsideRect()
    {
        var adorner = new SelectionAdorner(new System.Windows.Controls.Canvas());
        var rect    = new Rect(100, 100, 200, 100);
        var handle  = adorner.HitTestHandle(rect, new Point(5, 5)); // outside
        handle.Should().Be(CanvasGestureHandleKind.None);
    }

    [StaFact]
    public void SelectionAdorner_HitTestGeometryHandle_ReturnsPlannerHandleName()
    {
        var adorner = new SelectionAdorner(new System.Windows.Controls.Canvas());
        adorner.UpdateGeometryHandles([
            (Name: "adj1", Position: new Point(210, 70)),
            (Name: "adj2", Position: new Point(10, 70)),
        ]);

        adorner.HitTestGeometryHandle(new Point(211, 69)).Should().Be("adj1");
        adorner.HitTestGeometryHandle(new Point(10, 70)).Should().Be("adj2");
        adorner.HitTestGeometryHandle(new Point(100, 100)).Should().BeNull();
    }

    // ── CanvasGestureHandler.ComputeResizeBounds (pure logic) ────────────────────

    [StaFact]
    public void GestureHandler_ComputeResizeBounds_SE_ExpandsWidthAndHeight()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 914400L,   // 1 inch
            ExtentCyEmu = 914400L,
        };
        slide.Shapes.Add(shape);

        var canvas  = new SlideCanvas();
        var bus     = new PresentationCommandBus(p);
        var editor  = new EditingSession(p, bus);
        editor.Select(shape.Id);

        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);

        // Get the gesture handler via the canvas indirectly by reconstructing the resize calc:
        // Use SlideTransform(scale=1, offset=0,0)
        var xf = new SlideTransform(1, 0, 0, 960, 720);

        // Simulate SE drag +50px screen = +50 DIP = +476250 EMU
        // We reconstruct the helper directly since the handler is internal
        var handler = new ResizeBoundsTestHelper
        {
            StartScreen   = new Point(100, 100),
            OrigX         = 0L,
            OrigY         = 0L,
            OrigCx        = 914400L,
            OrigCy        = 914400L,
            Handle        = CanvasGestureHandleKind.ResizeSE
        };

        var (nx, ny, ncx, ncy) = handler.Compute(new Point(150, 160), xf);

        nx.Should().Be(0L,  "X unchanged for SE");
        ny.Should().Be(0L,  "Y unchanged for SE");
        ncx.Should().BeGreaterThan(914400L, "width grew");
        ncy.Should().BeGreaterThan(914400L, "height grew");
    }

    [StaFact]
    public void GestureHandler_ResizeBounds_NW_MovesOriginAndReducesSize()
    {
        var xf = new SlideTransform(1, 0, 0, 960, 720);
        var handler = new ResizeBoundsTestHelper
        {
            StartScreen   = new Point(100, 100),
            OrigX         = 476250L,  // 50 DIP
            OrigY         = 476250L,
            OrigCx        = 952500L,  // 100 DIP
            OrigCy        = 952500L,
            Handle        = CanvasGestureHandleKind.ResizeNW
        };

        // Drag NW +10px screen → should shrink (10 DIP = 95250 EMU)
        var (nx, ny, ncx, ncy) = handler.Compute(new Point(110, 110), xf);

        nx.Should().BeGreaterThan(476250L,  "origin moved right (drag right = NW shrinks from left)");
        ny.Should().BeGreaterThan(476250L);
        ncx.Should().BeLessThan(952500L,   "width shrank");
        ncy.Should().BeLessThan(952500L);
    }

    // ── EditingSession + command bus integration (undo = one command per gesture) ─

    [Fact]
    public void MoveSelected_IssuesMoveCommand_UndoRestoresPosition()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id         = 1,
            OffsetXEmu = 0L,
            OffsetYEmu = 0L,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 914400L
        };
        slide.Shapes.Add(shape);

        var bus    = new PresentationCommandBus(p);
        var editor = new EditingSession(p, bus);
        editor.Select(shape.Id);

        long origX = shape.OffsetXEmu;
        long origY = shape.OffsetYEmu;

        // Simulate one move gesture (one command)
        editor.MoveSelected(914400L, 457200L);

        shape.OffsetXEmu.Should().Be(origX + 914400L);
        shape.OffsetYEmu.Should().Be(origY + 457200L);

        editor.CanUndo.Should().BeTrue("move command should be undoable");

        editor.Undo();

        shape.OffsetXEmu.Should().Be(origX, "undo restores original X");
        shape.OffsetYEmu.Should().Be(origY, "undo restores original Y");
    }

    [Fact]
    public void ResizeShape_IssuesOneCommand_UndoRestoresBounds()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 0L,
            OffsetYEmu  = 0L,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 914400L
        };
        slide.Shapes.Add(shape);

        var bus    = new PresentationCommandBus(p);
        var editor = new EditingSession(p, bus);

        editor.ResizeShape(shape.Id, 100L, 200L, 1828800L, 1371600L);

        shape.OffsetXEmu.Should().Be(100L);
        shape.ExtentCxEmu.Should().Be(1828800L);

        editor.CanUndo.Should().BeTrue();
        editor.Undo();
        shape.OffsetXEmu.Should().Be(0L);
        shape.ExtentCxEmu.Should().Be(914400L);
    }

    [Fact]
    public void RotateShape_IssuesOneCommand_UndoRestoresAngle()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape { Id = 1, ExtentCxEmu = 914400L, ExtentCyEmu = 914400L };
        slide.Shapes.Add(shape);

        var bus    = new PresentationCommandBus(p);
        var editor = new EditingSession(p, bus);

        editor.RotateShape(shape.Id, 45.0);

        shape.RotationDeg.Should().BeApproximately(45.0, 1e-9);
        editor.CanUndo.Should().BeTrue();

        editor.Undo();
        shape.RotationDeg.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void DeleteSelected_IssuesOneCommandPerShape_UndoRestoresAll()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();
        var s1 = new SlideShape { Id = 1, ExtentCxEmu = 914400L, ExtentCyEmu = 914400L };
        var s2 = new SlideShape { Id = 2, ExtentCxEmu = 914400L, ExtentCyEmu = 914400L };
        slide.Shapes.Add(s1);
        slide.Shapes.Add(s2);

        var bus    = new PresentationCommandBus(p);
        var editor = new EditingSession(p, bus);
        editor.Select(s1.Id);
        editor.Select(s2.Id, addToSelection: true);

        editor.DeleteSelected();
        slide.Shapes.Should().BeEmpty();

        // Undo both deletions
        editor.Undo();
        editor.Undo();
        slide.Shapes.Should().HaveCount(2);
    }

    [StaFact]
    public void SelectionAdorner_UpdateSelection_CountMatchesSelectedShapes()
    {
        var adorner = new SelectionAdorner(new System.Windows.Controls.Canvas());

        var rects = new[]
        {
            (id: 1u, screenRect: new Rect(0, 0, 100, 50)),
            (id: 2u, screenRect: new Rect(100, 0, 100, 50)),
            (id: 3u, screenRect: new Rect(200, 0, 100, 50)),
        };

        adorner.UpdateSelection(rects);
        adorner.SelectionRects.Should().HaveCount(3, "adorner count == selected shape count");
    }

    [StaFact]
    public void AttachEditing_DoesNotThrow()
    {
        var canvas  = new SlideCanvas();
        var p       = Presentation.CreateEmpty();
        var bus     = new PresentationCommandBus(p);
        var editor  = new EditingSession(p, bus);
        var overlay = new System.Windows.Controls.Canvas();

        var act = () => canvas.AttachEditing(editor, overlay);
        act.Should().NotThrow();
    }

    [StaFact]
    public void AttachEditing_ExposesCurrentTransformIdentity_BeforeRender()
    {
        var canvas = new SlideCanvas();
        canvas.CurrentTransform.Should().NotBeNull();
    }

    [StaFact]
    public void SlideCanvas_EditPointsMode_IsForwardedToGestureHandler()
    {
        var canvas  = new SlideCanvas();
        var p       = Presentation.CreateEmpty();
        var bus     = new PresentationCommandBus(p);
        var editor  = new EditingSession(p, bus);
        var overlay = new System.Windows.Controls.Canvas();

        canvas.AttachEditing(editor, overlay);
        canvas.EditPointsEnabled.Should().BeTrue();

        canvas.SetEditPointsMode(false);

        canvas.EditPointsEnabled.Should().BeFalse();
    }

    [StaFact]
    public void SlideCanvas_ReattachEditing_PreservesEditPointsMode()
    {
        var canvas = new SlideCanvas();
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var overlay = new System.Windows.Controls.Canvas();

        canvas.AttachEditing(editor, overlay);
        canvas.SetEditPointsMode(false);
        canvas.AttachEditing(editor, overlay);

        canvas.EditPointsEnabled.Should().BeFalse();
    }

    [StaFact]
    public void WpfEditPointsRibbonState_FollowsSharedModePlannerAndCanvas()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var registry = FreePRibbonCommands.Build(
                new RibbonStateStore(),
                window.Editor,
                getEditPointsEnabled: () => window.SlideCanvas.EditPointsEnabled,
                setEditPointsEnabled: enabled => window.SlideCanvas.SetEditPointsMode(enabled));
            registry.TryGet(PresentationEditPointsModePlanner.CommandId, out var registered)
                .Should().BeTrue();
            var command = registered.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;

            command.GetState().IsChecked.Should().BeTrue();
            command.Execute(RibbonCommandContext.Empty);
            command.GetState().IsChecked.Should().BeFalse();
            window.SlideCanvas.EditPointsEnabled.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void WpfEditPointsRoute_UsesSharedPlannerAndSingleCommandBoundary()
    {
        var gestures = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "CanvasGestureHandler.cs");
        var avaloniaGestures = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "AvaloniaCanvasGestureHandler.cs");
        var adorner = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SelectionAdorner.cs");

        gestures.Should().Contain("ShapeGeometryAdjustmentPlanner.BuildMutationPlan");
        gestures.Should().Contain("_editor.SetShapeGeometryAdjustment");
        gestures.Should().Contain("PictureCropAuthoringPlanner.BuildMutationPlan");
        gestures.Should().Contain("_editor.SetPictureCrop");
        gestures.Should().Contain("GestureKind.GeometryAdjustment");
        avaloniaGestures.Should().Contain("PictureCropAuthoringPlanner.BuildMutationPlan");
        avaloniaGestures.Should().Contain("_editor.SetPictureCrop");
        adorner.Should().Contain("UpdateGeometryHandles");
        adorner.Should().Contain("HitTestGeometryHandle");
    }

    [StaFact]
    public void MainWindow_HasSlideCanvas_AndEditorAfterConstruction()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.SlideCanvas.Should().NotBeNull();
            window.Editor.Should().NotBeNull();
        }
        finally
        {
            window.Close();
        }
    }
}

// ── Test helper: mirrors CanvasGestureHandler.ComputeResizeBounds logic ────────────────────────

/// <summary>
/// Test-only struct that mirrors the pure resize-bounds computation in
/// <see cref="CanvasGestureHandler.ComputeResizeBounds"/>, factored out so it can be
/// tested without STA mouse simulation.
/// </summary>
internal struct ResizeBoundsTestHelper
{
    public Point StartScreen;
    public long OrigX, OrigY, OrigCx, OrigCy;
    public CanvasGestureHandleKind Handle;

    public (long nx, long ny, long ncx, long ncy) Compute(Point endScreen, SlideTransform xf)
    {
        double dxPx = endScreen.X - StartScreen.X;
        double dyPx = endScreen.Y - StartScreen.Y;
        long   dx   = xf.ScreenDeltaToEmu(dxPx);
        long   dy   = xf.ScreenDeltaToEmu(dyPx);

        long x  = OrigX;
        long y  = OrigY;
        long cx = OrigCx;
        long cy = OrigCy;
        const long MinEmu = 91440L;

        switch (Handle)
        {
            case CanvasGestureHandleKind.ResizeN:
                y  = OrigY  + dy;
                cy = Math.Max(MinEmu, OrigCy - dy);
                break;
            case CanvasGestureHandleKind.ResizeS:
                cy = Math.Max(MinEmu, OrigCy + dy);
                break;
            case CanvasGestureHandleKind.ResizeW:
                x  = OrigX  + dx;
                cx = Math.Max(MinEmu, OrigCx - dx);
                break;
            case CanvasGestureHandleKind.ResizeE:
                cx = Math.Max(MinEmu, OrigCx + dx);
                break;
            case CanvasGestureHandleKind.ResizeNE:
                y  = OrigY  + dy;
                cy = Math.Max(MinEmu, OrigCy - dy);
                cx = Math.Max(MinEmu, OrigCx + dx);
                break;
            case CanvasGestureHandleKind.ResizeNW:
                x  = OrigX  + dx;
                y  = OrigY  + dy;
                cx = Math.Max(MinEmu, OrigCx - dx);
                cy = Math.Max(MinEmu, OrigCy - dy);
                break;
            case CanvasGestureHandleKind.ResizeSE:
                cx = Math.Max(MinEmu, OrigCx + dx);
                cy = Math.Max(MinEmu, OrigCy + dy);
                break;
            case CanvasGestureHandleKind.ResizeSW:
                x  = OrigX  + dx;
                cx = Math.Max(MinEmu, OrigCx - dx);
                cy = Math.Max(MinEmu, OrigCy + dy);
                break;
        }

        return (x, y, cx, cy);
    }
}
