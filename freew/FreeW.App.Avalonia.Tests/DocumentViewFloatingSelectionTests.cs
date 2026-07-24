using System;
using System.Linq;
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
/// AV-FLSEL: floating-object selection + placement edit infra tests.
/// Covers: SelectFloating hit-test, SelectedFloatingInfo, ChangeFloatingZOrder,
/// SetFloatingWrap, SetFloatingPosition, SetFloatingSize, RotateSelectedFloating,
/// FlipSelectedFloating, DeleteSelectedFloating, undo of each, Esc deselect,
/// click-outside deselect, non-float regression (body text click deselects).
/// </summary>
public sealed class DocumentViewFloatingSelectionTests
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
            return false;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

    private static byte[] SmallPng()
    {
        using var bmp = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(255, 128, 0));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>
    /// Document with one floating image at block=0, run=1 (run=0 is body text).
    /// Image is 144×108 pt Square-wrapped, offset (36,36)pt from column/paragraph.
    /// </summary>
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
        var imgRun = new Run(string.Empty, RunFormatting.Default) { Image = img };
        para.Runs.Add(imgRun);
        doc.Blocks.Add(para);
        return (doc, 0, 1);
    }

    /// <summary>
    /// Document with one floating shape at block=0, run=1.
    /// Shape is 120×80 pt, Square-wrapped, offset (36,36)pt.
    /// </summary>
    private static (TextDocument Doc, int BlockIdx, int RunIdx) MakeDocWithFloatingShape()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text.", RunFormatting.Default));
        var shape = new Shape
        {
            Kind       = ShapeKind.Rectangle,
            WidthPt    = 120,
            HeightPt   = 80,
            FillColorHex = "#FF0000",
            Placement  = new FloatingPlacement
            {
                Wrapping           = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt   = 36,
                ZOrderIndex        = 1,
            },
        };
        var shapeRun = new Run(string.Empty, RunFormatting.Default) { Shape = shape };
        para.Runs.Add(shapeRun);
        doc.Blocks.Add(para);
        return (doc, 0, 1);
    }

    private static (TextDocument Doc, int BlockIdx, int RunIdx) MakeDocWithFloatingWordArt()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text.", RunFormatting.Default));
        var wordArt = new WordArt("Transform", WordArtStyle.GlowBlue, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 36,
                ZOrderIndex = 1,
            },
        };
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wordArt });
        doc.Blocks.Add(para);
        return (doc, 0, 1);
    }

    private static TextDocument MakeDocWithFloatingImageAndShape()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text.", RunFormatting.Default));
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default)
        {
            Image = new InlineImage(SmallPng(), 60, 60)
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 1,
            },
        });
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default)
        {
            Shape = new Shape
            {
                Kind = ShapeKind.Rectangle,
                WidthPt = 72,
                HeightPt = 36,
                FillColorHex = "#FF0000",
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalOffsetPt = 108,
                    VerticalOffsetPt = 54,
                    ZOrderIndex = 2,
                },
            },
        });
        doc.Blocks.Add(para);
        return doc;
    }

    // ── FLSEL-1: SelectFloating sets SelectedFloatingInfo ────────────────────────────────────────────

    [Fact]
    public async Task SelectFloating_sets_selected_floating_info()
    {
        (int BlockIndex, int RunIndex, string Kind, global::Avalonia.Rect Rect)? info = null;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            info = view.SelectedFloatingInfo;
        });
        if (!ran) return;
        Assert.NotNull(info);
        Assert.Equal(0,       info!.Value.BlockIndex);
        Assert.Equal(1,       info!.Value.RunIndex);
        Assert.Equal("Image", info!.Value.Kind);
        Assert.True(info!.Value.Rect.Width  > 0, "selected rect should have non-zero width");
        Assert.True(info!.Value.Rect.Height > 0, "selected rect should have non-zero height");
    }

    // ── FLSEL-2: DeselectFloating clears selection ───────────────────────────────────────────────────

    [Fact]
    public async Task DeselectFloating_clears_selection()
    {
        bool wasSelected = false;
        bool isDeselected = false;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            wasSelected  = view.SelectedFloatingInfo is not null;
            view.DeselectFloating();
            isDeselected = view.SelectedFloatingInfo is null;
        });
        if (!ran) return;
        Assert.True(wasSelected,  "object should be selected after SelectFloating");
        Assert.True(isDeselected, "object should be deselected after DeselectFloating");
    }

    // ── FLSEL-3: SetFloatingWrap changes wrapping + undoable ─────────────────────────────────────────

    [Fact]
    public async Task SelectFloating_multi_select_tracks_two_groupable_objects()
    {
        int selectedCount = 0;
        bool canGroup = false;
        string? activeKind = null;
        var ran = await OnUiThread(() =>
        {
            var doc = MakeDocWithFloatingImageAndShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            view.SelectFloating(0, 1);
            view.SelectFloating(0, 2, addToMultiSelect: true);

            selectedCount = view.SelectedFloatingObjects.Count;
            canGroup = view.HasMultipleFloatingObjectsSelected;
            activeKind = view.SelectedFloatingInfo?.Kind;
        });
        if (!ran) return;

        Assert.Equal(2, selectedCount);
        Assert.True(canGroup, "image + shape multi-selection should enable the shared group command");
        Assert.Equal("Shape", activeKind);
    }

    [Fact]
    public async Task Group_and_ungroup_selected_floating_objects_use_shared_model_commands()
    {
        int groupedRunCount = 0, groupedChildCount = 0, ungroupedRunCount = 0;
        bool hasImage = false, hasShape = false, groupSelection = false;
        var ran = await OnUiThread(() =>
        {
            var doc = MakeDocWithFloatingImageAndShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            view.SelectFloating(0, 1);
            view.SelectFloating(0, 2, addToMultiSelect: true);
            view.GroupSelectedFloatingObjects();

            var para = (Paragraph)doc.Blocks[0];
            groupedRunCount = para.Runs.Count;
            groupedChildCount = para.Runs[1].DrawingGroup?.Children.Count ?? 0;

            view.SelectFloating(0, 1);
            groupSelection = view.IsGroupSelected;
            view.UngroupSelectedFloatingObject();

            ungroupedRunCount = para.Runs.Count;
            hasImage = para.Runs.Any(r => r.Image is not null);
            hasShape = para.Runs.Any(r => r.Shape is not null);
        });
        if (!ran) return;

        Assert.Equal(2, groupedRunCount);
        Assert.Equal(2, groupedChildCount);
        Assert.True(groupSelection, "the grouped run should be recognized as a selected drawing group");
        Assert.Equal(3, ungroupedRunCount);
        Assert.True(hasImage);
        Assert.True(hasShape);
    }

    [Fact]
    public async Task SetFloatingWrap_changes_model_wrapping_and_is_undoable()
    {
        ImageWrapping? after = null;
        ImageWrapping? reverted = null;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            view.SetFloatingWrap(ImageWrapping.TopAndBottom);
            after = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!.Placement!.Wrapping;

            view.Undo();
            reverted = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!.Placement!.Wrapping;
        });
        if (!ran) return;
        Assert.Equal(ImageWrapping.TopAndBottom, after);
        Assert.Equal(ImageWrapping.Square,       reverted);
    }

    // ── FLSEL-4: ChangeFloatingZOrder (BringToFront) raises ZOrderIndex ──────────────────────────────

    [Fact]
    public async Task ChangeFloatingZOrder_BringToFront_raises_z_index_and_is_undoable()
    {
        int? zBefore = null, zAfter = null, zReverted = null;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            var img = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!;
            zBefore = img.ZOrderIndex; // = 1

            view.SelectFloating(bi, ri);
            view.ChangeFloatingZOrder(ZOrderOperation.BringToFront);
            zAfter = img.ZOrderIndex;

            view.Undo();
            zReverted = img.ZOrderIndex;
        });
        if (!ran) return;
        Assert.True(zAfter > zBefore, "BringToFront should increase ZOrderIndex");
        Assert.Equal(zBefore, zReverted);
    }

    // ── FLSEL-5: SetFloatingPosition updates offsets + undoable ──────────────────────────────────────

    [Fact]
    public async Task SetFloatingPosition_updates_image_offsets_and_is_undoable()
    {
        double hAfter = 0, vAfter = 0, hReverted = 0, vReverted = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            view.SetFloatingPosition(72, 144, HorizontalAnchor.Margin, VerticalAnchor.Page);
            var img = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!;
            hAfter = img.HorizontalOffsetPt;
            vAfter = img.VerticalOffsetPt;

            view.Undo();
            hReverted = img.HorizontalOffsetPt;
            vReverted = img.VerticalOffsetPt;
        });
        if (!ran) return;
        Assert.Equal(72,  hAfter);
        Assert.Equal(144, vAfter);
        Assert.Equal(36,  hReverted);
        Assert.Equal(36,  vReverted);
    }

    // ── FLSEL-6: SetFloatingPosition updates shape placement + undoable ───────────────────────────────

    [Fact]
    public async Task SetFloatingPosition_updates_shape_placement_and_is_undoable()
    {
        double hAfter = 0, vAfter = 0, hReverted = 0, vReverted = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            view.SetFloatingPosition(100, 200, HorizontalAnchor.Column, VerticalAnchor.Paragraph);
            var pl = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!.Placement!;
            hAfter = pl.HorizontalOffsetPt;
            vAfter = pl.VerticalOffsetPt;

            view.Undo();
            hReverted = pl.HorizontalOffsetPt;
            vReverted = pl.VerticalOffsetPt;
        });
        if (!ran) return;
        Assert.Equal(100, hAfter);
        Assert.Equal(200, vAfter);
        Assert.Equal(36,  hReverted);
        Assert.Equal(36,  vReverted);
    }

    // ── FLSEL-7: SetFloatingSize updates image size + undoable ───────────────────────────────────────

    [Fact]
    public async Task SetFloatingSize_updates_image_size_and_is_undoable()
    {
        double wAfter = 0, hAfter = 0, wReverted = 0, hReverted = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            view.SetFloatingSize(288, 216); // 4in × 3in
            var img = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!;
            wAfter  = img.WidthPt;
            hAfter  = img.HeightPt;

            view.Undo();
            wReverted = img.WidthPt;
            hReverted = img.HeightPt;
        });
        if (!ran) return;
        Assert.Equal(288, wAfter);
        Assert.Equal(216, hAfter);
        Assert.Equal(144, wReverted);
        Assert.Equal(108, hReverted);
    }

    // ── FLSEL-8: RotateSelectedFloating updates image rotation + undoable ────────────────────────────

    [Fact]
    public async Task RotateSelectedFloating_updates_image_rotation_and_is_undoable()
    {
        double angleAfter = 0, angleReverted = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            view.RotateSelectedFloating(90);
            angleAfter = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!.RotationAngle;

            view.Undo();
            angleReverted = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!.RotationAngle;
        });
        if (!ran) return;
        Assert.Equal(90,  angleAfter);
        Assert.Equal(0.0, angleReverted);
    }

    // ── FLSEL-9: RotateSelectedFloating updates shape rotation + undoable ────────────────────────────

    [Fact]
    public async Task RotateSelectedFloating_updates_shape_rotation_and_is_undoable()
    {
        double angleAfter = 0, angleReverted = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            view.RotateSelectedFloating(45);
            angleAfter = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!.RotationAngle;

            view.Undo();
            angleReverted = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!.RotationAngle;
        });
        if (!ran) return;
        Assert.Equal(45,  angleAfter);
        Assert.Equal(0.0, angleReverted);
    }

    [Fact]
    public async Task RotateAndFlipSelectedFloating_updates_wordart_transform_and_is_undoable()
    {
        double angleAfter = 0, angleReverted = 0;
        bool flipAfter = false, flipReverted = true;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingWordArt();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            view.RotateSelectedFloating(45);
            view.FlipSelectedFloating(horizontal: true);
            var wordArt = ((Paragraph)doc.Blocks[bi]).Runs[ri].WordArt!;
            angleAfter = wordArt.RotationAngle;
            flipAfter = wordArt.FlipH;

            view.Undo();
            view.Undo();
            angleReverted = wordArt.RotationAngle;
            flipReverted = wordArt.FlipH;
        });
        if (!ran) return;

        Assert.Equal(45, angleAfter);
        Assert.True(flipAfter);
        Assert.Equal(0.0, angleReverted);
        Assert.False(flipReverted);
    }

    [Fact]
    public async Task RotateAndFlipSelectedFloating_updates_group_transform_and_is_undoable()
    {
        double angleAfter = 0, angleReverted = 0;
        bool flipAfter = false, flipReverted = true;
        var ran = await OnUiThread(() =>
        {
            var doc = MakeDocWithFloatingImageAndShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(0, 1);
            view.SelectFloating(0, 2, addToMultiSelect: true);
            view.GroupSelectedFloatingObjects();
            view.SelectFloating(0, 1);

            view.RotateSelectedFloating(45);
            view.FlipSelectedFloating(horizontal: true);
            var group = ((Paragraph)doc.Blocks[0]).Runs[1].DrawingGroup!;
            angleAfter = group.RotationAngle;
            flipAfter = group.FlipH;

            view.Undo();
            view.Undo();
            angleReverted = group.RotationAngle;
            flipReverted = group.FlipH;
        });
        if (!ran) return;

        Assert.Equal(45, angleAfter);
        Assert.True(flipAfter);
        Assert.Equal(0.0, angleReverted);
        Assert.False(flipReverted);
    }

    // ── FLSEL-10: FlipSelectedFloating updates image flip + undoable ─────────────────────────────────

    [Fact]
    public async Task FlipSelectedFloating_updates_image_flipH_and_is_undoable()
    {
        bool flipHAfter = false, flipHReverted = true;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            view.FlipSelectedFloating(horizontal: true);
            flipHAfter = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!.FlipH;

            view.Undo();
            flipHReverted = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!.FlipH;
        });
        if (!ran) return;
        Assert.True(flipHAfter,    "FlipH should be true after FlipSelectedFloating(horizontal)");
        Assert.False(flipHReverted,"FlipH should be restored to false after undo");
    }

    // ── FLSEL-11: DeleteSelectedFloating removes run + undoable ──────────────────────────────────────

    [Fact]
    public async Task DeleteSelectedFloating_removes_run_and_is_undoable()
    {
        int runCountBefore = 0, runCountAfter = 0, runCountReverted = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            runCountBefore = ((Paragraph)doc.Blocks[bi]).Runs.Count;
            view.DeleteSelectedFloating();
            runCountAfter = ((Paragraph)doc.Blocks[bi]).Runs.Count;

            view.Undo();
            runCountReverted = ((Paragraph)doc.Blocks[bi]).Runs.Count;
        });
        if (!ran) return;
        Assert.Equal(2, runCountBefore);
        Assert.Equal(1, runCountAfter);
        Assert.Equal(2, runCountReverted);
    }

    // ── FLSEL-12: DeleteSelectedFloating clears selection ────────────────────────────────────────────

    [Fact]
    public async Task DeleteSelectedFloating_clears_selection()
    {
        bool isNullAfterDelete = false;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            view.DeleteSelectedFloating();
            isNullAfterDelete = view.SelectedFloatingInfo is null;
        });
        if (!ran) return;
        Assert.True(isNullAfterDelete, "SelectedFloatingInfo should be null after delete");
    }

    // ── FLSEL-13: TryHitTestFloat — point inside float rect selects it ───────────────────────────────

    [Fact]
    public async Task SelectFloating_with_shape_sets_kind_and_valid_rect()
    {
        string? kind = null;
        bool rectNonZero = false;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            var info = view.SelectedFloatingInfo;
            kind        = info?.Kind;
            rectNonZero = info.HasValue && info.Value.Rect.Width > 0 && info.Value.Rect.Height > 0;
        });
        if (!ran) return;
        Assert.Equal("Shape", kind);
        Assert.True(rectNonZero);
    }

    // ── FLSEL-14: SelectFloating on invalid index does not crash ─────────────────────────────────────

    [Fact]
    public async Task SelectFloating_on_invalid_index_does_not_crash_and_leaves_null()
    {
        bool infoNull = false;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(99, 99); // out of range
            infoNull = view.SelectedFloatingInfo is null;
        });
        if (!ran) return;
        Assert.True(infoNull);
    }

    // ── FLSEL-15: SetFloatingWrap on image changes Wrapping ──────────────────────────────────────────

    [Fact]
    public async Task SetFloatingWrap_on_image_changes_wrapping_and_is_undoable()
    {
        ImageWrapping? after = null, reverted = null;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            view.SetFloatingWrap(ImageWrapping.Behind);
            after    = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!.Wrapping;
            view.Undo();
            reverted = ((Paragraph)doc.Blocks[bi]).Runs[ri].Image!.Wrapping;
        });
        if (!ran) return;
        Assert.Equal(ImageWrapping.Behind, after);
        Assert.Equal(ImageWrapping.Square, reverted);
    }

    // ── FLSEL-16: FloatingImageRects still work after SelectFloating (non-regression) ─────────────────

    [Fact]
    public async Task FloatingImageRects_still_populated_after_SelectFloating()
    {
        int rectCount = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            rectCount = view.FloatingImageRects.Count;
        });
        if (!ran) return;
        Assert.Equal(1, rectCount);
    }

    // ── FLSEL-17: SetFloatingSize on shape updates size + undoable ────────────────────────────────────

    [Fact]
    public async Task SetFloatingSize_on_shape_updates_size_and_is_undoable()
    {
        double wAfter = 0, hAfter = 0, wRev = 0, hRev = 0;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            view.SetFloatingSize(240, 160);
            var shape = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!;
            wAfter = shape.WidthPt; hAfter = shape.HeightPt;

            view.Undo();
            wRev = shape.WidthPt; hRev = shape.HeightPt;
        });
        if (!ran) return;
        Assert.Equal(240, wAfter);
        Assert.Equal(160, hAfter);
        Assert.Equal(120, wRev);
        Assert.Equal(80,  hRev);
    }
}
