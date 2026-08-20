using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using SkiaSharp;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-HANDLES: interactive drag-move + resize-handle manipulation for selected floating objects.
/// Covers: 8-handle geometry exposure, move-drag commit + single-step undo, corner-resize commit +
/// undo, edge-resize one-dimension, aspect-lock (Shift on a corner), min-size clamp, and Esc/cancel
/// reverting an in-flight drag without touching the model.
/// </summary>
public sealed class DocumentViewFloatingHandleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static byte[] SmallPng()
    {
        using var bmp = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(255, 128, 0));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>Document with one floating image at block=0, run=1 (144×108pt, offset 36,36pt).</summary>
    private static (TextDocument Doc, int BlockIdx, int RunIdx) MakeDocWithFloatingImage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text.", RunFormatting.Default));
        var img = new InlineImage(SmallPng(), 144, 108)
        {
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = 36,
            VerticalOffsetPt   = 36,
            ZOrderIndex        = 1,
        };
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = img });
        doc.Blocks.Add(para);
        return (doc, 0, 1);
    }

    /// <summary>Document with one floating shape at block=0, run=1 (120×80pt, offset 36,36pt).</summary>
    private static (TextDocument Doc, int BlockIdx, int RunIdx) MakeDocWithFloatingShape()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text.", RunFormatting.Default));
        var shape = new Shape
        {
            Kind = ShapeKind.Rectangle,
            WidthPt = 120,
            HeightPt = 80,
            FillColorHex = "#FF0000",
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 36,
                ZOrderIndex = 1,
            },
        };
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Shape = shape });
        doc.Blocks.Add(para);
        return (doc, 0, 1);
    }

    private const double PxPerPoint = 96.0 / 72.0;

    // ── H-1: selecting a float exposes exactly 8 handles in the expected geometry ─────────────────────

    [Fact]
    public async Task SelectedFloat_exposes_eight_handles_on_its_bounding_box()
    {
        int handleCount = 0;
        bool cornersPresent = false, edgesPresent = false;
        Rect selRect = default;
        var handlesInsideOrOnRect = true;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            selRect = view.SelectedFloatingInfo!.Value.Rect;

            var handles = view.HandleRectsForSelection();
            handleCount = handles.Count;
            cornersPresent = handles.ContainsKey(FloatHandle.TopLeft)
                          && handles.ContainsKey(FloatHandle.TopRight)
                          && handles.ContainsKey(FloatHandle.BottomLeft)
                          && handles.ContainsKey(FloatHandle.BottomRight);
            edgesPresent = handles.ContainsKey(FloatHandle.Top)
                        && handles.ContainsKey(FloatHandle.Bottom)
                        && handles.ContainsKey(FloatHandle.Left)
                        && handles.ContainsKey(FloatHandle.Right);

            // Each handle centre should sit on a corner or edge-midpoint of the selection rect.
            var inflated = selRect.Inflate(6);
            foreach (var (_, r) in handles)
                if (!inflated.Contains(r.Center))
                    handlesInsideOrOnRect = false;
        });
        if (!ran) return;
        Assert.Equal(8, handleCount);
        Assert.True(cornersPresent, "all four corner handles should be present");
        Assert.True(edgesPresent, "all four edge-midpoint handles should be present");
        Assert.True(handlesInsideOrOnRect, "handles should sit on the selection bounding box");
    }

    [Fact]
    public async Task No_selection_exposes_no_handles()
    {
        int handleCount = -1;
        var ran = await OnUiThread(() =>
        {
            var (doc, _, _) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            handleCount = view.HandleRectsForSelection().Count;
        });
        if (!ran) return;
        Assert.Equal(0, handleCount);
    }

    // ── H-2: move-drag changes the float's offset, committed + single-step undo ───────────────────────

    [Fact]
    public async Task MoveDrag_changes_image_offset_and_is_single_step_undoable()
    {
        double hBefore = 0, vBefore = 0, hAfter = 0, vAfter = 0, hUndo = 0, vUndo = 0;
        FloatHandle beginHandle = FloatHandle.None;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var img = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!;
            hBefore = img.HorizontalOffsetPt;
            vBefore = img.VerticalOffsetPt;

            // Start a drag from the centre of the float (body) and move +48px,+24px.
            var rect = view.SelectedFloatingInfo!.Value.Rect;
            beginHandle = view.BeginFloatDrag(rect.Center);
            view.SimulateDragTo(rect.Center + new Vector(48, 24));
            view.EndFloatDrag(rect.Center + new Vector(48, 24));

            hAfter = img.HorizontalOffsetPt;
            vAfter = img.VerticalOffsetPt;

            view.Undo();
            hUndo = img.HorizontalOffsetPt;
            vUndo = img.VerticalOffsetPt;
        });
        if (!ran) return;
        Assert.Equal(FloatHandle.Body, beginHandle);
        // +48px ≈ +36pt, +24px ≈ +18pt.
        Assert.True(Math.Abs(hAfter - (hBefore + 48 / PxPerPoint)) < 1.0, $"hAfter={hAfter}");
        Assert.True(Math.Abs(vAfter - (vBefore + 24 / PxPerPoint)) < 1.0, $"vAfter={vAfter}");
        // One undo restores the original offset.
        Assert.Equal(hBefore, hUndo, 3);
        Assert.Equal(vBefore, vUndo, 3);
    }

    // ── H-3: corner-resize changes size, committed + single-step undo ─────────────────────────────────

    [Fact]
    public async Task CornerResize_changes_size_and_is_single_step_undoable()
    {
        double wBefore = 0, hBefore = 0, wAfter = 0, hAfter = 0, wUndo = 0, hUndo = 0;
        FloatHandle beginHandle = FloatHandle.None;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var shape = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!;
            wBefore = shape.WidthPt; hBefore = shape.HeightPt;

            // Grab the bottom-right corner and drag it out by +60px,+40px → bigger box, anchor (top-left) fixed.
            var rect = view.SelectedFloatingInfo!.Value.Rect;
            var br = new Point(rect.Right, rect.Bottom);
            beginHandle = view.BeginFloatDrag(br);
            var target = br + new Vector(60, 40);
            view.SimulateDragTo(target);
            view.EndFloatDrag(target);

            wAfter = shape.WidthPt; hAfter = shape.HeightPt;

            view.Undo();
            wUndo = shape.WidthPt; hUndo = shape.HeightPt;
        });
        if (!ran) return;
        Assert.Equal(FloatHandle.BottomRight, beginHandle);
        Assert.True(wAfter > wBefore + 30, $"width should grow: {wBefore} -> {wAfter}");
        Assert.True(hAfter > hBefore + 20, $"height should grow: {hBefore} -> {hAfter}");
        // Single undo restores BOTH dimensions (anchor at top-left did not move → size-only command).
        Assert.Equal(wBefore, wUndo, 3);
        Assert.Equal(hBefore, hUndo, 3);
    }

    // ── H-4: edge-resize changes only one dimension ──────────────────────────────────────────────────

    [Fact]
    public async Task EdgeResize_right_changes_only_width()
    {
        double wBefore = 0, hBefore = 0, wAfter = 0, hAfter = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var shape = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!;
            wBefore = shape.WidthPt; hBefore = shape.HeightPt;

            var rect = view.SelectedFloatingInfo!.Value.Rect;
            var right = new Point(rect.Right, rect.Y + rect.Height / 2);
            view.BeginFloatDrag(right);
            var target = right + new Vector(40, 30); // y delta should be ignored for a Right edge
            view.SimulateDragTo(target);
            view.EndFloatDrag(target);

            wAfter = shape.WidthPt; hAfter = shape.HeightPt;
        });
        if (!ran) return;
        Assert.True(wAfter > wBefore + 20, $"width should grow: {wBefore} -> {wAfter}");
        Assert.Equal(hBefore, hAfter, 3); // height unchanged
    }

    // ── H-5: Shift on a corner preserves aspect ratio ────────────────────────────────────────────────

    [Fact]
    public async Task ShiftCornerResize_preserves_aspect_ratio()
    {
        double ratioBefore = 0, ratioAfter = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape(); // 120×80 → ratio 1.5
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var shape = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!;
            ratioBefore = shape.WidthPt / shape.HeightPt;

            var rect = view.SelectedFloatingInfo!.Value.Rect;
            var br = new Point(rect.Right, rect.Bottom);
            view.BeginFloatDrag(br);
            // Drag mostly horizontally with Shift held; height should scale to keep the ratio.
            var target = br + new Vector(90, 10);
            view.SimulateDragTo(target, shift: true);
            view.EndFloatDrag(target, shift: true);

            ratioAfter = shape.WidthPt / shape.HeightPt;
        });
        if (!ran) return;
        Assert.True(Math.Abs(ratioAfter - ratioBefore) < 0.05,
            $"aspect ratio should be preserved: {ratioBefore} -> {ratioAfter}");
    }

    // ── H-6: min-size clamp — dragging a corner inward past the minimum clamps the size ──────────────

    [Fact]
    public async Task Resize_clamps_to_minimum_size()
    {
        double wAfter = 0, hAfter = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var shape = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!;
            var rect = view.SelectedFloatingInfo!.Value.Rect;

            // Drag bottom-right corner far PAST the top-left (way negative) → should clamp, not invert.
            var br = new Point(rect.Right, rect.Bottom);
            view.BeginFloatDrag(br);
            var target = new Point(rect.X - 500, rect.Y - 500);
            view.SimulateDragTo(target);
            view.EndFloatDrag(target);

            wAfter = shape.WidthPt; hAfter = shape.HeightPt;
        });
        if (!ran) return;
        // Clamped to ~9pt minimum (allow a little slack for px→pt rounding); never negative/zero.
        Assert.True(wAfter >= 8 && wAfter <= 12, $"width clamped near min: {wAfter}");
        Assert.True(hAfter >= 8 && hAfter <= 12, $"height clamped near min: {hAfter}");
    }

    // ── H-7: Esc cancels an in-flight drag, reverting the rect, model untouched ───────────────────────

    [Fact]
    public async Task CancelDrag_reverts_rect_and_leaves_model_unchanged()
    {
        double wBefore = 0, hBefore = 0, wAfter = 0, hAfter = 0;
        bool rectReverted = false, stillSelected = false, cancelled = false;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var shape = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!;
            wBefore = shape.WidthPt; hBefore = shape.HeightPt;

            var startRect = view.SelectedFloatingInfo!.Value.Rect;
            var br = new Point(startRect.Right, startRect.Bottom);
            view.BeginFloatDrag(br);
            view.SimulateDragTo(br + new Vector(80, 60));
            // Cancel instead of committing.
            cancelled = view.CancelFloatDrag();

            var endRect = view.SelectedFloatingInfo!.Value.Rect;
            rectReverted = Math.Abs(endRect.Width - startRect.Width) < 0.01
                        && Math.Abs(endRect.Height - startRect.Height) < 0.01;
            stillSelected = view.SelectedFloatingInfo is not null;

            wAfter = shape.WidthPt; hAfter = shape.HeightPt;
        });
        if (!ran) return;
        Assert.True(cancelled, "CancelFloatDrag should report an in-flight drag was cancelled");
        Assert.True(rectReverted, "selection rect should revert to drag-start geometry");
        Assert.True(stillSelected, "the object should stay selected after a cancel");
        Assert.Equal(wBefore, wAfter, 3); // model size untouched
        Assert.Equal(hBefore, hAfter, 3);
    }

    // ── H-8: a click (sub-threshold drag) on the body does not move the float ─────────────────────────

    [Fact]
    public async Task SubThreshold_drag_does_not_change_model()
    {
        double hBefore = 0, vBefore = 0, hAfter = 0, vAfter = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var img = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!;
            hBefore = img.HorizontalOffsetPt; vBefore = img.VerticalOffsetPt;

            var c = view.SelectedFloatingInfo!.Value.Rect.Center;
            view.BeginFloatDrag(c);
            view.EndFloatDrag(c + new Vector(0.5, 0.5)); // < 1px → treated as a click

            hAfter = img.HorizontalOffsetPt; vAfter = img.VerticalOffsetPt;
        });
        if (!ran) return;
        Assert.Equal(hBefore, hAfter, 3);
        Assert.Equal(vBefore, vAfter, 3);
    }

    // ── H-9: top-left corner resize moves the anchor — single undo reverts size AND position ──────────

    [Fact]
    public async Task TopLeftResize_moves_anchor_and_single_undo_reverts_both()
    {
        double wB = 0, hB = 0, hOffB = 0, vOffB = 0;
        double wA = 0, hA = 0, hOffA = 0, vOffA = 0;
        double wU = 0, hU = 0, hOffU = 0, vOffU = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var img = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!;
            wB = img.WidthPt; hB = img.HeightPt; hOffB = img.HorizontalOffsetPt; vOffB = img.VerticalOffsetPt;

            var rect = view.SelectedFloatingInfo!.Value.Rect;
            var tl = new Point(rect.X, rect.Y);
            view.BeginFloatDrag(tl);
            var target = tl + new Vector(-40, -30); // pull the top-left corner out → grows + moves offset
            view.SimulateDragTo(target);
            view.EndFloatDrag(target);

            wA = img.WidthPt; hA = img.HeightPt; hOffA = img.HorizontalOffsetPt; vOffA = img.VerticalOffsetPt;

            view.Undo();
            wU = img.WidthPt; hU = img.HeightPt; hOffU = img.HorizontalOffsetPt; vOffU = img.VerticalOffsetPt;
        });
        if (!ran) return;
        Assert.True(wA > wB + 20, $"width should grow: {wB} -> {wA}");
        Assert.True(hA > hB + 15, $"height should grow: {hB} -> {hA}");
        Assert.True(hOffA < hOffB - 20, $"left offset should shift left: {hOffB} -> {hOffA}");
        Assert.True(vOffA < vOffB - 15, $"top offset should shift up: {vOffB} -> {vOffA}");
        // ONE undo reverts the whole composite (size + position).
        Assert.Equal(wB, wU, 3);
        Assert.Equal(hB, hU, 3);
        Assert.Equal(hOffB, hOffU, 3);
        Assert.Equal(vOffB, vOffU, 3);
    }

    // ── H-10 (FB1): rotated corner resize grows the object along ITS OWN axes, anchoring the opposite
    // corner in the object's local frame — not the screen axes ──────────────────────────────────────────

    [Fact]
    public async Task CornerResize_on_90DegreeRotatedShape_growsAlongLocalAxesAndAnchorsOppositeCorner()
    {
        double wBefore = 0, hBefore = 0, wAfter = 0, hAfter = 0;
        FloatHandle grabbedHandle = FloatHandle.None;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape(); // 120x80pt
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            view.RotateSelectedFloating(90);

            var shape = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!;
            wBefore = shape.WidthPt; hBefore = shape.HeightPt;

            // After a +90° rotation, the drawn (visible) handles are the SAME 8 axis-aligned positions
            // relabeled (a square rotated 90° about its centre maps corners onto adjacent corners) — so
            // the model's BottomRight handle is now drawn at the rect's TopRight screen position. Find
            // the visible handle tagged BottomRight (the one this fix must resolve correctly) and drag it
            // outward along the SCREEN axis it is actually drawn on.
            var handles = view.HandleRectsForSelection();
            var bottomRightHandle = handles[FloatHandle.BottomRight].Center;
            grabbedHandle = view.BeginFloatDrag(bottomRightHandle);
            // Drag further in the direction the visible handle already points (away from centre) so the
            // object grows rather than clamps to the minimum size.
            var rect = view.SelectedFloatingInfo!.Value.Rect;
            var centre = rect.Center;
            var outward = bottomRightHandle - centre;
            var target = bottomRightHandle + outward * 0.5; // move further outward along the same ray
            view.SimulateDragTo(target);
            view.EndFloatDrag(target);

            wAfter = shape.WidthPt; hAfter = shape.HeightPt;
        });
        if (!ran) return;
        Assert.Equal(FloatHandle.BottomRight, grabbedHandle);
        // Dragging the visible BottomRight handle further outward must grow the object (along its own
        // local axes once un-rotated) rather than shrink it or leave it unchanged, which is what the
        // pre-fix screen-axis math would do for a rotated object (wrong axis / wrong direction).
        Assert.True(wAfter > wBefore || hAfter > hBefore,
            $"rotated resize should grow the shape along its own axis: {wBefore}x{hBefore} -> {wAfter}x{hAfter}");
    }

    // ── H-11 (FB3): a flipped object's visible corner handle resizes that visual corner ─────────────────

    [Fact]
    public async Task CornerResize_on_HorizontallyFlippedShape_dragsVisibleCornerCorrectly()
    {
        double wBefore = 0, hBefore = 0, wAfter = 0, hAfter = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape(); // 120x80pt
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            view.FlipSelectedFloating(horizontal: true);

            var shape = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!;
            wBefore = shape.WidthPt; hBefore = shape.HeightPt;

            // FlipH mirrors left/right about the centre, so the model's BottomRight handle is now drawn
            // at the rect's BOTTOM-LEFT screen position. Grab the VISIBLE handle tagged BottomRight and
            // drag it further outward (down-left) — this must grow the object, proving the flip was
            // composed into the resize math rather than resizing/anchoring the wrong (un-flipped) corner.
            var handles = view.HandleRectsForSelection();
            var visibleBottomRight = handles[FloatHandle.BottomRight].Center;
            var rect = view.SelectedFloatingInfo!.Value.Rect;
            var outward = visibleBottomRight - rect.Center;
            var target = visibleBottomRight + outward * 0.5;
            view.BeginFloatDrag(visibleBottomRight);
            view.SimulateDragTo(target);
            view.EndFloatDrag(target);

            wAfter = shape.WidthPt; hAfter = shape.HeightPt;
        });
        if (!ran) return;
        Assert.True(wAfter > wBefore || hAfter > hBefore,
            $"flipped resize should grow the shape from its visible corner: {wBefore}x{hBefore} -> {wAfter}x{hAfter}");
    }

    // ── H-12 (FB4): resizing a non-Image float via a top/left handle when GetFloatingPlacement returns
    // null must NOT silently shift the anchored edge (skip the size-only commit rather than apply a
    // visually-wrong resize) ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TopLeftResize_onGroupWithNullPlacement_doesNotShiftAnchorOrChangeSize()
    {
        double wBefore = 0, hBefore = 0, wAfter = 0, hAfter = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Body text.", RunFormatting.Default));
            var group = new FreeW.Core.Model.DrawingGroup
            {
                WidthPt = 120,
                HeightPt = 80,
#pragma warning disable CS8625 // FB4 regression fixture: Placement is documented "always non-null" but
                                // GetFloatingPlacement's `is { } pl` match must still be guarded against
                                // a null value reaching it defensively.
                Placement = null!,
#pragma warning restore CS8625
            };
            group.Children.Add(new InlineImage([1], widthPt: 36, heightPt: 18));
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { DrawingGroup = group });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(0, 1);

            wBefore = group.WidthPt; hBefore = group.HeightPt;

            var rect = view.SelectedFloatingInfo!.Value.Rect;
            var tl = new Point(rect.X, rect.Y);
            view.BeginFloatDrag(tl);
            var target = tl + new Vector(-40, -30); // pull the top-left corner out
            view.SimulateDragTo(target);
            view.EndFloatDrag(target);

            wAfter = group.WidthPt; hAfter = group.HeightPt;
        });
        if (!ran) return;
        // No placement to carry the anchor delta on -> the size-only commit must be skipped entirely
        // rather than silently growing the group from the WRONG (unmoved) top-left, which would visually
        // slide the corner the user just dragged.
        Assert.Equal(wBefore, wAfter, 3);
        Assert.Equal(hBefore, hAfter, 3);
    }
}
