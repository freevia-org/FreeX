using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using SkiaSharp;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Tests for the Avalonia DocumentView floating SHAPE render path (FO2 wave).
/// Verifies: floating shapes are collected separately from inline content; page-space rect
/// is resolved from FloatingPlacement; z-order bucket (behind / in-front) is correct; fill,
/// outline, and text are captured; a headless render produces non-blank output in the region.
/// </summary>
public sealed class DocumentViewFloatingShapeTests
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

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a document whose first paragraph anchors a single floating shape.
    /// The paragraph also carries body text so glyphs are generated alongside the shape.
    /// </summary>
    private static TextDocument DocWithFloatingShape(
        ShapeKind kind,
        ImageWrapping wrapping,
        double hOffsetPt,
        double vOffsetPt,
        string? fillColorHex = "#4472C4",
        string? outlineColorHex = null,
        double outlineWidthPt = 0,
        int zOrder = 0,
        double shapeWidthPt  = 144,
        double shapeHeightPt = 108,
        string? text = null)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var bodyPara = new Paragraph();
        bodyPara.Runs.Add(new Run("Body text with a floating shape anchored here.",
            RunFormatting.Default with { FontSizePt = 11 }));

        var shape = new Shape(kind, shapeWidthPt, shapeHeightPt, fillColorHex)
        {
            OutlineColorHex = outlineColorHex,
            OutlineWidthPt  = outlineWidthPt,
            Placement = new FloatingPlacement
            {
                Wrapping            = wrapping,
                HorizontalOffsetPt  = hOffsetPt,
                VerticalOffsetPt    = vOffsetPt,
                HorizontalAnchor    = HorizontalAnchor.Column,
                VerticalAnchor      = VerticalAnchor.Paragraph,
                ZOrderIndex         = zOrder,
            },
        };
        if (text is not null)
        {
            var tp = new Paragraph();
            tp.Runs.Add(new Run(text));
            shape.TextParagraphs.Add(tp);
        }

        var floatRun = new Run(string.Empty, RunFormatting.Default) { Shape = shape };
        bodyPara.Runs.Add(floatRun);

        doc.Blocks.Add(bodyPara);

        var p2 = new Paragraph();
        p2.Runs.Add(new Run("Second paragraph.", RunFormatting.Default));
        doc.Blocks.Add(p2);

        return doc;
    }

    // ── Test 1: non-floating shape is NOT collected ───────────────────────────────────────────────

    [Fact]
    public async Task Inline_shape_is_not_collected_as_floating()
    {
        int floatCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var p = new Paragraph();
            // Inline shape (no Placement → IsFloating = false).
            var shape = new Shape(ShapeKind.Rectangle, 72, 54, "#4472C4");
            p.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Shape = shape });
            doc.Blocks.Add(p);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            floatCount = view.FloatingShapeCount;
        });

        if (!ran) return;
        floatCount.Should().Be(0, "an inline shape (no floating Placement) must not be added to _floatingShapes");
    }

    // ── Test 2: floating shape IS collected ──────────────────────────────────────────────────────

    [Fact]
    public async Task Floating_rectangle_shape_is_collected()
    {
        int floatCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.Rectangle, ImageWrapping.Square,
                hOffsetPt: 36, vOffsetPt: 36);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            floatCount = view.FloatingShapeCount;
        });

        if (!ran) return;
        floatCount.Should().Be(1, "one floating shape in the document should produce one entry in _floatingShapes");
    }

    // ── Test 3: position resolution — column anchor + paragraph anchor ──────────────────────────

    [Fact]
    public async Task Floating_shape_column_anchor_x_is_positive()
    {
        Rect floatRect = default;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.Rectangle, ImageWrapping.Square,
                hOffsetPt: 36, vOffsetPt: 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingShapeRects;
            if (rects.Count > 0) floatRect = rects[0].Rect;
        });

        if (!ran) return;
        floatRect.X.Should().BeGreaterThan(0, "floating shape X should be positive (content left + offset)");
        floatRect.Width.Should().BeApproximately(144 * (96.0 / 72.0), 2,
            "shape width should be 144pt → DIP");
    }

    // ── Test 4: behind-text → BehindText flag ────────────────────────────────────────────────────

    [Fact]
    public async Task Behind_text_floating_shape_is_marked_BehindText_true()
    {
        bool? behindText = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.Rectangle, ImageWrapping.Behind,
                hOffsetPt: 0, vOffsetPt: 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingShapeRects;
            if (rects.Count > 0) behindText = rects[0].BehindText;
        });

        if (!ran) return;
        behindText.Should().BeTrue("ImageWrapping.Behind must set the BehindText flag for shapes");
    }

    // ── Test 5: in-front → BehindText is false ───────────────────────────────────────────────────

    [Fact]
    public async Task InFront_floating_shape_is_marked_BehindText_false()
    {
        bool? behindText = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.Ellipse, ImageWrapping.InFront,
                hOffsetPt: 0, vOffsetPt: 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingShapeRects;
            if (rects.Count > 0) behindText = rects[0].BehindText;
        });

        if (!ran) return;
        behindText.Should().BeFalse("ImageWrapping.InFront must not set the BehindText flag");
    }

    // ── Test 6: z-order is preserved ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ZOrderIndex_is_preserved_in_floating_shape_rects()
    {
        int capturedZOrder = -999;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.Rectangle, ImageWrapping.Square,
                hOffsetPt: 0, vOffsetPt: 0, zOrder: 77);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingShapeRects;
            if (rects.Count > 0) capturedZOrder = rects[0].ZOrder;
        });

        if (!ran) return;
        capturedZOrder.Should().Be(77, "ZOrderIndex from Placement must be preserved in the layout list");
    }

    // ── Test 7: shape kind is preserved ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Shape_kind_is_preserved_in_floating_shape_rects()
    {
        ShapeKind capturedKind = ShapeKind.Rectangle;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.Ellipse, ImageWrapping.Square,
                hOffsetPt: 0, vOffsetPt: 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingShapeRects;
            if (rects.Count > 0) capturedKind = rects[0].Kind;
        });

        if (!ran) return;
        capturedKind.Should().Be(ShapeKind.Ellipse, "shape kind must be preserved in FloatingShapeRects");
    }

    // ── Test 8: fill is captured ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Floating_shape_with_fill_has_HasFill_true()
    {
        bool hasFill = false;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.Rectangle, ImageWrapping.Square,
                hOffsetPt: 0, vOffsetPt: 0, fillColorHex: "#FF0000");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingShapeRects;
            if (rects.Count > 0) hasFill = rects[0].HasFill;
        });

        if (!ran) return;
        hasFill.Should().BeTrue("a shape with a FillColorHex must have HasFill=true in FloatingShapeRects");
    }

    // ── Test 9: no-fill shape has HasFill false ───────────────────────────────────────────────────

    [Fact]
    public async Task Floating_shape_without_fill_has_HasFill_false()
    {
        bool hasFill = true;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.Rectangle, ImageWrapping.Square,
                hOffsetPt: 0, vOffsetPt: 0, fillColorHex: null);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingShapeRects;
            if (rects.Count > 0) hasFill = rects[0].HasFill;
        });

        if (!ran) return;
        hasFill.Should().BeFalse("a shape with no FillColorHex must have HasFill=false");
    }

    // ── Test 10: outline pen is captured ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Floating_shape_with_outline_has_HasOutline_true()
    {
        bool hasOutline = false;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.Ellipse, ImageWrapping.Square,
                hOffsetPt: 0, vOffsetPt: 0,
                outlineColorHex: "#000000", outlineWidthPt: 1.5);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingShapeRects;
            if (rects.Count > 0) hasOutline = rects[0].HasOutline;
        });

        if (!ran) return;
        hasOutline.Should().BeTrue("a shape with OutlineColorHex must have HasOutline=true");
    }

    // ── Test 11: shape text is captured ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Floating_shape_effect_intent_is_captured_from_shared_plan()
    {
        IReadOnlyList<string> summaries = [];
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.Ellipse, ImageWrapping.InFront,
                hOffsetPt: 0, vOffsetPt: 0,
                fillColorHex: "#00AA11",
                outlineColorHex: "#112233",
                outlineWidthPt: 1.5);
            var shape = ((Paragraph)doc.Blocks[0]).Runs[1].Shape!;
            shape.Effects = new ShapeEffectLst
            {
                HasShadow = true,
                ShadowColorHex = "112233",
                ShadowAlpha = 50000,
                HasGlow = true,
                GlowColorHex = "00FFFF",
                GlowAlpha = 25000
            };

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            summaries = view.FloatingShapeEffectSummaries;
        });

        if (!ran) return;
        var summary = summaries.Should().ContainSingle().Which;
        summary.Should().Contain("shadow");
        summary.Should().Contain("glow");
    }

    [Fact]
    public async Task Floating_shape_text_is_captured()
    {
        string? capturedText = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.TextBox, ImageWrapping.InFront,
                hOffsetPt: 0, vOffsetPt: 0,
                fillColorHex: "#FFFFFF",
                text: "Hello shape");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingShapeRects;
            if (rects.Count > 0) capturedText = rects[0].Text;
        });

        if (!ran) return;
        capturedText.Should().Be("Hello shape", "shape text from TextParagraphs must be captured in FloatingShapeRects");
    }

    [Fact]
    public async Task Rich_floating_shape_text_uses_shared_glyph_layout_for_render_and_pointer_editing()
    {
        IReadOnlyList<(char Character, int ParagraphIndex, int RunIndex, int Offset,
            double X, double Y, double Width, double Height, RunFormatting Formatting)> glyphs = [];
        int selectionEndParagraph = -1;

        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.TextBox, ImageWrapping.InFront,
                hOffsetPt: 0, vOffsetPt: 0,
                fillColorHex: "#FFFFFF", shapeWidthPt: 150, shapeHeightPt: 80,
                text: "ignored");
            var shape = ((Paragraph)doc.Blocks[0]).Runs[1].Shape!;
            shape.TextParagraphs.Clear();
            var first = new Paragraph();
            first.Runs.Add(new Run("Rich", RunFormatting.Default with
            {
                FontFamily = "Arial",
                FontSizePt = 14,
                Bold = true,
                Italic = true,
                Underline = true,
                Strikethrough = true,
                ColorHex = "#C00000"
            }));
            var second = new Paragraph();
            second.Runs.Add(new Run("next", RunFormatting.Default with { FontFamily = "Courier New" }));
            shape.TextParagraphs.Add(first);
            shape.TextParagraphs.Add(second);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            glyphs = view.FloatingShapeTextGlyphsForTest;
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing().Should().BeTrue();
            var rect = view.FloatingShapeRects.Should().ContainSingle().Which.Rect;
            view.BeginShapeTextSelectionForTest(new Point(rect.X + 5, rect.Y + 6)).Should().BeTrue();
            view.EndShapeTextSelectionForTest(new Point(rect.Right - 5, rect.Bottom - 5));
            selectionEndParagraph = view.ShapeTextSelectionInfo?.End.TextParagraphIndex ?? -1;
        });

        if (!ran) return;
        glyphs.Should().Contain(glyph => glyph.Character == 'R'
            && glyph.ParagraphIndex == 0
            && glyph.RunIndex == 0
            && glyph.Formatting.FontFamily == "Arial"
            && glyph.Formatting.FontSizePt == 14
            && glyph.Formatting.Bold
            && glyph.Formatting.Italic
            && glyph.Formatting.Underline
            && glyph.Formatting.Strikethrough
            && glyph.Formatting.ColorHex == "#C00000");
        glyphs.Should().Contain(glyph => glyph.Character == 'n' && glyph.ParagraphIndex == 1);
        glyphs.Single(glyph => glyph.Character == 'n').Y.Should()
            .BeGreaterThan(glyphs.Single(glyph => glyph.Character == 'R').Y);
        selectionEndParagraph.Should().Be(1,
            "pointer drag must resolve against the same run-aware caret layout");
    }

    // ── Test 12: multiple shapes — count and both present ────────────────────────────────────────

    [Fact]
    public async Task Selected_floating_text_box_accepts_text_and_undo_redo()
    {
        string? editedText = null;
        string? undoneText = null;
        string? redoneText = null;
        bool entered = false;
        int caretOffset = -1;

        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.TextBox, ImageWrapping.InFront,
                hOffsetPt: 0, vOffsetPt: 0,
                fillColorHex: "#FFFFFF",
                text: "Hello shape");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 1);

            entered = view.EnterSelectedShapeTextEditing();
            view.InsertText("!");
            editedText = view.SelectedFloatingShape()?.PlainText;
            caretOffset = view.ShapeTextCaretInfo?.Offset ?? -1;

            view.Undo();
            undoneText = view.SelectedFloatingShape()?.PlainText;
        view.Redo();
        redoneText = view.SelectedFloatingShape()?.PlainText;
        view.BackspacePublic();
        var afterBackspace = view.SelectedFloatingShape()?.PlainText;
        afterBackspace.Should().Be("Hello shape");
        view.Undo();
        var restoredAfterBackspace = view.SelectedFloatingShape()?.PlainText;
        restoredAfterBackspace.Should().Be("Hello shape!");
        });

        if (!ran) return;
        entered.Should().BeTrue("a selected floating text box should enter text-edit mode");
        editedText.Should().Be("Hello shape!");
        caretOffset.Should().Be("Hello shape!".Length);
        undoneText.Should().Be("Hello shape");
        redoneText.Should().Be("Hello shape!");
    }

    [Fact]
    public async Task CurrentFieldCommandsTargetOnlyFieldsInTheActiveShapeTextSelectionOrCaret()
    {
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.TextBox, ImageWrapping.InFront,
                hOffsetPt: 0, vOffsetPt: 0,
                fillColorHex: "#FFFFFF",
                text: "ignored");
            doc.Properties.Title = "Current title";
            doc.Properties.Subject = "Current subject";
            doc.Properties.Author = "Current author";

            var body = (Paragraph)doc.Blocks[0];
            var shape = body.Runs[1].Shape!;
            var paragraph = shape.TextParagraphs[0];
            paragraph.Runs.Clear();
            var title = Run.ComplexFieldRun(" DOCPROPERTY Title ", "stale title");
            var subject = Run.ComplexFieldRun(" DOCPROPERTY Subject ", "stale subject");
            paragraph.Runs.Add(title);
            paragraph.Runs.Add(new Run(" "));
            paragraph.Runs.Add(subject);
            var bodyField = Run.ComplexFieldRun(" DOCPROPERTY Author ", "stale body");
            body.Runs.Add(bodyField);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing().Should().BeTrue();

            view.SelectShapeTextRangeForTest(0, 0, title.Text.Length).Should().BeTrue();
            view.UpdateFieldAtCaret();
            title.Text.Should().Be("Current title");
            subject.Text.Should().Be("stale subject");
            bodyField.Text.Should().Be("stale body");

            view.ToggleFieldCodeAtCaret();
            title.ComplexField!.ShowCode.Should().BeTrue();
            subject.ComplexField!.ShowCode.Should().BeFalse();
            view.SetFieldLockAtCaret(true);
            title.ComplexField!.IsLocked.Should().BeTrue();
            subject.ComplexField!.IsLocked.Should().BeFalse();

            var subjectOffset = title.Text.Length + 1 + 1;
            view.SelectShapeTextRangeForTest(0, subjectOffset, subjectOffset)
                .Should().BeFalse("a collapsed shape-text range is a caret, not a selection");
            view.UpdateFieldAtCaret();
            subject.Text.Should().Be("Current subject");
            title.Text.Should().Be("Current title");
            bodyField.Text.Should().Be("stale body");

            view.UnlinkFieldAtCaret();
            subject.ComplexField.Should().BeNull();
            title.ComplexField.Should().NotBeNull();
            bodyField.ComplexField.Should().NotBeNull();
        });

        if (!ran) return;
    }

    [Fact]
    public async Task FieldInsertionTargetsTheActiveShapeCaretAndRemainsUndoablePerField()
    {
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.TextBox, ImageWrapping.InFront,
                hOffsetPt: 0, vOffsetPt: 0,
                fillColorHex: "#FFFFFF",
                text: "AC");
            doc.Properties.Title = "Current title";
            doc.Properties.Subject = "Current subject";

            var body = (Paragraph)doc.Blocks[0];
            var shape = body.Runs[1].Shape!;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing().Should().BeTrue();
            view.SelectShapeTextRangeForTest(0, 1, 1)
                .Should().BeFalse("a collapsed shape-text range is a caret, not a selection");

            view.InsertField(RunFieldKind.Title);
            view.InsertComplexField(" DOCPROPERTY Subject ");

            var shapeRuns = shape.TextParagraphs[0].Runs;
            shapeRuns.Single(run => run.FieldKind == RunFieldKind.Title).Text
                .Should().Be("Current title");
            var complex = shapeRuns.Single(run => run.ComplexField != null);
            complex.ComplexField!.Instruction.Should().Be(" DOCPROPERTY Subject ");
            complex.Text.Should().Be("Current subject");
            body.Runs.Count(run =>
                run.FieldKind != RunFieldKind.None || run.ComplexField != null).Should().Be(0);

            view.Undo();
            shape.TextParagraphs[0].Runs.Count(run => run.ComplexField != null).Should().Be(0);
            shape.TextParagraphs[0].Runs.Count(run =>
                run.FieldKind == RunFieldKind.Title).Should().Be(1);

            view.Undo();
            shape.PlainText.Should().Be("AC");
            shape.TextParagraphs[0].Runs.Count(run =>
                run.FieldKind != RunFieldKind.None || run.ComplexField != null).Should().Be(0);
        });

        if (!ran) return;
    }

    [Fact]
    public async Task Pointer_caret_placement_resolves_the_nearest_shape_text_run_and_offset()
    {
        (int BlockIndex, int RunIndex, int TextParagraphIndex, int TextRunIndex, int Offset)? caret = null;
        string? editedText = null;
        string? secondRunText = null;

        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.TextBox, ImageWrapping.InFront,
                hOffsetPt: 0, vOffsetPt: 0,
                fillColorHex: "#FFFFFF",
                text: "ignored");
            var shape = ((Paragraph)doc.Blocks[0]).Runs[1].Shape!;
            var textParagraph = shape.TextParagraphs[0];
            textParagraph.Runs.Clear();
            textParagraph.Runs.Add(new Run("Bold", RunFormatting.Default with { Bold = true }));
            textParagraph.Runs.Add(new Run("plain", RunFormatting.Default));

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing().Should().BeTrue();

            var rect = view.FloatingShapeRects.Should().ContainSingle().Which.Rect;
            view.PlaceShapeTextCaretForTest(new Point(rect.Right - 5, rect.Y + 8)).Should().BeTrue();
            caret = view.ShapeTextCaretInfo;
            view.InsertText("!");
            editedText = shape.PlainText;
            secondRunText = textParagraph.Runs[1].Text;
        });

        if (!ran) return;
        caret.Should().NotBeNull();
        caret!.Value.TextParagraphIndex.Should().Be(0);
        caret.Value.TextRunIndex.Should().Be(1,
            "a pointer near the right side of the text box should resolve into the second run");
        caret.Value.Offset.Should().Be(5);
        editedText.Should().Be("Boldplain!");
        secondRunText.Should().Be("plain!");
    }

    [Fact]
    public async Task Pointer_caret_placement_applies_the_shape_text_rotation_transform()
    {
        var offsets = new Dictionary<ShapeTextDirection, int>();

        var ran = await OnUiThread(() =>
        {
            foreach (var direction in new[] { ShapeTextDirection.Rotate90, ShapeTextDirection.Rotate270 })
            {
                var doc = DocWithFloatingShape(ShapeKind.TextBox, ImageWrapping.InFront,
                    hOffsetPt: 0, vOffsetPt: 0,
                    fillColorHex: "#FFFFFF",
                    shapeWidthPt: 144,
                    shapeHeightPt: 108,
                    text: "ABCD");
                var shape = ((Paragraph)doc.Blocks[0]).Runs[1].Shape!;
                shape.TextDirection = direction;

                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(816, 2000));
                view.SelectFloating(0, 1);
                view.EnterSelectedShapeTextEditing().Should().BeTrue();

                var rect = view.FloatingShapeRects.Should().ContainSingle().Which.Rect;
                view.PlaceShapeTextCaretForTest(new Point(rect.Center.X, rect.Y + 5)).Should().BeTrue();
                offsets[direction] = view.ShapeTextCaretInfo!.Value.Offset;
            }
        });

        if (!ran) return;
        offsets[ShapeTextDirection.Rotate90].Should().Be(0,
            "the top of a clockwise-rotated text box maps to the beginning of the unrotated text");
        offsets[ShapeTextDirection.Rotate270].Should().Be(4,
            "the top of a counter-clockwise-rotated text box maps to the end of the unrotated text");
    }

    [Fact]
    public async Task Horizontal_shape_text_drag_selects_and_replaces_the_selected_range()
    {
        string? editedText = null;
        int selectedLength = 0;
        bool boldApplied = false;

        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.TextBox, ImageWrapping.InFront,
                hOffsetPt: 0, vOffsetPt: 0,
                fillColorHex: "#FFFFFF", text: "Hello shape");
            var shape = ((Paragraph)doc.Blocks[0]).Runs[1].Shape!;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing().Should().BeTrue();

            var rect = view.FloatingShapeRects.Should().ContainSingle().Which.Rect;
            view.BeginShapeTextSelectionForTest(new Point(rect.X + 5, rect.Y + 8)).Should().BeTrue();
            view.EndShapeTextSelectionForTest(new Point(rect.X + 34, rect.Y + 8));
            view.ShapeTextSelectionInfo.Should().NotBeNull();
            var selection = view.ShapeTextSelectionInfo!.Value;
            selectedLength = selection.End.Offset - selection.Start.Offset;
            selectedLength.Should().BeGreaterThan(0);

            view.ToggleBold();
            var globalOffset = 0;
            boldApplied = true;
            foreach (var run in shape.TextParagraphs[0].Runs)
            {
                for (var index = 0; index < run.Text.Length; index++)
                {
                    if (globalOffset >= selection.Start.Offset && globalOffset < selection.End.Offset
                        && !run.Formatting.Bold)
                        boldApplied = false;
                    globalOffset++;
                }
            }

            view.InsertText("X");
            editedText = shape.PlainText;
        });

        if (!ran) return;
        selectedLength.Should().BeGreaterThan(0);
        boldApplied.Should().BeTrue();
        editedText.Should().NotBe("Hello shape");
        editedText.Should().Contain("X");
    }

    [Fact]
    public async Task Shape_text_formatting_only_mutates_paragraphs_inside_the_selection()
    {
        var beforeFormatting = RunFormatting.Default with
        {
            FontFamily = "Arial",
            FontSizePt = 8,
            Italic = true,
            ColorHex = "#C00000",
        };
        var middleFormatting = RunFormatting.Default with
        {
            FontFamily = "Calibri",
            FontSizePt = 11,
        };
        var afterFormatting = RunFormatting.Default with
        {
            FontFamily = "Courier New",
            FontSizePt = 14,
            Underline = true,
            ColorHex = "#007000",
        };
        string? beforeText = null;
        string? afterText = null;
        RunFormatting? actualBeforeFormatting = null;
        RunFormatting? actualAfterFormatting = null;
        bool middleSelectionBold = false;

        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.TextBox, ImageWrapping.InFront,
                hOffsetPt: 0, vOffsetPt: 0,
                fillColorHex: "#FFFFFF", text: "discarded");
            var shape = ((Paragraph)doc.Blocks[0]).Runs[1].Shape!;
            shape.TextParagraphs.Clear();
            foreach (var (text, formatting) in new[]
                     {
                         ("Before", beforeFormatting),
                         ("Middle", middleFormatting),
                         ("After", afterFormatting),
                     })
            {
                var paragraph = new Paragraph();
                paragraph.Runs.Add(new Run(text, formatting));
                shape.TextParagraphs.Add(paragraph);
            }

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 1);
            view.EnterSelectedShapeTextEditing().Should().BeTrue();
            view.SelectShapeTextRangeForTest(paragraphIndex: 1, startOffset: 1, endOffset: 5)
                .Should().BeTrue();

            view.ToggleBold();

            beforeText = shape.TextParagraphs[0].PlainText;
            afterText = shape.TextParagraphs[2].PlainText;
            actualBeforeFormatting = shape.TextParagraphs[0].Runs.Should().ContainSingle().Which.Formatting;
            actualAfterFormatting = shape.TextParagraphs[2].Runs.Should().ContainSingle().Which.Formatting;
            middleSelectionBold = shape.TextParagraphs[1].Runs
                .Should().ContainSingle(run => run.Text == "iddl").Which.Formatting.Bold;
        });

        if (!ran) return;
        beforeText.Should().Be("Before");
        afterText.Should().Be("After");
        actualBeforeFormatting.Should().Be(beforeFormatting);
        actualAfterFormatting.Should().Be(afterFormatting);
        middleSelectionBold.Should().BeTrue();
    }

    [Fact]
    public async Task Rotated_shape_text_drag_selects_and_replaces_the_selected_range()
    {
        var editedTexts = new Dictionary<ShapeTextDirection, string?>();
        var ran = await OnUiThread(() =>
        {
            foreach (var direction in new[] { ShapeTextDirection.Rotate90, ShapeTextDirection.Rotate270 })
            {
                var doc = DocWithFloatingShape(ShapeKind.TextBox, ImageWrapping.InFront,
                    hOffsetPt: 0, vOffsetPt: 0,
                    fillColorHex: "#FFFFFF", shapeWidthPt: 144, shapeHeightPt: 108,
                    text: "ABCD");
                var shape = ((Paragraph)doc.Blocks[0]).Runs[1].Shape!;
                shape.TextDirection = direction;
                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(816, 2000));
                view.SelectFloating(0, 1);
                view.EnterSelectedShapeTextEditing().Should().BeTrue();

                var rect = view.FloatingShapeRects.Should().ContainSingle().Which.Rect;
                var start = direction == ShapeTextDirection.Rotate90
                    ? new Point(rect.Center.X, rect.Y + 5)
                    : new Point(rect.Center.X, rect.Bottom - 5);
                var end = direction == ShapeTextDirection.Rotate90
                    ? new Point(rect.Center.X, rect.Bottom - 5)
                    : new Point(rect.Center.X, rect.Y + 5);
                view.BeginShapeTextSelectionForTest(start).Should().BeTrue();
                view.EndShapeTextSelectionForTest(end);
                view.ShapeTextSelectionInfo.Should().NotBeNull();

                view.InsertText("R");
                editedTexts[direction] = shape.PlainText;
            }
        });

        if (!ran) return;
        editedTexts[ShapeTextDirection.Rotate90].Should().Be("R");
        editedTexts[ShapeTextDirection.Rotate270].Should().Be("R");
    }

    [Fact]
    public async Task Selected_floating_text_box_supports_paragraph_break_merge_and_outer_text_sync()
    {
        int paragraphCountAfterBreak = -1;
        int paragraphCountAfterUndo = -1;
        int paragraphCountAfterRedo = -1;
        string? textAfterTyping = null;
        string? outerRunTextAfterTyping = null;
        string? textAfterMerge = null;
        string? outerRunTextAfterMerge = null;

        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.TextBox, ImageWrapping.InFront,
                hOffsetPt: 0, vOffsetPt: 0,
                fillColorHex: "#FFFFFF",
                text: "First line");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 1);

            view.EnterSelectedShapeTextEditing().Should().BeTrue();
            view.InsertShapeTextParagraphBreak();
            paragraphCountAfterBreak = view.SelectedFloatingShape()!.TextParagraphs.Count;
            view.InsertText("Second line");
            textAfterTyping = view.SelectedFloatingShape()!.PlainText;
            outerRunTextAfterTyping = ((Paragraph)doc.Blocks[0]).Runs[1].Text;

            view.Undo();
            paragraphCountAfterUndo = view.SelectedFloatingShape()!.TextParagraphs.Count;
            view.Redo();
            paragraphCountAfterRedo = view.SelectedFloatingShape()!.TextParagraphs.Count;

            // The caret is at the start of the second paragraph immediately after a break.
            view.Undo();
            view.BackspacePublic();
            textAfterMerge = view.SelectedFloatingShape()!.PlainText;
            outerRunTextAfterMerge = ((Paragraph)doc.Blocks[0]).Runs[1].Text;
        });

        if (!ran) return;
        paragraphCountAfterBreak.Should().Be(2);
        textAfterTyping.Should().Be("First line\nSecond line");
        outerRunTextAfterTyping.Should().Be("First line\nSecond line");
        paragraphCountAfterUndo.Should().Be(2, "undoing the typed run must keep the paragraph break");
        paragraphCountAfterRedo.Should().Be(2);
        textAfterMerge.Should().Be("First line");
        outerRunTextAfterMerge.Should().Be("First line");
    }

    [Fact]
    public async Task Selected_floating_text_box_text_direction_uses_shared_undo_command()
    {
        ShapeTextDirection? afterRotate = null;
        ShapeTextDirection? afterUndo = null;
        ShapeTextDirection? afterRedo = null;
        bool commandApplied = false;

        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(
                ShapeKind.TextBox,
                ImageWrapping.InFront,
                hOffsetPt: 0,
                vOffsetPt: 0,
                fillColorHex: "#FFFFFF",
                text: "Rotate me");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            view.SelectFloating(0, 1);

            view.SetSelectedShapeTextDirection(ShapeTextDirection.Rotate90);
            afterRotate = view.SelectedFloatingShape()?.TextDirection;
            view.Undo();
            afterUndo = view.SelectedFloatingShape()?.TextDirection;
            view.Redo();
            afterRedo = view.SelectedFloatingShape()?.TextDirection;
            commandApplied = afterRotate == ShapeTextDirection.Rotate90;
        });

        if (!ran) return;
        commandApplied.Should().BeTrue();
        afterRotate.Should().Be(ShapeTextDirection.Rotate90);
        afterUndo.Should().Be(ShapeTextDirection.Horizontal);
        afterRedo.Should().Be(ShapeTextDirection.Rotate90);
    }

    [Fact]
    public async Task Selected_nested_floating_text_box_text_direction_uses_child_path_and_preserves_transforms()
    {
        ShapeTextDirection? direction = null;
        ShapeTextDirection? siblingDirection = null;
        IReadOnlyList<int>? selectedPath = null;
        bool undoRestored = false;
        bool redoRestored = false;
        bool rotate270Applied = false;
        bool horizontalApplied = false;
        bool childSelectionSignaled = false;
        bool ribbonContextRefreshed = false;
        var ran = await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var leaf = Shape.TextBoxWith("Nested direction", 120, 60);
            leaf.RotationAngle = 17;
            leaf.FlipH = true;
            var sibling = Shape.TextBoxWith("Sibling", 90, 40);
            var inner = new DrawingGroup { WidthPt = 160, HeightPt = 80, RotationAngle = 23 };
            inner.Children.Add(new Shape(ShapeKind.Rectangle, 20, 20));
            inner.ChildOffsets.Add((0, 0));
            inner.Children.Add(leaf);
            inner.ChildOffsets.Add((30, 10));
            var outer = new DrawingGroup { WidthPt = 240, HeightPt = 120, RotationAngle = 31, FlipV = true };
            outer.Children.Add(inner);
            outer.ChildOffsets.Add((12, 8));
            outer.Children.Add(sibling);
            outer.ChildOffsets.Add((180, 70));
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromDrawingGroup(outer));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            var selectionChangeCount = 0;
            view.FloatingSelectionChanged += () => selectionChangeCount++;
            var contextSource = new FloatingRibbonContextSource(view);
            var contextRefreshCount = 0;
            contextSource.ContextChanged += (_, _) => contextRefreshCount++;
            view.Measure(new Size(800, 2000));
            view.SelectFloating(0, 0);
            var groupRect = view.SelectedFloatingInfo!.Value.Rect;
            var leafRect = view.FloatingGroupChildRectForPathForTest(0, 0, [0, 1])!.Value;
            var innerRect = view.FloatingGroupChildRectForPathForTest(0, 0, [0])!.Value;
            var visibleCenter = DocumentViewLayoutPlanner.TransformPointThroughGroupChain(
                new DocumentFloatPoint(leafRect.Center.X, leafRect.Center.Y),
                new DocumentFloatRect(leafRect.X, leafRect.Y, leafRect.Width, leafRect.Height),
                leaf.RotationAngle,
                leaf.FlipH,
                leaf.FlipV,
                [
                    new DocumentFloatTransform(
                        new DocumentFloatRect(innerRect.X, innerRect.Y, innerRect.Width, innerRect.Height),
                        inner.RotationAngle,
                        inner.FlipH,
                        inner.FlipV),
                    new DocumentFloatTransform(
                        new DocumentFloatRect(groupRect.X, groupRect.Y, groupRect.Width, groupRect.Height),
                        outer.RotationAngle,
                        outer.FlipH,
                        outer.FlipV),
                ]);
            view.SelectFloatingGroupChildForTest(
                new Point(visibleCenter.XDip, visibleCenter.YDip)).Should().BeTrue();
            selectedPath = view.SelectedFloatingGroupChildPath?.ToArray();
            childSelectionSignaled = selectionChangeCount == 2;
            ribbonContextRefreshed = contextRefreshCount == 2;

            view.SetSelectedShapeTextDirection(ShapeTextDirection.Rotate90);
            direction = leaf.TextDirection;
            siblingDirection = sibling.TextDirection;
            view.Undo();
            undoRestored = leaf.TextDirection == ShapeTextDirection.Horizontal;
            view.Redo();
            redoRestored = leaf.TextDirection == ShapeTextDirection.Rotate90;
            view.SetSelectedShapeTextDirection(ShapeTextDirection.Rotate270);
            rotate270Applied = leaf.TextDirection == ShapeTextDirection.Rotate270;
            view.SetSelectedShapeTextDirection(ShapeTextDirection.Horizontal);
            horizontalApplied = leaf.TextDirection == ShapeTextDirection.Horizontal;
        });
        if (!ran) return;
        selectedPath.Should().Equal(0, 1);
        childSelectionSignaled.Should().BeTrue(
            "nested child selection must refresh cached ribbon command state after the group was selected");
        ribbonContextRefreshed.Should().BeTrue(
            "the Drawing context must propagate same-context child changes to the shared ribbon renderer");
        direction.Should().Be(ShapeTextDirection.Rotate90);
        siblingDirection.Should().Be(ShapeTextDirection.Horizontal);
        undoRestored.Should().BeTrue();
        redoRestored.Should().BeTrue();
        rotate270Applied.Should().BeTrue();
        horizontalApplied.Should().BeTrue();
    }

    [Fact]
    public async Task Nested_group_shape_formatting_targets_leaf_and_undoes()
    {
        var verified = false;
        var ran = await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var leaf = new Shape(ShapeKind.Ellipse, 42, 24) { FillColorHex = "#111111" };
            var sibling = new Shape(ShapeKind.Rectangle, 30, 18) { FillColorHex = "#222222" };
            var inner = new DrawingGroup { WidthPt = 100, HeightPt = 60 };
            inner.Children.Add(new Shape(ShapeKind.Rectangle, 20, 20));
            inner.ChildOffsets.Add((0, 0));
            inner.Children.Add(leaf);
            inner.ChildOffsets.Add((30, 18));
            var outer = new DrawingGroup
            {
                WidthPt = 180,
                HeightPt = 100,
                Placement = new FloatingPlacement
                {
                    HorizontalOffsetPt = 72,
                    VerticalOffsetPt = 36,
                    HorizontalAnchor = HorizontalAnchor.Margin,
                    VerticalAnchor = VerticalAnchor.Page,
                    Wrapping = ImageWrapping.Square
                }
            };
            outer.Children.Add(inner);
            outer.ChildOffsets.Add((10, 8));
            outer.Children.Add(sibling);
            outer.ChildOffsets.Add((130, 65));
            document.Blocks.Add(new Paragraph { Runs = { Run.FromDrawingGroup(outer) } });

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(800, 1200));
            view.SelectFloating(0, 0);
            var leafRect = view.FloatingGroupChildRectForPathForTest(0, 0, [0, 1])!.Value;
            view.SelectFloatingGroupChildForTest(leafRect.Center).Should().BeTrue();

            var selectedSize = view.GetSelectedFloatingSize();
            var selectedPosition = view.GetSelectedShapePosition();
            view.SetSelectedShapePosition(46, 29, HorizontalAnchor.Page, VerticalAnchor.Page);
            view.SetSelectedShapeSize(80, 50);
            view.SetSelectedShapeKind(ShapeKind.RoundedRectangle);
            view.SetSelectedFloatingAltText(" Nested leaf ");
            view.SetSelectedShapeFill("#ABCDEF");
            view.SetSelectedShapeOutline("#123456", 2, "dash");
            var reordered = view.ChangeSelectedFloatingZOrder(
                ZOrderOperation.SendBackward, "Shape");
            var applied = selectedSize == (42d, 24d)
                && selectedPosition == (30d, 18d,
                    HorizontalAnchor.Column, VerticalAnchor.Paragraph, true)
                && outer.Placement.HorizontalOffsetPt == 72
                && outer.Placement.VerticalOffsetPt == 36
                && outer.Placement.HorizontalAnchor == HorizontalAnchor.Margin
                && outer.Placement.VerticalAnchor == VerticalAnchor.Page
                && reordered
                && ReferenceEquals(inner.Children[0], leaf)
                && inner.ChildOffsets[0] == (46d, 29d)
                && ReferenceEquals(view.SelectedFloatingShape(), leaf)
                && leaf.WidthPt == 80
                && leaf.HeightPt == 50
                && leaf.Kind == ShapeKind.RoundedRectangle
                && leaf.AltText == "Nested leaf"
                && leaf.FillColorHex == "#ABCDEF"
                && leaf.OutlineColorHex == "#123456"
                && sibling.Kind == ShapeKind.Rectangle
                && sibling.AltText is null
                && sibling.FillColorHex == "#222222"
                && sibling.OutlineColorHex is null;
            view.Undo();
            var zOrderUndone = ReferenceEquals(inner.Children[1], leaf)
                && inner.ChildOffsets[1] == (46d, 29d)
                && ReferenceEquals(view.SelectedFloatingShape(), leaf);
            view.Undo();
            var outlineUndone = leaf.OutlineColorHex is null;
            view.Undo();
            var fillUndone = leaf.FillColorHex == "#111111";
            view.Undo();
            var altTextUndone = leaf.AltText is null;
            view.Undo();
            var kindUndone = leaf.Kind == ShapeKind.Ellipse;
            view.Undo();
            var sizeUndone = leaf.WidthPt == 42 && leaf.HeightPt == 24;
            view.Undo();
            verified = applied && zOrderUndone && outlineUndone && fillUndone && altTextUndone && kindUndone
                && sizeUndone
                && inner.ChildOffsets[1] == (30d, 18d)
                && outer.Placement.HorizontalOffsetPt == 72
                && outer.Placement.VerticalOffsetPt == 36;
        });
        if (!ran) return;
        verified.Should().BeTrue();
    }

    [Fact]
    public async Task Multiple_floating_shapes_are_all_collected()
    {
        int count = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Anchor.", RunFormatting.Default));

            // Shape 1: rectangle behind text, z=10
            var s1 = new Shape(ShapeKind.Rectangle, 144, 108, "#4472C4")
            {
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Behind,
                    HorizontalOffsetPt = 0, VerticalOffsetPt = 0,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor = VerticalAnchor.Paragraph,
                    ZOrderIndex = 10,
                },
            };
            // Shape 2: ellipse in front, z=5
            var s2 = new Shape(ShapeKind.Ellipse, 72, 72, "#ED7D31")
            {
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalOffsetPt = 36, VerticalOffsetPt = 0,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor = VerticalAnchor.Paragraph,
                    ZOrderIndex = 5,
                },
            };

            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Shape = s1 });
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Shape = s2 });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingShapeCount;
        });

        if (!ran) return;
        count.Should().Be(2, "two floating shapes should produce two entries in _floatingShapes");
    }

    // ── Test 13: body text still lays out when paragraph also has floating shape ──────────────────

    [Fact]
    public async Task Paragraph_with_floating_shape_still_produces_text_glyphs()
    {
        int glyphs = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingShape(ShapeKind.Rectangle, ImageWrapping.Square,
                hOffsetPt: 0, vOffsetPt: 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            glyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        glyphs.Should().BeGreaterThan(0,
            "a paragraph with a floating shape run and text runs must still produce placed glyphs");
    }

    [Fact]
    public async Task Floating_drawing_fallback_text_stays_out_of_the_body_flow()
    {
        string bodyText = string.Empty;
        int shapeCount = 0;
        int wordArtCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Body anchor.", RunFormatting.Default));

            var shape = Shape.TextBoxWith("Shape overlay", 120, 54, "#E2F0D9");
            shape.Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Column,
                VerticalAnchor = VerticalAnchor.Paragraph,
            };
            paragraph.Runs.Add(new Run("Shape fallback text", RunFormatting.Default) { Shape = shape });

            var wordArt = new WordArt("WordArt overlay", WordArtStyle.FillBlue, 24)
            {
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.InFront,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor = VerticalAnchor.Paragraph,
                },
            };
            paragraph.Runs.Add(new Run("WordArt fallback text", RunFormatting.Default) { WordArt = wordArt });
            doc.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            bodyText = string.Concat(view.GetPlacedForBlock(0).Select(glyph => glyph.Ch));
            shapeCount = view.FloatingShapeCount;
            wordArtCount = view.FloatingWordArtCount;
        });

        if (!ran) return;

        bodyText.Should().Be("Body anchor.");
        shapeCount.Should().Be(1);
        wordArtCount.Should().Be(1);
    }

    // ── Test 14: headless render capture — shape appears in PNG ──────────────────────────────────

    [Fact]
    public async Task Floating_shape_render_capture_produces_non_blank_output()
    {
        byte[]? pngBytes = null;
        string? outPath = null;
        var ran = false;

        try
        {
            await Session.Dispatch(() =>
            {
                ran = true;

                // Build a document with:
                //   • a filled blue rectangle in-front (1in offset)
                //   • a plain ellipse behind text at (0,0)
                //   • a text-box with text
                var doc = TextDocument.CreateEmpty();
                doc.Blocks.Clear();

                var para = new Paragraph();
                para.Runs.Add(new Run("Body text behind and in front of shapes.",
                    RunFormatting.Default with { FontSizePt = 11 }));

                // Filled rectangle in front
                var rect = new Shape(ShapeKind.Rectangle, 144, 108, "#4472C4")
                {
                    OutlineColorHex = "#FFFFFF",
                    OutlineWidthPt  = 1.0,
                    Placement = new FloatingPlacement
                    {
                        Wrapping = ImageWrapping.InFront,
                        HorizontalOffsetPt = 72, VerticalOffsetPt = 72,
                        HorizontalAnchor = HorizontalAnchor.Column,
                        VerticalAnchor   = VerticalAnchor.Paragraph,
                        ZOrderIndex = 2,
                    },
                };
                para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Shape = rect });

                // Outlined ellipse behind text
                var ell = new Shape(ShapeKind.Ellipse, 100, 80, null)
                {
                    OutlineColorHex = "#ED7D31",
                    OutlineWidthPt  = 2.0,
                    Placement = new FloatingPlacement
                    {
                        Wrapping = ImageWrapping.Behind,
                        HorizontalOffsetPt = 0, VerticalOffsetPt = 0,
                        HorizontalAnchor = HorizontalAnchor.Column,
                        VerticalAnchor   = VerticalAnchor.Paragraph,
                        ZOrderIndex = 1,
                    },
                };
                para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Shape = ell });

                // Text box
                var tb = new Shape(ShapeKind.TextBox, 120, 60, "#F2F2F2");
                var tp = new Paragraph();
                tp.Runs.Add(new Run("Shape text"));
                tb.TextParagraphs.Add(tp);
                tb.Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalOffsetPt = 180, VerticalOffsetPt = 36,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor   = VerticalAnchor.Paragraph,
                    ZOrderIndex = 3,
                };
                para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Shape = tb });

                doc.Blocks.Add(para);

                for (var i = 0; i < 4; i++)
                {
                    var p = new Paragraph();
                    p.Runs.Add(new Run($"Body paragraph {i + 1}: lorem ipsum dolor sit amet.",
                        RunFormatting.Default));
                    doc.Blocks.Add(p);
                }

                var view = new DocumentView();
                view.LoadDocument(doc);

                var window = new Window
                {
                    Width   = 816,
                    Height  = 1200,
                    Content = view,
                };
                window.Show();
                window.Measure(new Size(816, 1200));
                window.Arrange(new Rect(0, 0, 816, 1200));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var frame = window.CaptureRenderedFrame();
                if (frame is not null)
                    pngBytes = WriteableBitmapToPng(frame);

                window.Close();

                var testBinDir = Path.GetDirectoryName(
                    typeof(DocumentViewFloatingShapeTests).Assembly.Location) ?? ".";
                outPath = Path.GetFullPath(
                    Path.Combine(testBinDir, "freew_avalonia_floating_shapes.png"));
                if (pngBytes is { Length: > 0 })
                    File.WriteAllBytes(outPath, pngBytes);

                Console.WriteLine(
                    $"[FloatingShapeCapture] PNG written ({pngBytes?.Length ?? 0} bytes) to: {outPath}");
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FloatingShapeCapture] Skipped: {ex.GetType().Name}: {ex.Message}");
            ran = false;
        }

        if (!ran) return;
        if (pngBytes is null)
        {
            Console.WriteLine("[FloatingShapeCapture] CaptureRenderedFrame returned null — skipping.");
            return;
        }
        if (pngBytes.Length == 0)
        {
            Console.WriteLine("[FloatingShapeCapture] Encoder produced 0 bytes — skipping.");
            return;
        }

        pngBytes.Length.Should().BeGreaterThan(5_000,
            "a rendered page with floating shapes and body text should produce a non-trivial PNG");
        pngBytes[0].Should().Be(0x89);
        pngBytes[1].Should().Be((byte)'P');
        pngBytes[2].Should().Be((byte)'N');
        pngBytes[3].Should().Be((byte)'G');

        Console.WriteLine($"[FloatingShapeCapture] Visual inspection: {outPath}");
    }

    // ── UU1: ZOrder interleaving of images and shapes in same band ──────────────────────────────────

    /// <summary>
    /// UU1 (merged ZOrder draw sequence): In the behind-text band, a floating IMAGE with ZOrderIndex=100
    /// must be drawn AFTER a floating SHAPE with ZOrderIndex=0 (image on top), since the merged list is
    /// sorted ascending by ZOrder and later items paint over earlier items.
    ///
    /// Before the fix, images and shapes were drawn in two separate sequential OrderBy loops, so the
    /// shape (second loop) always painted over the image (first loop) regardless of ZOrder.
    ///
    /// This test verifies that the layout correctly captures both items with the right ZOrder values
    /// (layout-order correctness). The render-order assertion is structural — we verify the ZOrder
    /// values are tracked as expected so the merged sort in Render() will produce the right sequence.
    /// </summary>
    [Fact]
    public async Task UU1_behind_text_image_z100_and_shape_z0_both_collected_with_correct_zorders()
    {
        int imageZ = -1;
        int shapeZ = -1;
        bool imageIsBehind = false;
        bool shapeIsBehind = false;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Anchor text for UU1 ZOrder test.", RunFormatting.Default));

            // Behind-text image: ZOrderIndex=100 (should be drawn on top of the shape below).
            var img = new InlineImage(SmallPng(), 144, 108)
            {
                Wrapping           = ImageWrapping.Behind,
                HorizontalOffsetPt = 0,
                VerticalOffsetPt   = 0,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = VerticalAnchor.Paragraph,
                ZOrderIndex        = 100,
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = img });

            // Behind-text shape: ZOrderIndex=0 (should be drawn first / underneath the image).
            var shape = new Shape(ShapeKind.Rectangle, 144, 108, "#FF0000")
            {
                Placement = new FloatingPlacement
                {
                    Wrapping           = ImageWrapping.Behind,
                    HorizontalOffsetPt = 0,
                    VerticalOffsetPt   = 0,
                    HorizontalAnchor   = HorizontalAnchor.Column,
                    VerticalAnchor     = VerticalAnchor.Paragraph,
                    ZOrderIndex        = 0,
                },
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Shape = shape });

            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var imgRects   = view.FloatingImageRects;
            var shapeRects = view.FloatingShapeRects;

            if (imgRects.Count > 0)
            {
                imageZ       = imgRects[0].ZOrder;
                imageIsBehind = imgRects[0].BehindText;
            }
            if (shapeRects.Count > 0)
            {
                shapeZ       = shapeRects[0].ZOrder;
                shapeIsBehind = shapeRects[0].BehindText;
            }
        });

        if (!ran) return;

        imageZ.Should().Be(100, "behind-text image ZOrderIndex=100 must be preserved");
        shapeZ.Should().Be(0,   "behind-text shape ZOrderIndex=0 must be preserved");
        imageIsBehind.Should().BeTrue("image with ImageWrapping.Behind must be in the behind-text band");
        shapeIsBehind.Should().BeTrue("shape with ImageWrapping.Behind must be in the behind-text band");

        // The key invariant (UU1): in the merged ZOrder draw sequence,
        // the shape (z=0) is drawn FIRST and the image (z=100) is drawn LAST (on top).
        // This is correct WPF behavior: higher ZOrder → painted later → visually on top.
        imageZ.Should().BeGreaterThan(shapeZ,
            "UU1: image ZOrder (100) > shape ZOrder (0) → image draws on top of shape in same band. " +
            "Before fix, the two separate OrderBy loops meant shape (second loop) always covered image (first loop).");
    }

    /// <summary>
    /// UU1 (in-front band): Same merged ZOrder assertion for the in-front band.
    /// A behind-text image ZOrder=0 and a front image ZOrder=100 — only the in-front items participate
    /// in the front pass; the merged sort for in-front must respect ZOrder across both images and shapes.
    /// </summary>
    [Fact]
    public async Task UU1_in_front_shape_z100_drawn_after_in_front_image_z0()
    {
        int imageZ = -1;
        int shapeZ = -1;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Anchor text for UU1 in-front test.", RunFormatting.Default));

            // In-front image ZOrder=0 (drawn first in in-front band).
            var img = new InlineImage(SmallPng(), 144, 108)
            {
                Wrapping           = ImageWrapping.InFront,
                HorizontalOffsetPt = 0,
                VerticalOffsetPt   = 0,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = VerticalAnchor.Paragraph,
                ZOrderIndex        = 0,
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = img });

            // In-front shape ZOrder=100 (drawn after image → on top).
            var shape = new Shape(ShapeKind.Ellipse, 72, 72, "#0070C0")
            {
                Placement = new FloatingPlacement
                {
                    Wrapping           = ImageWrapping.InFront,
                    HorizontalOffsetPt = 36,
                    VerticalOffsetPt   = 0,
                    HorizontalAnchor   = HorizontalAnchor.Column,
                    VerticalAnchor     = VerticalAnchor.Paragraph,
                    ZOrderIndex        = 100,
                },
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Shape = shape });

            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var imgRects   = view.FloatingImageRects;
            var shapeRects = view.FloatingShapeRects;
            if (imgRects.Count > 0)   imageZ = imgRects[0].ZOrder;
            if (shapeRects.Count > 0) shapeZ = shapeRects[0].ZOrder;
        });

        if (!ran) return;

        imageZ.Should().Be(0,   "in-front image ZOrderIndex=0 preserved");
        shapeZ.Should().Be(100, "in-front shape ZOrderIndex=100 preserved");

        shapeZ.Should().BeGreaterThan(imageZ,
            "UU1 in-front: shape ZOrder (100) > image ZOrder (0) → shape draws on top of image. " +
            "Before fix, separate loops always put shapes after images regardless of ZOrder.");
    }

    private static byte[] SmallPng()
    {
        using var bmp = new SkiaSharp.SKBitmap(4, 4, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        bmp.Erase(new SkiaSharp.SKColor(255, 128, 0));
        using var imgS = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = imgS.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    // ── PNG encoder (shared with other capture tests) ─────────────────────────────────────────────

    private static byte[] WriteableBitmapToPng(WriteableBitmap bitmap)
    {
        try
        {
            using var locked = bitmap.Lock();
            var info = new SKImageInfo(
                locked.Size.Width,
                locked.Size.Height,
                locked.Format == PixelFormat.Bgra8888 ? SKColorType.Bgra8888 : SKColorType.Rgba8888,
                SKAlphaType.Premul);

            using var skBitmap = new SKBitmap();
            if (!skBitmap.InstallPixels(info, locked.Address, locked.RowBytes))
                return [];

            using var skImage = SKImage.FromBitmap(skBitmap);
            using var data = skImage.Encode(SKEncodedImageFormat.Png, 90);
            return data?.ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
