using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
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

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

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

    private static TextDocument MakeDocWithNestedGroupAndShape()
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();

        var nested = new DrawingGroup
        {
            WidthPt = 96,
            HeightPt = 48,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 1
            }
        };
        nested.Children.Add(new Shape(ShapeKind.Rectangle, 48, 24));
        nested.Children.Add(new Shape(ShapeKind.Ellipse, 36, 24));
        nested.ChildOffsets.Add((0, 0));
        nested.ChildOffsets.Add((60, 24));

        var first = new Paragraph("Body text.");
        first.Runs.Add(Run.FromDrawingGroup(nested));
        doc.Blocks.Add(first);

        var second = new Paragraph("More text.");
        second.Runs.Add(Run.FromShape(new Shape(ShapeKind.Rectangle, 72, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 156,
                VerticalOffsetPt = 54,
                ZOrderIndex = 2
            }
        }));
        doc.Blocks.Add(second);
        return doc;
    }

    private static DocumentFloatRect PlannerRect(Rect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static TextDocument MakeDocWithOuterAndNestedGroupChild(
        out DrawingGroup outer,
        out DrawingGroup inner,
        out Shape leaf)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        inner = new DrawingGroup
        {
            WidthPt = 126,
            HeightPt = 72,
            RotationAngle = -16,
            FlipV = true
        };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 36, 22));
        inner.ChildOffsets.Add((10, 8));
        leaf = new Shape(ShapeKind.Ellipse, 44, 28)
        {
            RotationAngle = 13,
            FlipH = true
        };
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((58, 30));

        outer = new DrawingGroup
        {
            WidthPt = 252,
            HeightPt = 144,
            RotationAngle = 24,
            FlipH = true,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalAnchor = HorizontalAnchor.Page,
                VerticalAnchor = VerticalAnchor.Page,
                HorizontalOffsetPt = 72,
                VerticalOffsetPt = 36,
                ZOrderIndex = 4
            }
        };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((28, 22));
        outer.Children.Add(new Shape(ShapeKind.Rectangle, 54, 34));
        outer.ChildOffsets.Add((168, 76));

        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    private static TextDocument MakeDocWithTwoNestedBranches(out DrawingGroup outer)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        static DrawingGroup Branch()
        {
            var group = new DrawingGroup { WidthPt = 120, HeightPt = 70 };
            group.Children.Add(new Shape(ShapeKind.Rectangle, 30, 20));
            group.ChildOffsets.Add((8, 8));
            group.Children.Add(new Shape(ShapeKind.Ellipse, 44, 28));
            group.ChildOffsets.Add((54, 28));
            return group;
        }

        outer = new DrawingGroup
        {
            WidthPt = 360,
            HeightPt = 180,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalAnchor = HorizontalAnchor.Page,
                VerticalAnchor = VerticalAnchor.Page,
                HorizontalOffsetPt = 72,
                VerticalOffsetPt = 36,
                ZOrderIndex = 4
            }
        };
        outer.Children.Add(Branch());
        outer.ChildOffsets.Add((20, 20));
        outer.Children.Add(Branch());
        outer.ChildOffsets.Add((200, 100));

        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        doc.Blocks.Add(paragraph);
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
    public async Task Nested_group_can_be_selected_grouped_ungrouped_and_undone()
    {
        int outerChildCount = 0;
        int restoredGroupCount = 0;
        bool canGroup = false;
        var ran = await OnUiThread(() =>
        {
            var doc = MakeDocWithNestedGroupAndShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            view.SelectFloating(0, 1);
            view.SelectFloating(1, 1, addToMultiSelect: true);
            canGroup = view.HasMultipleFloatingObjectsSelected;
            view.GroupSelectedFloatingObjects();

            var grouped = ((Paragraph)doc.Blocks[0]).Runs[1].DrawingGroup!;
            outerChildCount = grouped.Children.Count;
            grouped.Children[0].Should().BeOfType<DrawingGroup>();

            view.SelectFloating(0, 1);
            view.IsGroupSelected.Should().BeTrue();
            view.UngroupSelectedFloatingObject();
            restoredGroupCount = ((Paragraph)doc.Blocks[0]).Runs.Count(run => run.DrawingGroup is not null);
            view.Undo();
        });
        if (!ran) return;

        Assert.True(canGroup, "a valid nested group must be eligible for multi-selection");
        Assert.Equal(2, outerChildCount);
        Assert.Equal(1, restoredGroupCount);
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
    public async Task RotateAndFlipSelectedFloating_updates_grouped_chart_and_nested_smartart_with_undo()
    {
        double chartAngleAfter = 0, chartAngleReverted = -1;
        bool chartFlipAfter = false, chartFlipReverted = true;
        double smartArtAngleAfter = 0, smartArtAngleReverted = -1;
        bool smartArtFlipAfter = false, smartArtFlipReverted = true;

        var ran = await OnUiThread(() =>
        {
            // A chart and a SmartArt keep their DEFAULT sizes (360x216pt and 468x216pt) unless told
            // otherwise -- far larger than the group holding them, so their rects overlapped and a click
            // at the chart's centre landed on the SmartArt drawn over it. Size them to fit their slots,
            // which is what this test's geometry always assumed.
            var chart = Chart.Create(ChartKind.Column, ["A", "B"], [1, 2]);
            chart.WidthPt = 100;
            chart.HeightPt = 60;
            var smartArt = SmartArt.Create(SmartArtKind.Process, ["Step"]);
            smartArt.WidthPt = 90;
            smartArt.HeightPt = 50;
            var inner = new DrawingGroup { WidthPt = 110, HeightPt = 64 };
            inner.Children.Add(smartArt);
            inner.ChildOffsets.Add((8, 6));

            var outer = new DrawingGroup
            {
                WidthPt = 280,
                HeightPt = 170,
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalOffsetPt = 30,
                    VerticalOffsetPt = 24
                }
            };
            outer.Children.Add(chart);
            outer.ChildOffsets.Add((16, 12));
            outer.Children.Add(inner);
            outer.ChildOffsets.Add((150, 76));

            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromDrawingGroup(outer));
            var document = new TextDocument();
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 2000));
            view.SelectFloating(0, 0);

            var chartRect = view.FloatingGroupChildRectForPathForTest(0, 0, [0])!.Value;
            view.SelectFloatingGroupChildForTest(chartRect.Center).Should().BeTrue();
            view.SelectedFloatingGroupChildPath.Should().Equal(0);
            view.RotateSelectedFloating(90);
            view.FlipSelectedFloating(horizontal: true);
            chartAngleAfter = chart.RotationAngle;
            chartFlipAfter = chart.FlipH;
            view.Undo();
            view.Undo();
            chartAngleReverted = chart.RotationAngle;
            chartFlipReverted = chart.FlipH;

            var smartArtRect = view.FloatingGroupChildRectForPathForTest(0, 0, [1, 0])!.Value;
            view.SelectFloatingGroupChildForTest(smartArtRect.Center).Should().BeTrue();
            view.SelectedFloatingGroupChildPath.Should().Equal(1, 0);
            view.RotateSelectedFloating(-90);
            view.FlipSelectedFloating(horizontal: false);
            smartArtAngleAfter = smartArt.RotationAngle;
            smartArtFlipAfter = smartArt.FlipV;
            view.Undo();
            view.Undo();
            smartArtAngleReverted = smartArt.RotationAngle;
            smartArtFlipReverted = smartArt.FlipV;
        });
        if (!ran) return;

        Assert.Equal(90, chartAngleAfter);
        Assert.True(chartFlipAfter);
        Assert.Equal(0, chartAngleReverted);
        Assert.False(chartFlipReverted);
        Assert.Equal(270, smartArtAngleAfter);
        Assert.True(smartArtFlipAfter);
        Assert.Equal(0, smartArtAngleReverted);
        Assert.False(smartArtFlipReverted);
    }

    [Fact]
    public async Task Group_child_hit_test_selects_child_and_rotates_child_with_undo()
    {
        int selectedChildIndex = -1;
        string? selectedChildKind = null;
        double childAngleAfter = 0, childAngleReverted = 0, groupAngleAfter = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = MakeDocWithNestedGroupAndShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            var childRect = view.FloatingGroupChildRectsForTest(0, 1)
                .Single(child => child.ChildIndex == 1).Rect;
            view.SelectFloatingGroupChildForTest(childRect.Center).Should().BeTrue();

            var selected = view.SelectedFloatingGroupChildInfo;
            selected.Should().NotBeNull();
            selectedChildIndex = selected!.Value.ChildIndex;
            selectedChildKind = selected.Value.Kind;

            view.RotateSelectedFloating(45);
            var group = ((Paragraph)doc.Blocks[0]).Runs[1].DrawingGroup!;
            var childShape = group.Children[1].Should().BeOfType<Shape>().Subject;
            childAngleAfter = childShape.RotationAngle;
            groupAngleAfter = group.RotationAngle;

            view.Undo();
            childAngleReverted = childShape.RotationAngle;
        });
        if (!ran) return;

        Assert.Equal(1, selectedChildIndex);
        Assert.Equal("Shape", selectedChildKind);
        Assert.Equal(45, childAngleAfter);
        Assert.Equal(0, groupAngleAfter);
        Assert.Equal(0, childAngleReverted);
    }

    [Fact]
    public async Task Transformed_group_child_move_and_resize_use_local_geometry_and_keep_selection()
    {
        double offsetXBefore = 0, offsetYBefore = 0;
        double offsetXAfterMove = 0, offsetYAfterMove = 0;
        double childWidthBefore = 0, childWidthAfter = 0;
        double groupWidthBefore = 0, groupWidthAfter = 0;
        int selectedChildIndex = -1;
        int handleCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = MakeDocWithNestedGroupAndShape();
            var group = ((Paragraph)doc.Blocks[0]).Runs[1].DrawingGroup!;
            group.RotationAngle = 90;
            group.FlipV = true;
            var childShape = group.Children[1].Should().BeOfType<Shape>().Subject;
            childShape.RotationAngle = 30;
            childShape.FlipH = true;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            view.SelectFloating(0, 1);
            var childRect = view.FloatingGroupChildRectsForTest(0, 1)
                .Single(child => child.ChildIndex == 1).Rect;
            var groupRect = view.SelectedFloatingInfo!.Value.Rect;
            var visibleChildCenter = DocumentViewLayoutPlanner.TransformPoint(
                new DocumentFloatPoint(childRect.Center.X, childRect.Center.Y),
                new DocumentFloatRect(groupRect.X, groupRect.Y, groupRect.Width, groupRect.Height),
                group.RotationAngle,
                group.FlipH,
                group.FlipV);
            // The group is rotated and flipped, so the child is drawn at the TRANSFORMED centre -- which is
            // where a user clicks and drags it. The untransformed rect centre is not on the child at all.
            var visibleCenterPoint = new Point(visibleChildCenter.XDip, visibleChildCenter.YDip);
            view.SelectFloatingGroupChildForTest(visibleCenterPoint).Should().BeTrue();
            var selected = view.SelectedFloatingGroupChildInfo;
            selected.Should().NotBeNull();
            selectedChildIndex = selected!.Value.ChildIndex;
            offsetXBefore = group.ChildOffsets[1].X;
            offsetYBefore = group.ChildOffsets[1].Y;
            childWidthBefore = childShape.WidthPt;
            groupWidthBefore = group.WidthPt;

            view.BeginFloatDrag(visibleCenterPoint).Should().Be(FloatHandle.Body);
            var screenDelta = new Vector(48, 24);
            view.SimulateDragTo(visibleCenterPoint + screenDelta);
            view.EndFloatDrag(visibleCenterPoint + screenDelta);

            var localDelta = DocumentViewLayoutPlanner.UnTransformVector(
                new DocumentFloatPoint(screenDelta.X, screenDelta.Y),
                group.RotationAngle,
                group.FlipH,
                group.FlipV);
            offsetXAfterMove = group.ChildOffsets[1].X;
            offsetYAfterMove = group.ChildOffsets[1].Y;
            offsetXAfterMove.Should().BeApproximately(
                offsetXBefore + localDelta.XDip / PageLayout.PointsToDip(1), 0.1);
            offsetYAfterMove.Should().BeApproximately(
                offsetYBefore + localDelta.YDip / PageLayout.PointsToDip(1), 0.1);
            group.WidthPt.Should().Be(groupWidthBefore);

            var handles = view.HandleRectsForSelection();
            handleCount = handles.Count;
            var bottomRight = handles[FloatHandle.BottomRight].Center;
            var outward = bottomRight - view.SelectedFloatingGroupChildInfo!.Value.Rect.Center;
            var resizeTarget = bottomRight + outward * 0.5;
            view.BeginFloatDrag(bottomRight).Should().Be(FloatHandle.BottomRight);
            view.SimulateDragTo(resizeTarget);
            view.EndFloatDrag(resizeTarget);

            childWidthAfter = childShape.WidthPt;
            groupWidthAfter = group.WidthPt;
            view.SelectedFloatingGroupChildInfo.Should().NotBeNull();
        });
        if (!ran) return;

        Assert.Equal(1, selectedChildIndex);
        Assert.Equal(8, handleCount);
        Assert.True(Math.Abs(offsetXAfterMove - offsetXBefore) > 0.1
            || Math.Abs(offsetYAfterMove - offsetYBefore) > 0.1,
            $"transformed child move should persist a local offset change: ({offsetXBefore},{offsetYBefore}) -> ({offsetXAfterMove},{offsetYAfterMove})");
        Assert.True(childWidthAfter > childWidthBefore,
            $"transformed child resize should grow the child: {childWidthBefore} -> {childWidthAfter}");
        Assert.Equal(groupWidthBefore, groupWidthAfter);
    }

    [Fact]
    public async Task Nested_group_child_select_move_resize_uses_composed_transforms_and_keeps_groups()
    {
        var selectedPath = Array.Empty<int>();
        double childOffsetXBefore = 0, childOffsetYBefore = 0;
        double childOffsetXAfter = 0, childOffsetYAfter = 0;
        double leafWidthBefore = 0, leafWidthAfter = 0;
        double outerWidthBefore = 0, innerWidthBefore = 0;
        double outerWidthAfter = 0, innerWidthAfter = 0;
        int handleCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = MakeDocWithOuterAndNestedGroupChild(out var outer, out var inner, out var leaf);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 2000));
            view.SelectFloating(0, 0);

            var path = new[] { 0, 1 };
            var leafRect = view.FloatingGroupChildRectForPathForTest(0, 0, path);
            var innerRect = view.FloatingGroupChildRectForPathForTest(0, 0, [0]);
            var outerRect = view.SelectedFloatingInfo!.Value.Rect;
            leafRect.Should().NotBeNull();
            innerRect.Should().NotBeNull();
            var parents = new DocumentFloatTransform[]
            {
                new(PlannerRect(innerRect!.Value), inner.RotationAngle, inner.FlipH, inner.FlipV),
                new(PlannerRect(outerRect), outer.RotationAngle, outer.FlipH, outer.FlipV)
            };
            var leafPlannerRect = PlannerRect(leafRect!.Value);
            var visibleCenter = DocumentViewLayoutPlanner.TransformPointThroughGroupChain(
                new DocumentFloatPoint(leafPlannerRect.CenterXDip, leafPlannerRect.CenterYDip),
                leafPlannerRect,
                leaf.RotationAngle,
                leaf.FlipH,
                leaf.FlipV,
                parents);
            view.SelectFloatingGroupChildForTest(
                new Point(visibleCenter.XDip, visibleCenter.YDip)).Should().BeTrue();
            selectedPath = view.SelectedFloatingGroupChildPath!.ToArray();

            childOffsetXBefore = inner.ChildOffsets[1].X;
            childOffsetYBefore = inner.ChildOffsets[1].Y;
            leafWidthBefore = leaf.WidthPt;
            outerWidthBefore = outer.WidthPt;
            innerWidthBefore = inner.WidthPt;

            var screenDelta = new Vector(38, -21);
            view.BeginFloatDrag(new Point(visibleCenter.XDip, visibleCenter.YDip))
                .Should().Be(FloatHandle.Body);
            view.SimulateDragTo(
                new Point(visibleCenter.XDip + screenDelta.X, visibleCenter.YDip + screenDelta.Y));
            view.EndFloatDrag(
                new Point(visibleCenter.XDip + screenDelta.X, visibleCenter.YDip + screenDelta.Y));

            var localDelta = DocumentViewLayoutPlanner.UnTransformVectorThroughGroupChain(
                new DocumentFloatPoint(screenDelta.X, screenDelta.Y),
                parents);
            childOffsetXAfter = inner.ChildOffsets[1].X;
            childOffsetYAfter = inner.ChildOffsets[1].Y;
            childOffsetXAfter.Should().BeApproximately(
                childOffsetXBefore + localDelta.XDip / PageLayout.PointsToDip(1), 0.2);
            childOffsetYAfter.Should().BeApproximately(
                childOffsetYBefore + localDelta.YDip / PageLayout.PointsToDip(1), 0.2);

            var movedLeafRect = view.FloatingGroupChildRectForPathForTest(0, 0, path)!.Value;
            var movedLeafPlannerRect = PlannerRect(movedLeafRect);
            // The parent chain has to be re-read AFTER the move: the inner group's rect is where the
            // chain is anchored, and the stale pre-move copy put the expected handle ~32dip away from
            // where the handle actually is.
            var movedParents = new DocumentFloatTransform[]
            {
                new(PlannerRect(view.FloatingGroupChildRectForPathForTest(0, 0, [0])!.Value),
                    inner.RotationAngle, inner.FlipH, inner.FlipV),
                new(PlannerRect(view.SelectedFloatingInfo!.Value.Rect),
                    outer.RotationAngle, outer.FlipH, outer.FlipV)
            };
            var handles = view.HandleRectsForSelection();
            handleCount = handles.Count;
            var bottomRight = handles[FloatHandle.BottomRight].Center;
            var expectedBottomRight = DocumentViewLayoutPlanner.TransformPointThroughGroupChain(
                new DocumentFloatPoint(movedLeafPlannerRect.RightDip, movedLeafPlannerRect.BottomDip),
                movedLeafPlannerRect,
                leaf.RotationAngle,
                leaf.FlipH,
                leaf.FlipV,
                movedParents);
            bottomRight.X.Should().BeApproximately(expectedBottomRight.XDip, 0.001);
            bottomRight.Y.Should().BeApproximately(expectedBottomRight.YDip, 0.001);
            var resizeTarget = bottomRight
                + (bottomRight - handles[FloatHandle.TopLeft].Center) * 0.5;
            view.BeginFloatDrag(bottomRight).Should().Be(FloatHandle.BottomRight);
            view.FloatDragBaseRectForTest.Should().Be(movedLeafRect);
            view.SimulateDragTo(resizeTarget);
            view.EndFloatDrag(resizeTarget);
            leafWidthAfter = leaf.WidthPt;
            outerWidthAfter = outer.WidthPt;
            innerWidthAfter = inner.WidthPt;
        });
        if (!ran) return;

        Assert.Equal(new[] { 0, 1 }, selectedPath);
        Assert.Equal(8, handleCount);
        Assert.True(Math.Abs(childOffsetXAfter - childOffsetXBefore) > 0.1
            || Math.Abs(childOffsetYAfter - childOffsetYBefore) > 0.1);
        Assert.True(leafWidthAfter > leafWidthBefore);
        Assert.Equal(outerWidthBefore, outerWidthAfter);
        Assert.Equal(innerWidthBefore, innerWidthAfter);
    }

    [Fact]
    public async Task Nested_grouped_text_box_supports_composed_caret_editing_and_path_undo()
    {
        string? editedText = null;
        string? undoneText = null;
        string? redoneText = null;
        IReadOnlyList<int>? selectedPath = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var leaf = Shape.TextBoxWith("hello", 96, 42);
            leaf.RotationAngle = 11;
            var inner = new DrawingGroup { WidthPt = 150, HeightPt = 72, RotationAngle = -14, FlipV = true };
            inner.Children.Add(new Shape(ShapeKind.Rectangle, 24, 18));
            inner.ChildOffsets.Add((4, 5));
            inner.Children.Add(leaf);
            inner.ChildOffsets.Add((38, 20));
            var outer = new DrawingGroup
            {
                WidthPt = 240,
                HeightPt = 130,
                RotationAngle = 22,
                FlipH = true,
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalAnchor = HorizontalAnchor.Page,
                    VerticalAnchor = VerticalAnchor.Page,
                    HorizontalOffsetPt = 72,
                    VerticalOffsetPt = 36,
                    ZOrderIndex = 2
                }
            };
            outer.Children.Add(inner);
            outer.ChildOffsets.Add((24, 18));
            outer.Children.Add(new Shape(ShapeKind.Ellipse, 30, 20));
            outer.ChildOffsets.Add((180, 80));
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromDrawingGroup(outer));
            doc.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(900, 2000));
            view.SelectFloating(0, 0);
            var path = new[] { 0, 1 };
            var leafRect = view.FloatingGroupChildRectForPathForTest(0, 0, path)!.Value;
            var innerRect = view.FloatingGroupChildRectForPathForTest(0, 0, [0])!.Value;
            var parents = new DocumentFloatTransform[]
            {
                new(PlannerRect(innerRect), inner.RotationAngle, inner.FlipH, inner.FlipV),
                new(PlannerRect(view.SelectedFloatingInfo!.Value.Rect), outer.RotationAngle, outer.FlipH, outer.FlipV)
            };
            var leafPlannerRect = PlannerRect(leafRect);
            var visibleCenter = DocumentViewLayoutPlanner.TransformPointThroughGroupChain(
                new DocumentFloatPoint(leafPlannerRect.CenterXDip, leafPlannerRect.CenterYDip),
                leafPlannerRect, leaf.RotationAngle, leaf.FlipH, leaf.FlipV, parents);
            view.SelectFloatingGroupChildForTest(
                new Point(visibleCenter.XDip, visibleCenter.YDip)).Should().BeTrue();
            selectedPath = view.SelectedFloatingGroupChildPath;
            view.EnterSelectedShapeTextEditing().Should().BeTrue();
            view.PlaceShapeTextCaretForTest(
                new Point(visibleCenter.XDip, visibleCenter.YDip)).Should().BeTrue();
            view.SelectShapeTextRangeForTest(0, 1, 4).Should().BeTrue();
            view.ToggleBold();
            view.InsertText("i");
            view.InsertShapeTextParagraphBreak();
            view.InsertText("world");
            // The original trailing "o" remains after the caret split; backspace must edit the
            // nested leaf rather than stretching or flattening the selection.
            view.BackspacePublic();
            editedText = leaf.PlainText;
            view.Undo();
            undoneText = leaf.PlainText;
            view.Redo();
            redoneText = leaf.PlainText;
        });

        if (!ran) return;
        selectedPath.Should().Equal(0, 1);
        editedText.Should().Be("hi\nworlo");
        undoneText.Should().Be("hi\nworldo");
        redoneText.Should().Be("hi\nworlo");
    }

    [Fact]
    public async Task Nested_branches_with_same_terminal_index_keep_child_paths_distinct()
    {
        var firstPath = Array.Empty<int>();
        var secondPath = Array.Empty<int>();
        bool secondPointMatchedFirst = true;
        var ran = await OnUiThread(() =>
        {
            var doc = MakeDocWithTwoNestedBranches(out _);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(1000, 2000));
            view.SelectFloating(0, 0);

            var firstRect = view.FloatingGroupChildRectForPathForTest(0, 0, [0, 1])!.Value;
            var secondRect = view.FloatingGroupChildRectForPathForTest(0, 0, [1, 1])!.Value;
            var firstPoint = firstRect.Center;
            var secondPoint = secondRect.Center;
            view.SelectFloatingGroupChildForTest(firstPoint).Should().BeTrue();
            firstPath = view.SelectedFloatingGroupChildPath!.ToArray();
            secondPointMatchedFirst = view.SelectedFloatingGroupChildMatchesPointForTest(secondPoint);
            view.SelectFloatingGroupChildForTest(secondPoint).Should().BeTrue();
            secondPath = view.SelectedFloatingGroupChildPath!.ToArray();
        });
        if (!ran) return;

        Assert.Equal(new[] { 0, 1 }, firstPath);
        Assert.False(secondPointMatchedFirst);
        Assert.Equal(new[] { 1, 1 }, secondPath);
    }

    [Fact]
    public async Task Nested_group_node_can_be_selected_moved_and_resized_without_mutating_outer_group()
    {
        double outerWidthBefore = 0, outerWidthAfter = 0;
        double innerWidthBefore = 0, innerWidthAfter = 0;
        double offsetBefore = 0, offsetAfter = 0;
        var selectedPath = Array.Empty<int>();
        var ran = await OnUiThread(() =>
        {
            var doc = MakeDocWithTwoNestedBranches(out var outer);
            var inner = outer.Children[0].Should().BeOfType<DrawingGroup>().Subject;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(1000, 2000));
            view.SelectFloating(0, 0);

            var innerRect = view.FloatingGroupChildRectForPathForTest(0, 0, [0])!.Value;
            // This point is inside the first nested group but in its empty body area,
            // outside both leaf children.
            var bodyPoint = new Point(innerRect.X + 80, innerRect.Y + 20);
            view.SelectFloatingGroupChildForTest(bodyPoint).Should().BeTrue();
            selectedPath = view.SelectedFloatingGroupChildPath!.ToArray();
            outerWidthBefore = outer.WidthPt;
            innerWidthBefore = inner.WidthPt;
            offsetBefore = outer.ChildOffsets[0].X;

            var movedPoint = bodyPoint + new Vector(20, 12);
            view.BeginFloatDrag(bodyPoint).Should().Be(FloatHandle.Body);
            view.SimulateDragTo(movedPoint);
            view.EndFloatDrag(movedPoint);
            offsetAfter = outer.ChildOffsets[0].X;

            var bottomRight = view.HandleRectsForSelection()[FloatHandle.BottomRight].Center;
            var resizeTarget = bottomRight + new Vector(24, 16);
            view.BeginFloatDrag(bottomRight).Should().Be(FloatHandle.BottomRight);
            view.SimulateDragTo(resizeTarget);
            view.EndFloatDrag(resizeTarget);
            innerWidthAfter = inner.WidthPt;
            outerWidthAfter = outer.WidthPt;
        });
        if (!ran) return;

        Assert.Equal(new[] { 0 }, selectedPath);
        Assert.NotEqual(offsetBefore, offsetAfter);
        Assert.True(innerWidthAfter > innerWidthBefore);
        Assert.Equal(outerWidthBefore, outerWidthAfter);
    }

    [Fact]
    public async Task Page_anchored_group_child_hit_uses_visible_transformed_center()
    {
        bool selected = false;
        var ran = await OnUiThread(() =>
        {
            var doc = new TextDocument();
            doc.Blocks.Clear();
            var group = new DrawingGroup
            {
                WidthPt = 210,
                HeightPt = 130,
                RotationAngle = 25,
                FlipH = true,
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalAnchor = HorizontalAnchor.Page,
                    VerticalAnchor = VerticalAnchor.Page,
                    HorizontalOffsetPt = 180,
                    VerticalOffsetPt = 150,
                    ZOrderIndex = 5
                }
            };
            group.Children.Add(new Shape(ShapeKind.Rectangle, 70, 40));
            group.Children.Add(new Shape(ShapeKind.Ellipse, 65, 35)
            {
                RotationAngle = 15,
                FlipV = true
            });
            group.ChildOffsets.Add((20, 20));
            group.ChildOffsets.Add((110, 55));
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromDrawingGroup(group));
            doc.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(1280, 2000));
            view.SelectFloating(0, 0);
            var groupRect = view.SelectedFloatingInfo!.Value.Rect;
            var childRect = view.FloatingGroupChildRectsForTest(0, 0)
                .Single(child => child.ChildIndex == 1).Rect;
            var visibleCenter = DocumentViewLayoutPlanner.TransformPoint(
                new DocumentFloatPoint(childRect.Center.X, childRect.Center.Y),
                new DocumentFloatRect(groupRect.X, groupRect.Y, groupRect.Width, groupRect.Height),
                group.RotationAngle,
                group.FlipH,
                group.FlipV);
            selected = view.SelectFloatingGroupChildForTest(
                new Point(visibleCenter.XDip, visibleCenter.YDip));
        });
        if (!ran) return;
        Assert.True(selected, "the visible child center should enter child selection");
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

    [Fact]
    public async Task Enter_on_selected_image_does_not_insert_a_body_paragraph_break()
    {
        string? before = null;
        string? after = null;
        bool handled = false;
        var ran = await OnUiThread(() =>
        {
            var (doc, bi, ri) = MakeDocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            before = ((Paragraph)doc.Blocks[bi]).PlainText;

            var args = new KeyEventArgs { Key = Key.Enter };
            view.RaiseKeyDownForContextMenuTests(args);
            handled = args.Handled;
            after = ((Paragraph)doc.Blocks[bi]).PlainText;
        });

        if (!ran) return;
        handled.Should().BeTrue("Enter belongs to the selected floating object route");
        after.Should().Be(before, "WPF keeps object selection active instead of inserting a body paragraph break");
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

    // FLSEL-18: arrangement falls back to all document floating objects like WPF.

    [Fact]
    public async Task ArrangeFloatingObjects_without_multi_selection_uses_document_objects_and_is_undoable()
    {
        bool canArrange = false, arranged = false;
        double imageOffset = 0, shapeOffset = 0, imageReverted = 0, shapeReverted = 0;
        HorizontalAnchor imageAnchor = default, shapeAnchor = default;
        var ran = await OnUiThread(() =>
        {
            var doc = MakeDocWithFloatingImageAndShape();
            doc.Page.MarginLeftPt = 90;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            canArrange = view.CanArrangeSelectedFloatingObjects(FloatingObjectArrangeKind.AlignToMargin);
            arranged = view.ArrangeSelectedFloatingObjects(FloatingObjectArrangeKind.AlignToMargin);

            var paragraph = (Paragraph)doc.Blocks[0];
            var image = paragraph.Runs[1].Image!;
            var shape = paragraph.Runs[2].Shape!;
            imageOffset = image.HorizontalOffsetPt;
            imageAnchor = image.HorizontalAnchor;
            shapeOffset = shape.Placement!.HorizontalOffsetPt;
            shapeAnchor = shape.Placement.HorizontalAnchor;

            view.Undo();
            imageReverted = image.HorizontalOffsetPt;
            shapeReverted = shape.Placement.HorizontalOffsetPt;
        });
        if (!ran) return;

        Assert.True(canArrange, "the ribbon command should be enabled for document-wide arrangement");
        Assert.True(arranged);
        Assert.Equal(90, imageOffset);
        Assert.Equal(HorizontalAnchor.Margin, imageAnchor);
        Assert.Equal(90, shapeOffset);
        Assert.Equal(HorizontalAnchor.Margin, shapeAnchor);
        Assert.Equal(36, imageReverted);
        Assert.Equal(108, shapeReverted);
    }
}
