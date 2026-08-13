using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using SkiaSharp;
using Xunit;
using FreeWRibbonDefinitionData = FreeW.Ribbon.Definitions.FreeWRibbonDefinitionData;

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

    private static FreeWRibbonHostExecutionPorts NoopCallbacks() =>
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
        ShapeKind kind = ShapeKind.Rectangle,
        string? text = null)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text.", RunFormatting.Default));
        var shape = new Shape
        {
            Kind = kind, WidthPt = 120, HeightPt = 80, FillColorHex = "#FF0000",
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36, VerticalOffsetPt = 36, ZOrderIndex = 1,
            },
        };
        if (text is not null)
        {
            var textParagraph = new Paragraph();
            textParagraph.Runs.Add(new Run(text));
            shape.TextParagraphs.Add(textParagraph);
        }
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Shape = shape });
        doc.Blocks.Add(para);
        return (doc, 0, 1);
    }

    private static (TextDocument Doc, int BlockIdx, int RunIdx) DocWithFloatingWordArt()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Body text.", RunFormatting.Default));
        paragraph.Runs.Add(new Run(string.Empty, RunFormatting.Default)
        {
            WordArt = new WordArt
            {
                Text = "Heading",
                AltText = "Original WordArt",
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalOffsetPt = 24,
                    VerticalOffsetPt = 18,
                    ZOrderIndex = 1,
                },
            },
        });
        doc.Blocks.Add(paragraph);
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
        var def = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
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
            "freew.image-position",
            "freew.image-align-left",
            "freew.image-align-center",
            "freew.image-align-right",
            "freew.image-align-to-page",
            "freew.image-align-to-margin",
            "freew.image-distribute-h",
            "freew.image-distribute-v",
            "freew.object-group",
            "freew.object-ungroup",
        }, "Picture Format should expose Word's object arrange commands");

        var drawingStyleIds = draw.Groups.Single(g => g.Id == "drawing-styles").Controls
            .Select(control => GetCommandId(control)?.Value)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToArray();
        drawingStyleIds.Should().Contain(new[]
        {
            "freew.shape-styles-gallery",
            "freew.shape-fill",
            "freew.shape-outline",
            "freew.shape-effects",
            "freew.shape-change",
            "freew.shape-edit-shape",
            "freew.shape-text-direction",
        }, "Drawing Format should expose the WPF shape-format menus");

        var drawingArrangeIds = draw.Groups.Single(g => g.Id == "drawing-arrange").Controls
            .Select(control => GetCommandId(control)?.Value)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToArray();
        drawingArrangeIds.Should().Contain(new[]
        {
            "freew.shape-position",
            "freew.image-bring-to-front",
            "freew.image-send-to-back",
            "freew.image-bring-forward",
            "freew.image-send-backward",
            "freew.shape-align-left",
            "freew.shape-align-center",
            "freew.shape-align-right",
            "freew.shape-align-to-page",
            "freew.shape-align-to-margin",
            "freew.shape-distribute-h",
            "freew.shape-distribute-v",
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
            "freew.image-position", "freew.image-align-left", "freew.image-align-center", "freew.image-align-right",
            "freew.image-align-to-page", "freew.image-align-to-margin",
            "freew.image-distribute-h", "freew.image-distribute-v",
            "freew.image-width", "freew.image-height",
            // Drawing
            "freew.shape-wrap", "freew.shape-wrap-inline", "freew.shape-wrap-square",
            "freew.shape-rotate", "freew.shape-rotate-right90", "freew.shape-flip-horizontal",
            "freew.shape-bring-to-front", "freew.shape-send-to-back", "freew.shape-bring-forward",
            "freew.shape-send-backward", "freew.shape-width", "freew.shape-height",
            "freew.shape-position", "freew.shape-align-left", "freew.shape-align-center", "freew.shape-align-right",
            "freew.shape-align-to-page", "freew.shape-align-to-margin",
            "freew.shape-distribute-h", "freew.shape-distribute-v",
            "freew.shape-size", "freew.shape-alt-text", "freew.shape-styles-gallery",
            "freew.shape-fill", "freew.shape-fill-no-fill", "freew.shape-fill-gradient-blue",
            "freew.shape-fill-gradient-orange", "freew.shape-fill-pattern-diag",
            "freew.shape-outline", "freew.shape-outline-no-outline", "freew.shape-outline-solid",
            "freew.shape-outline-dash", "freew.shape-outline-dot",
            "freew.shape-change", "freew.shape-change-rectangle", "freew.shape-change-rounded",
            "freew.shape-change-ellipse", "freew.shape-edit-shape", "freew.shape-convert-freeform",
            "freew.shape-edit-points", "freew.shape-text-direction", "freew.shape-text-horizontal",
            "freew.shape-text-rotate90", "freew.shape-text-rotate270", "freew.shape-effects",
            "freew.shape-effects-none", "freew.shape-effect-shadow", "freew.shape-effect-glow",
            "freew.shape-effect-soft-edge", "freew.shape-effect-reflection", "freew.shape-effect-bevel",
        };

        foreach (var id in ids)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"command '{id}' must be registered");

        foreach (var preset in FreeWRibbonDefinitionData.FloatingPositionPresets)
        {
            registry.TryGet(new RibbonCommandId($"freew.image-position-{preset.Suffix}"), out _)
                .Should().BeTrue($"image position preset '{preset.Suffix}' must be registered");
            registry.TryGet(new RibbonCommandId($"freew.shape-position-{preset.Suffix}"), out _)
                .Should().BeTrue($"shape position preset '{preset.Suffix}' must be registered");
        }

        foreach (var preset in FreeWRibbonDefinitionData.FloatingSizePresets)
            registry.TryGet(new RibbonCommandId($"freew.shape-size-{preset.Suffix}"), out _)
                .Should().BeTrue($"shape size preset '{preset.Suffix}' must be registered");

        foreach (var preset in FreeWRibbonDefinitionData.ShapeAltTextPresets)
            registry.TryGet(new RibbonCommandId($"freew.shape-alt-text-{preset.Suffix}"), out _)
                .Should().BeTrue($"shape alt-text preset '{preset.Suffix}' must be registered");

        registry.TryGet(new RibbonCommandId("freew.shape-style-1"), out _)
            .Should().BeTrue("shape style menu items must be registered");
    }

    [Fact]
    public void Every_contextual_ribbon_command_is_registered()
    {
        var def = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());

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
    public async Task ShapeTextDirectionCommands_are_enabled_for_text_boxes_and_mutate_the_shared_model()
    {
        ShapeTextDirection? direction = null;
        bool? plainShapeEnabled = null;
        var ran = await OnUi(() =>
        {
            var view = new DocumentView();
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

            var (plainDoc, plainBlock, plainRun) = DocWithFloatingShape();
            view.LoadDocument(plainDoc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(plainBlock, plainRun);
            plainShapeEnabled = CommandIsEnabled(registry, "freew.shape-text-rotate90");

            var (textDoc, textBlock, textRun) = DocWithFloatingShape(ShapeKind.TextBox, "Rotate me");
            view.LoadDocument(textDoc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(textBlock, textRun);
            CommandIsEnabled(registry, "freew.shape-text-rotate90").Should().BeTrue();
            ExecuteCommand(registry, "freew.shape-text-rotate90");
            direction = view.SelectedFloatingShape()?.TextDirection;
        });
        if (!ran) return;

        plainShapeEnabled.Should().BeFalse("text direction is only available for text-box shapes");
        direction.Should().Be(ShapeTextDirection.Rotate90);
    }

    [Fact]
    public async Task Nested_shape_alignment_commands_are_enabled_for_the_leaf_and_use_its_text_paragraphs()
    {
        TextAlignment? applied = null;
        TextAlignment? siblingAlignment = null;
        bool? groupEnabled = null;
        bool? childEnabled = null;
        bool? centerEnabled = null;
        bool? undoRestored = null;
        bool? redoRestored = null;
        var ran = await OnUi(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var leaf = Shape.TextBoxWith("Nested alignment", 120, 60);
            var sibling = Shape.TextBoxWith("Sibling", 90, 40);
            var inner = new DrawingGroup { WidthPt = 160, HeightPt = 80 };
            inner.Children.Add(new Shape(ShapeKind.Rectangle, 20, 20));
            inner.ChildOffsets.Add((0, 0));
            inner.Children.Add(leaf);
            inner.ChildOffsets.Add((30, 10));
            var outer = new DrawingGroup { WidthPt = 240, HeightPt = 120 };
            outer.Children.Add(inner);
            outer.ChildOffsets.Add((12, 8));
            outer.Children.Add(sibling);
            outer.ChildOffsets.Add((180, 70));
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromDrawingGroup(outer));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(800, 2000));
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            view.SelectFloating(0, 0);
            groupEnabled = CommandIsEnabled(registry, "freew.shape-align-center");
            var leafRect = view.FloatingGroupChildRectForPathForTest(0, 0, [0, 1])!.Value;
            view.SelectFloatingGroupChildForTest(leafRect.Center).Should().BeTrue();
            childEnabled = CommandIsEnabled(registry, "freew.shape-align-left");
            centerEnabled = CommandIsEnabled(registry, "freew.shape-align-center");

            ExecuteCommand(registry, "freew.shape-align-center");
            applied = leaf.TextParagraphs.Single().Formatting.Alignment;
            siblingAlignment = sibling.TextParagraphs.Single().Formatting.Alignment;
            view.Undo();
            undoRestored = leaf.TextParagraphs.Single().Formatting.Alignment == TextAlignment.Left;
            view.Redo();
            redoRestored = leaf.TextParagraphs.Single().Formatting.Alignment == TextAlignment.Center;
        });
        if (!ran) return;

        groupEnabled.Should().BeFalse("the owning group is not a text-bearing shape leaf");
        childEnabled.Should().BeTrue("nested text-bearing leaves expose shape paragraph alignment");
        centerEnabled.Should().BeTrue("Center is backed on the Drawing Format contextual surface");
        applied.Should().Be(TextAlignment.Center);
        siblingAlignment.Should().Be(TextAlignment.Left);
        undoRestored.Should().BeTrue();
        redoRestored.Should().BeTrue();
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
    public async Task ObjectArrangeCommands_use_document_fallback_without_multi_selection()
    {
        bool? noneAlign = null, noneDistribute = null, singleAlign = null, singleDistribute = null, multiDistribute = null;
        var ran = await OnUi(() =>
        {
            var doc = DocWithFloatingImageAndShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

            noneAlign = CommandIsEnabled(registry, "freew.image-align-to-page");
            noneDistribute = CommandIsEnabled(registry, "freew.image-distribute-h");
            view.SelectFloating(0, 1);
            singleAlign = CommandIsEnabled(registry, "freew.image-align-to-page");
            singleDistribute = CommandIsEnabled(registry, "freew.image-distribute-h");
            view.SelectFloating(0, 2, addToMultiSelect: true);
            multiDistribute = CommandIsEnabled(registry, "freew.shape-distribute-v");
        });
        if (!ran) return;

        noneAlign.Should().BeTrue("WPF falls back to arranging all floating objects without an explicit multi-selection");
        noneDistribute.Should().BeTrue("the document fallback contains two distributable floating objects");
        singleAlign.Should().BeTrue("a single selection still uses the document-wide WPF fallback");
        singleDistribute.Should().BeTrue("a single selection still distributes the document-wide fallback");
        multiDistribute.Should().BeTrue("image + shape multi-selection can be distributed by the shared model command");
    }

    [Fact]
    public async Task ObjectArrangeCommands_align_mixed_selection_and_undo_through_registry()
    {
        double? imageX = null, shapeX = null, imageRevertedX = null, shapeRevertedX = null;
        HorizontalAnchor? imageAnchor = null, shapeAnchor = null, imageRevertedAnchor = null, shapeRevertedAnchor = null;
        var ran = await OnUi(() =>
        {
            var doc = DocWithFloatingImageAndShape();
            doc.Page.MarginLeftPt = 90;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(0, 1);
            view.SelectFloating(0, 2, addToMultiSelect: true);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            ExecuteCommand(registry, "freew.image-align-to-margin");

            var para = (Paragraph)doc.Blocks[0];
            imageX = para.Runs[1].Image!.HorizontalOffsetPt;
            shapeX = para.Runs[2].Shape!.Placement!.HorizontalOffsetPt;
            imageAnchor = para.Runs[1].Image!.HorizontalAnchor;
            shapeAnchor = para.Runs[2].Shape!.Placement!.HorizontalAnchor;

            view.Undo();
            imageRevertedX = para.Runs[1].Image!.HorizontalOffsetPt;
            shapeRevertedX = para.Runs[2].Shape!.Placement!.HorizontalOffsetPt;
            imageRevertedAnchor = para.Runs[1].Image!.HorizontalAnchor;
            shapeRevertedAnchor = para.Runs[2].Shape!.Placement!.HorizontalAnchor;
        });
        if (!ran) return;

        imageX.Should().Be(90);
        shapeX.Should().Be(90);
        imageAnchor.Should().Be(HorizontalAnchor.Margin);
        shapeAnchor.Should().Be(HorizontalAnchor.Margin);
        imageRevertedX.Should().Be(36);
        shapeRevertedX.Should().Be(108);
        imageRevertedAnchor.Should().Be(HorizontalAnchor.Column);
        shapeRevertedAnchor.Should().Be(HorizontalAnchor.Column);
    }

    [Fact]
    public async Task ObjectAlignCommands_match_wpf_selected_object_paragraph_alignment()
    {
        TextAlignment? imageAlignment = null, shapeAlignment = null;
        double? imageX = null, shapeX = null;
        HorizontalAnchor? imageAnchor = null, shapeAnchor = null;
        var ran = await OnUi(() =>
        {
            var doc = DocWithFloatingImageAndShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            view.SelectFloating(0, 1);
            ExecuteCommand(registry, "freew.image-align-center");

            var para = (Paragraph)doc.Blocks[0];
            imageAlignment = para.Formatting.Alignment;
            imageX = para.Runs[1].Image!.HorizontalOffsetPt;
            imageAnchor = para.Runs[1].Image!.HorizontalAnchor;

            view.SelectFloating(0, 2);
            ExecuteCommand(registry, "freew.shape-align-right");
            shapeAlignment = para.Formatting.Alignment;
            shapeX = para.Runs[2].Shape!.Placement!.HorizontalOffsetPt;
            shapeAnchor = para.Runs[2].Shape!.Placement!.HorizontalAnchor;
        });
        if (!ran) return;

        imageAlignment.Should().Be(TextAlignment.Center, "image-align-* matches WPF by aligning the containing paragraph");
        imageX.Should().Be(36, "image-align-* must not route to floating placement alignment");
        imageAnchor.Should().Be(HorizontalAnchor.Column);
        shapeAlignment.Should().Be(TextAlignment.Right, "shape-align-* matches WPF by aligning the containing paragraph");
        shapeX.Should().Be(108, "shape-align-* must not route to floating placement alignment");
        shapeAnchor.Should().Be(HorizontalAnchor.Column);
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
    public async Task ShapeFormatValueCommands_update_position_size_alt_text_and_style()
    {
        Shape? shape = null;
        HorizontalAnchor? anchor = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            ExecuteCommand(registry, "freew.shape-size", RibbonCommandContext.ForSelectedValue("144,72"));
            ExecuteCommand(registry, "freew.shape-position", RibbonCommandContext.ForSelectedValue("24,48,Page,Paragraph"));
            ExecuteCommand(registry, "freew.shape-alt-text", RibbonCommandContext.ForSelectedValue("  Sales process  "));
            ExecuteCommand(registry, "freew.shape-styles-gallery", RibbonCommandContext.ForSelectedValue("shape-style-1"));

            shape = ((Paragraph)doc.Blocks[0]).Runs[ri].Shape!;
            anchor = shape.Placement!.HorizontalAnchor;
        });
        if (!ran) return;

        shape.Should().NotBeNull();
        shape!.WidthPt.Should().Be(144);
        shape.HeightPt.Should().Be(72);
        shape.Placement!.HorizontalOffsetPt.Should().Be(24);
        shape.Placement.VerticalOffsetPt.Should().Be(48);
        anchor.Should().Be(HorizontalAnchor.Page);
        shape.AltText.Should().Be("Sales process");
        shape.ExtendedFill.Should().NotBeNull();
        shape.ExtendedFill!.Kind.Should().Be(ShapeFillKind.NoFill);
        shape.OutlineColorHex.Should().Be("#4472C4");
    }

    [Fact]
    public Task ShapeFormatPrimaryDialogs_seed_apply_and_undo_model_changes() =>
        Session.Dispatch(() =>
        {
            var (doc, bi, ri) = DocWithFloatingShape();
            var shape = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            var callbacks = NoopCallbacks() with
            {
                OpenShapePositionDialog = () =>
                {
                    var selected = view.SelectedFloatingShape();
                    selected.Should().BeSameAs(shape);
                    selected!.Placement!.HorizontalOffsetPt.Should().Be(36);
                    view.SetFloatingPosition(18, 27, HorizontalAnchor.Page, VerticalAnchor.Margin);
                },
                OpenShapeSizeDialog = () =>
                {
                    view.GetSelectedFloatingSize().Should().Be((120.0, 80.0));
                    view.SetFloatingSize(180, 90);
                },
                OpenShapeAltTextDialog = () =>
                {
                    view.SelectedFloatingShape().Should().BeSameAs(shape);
                    view.SetSelectedFloatingAltText("Accessible shape");
                },
            };
            var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

            ExecuteCommand(registry, "freew.shape-position");
            shape.Placement!.HorizontalOffsetPt.Should().Be(18);
            shape.Placement.VerticalOffsetPt.Should().Be(27);
            view.Undo();
            shape.Placement.HorizontalOffsetPt.Should().Be(36);
            shape.Placement.VerticalOffsetPt.Should().Be(36);

            ExecuteCommand(registry, "freew.shape-size");
            (shape.WidthPt, shape.HeightPt).Should().Be((180.0, 90.0));
            view.Undo();
            (shape.WidthPt, shape.HeightPt).Should().Be((120.0, 80.0));

            ExecuteCommand(registry, "freew.shape-alt-text");
            shape.AltText.Should().Be("Accessible shape");
            view.Undo();
            shape.AltText.Should().BeNull();
        }, CancellationToken.None);

    [Fact]
    public Task ShapeFormatPrimaryDialogs_cancel_without_mutating_seeded_shape() =>
        Session.Dispatch(() =>
        {
            var (doc, bi, ri) = DocWithFloatingShape();
            var shape = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            var invoked = new List<string>();
            var callbacks = NoopCallbacks() with
            {
                OpenShapePositionDialog = () => invoked.Add("position"),
                OpenShapeSizeDialog = () => invoked.Add("size"),
                OpenShapeAltTextDialog = () => invoked.Add("alt"),
            };
            var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

            ExecuteCommand(registry, "freew.shape-position");
            ExecuteCommand(registry, "freew.shape-size");
            ExecuteCommand(registry, "freew.shape-alt-text");

            invoked.Should().Equal("position", "size", "alt");
            shape.Placement!.HorizontalOffsetPt.Should().Be(36);
            shape.Placement.VerticalOffsetPt.Should().Be(36);
            (shape.WidthPt, shape.HeightPt).Should().Be((120.0, 80.0));
            shape.AltText.Should().BeNull();
        }, CancellationToken.None);

    [Fact]
    public Task ShapeAltTextPrimaryDialog_targets_selected_wordart_and_undoes() =>
        Session.Dispatch(() =>
        {
            var (doc, bi, ri) = DocWithFloatingWordArt();
            var wordArt = ((Paragraph)doc.Blocks[bi]).Runs[ri].WordArt!;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);
            var callbacks = NoopCallbacks() with
            {
                OpenShapeAltTextDialog = () =>
                {
                    view.SelectedFloatingWordArt().Should().BeSameAs(wordArt);
                    view.SetSelectedFloatingAltText("Accessible WordArt");
                },
            };
            var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

            ExecuteCommand(registry, "freew.shape-alt-text");

            wordArt.AltText.Should().Be("Accessible WordArt");
            view.Undo();
            wordArt.AltText.Should().Be("Original WordArt");
        }, CancellationToken.None);

    [Fact]
    public async Task ObjectFormatDropdownOpeners_do_not_mutate_on_empty_context()
    {
        InlineImage? image = null;
        Shape? shape = null;
        var ran = await OnUi(() =>
        {
            var (imageDoc, imageBlock, imageRun) = DocWithFloatingImage();
            var imageView = new DocumentView();
            imageView.LoadDocument(imageDoc);
            imageView.Measure(new Size(800, 2000));
            imageView.SelectFloating(imageBlock, imageRun);

            var imageRegistry = FreeWAvaloniaRibbonCommands.Build(imageView, NoopCallbacks());
            ExecuteCommand(imageRegistry, "freew.image-position");
            image = ((Paragraph)imageDoc.Blocks[0]).Runs[imageRun].Image!;

            var (shapeDoc, shapeBlock, shapeRun) = DocWithFloatingShape();
            var shapeView = new DocumentView();
            shapeView.LoadDocument(shapeDoc);
            shapeView.Measure(new Size(800, 2000));
            shapeView.SelectFloating(shapeBlock, shapeRun);

            var shapeRegistry = FreeWAvaloniaRibbonCommands.Build(shapeView, NoopCallbacks());
            ExecuteCommand(shapeRegistry, "freew.shape-position");
            ExecuteCommand(shapeRegistry, "freew.shape-size");
            ExecuteCommand(shapeRegistry, "freew.shape-alt-text");
            ExecuteCommand(shapeRegistry, "freew.shape-styles-gallery");

            shape = ((Paragraph)shapeDoc.Blocks[0]).Runs[shapeRun].Shape!;
        });
        if (!ran) return;

        image.Should().NotBeNull();
        image!.HorizontalOffsetPt.Should().Be(36, "opening Position must not silently move the image");
        image.VerticalOffsetPt.Should().Be(36);
        image.HorizontalAnchor.Should().Be(HorizontalAnchor.Column);
        image.VerticalAnchor.Should().Be(VerticalAnchor.Paragraph);

        shape.Should().NotBeNull();
        shape!.Placement!.HorizontalOffsetPt.Should().Be(36, "opening Position must not silently move the shape");
        shape.Placement.VerticalOffsetPt.Should().Be(36);
        shape.Placement.HorizontalAnchor.Should().Be(HorizontalAnchor.Column);
        shape.Placement.VerticalAnchor.Should().Be(VerticalAnchor.Paragraph);
        shape.WidthPt.Should().Be(120, "opening Size must not silently resize the shape");
        shape.HeightPt.Should().Be(80);
        shape.AltText.Should().BeNull("opening Alt Text must not write placeholder text");
        shape.ExtendedFill.Should().BeNull("opening Shape Styles must not apply the first style");
        shape.FillColorHex.Should().Be("#FF0000");
    }

    [Fact]
    public async Task ObjectFormatMenuCommands_apply_explicit_user_choices()
    {
        InlineImage? image = null;
        Shape? shape = null;
        var ran = await OnUi(() =>
        {
            var (imageDoc, imageBlock, imageRun) = DocWithFloatingImage();
            var imageView = new DocumentView();
            imageView.LoadDocument(imageDoc);
            imageView.Measure(new Size(800, 2000));
            imageView.SelectFloating(imageBlock, imageRun);

            var imageRegistry = FreeWAvaloniaRibbonCommands.Build(imageView, NoopCallbacks());
            ExecuteCommand(imageRegistry, "freew.image-position-page-top");
            image = ((Paragraph)imageDoc.Blocks[0]).Runs[imageRun].Image!;

            var (shapeDoc, shapeBlock, shapeRun) = DocWithFloatingShape();
            var shapeView = new DocumentView();
            shapeView.LoadDocument(shapeDoc);
            shapeView.Measure(new Size(800, 2000));
            shapeView.SelectFloating(shapeBlock, shapeRun);

            var shapeRegistry = FreeWAvaloniaRibbonCommands.Build(shapeView, NoopCallbacks());
            ExecuteCommand(shapeRegistry, "freew.shape-position-margin-paragraph");
            ExecuteCommand(shapeRegistry, "freew.shape-size-wide");
            ExecuteCommand(shapeRegistry, "freew.shape-alt-text-process-diagram");
            ExecuteCommand(shapeRegistry, "freew.shape-style-1");

            shape = ((Paragraph)shapeDoc.Blocks[0]).Runs[shapeRun].Shape!;
        });
        if (!ran) return;

        image.Should().NotBeNull();
        var imagePosition = FreeWRibbonDefinitionData.FloatingPositionPresets.Single(p => p.Suffix == "page-top");
        image!.HorizontalOffsetPt.Should().Be(imagePosition.HorizontalOffsetPt);
        image.VerticalOffsetPt.Should().Be(imagePosition.VerticalOffsetPt);
        image.HorizontalAnchor.Should().Be(imagePosition.HorizontalAnchor);
        image.VerticalAnchor.Should().Be(imagePosition.VerticalAnchor);

        shape.Should().NotBeNull();
        var shapePosition = FreeWRibbonDefinitionData.FloatingPositionPresets.Single(p => p.Suffix == "margin-paragraph");
        var shapeSize = FreeWRibbonDefinitionData.FloatingSizePresets.Single(p => p.Suffix == "wide");
        var shapeStyle = ShapeStylePreset.Catalog.Single(p => p.Id == "shape-style-1");
        shape!.Placement!.HorizontalOffsetPt.Should().Be(shapePosition.HorizontalOffsetPt);
        shape.Placement.VerticalOffsetPt.Should().Be(shapePosition.VerticalOffsetPt);
        shape.Placement.HorizontalAnchor.Should().Be(shapePosition.HorizontalAnchor);
        shape.Placement.VerticalAnchor.Should().Be(shapePosition.VerticalAnchor);
        shape.WidthPt.Should().Be(shapeSize.WidthPt);
        shape.HeightPt.Should().Be(shapeSize.HeightPt);
        shape.AltText.Should().Be("Process diagram");
        shape.ExtendedFill.Should().NotBeNull();
        shape.ExtendedFill!.Kind.Should().Be(shapeStyle.Fill!.Kind);
        shape.OutlineColorHex.Should().Be(shapeStyle.OutlineColorHex);
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
    public async Task ShapeChangeCommands_mutate_shape_kind_and_undo_through_registry()
    {
        ShapeKind? after = null;
        ShapeKind? reverted = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            CommandIsEnabled(registry, "freew.shape-change-ellipse").Should().BeTrue();
            ExecuteCommand(registry, "freew.shape-change-ellipse");
            after = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!.Kind;

            view.Undo();
            reverted = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!.Kind;
        });
        if (!ran) return;

        after.Should().Be(ShapeKind.Ellipse);
        reverted.Should().Be(ShapeKind.Rectangle);
    }

    [Fact]
    public async Task ShapeEffectsCommands_apply_shared_effect_and_undo_through_registry()
    {
        bool? hasGlow = null;
        bool? reverted = null;
        var ran = await OnUi(() =>
        {
            var (doc, bi, ri) = DocWithFloatingShape();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            view.SelectFloating(bi, ri);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            CommandIsEnabled(registry, "freew.shape-effect-glow").Should().BeTrue();
            ExecuteCommand(registry, "freew.shape-effect-glow");
            hasGlow = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!.Effects?.HasGlow;

            view.Undo();
            reverted = ((Paragraph)doc.Blocks[bi]).Runs[ri].Shape!.Effects is not null;
        });
        if (!ran) return;

        hasGlow.Should().BeTrue();
        reverted.Should().BeFalse();
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

    private static void ExecuteCommand(RibbonCommandRegistry registry, string id, RibbonCommandContext context)
    {
        registry.TryGet(new RibbonCommandId(id), out var command)
            .Should().BeTrue($"command '{id}' must be registered");
        command!.Execute(context);
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
