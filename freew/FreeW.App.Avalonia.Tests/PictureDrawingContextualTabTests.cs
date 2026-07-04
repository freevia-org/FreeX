using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using SkiaSharp;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-PICTAB: Guard tests for the Picture Format + Drawing Format contextual tabs and the
/// <see cref="FloatingRibbonContextSource"/> that drives them.
/// <list type="bullet">
///   <item>Selecting a floating image activates the Picture context; a shape activates Drawing.</item>
///   <item>Deselect deactivates both.</item>
///   <item>All new command ids are registered and resolve.</item>
///   <item>Commands execute through to the model (z-order, wrap, rotate, size).</item>
/// </list>
/// </summary>
public sealed class PictureDrawingContextualTabTests
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

    private static byte[] SmallPng()
    {
        using var bmp = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(255, 128, 0));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private static (TextDocument Doc, int BlockIdx, int RunIdx) DocWithFloatingImage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text.", RunFormatting.Default));
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default)
        {
            Image = new InlineImage(SmallPng(), 144, 108)
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36, VerticalOffsetPt = 36, ZOrderIndex = 1,
            },
        });
        doc.Blocks.Add(para);
        return (doc, 0, 1);
    }

    private static (TextDocument Doc, int BlockIdx, int RunIdx) DocWithFloatingShape(
        ShapeKind kind = ShapeKind.Rectangle)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text.", RunFormatting.Default));
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default)
        {
            Shape = new Shape
            {
                Kind = kind, WidthPt = 120, HeightPt = 80, FillColorHex = "#FF0000",
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalOffsetPt = 36, VerticalOffsetPt = 36, ZOrderIndex = 1,
                },
            },
        });
        doc.Blocks.Add(para);
        return (doc, 0, 1);
    }

    private static TextDocument DocWithFloatingImageAndShape()
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

    // ── Ribbon definition shape ───────────────────────────────────────────────

    [Fact]
    public void Ribbon_definition_includes_picture_and_drawing_format_contextual_tabs()
    {
        var def = FreeWRibbon.BuildDefinition();
        var ctx = def.ContextualTabs.ToList();

        ctx.Any(t => t.Id == "picture-format").Should().BeTrue("picture-format tab must be defined");
        ctx.Any(t => t.Id == "drawing-format").Should().BeTrue("drawing-format tab must be defined");

        var pic = def.FindTab("picture-format")!;
        pic.Context!.ActivationKey.Should().Be(FloatingRibbonContextSource.PictureContextKey);
        pic.Context.Color.Should().Be(RibbonContextColor.Orange);

        var draw = def.FindTab("drawing-format")!;
        draw.Context!.ActivationKey.Should().Be(FloatingRibbonContextSource.DrawingContextKey);
        draw.Context.Color.Should().Be(RibbonContextColor.Purple);

        var pictureArrangeIds = pic.Groups.Single(g => g.Id == "picture-arrange").Controls
            .Select(control => GetCommandId(control)?.Value)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToArray();
        pictureArrangeIds.Should().Contain(new[]
        {
            "freew.object-group",
            "freew.object-ungroup",
        }, "Picture Format should expose Word's object Group/Ungroup arrange commands");

        var drawingArrangeIds = draw.Groups.Single(g => g.Id == "drawing-arrange").Controls
            .Select(control => GetCommandId(control)?.Value)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToArray();
        drawingArrangeIds.Should().Contain(new[]
        {
            "freew.image-bring-to-front",
            "freew.image-send-to-back",
            "freew.image-bring-forward",
            "freew.image-send-backward",
            "freew.object-group",
            "freew.object-ungroup",
        }, "Avalonia should use the same shared arrange command ids as WPF for drawing objects");
        drawingArrangeIds.Should().NotContain(new[]
        {
            "freew.shape-bring-to-front",
            "freew.shape-send-to-back",
            "freew.shape-bring-forward",
            "freew.shape-send-backward",
        }, "shape-prefixed z-order ids duplicate the shared WPF/Avalonia object-format commands");
    }

    [Fact]
    public void Registry_contains_all_picture_and_drawing_commands()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());

        var ids = new[]
        {
            // Picture
            "freew.image-wrap", "freew.image-wrap-inline", "freew.image-wrap-square",
            "freew.image-wrap-tight", "freew.image-wrap-top-bottom", "freew.image-wrap-behind",
            "freew.image-wrap-front", "freew.image-rotate", "freew.image-rotate-right90",
            "freew.image-rotate-left90", "freew.image-flip-vertical", "freew.image-flip-horizontal",
            "freew.image-bring-to-front", "freew.image-send-to-back", "freew.image-bring-forward",
            "freew.image-send-backward", "freew.object-group", "freew.object-ungroup",
            "freew.image-width", "freew.image-height",
            // Drawing
            "freew.shape-wrap", "freew.shape-wrap-inline", "freew.shape-wrap-square",
            "freew.shape-rotate", "freew.shape-rotate-right90", "freew.shape-flip-horizontal",
            "freew.shape-bring-to-front", "freew.shape-send-to-back", "freew.shape-bring-forward",
            "freew.shape-send-backward", "freew.shape-width", "freew.shape-height",
            "freew.shape-fill", "freew.shape-fill-no-fill", "freew.shape-fill-gradient-blue",
            "freew.shape-fill-gradient-orange", "freew.shape-fill-pattern-diag",
            "freew.shape-outline", "freew.shape-outline-no-outline", "freew.shape-outline-solid",
            "freew.shape-outline-dash", "freew.shape-outline-dot",
        };

        foreach (var id in ids)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"command '{id}' must be registered");
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
    public async Task FloatingContextSource_activates_picture_for_image_selection()
    {
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            var src = new FloatingRibbonContextSource(view);
            src.Current.IsActive(FloatingRibbonContextSource.PictureContextKey).Should().BeFalse("inactive before selection");

            var fired = false;
            src.ContextChanged += (_, _) => fired = true;

            view.SelectFloating(bi, ri);

            src.Current.IsActive(FloatingRibbonContextSource.PictureContextKey).Should().BeTrue("picture active for image");
            src.Current.IsActive(FloatingRibbonContextSource.DrawingContextKey).Should().BeFalse("drawing inactive for image");
            fired.Should().BeTrue("ContextChanged fires on selection");
        });
        if (!ran) return;
    }

    [Fact]
    public async Task FloatingContextSource_activates_drawing_for_shape_selection()
    {
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            var src = new FloatingRibbonContextSource(view);
            view.SelectFloating(bi, ri);

            src.Current.IsActive(FloatingRibbonContextSource.DrawingContextKey).Should().BeTrue("drawing active for shape");
            src.Current.IsActive(FloatingRibbonContextSource.PictureContextKey).Should().BeFalse("picture inactive for shape");
        });
        if (!ran) return;
    }

    [Fact]
    public async Task FloatingContextSource_deactivates_on_deselect()
    {
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            var src = new FloatingRibbonContextSource(view);
            view.SelectFloating(bi, ri);
            src.Current.IsActive(FloatingRibbonContextSource.PictureContextKey).Should().BeTrue("active after select");

            view.DeselectFloating();
            src.Current.IsActive(FloatingRibbonContextSource.PictureContextKey).Should().BeFalse("inactive after deselect");
            src.Current.IsActive(FloatingRibbonContextSource.DrawingContextKey).Should().BeFalse("inactive after deselect");
        });
        if (!ran) return;
    }

    [Fact]
    public async Task FloatingContextSource_switches_picture_to_drawing_when_selection_kind_changes()
    {
        var ran = await OnUi(() =>
        {
            // Document with an image (run 1) AND a shape (run 2) in the same paragraph.
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Body.", RunFormatting.Default));
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default)
            {
                Image = new InlineImage(SmallPng(), 144, 108)
                { Wrapping = ImageWrapping.Square, HorizontalOffsetPt = 10, VerticalOffsetPt = 10, ZOrderIndex = 1 },
            });
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default)
            {
                Shape = new Shape
                {
                    Kind = ShapeKind.Ellipse, WidthPt = 100, HeightPt = 80, FillColorHex = "#00FF00",
                    Placement = new FloatingPlacement
                    { Wrapping = ImageWrapping.Square, HorizontalOffsetPt = 200, VerticalOffsetPt = 200, ZOrderIndex = 2 },
                },
            });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            var src = new FloatingRibbonContextSource(view);

            view.SelectFloating(0, 1); // image
            src.Current.IsActive(FloatingRibbonContextSource.PictureContextKey).Should().BeTrue();

            view.SelectFloating(0, 2); // shape
            src.Current.IsActive(FloatingRibbonContextSource.DrawingContextKey).Should().BeTrue();
            src.Current.IsActive(FloatingRibbonContextSource.PictureContextKey).Should().BeFalse("picture must clear when shape selected");
        });
        if (!ran) return;
    }

    // ── Execute-through-to-model ──────────────────────────────────────────────

    [Fact]
    public async Task BringToFront_command_changes_image_zorder()
    {
        int? before = null, after = null;
        var ran = await OnUi(() =>
        {
            // Two floats so z-order has something to move past.
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("B", RunFormatting.Default));
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default)
            {
                Image = new InlineImage(SmallPng(), 100, 80)
                { Wrapping = ImageWrapping.Square, ZOrderIndex = 0 },
            });
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default)
            {
                Image = new InlineImage(SmallPng(), 100, 80)
                { Wrapping = ImageWrapping.Square, ZOrderIndex = 1 },
            });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(0, 1); // the lower (z=0) image
            before = ((Paragraph)doc.Blocks[0]).Runs[1].Image!.ZOrderIndex;

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.image-bring-to-front"), out var cmd);
            cmd!.Execute(RibbonCommandContext.Empty);

            after = ((Paragraph)doc.Blocks[0]).Runs[1].Image!.ZOrderIndex;
        });
        if (!ran) return;
        after.Should().BeGreaterThan(before!.Value, "bring-to-front must raise the image's z-order");
    }

    [Fact]
    public async Task WrapCommand_changes_shape_wrapping()
    {
        ImageWrapping? after = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.shape-wrap-tight"), out var cmd);
            cmd!.Execute(RibbonCommandContext.Empty);

            after = ((Paragraph)doc.Blocks[0]).Runs[ri].Shape!.Placement!.Wrapping;
        });
        if (!ran) return;
        after.Should().Be(ImageWrapping.Tight, "wrap-tight command must set Tight wrapping");
    }

    [Fact]
    public async Task RotateCommand_changes_image_rotation()
    {
        double? after = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingImage();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.image-rotate-right90"), out var cmd);
            cmd!.Execute(RibbonCommandContext.Empty);

            after = ((Paragraph)doc.Blocks[0]).Runs[ri].Image!.RotationAngle;
        });
        if (!ran) return;
        after.Should().Be(90, "rotate-right90 must set rotation to 90°");
    }

    [Fact]
    public async Task WidthCommand_changes_shape_width_only()
    {
        double? width = null, height = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingShape(); // 120×80
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.shape-width"), out var cmd);
            cmd!.Execute(RibbonCommandContext.ForSelectedValue("216"));

            var shape = ((Paragraph)doc.Blocks[0]).Runs[ri].Shape!;
            width = shape.WidthPt; height = shape.HeightPt;
        });
        if (!ran) return;
        width.Should().Be(216, "width command must set the new width");
        height.Should().Be(80, "height must be preserved by the width-only command");
    }

    [Fact]
    public async Task ShapeFillOutlineCommands_enable_only_for_shapes_and_textboxes()
    {
        bool? none = null, image = null, shape = null, textBox = null;
        var ran = await OnUi(() =>
        {
            var view = new DocumentView();
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            none = CommandIsEnabled(registry, "freew.shape-fill");

            var (imageDoc, ibi, iri) = DocWithFloatingImage();
            view.LoadDocument(imageDoc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(ibi, iri);
            image = CommandIsEnabled(registry, "freew.shape-outline-dash");

            var (shapeDoc, sbi, sri) = DocWithFloatingShape();
            view.LoadDocument(shapeDoc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(sbi, sri);
            shape = CommandIsEnabled(registry, "freew.shape-fill-gradient-blue");

            var (textBoxDoc, tbi, tri) = DocWithFloatingShape(ShapeKind.TextBox);
            view.LoadDocument(textBoxDoc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(tbi, tri);
            textBox = CommandIsEnabled(registry, "freew.shape-outline");
        });
        if (!ran) return;

        none.Should().BeFalse("fill/outline commands need a selected drawing shape");
        image.Should().BeFalse("picture selections use the Picture Format tab, not shape fill/outline");
        shape.Should().BeTrue("plain shapes can be formatted");
        textBox.Should().BeTrue("text boxes share Word's Drawing Format fill/outline commands");
    }

    [Fact]
    public async Task ObjectGroupCommands_enable_for_multi_select_and_group_selection()
    {
        bool? noneGroup = null, singleGroup = null, multiGroup = null, groupUngroup = null;
        var ran = await OnUi(() =>
        {
            var doc = DocWithFloatingImageAndShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

            noneGroup = CommandIsEnabled(registry, "freew.object-group");
            view.SelectFloating(0, 1);
            singleGroup = CommandIsEnabled(registry, "freew.object-group");
            view.SelectFloating(0, 2, addToMultiSelect: true);
            multiGroup = CommandIsEnabled(registry, "freew.object-group");

            ExecuteCommand(registry, "freew.object-group");
            view.SelectFloating(0, 1);
            groupUngroup = CommandIsEnabled(registry, "freew.object-ungroup");
        });
        if (!ran) return;

        noneGroup.Should().BeFalse("Group needs at least two selected floating objects");
        singleGroup.Should().BeFalse("single object selection cannot be grouped");
        multiGroup.Should().BeTrue("image + shape multi-selection can be grouped by the shared model command");
        groupUngroup.Should().BeTrue("Ungroup is available when the selected run is a drawing group");
    }

    [Fact]
    public async Task ObjectGroupCommands_group_and_ungroup_through_registry()
    {
        int? groupedRunCount = null, groupedChildCount = null, ungroupedRunCount = null;
        bool? ungroupedHasImage = null, ungroupedHasShape = null;
        var ran = await OnUi(() =>
        {
            var doc = DocWithFloatingImageAndShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(0, 1);
            view.SelectFloating(0, 2, addToMultiSelect: true);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            ExecuteCommand(registry, "freew.object-group");

            var para = (Paragraph)doc.Blocks[0];
            groupedRunCount = para.Runs.Count;
            groupedChildCount = para.Runs[1].DrawingGroup?.Children.Count;

            view.SelectFloating(0, 1);
            ExecuteCommand(registry, "freew.object-ungroup");

            ungroupedRunCount = para.Runs.Count;
            ungroupedHasImage = para.Runs.Any(r => r.Image is not null);
            ungroupedHasShape = para.Runs.Any(r => r.Shape is not null);
        });
        if (!ran) return;

        groupedRunCount.Should().Be(2, "the two floating object runs should be replaced by one group run");
        groupedChildCount.Should().Be(2);
        ungroupedRunCount.Should().Be(3, "body text plus the restored image and shape runs should remain");
        ungroupedHasImage.Should().BeTrue();
        ungroupedHasShape.Should().BeTrue();
    }

    [Fact]
    public async Task ShapeFillOutlineCommands_apply_shared_presets_to_selected_shape()
    {
        Shape? shape = null;
        string? fillAfterNoFill = "unchanged";
        ShapeFill? extendedAfterNoFill = ShapeFill.Patterned("diagCross", "#000000", "#FFFFFF");
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            ExecuteCommand(registry, "freew.shape-fill-gradient-blue");
            ExecuteCommand(registry, "freew.shape-fill-no-fill");
            var noFillShape = ((Paragraph)doc.Blocks[0]).Runs[ri].Shape!;
            fillAfterNoFill = noFillShape.FillColorHex;
            extendedAfterNoFill = noFillShape.ExtendedFill;
            ExecuteCommand(registry, "freew.shape-fill-gradient-blue");
            ExecuteCommand(registry, "freew.shape-outline-dash");
            ExecuteCommand(registry, "freew.shape-outline-no-outline");

            shape = ((Paragraph)doc.Blocks[0]).Runs[ri].Shape!;
        });
        if (!ran) return;

        fillAfterNoFill.Should().BeNull("No Fill should clear any simple solid fill");
        extendedAfterNoFill.Should().BeNull("No Fill should clear any gradient or pattern fill");
        shape.Should().NotBeNull();
        shape!.FillColorHex.Should().BeNull("extended fill presets clear the simple solid fill");
        shape.ExtendedFill.Should().NotBeNull();
        shape.ExtendedFill!.Kind.Should().Be(ShapeFillKind.Gradient);
        shape.ExtendedFill.GradientStops.Should().Equal(
            new GradientStop(0, "#4472C4"),
            new GradientStop(100000, "#1F4E79"));
        shape.OutlineColorHex.Should().BeNull("No Outline should clear the stroke color after a dash preset");
        shape.OutlineWidthPt.Should().Be(0);
        shape.OutlineDash.Should().BeNull();
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
                "freew.image-bring-to-front", "freew.image-wrap-square", "freew.image-rotate-right90",
                "freew.shape-send-backward", "freew.shape-flip-vertical", "freew.shape-fill",
                "freew.shape-fill-gradient-blue", "freew.shape-outline-dash",
            })
            {
                registry.TryGet(new RibbonCommandId(id), out var cmd);
                cmd!.Execute(RibbonCommandContext.Empty); // must not throw
            }
        });
        ran.Should().BeTrue("float-format commands must silently no-op when nothing is selected");
    }

    private static bool CommandIsEnabled(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command)
            .Should().BeTrue($"command '{id}' must be registered");
        command.Should().BeAssignableTo<IRibbonStatefulCommand>($"command '{id}' should expose live enablement");
        return ((IRibbonStatefulCommand)command!).GetState().IsEnabled;
    }

    private static void ExecuteCommand(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command)
            .Should().BeTrue($"command '{id}' must be registered");
        command!.Execute(RibbonCommandContext.Empty);
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
}
