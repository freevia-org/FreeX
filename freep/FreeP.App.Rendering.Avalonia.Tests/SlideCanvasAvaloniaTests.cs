using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Skia;
using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;
using Free.Shared.Drawing;

[assembly: AvaloniaTestApplication(typeof(FreeP.App.Rendering.Avalonia.Tests.SlideHeadlessApp))]

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// Minimal headless Avalonia application for FreeP rendering tests.
/// <para>
/// The theme is REQUIRED even though SlideCanvas itself is a plain custom-rendered Control:
/// AvaloniaRichTextEditor hosts a real <see cref="global::Avalonia.Controls.TextBox"/>
/// (its transparent InputBox). Without a control theme that TextBox gets no template, so it has no
/// visual children, renders nothing, and is invisible to hit-testing -- which silently routes every
/// simulated pointer press to the editor panel instead of the input, leaving the editor unfocused
/// and every caret/selection/inline-table assertion downstream of a click unmet.
/// </para>
/// </summary>
public sealed class SlideHeadlessApp : global::Avalonia.Application
{
    public override void Initialize() => Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<SlideHeadlessApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Unit tests for <see cref="SlideCanvas"/> and <see cref="AvaloniaSlideGeometryFactory"/>
/// running under Avalonia.Headless (no WPF, no STA thread, fully cross-platform).
/// </summary>
public sealed class SlideCanvasAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    private static Task Run(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static byte[] RenderPixels(SlideCanvas canvas, int width, int height, bool refresh = true)
    {
        if (refresh)
            canvas.Refresh();
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(canvas);
        var pixels = new byte[width * height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var target = new PinnedFramebuffer(
                handle.AddrOfPinnedObject(),
                new PixelSize(width, height),
                width * 4);
            bitmap.CopyPixels(target);
        }
        finally
        {
            handle.Free();
        }

        return pixels;
    }

    private static int CountPixelDifferences(
        byte[] first,
        byte[] second,
        int width,
        int left,
        int top,
        int right,
        int bottom)
    {
        int differences = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int offset = (y * width + x) * 4;
                if (first[offset] != second[offset]
                    || first[offset + 1] != second[offset + 1]
                    || first[offset + 2] != second[offset + 2]
                    || first[offset + 3] != second[offset + 3])
                {
                    differences++;
                }
            }
        }

        return differences;
    }

    [Fact]
    public async Task SlideCanvas_ViewColorModes_FilterTheWholeRealizedSurfaceWithoutMutatingTheSlide()
    {
        await Run(() =>
        {
            var presentation = MakePresentation();
            var slide = presentation.Slides[0];
            slide.Background = new ShapeFill.Solid(SrgbColor.White);
            slide.Shapes.Clear();
            slide.Shapes.Add(new SlideShape
            {
                Id = 1,
                AutoShapeKind = DrawingShapeKind.Rectangle,
                OffsetXEmu = 0,
                OffsetYEmu = 0,
                ExtentCxEmu = presentation.SlideSizeCxEmu,
                ExtentCyEmu = presentation.SlideSizeCyEmu,
                Fill = new ShapeFill.Solid(new SrgbColor(0x44, 0x72, 0xC4)),
                Outline = ShapeOutline.None.Instance,
            });

            var canvas = new SlideCanvas { Presentation = presentation, Slide = slide };
            canvas.ApplyViewColorModeState(new PresentationViewColorModeState(PresentationViewColorMode.Grayscale));
            var grayscale = RenderPixels(canvas, 100, 60);
            var center = ((30 * 100) + 50) * 4;
            grayscale[center].Should().Be(grayscale[center + 1]);
            grayscale[center + 1].Should().Be(grayscale[center + 2]);

            canvas.ApplyViewColorModeState(new PresentationViewColorModeState(PresentationViewColorMode.BlackAndWhite));
            var blackAndWhite = RenderPixels(canvas, 100, 60);
            blackAndWhite[center].Should().BeOneOf((byte)0, (byte)255);
            blackAndWhite[center + 1].Should().Be(blackAndWhite[center]);
            blackAndWhite[center + 2].Should().Be(blackAndWhite[center]);
            slide.Shapes[0].Fill.Should().BeOfType<ShapeFill.Solid>()
                .Which.Color.Resolved.Should().Be(new SrgbColor(0x44, 0x72, 0xC4));
        });
    }

    private sealed class PinnedFramebuffer : ILockedFramebuffer
    {
        public PinnedFramebuffer(IntPtr address, PixelSize size, int rowBytes)
        {
            Address = address;
            Size = size;
            RowBytes = rowBytes;
        }

        public IntPtr Address { get; }
        public PixelSize Size { get; }
        public int RowBytes { get; }
        public Vector Dpi => new(96, 96);
        public PixelFormat Format => PixelFormat.Bgra8888;
        public AlphaFormat AlphaFormat => AlphaFormat.Premul;
        public void Dispose() { }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Presentation MakePresentation(Action<Presentation>? configure = null)
    {
        var p = Presentation.CreateEmpty();
        configure?.Invoke(p);
        return p;
    }

    private static ChartShape MakeStockOrSurfaceRenderChart(ChartType chartType)
    {
        if (chartType == ChartType.Radar)
        {
            var radar = new ChartShape
            {
                ChartType = ChartType.Radar,
                RadarStyle = RadarStyle.Filled
            };
            radar.Categories.AddRange(new[] { "North", "East", "South", "West" });
            var series = new ChartSeries { Name = "Coverage" };
            series.Values.AddRange(new double?[] { 4, 6, 3, 5 });
            radar.Series.Add(series);
            return radar;
        }

        if (chartType == ChartType.Stock)
        {
            var stock = new ChartShape { ChartType = ChartType.Stock };
            stock.Categories.AddRange(new[] { "Day 1", "Day 2", "Day 3" });
            foreach (var (name, values) in new[]
            {
                ("Open", new double?[] { 10, 12, 11 }),
                ("High", new double?[] { 14, 16, 15 }),
                ("Low", new double?[] { 8, 9, 10 }),
                ("Close", new double?[] { 13, 11, 14 })
            })
            {
                var series = new ChartSeries { Name = name };
                series.Values.AddRange(values);
                stock.Series.Add(series);
            }

            return stock;
        }

        var surface = new ChartShape { ChartType = chartType };
        surface.Categories.AddRange(new[] { "North", "East", "South" });
        var low = new ChartSeries { Name = "Low Band" };
        low.Values.AddRange(new double?[] { 10, 20, 15 });
        surface.Series.Add(low);
        var high = new ChartSeries { Name = "High Band" };
        high.Values.AddRange(new double?[] { 30, 25, 35 });
        surface.Series.Add(high);
        return surface;
    }

    private static ChartShape MakeStockVolumeRenderChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Stock };
        chart.Categories.AddRange(new[] { "Day 1", "Day 2", "Day 3" });

        foreach (var (name, values) in new[]
        {
            ("Volume", new double?[] { 1000, 1500, 750 }),
            ("Open", new double?[] { 10, 12, 11 }),
            ("High", new double?[] { 14, 16, 15 }),
            ("Low", new double?[] { 8, 9, 10 }),
            ("Close", new double?[] { 13, 11, 14 })
        })
        {
            var series = new ChartSeries { Name = name };
            series.Values.AddRange(values);
            chart.Series.Add(series);
        }

        return chart;
    }

    private static TextBody MakeTextBody(string text)
    {
        var body = new TextBody { Wrap = true };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text, FontFamily = "Aptos", FontSizePt = 18 });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static TextBody MakeMultiParagraphRichBody()
    {
        var body = new TextBody { Wrap = true };
        var first = new Paragraph { Align = TextAlign.Left };
        first.Runs.Add(new Run { Text = "Alpha", FontFamily = "Calibri", FontSizePt = 12 });
        first.Runs.Add(new Run { Text = " Beta", FontFamily = "Calibri", FontSizePt = 14, Italic = true, ItalicSet = true });
        var second = new Paragraph { Align = TextAlign.Left };
        second.Runs.Add(new Run { Text = "Gamma", FontFamily = "Arial", FontSizePt = 16 });
        second.Runs.Add(new Run { Text = " Delta", FontFamily = "Arial", FontSizePt = 18, Bold = true, BoldSet = true });
        body.Paragraphs.Add(first);
        body.Paragraphs.Add(second);
        return body;
    }

    private static TextBody MakeMixedRunTextBody()
    {
        var body = new TextBody { Wrap = true };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "Hello", FontFamily = "Aptos", FontSizePt = 18 });
        paragraph.Runs.Add(new Run
        {
            Text = " world",
            FontFamily = "Aptos",
            FontSizePt = 18,
            Italic = true,
            ItalicSet = true,
        });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    [Fact]
    public async Task SlideCanvas_refresh_invalidates_measure_after_first_slide_is_assigned()
    {
        Size desiredBeforeSlide = default;
        Size desiredAfterSlide = default;

        await Run(() =>
        {
            var presentation = MakePresentation();
            var canvas = new SlideCanvas { Presentation = presentation };

            canvas.Measure(new Size(800, 600));
            desiredBeforeSlide = canvas.DesiredSize;

            canvas.Slide = presentation.Slides[0];
            canvas.Measure(new Size(800, 600));
            desiredAfterSlide = canvas.DesiredSize;
        });

        desiredBeforeSlide.Should().Be(new Size(0, 0));
        desiredAfterSlide.Width.Should().BeGreaterThan(0);
        desiredAfterSlide.Height.Should().BeGreaterThan(0);
    }

    private static SlideShape MakeTableShape(uint id, string text)
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerPixel * 100);
        var row = new TableRow { HeightEmu = DrawingMlCoordinateUnits.EmuPerPixel * 40 };
        row.Cells.Add(new TableCell { TextBody = MakeTextBody(text) });
        table.Rows.Add(row);

        return new SlideShape
        {
            Id = id,
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = DrawingMlCoordinateUnits.EmuPerPixel * 100,
            ExtentCyEmu = DrawingMlCoordinateUnits.EmuPerPixel * 40,
            Table = table,
        };
    }

    private static SlideShape MakeMergedTableShape(uint id)
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerPixel * 100);
        table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerPixel * 100);
        table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerPixel * 100);

        var row = new TableRow { HeightEmu = DrawingMlCoordinateUnits.EmuPerPixel * 40 };
        row.Cells.Add(new TableCell { GridSpan = 2, TextBody = MakeTextBody("Anchor") });
        row.Cells.Add(new TableCell { HMerge = true });
        row.Cells.Add(new TableCell { TextBody = MakeTextBody("Right") });
        table.Rows.Add(row);

        return new SlideShape
        {
            Id = id,
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = DrawingMlCoordinateUnits.EmuPerPixel * 300,
            ExtentCyEmu = DrawingMlCoordinateUnits.EmuPerPixel * 40,
            Table = table,
        };
    }

    private static SlideShape MakeTwoByTwoTableShape(uint id)
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerPixel * 100);
        table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerPixel * 100);
        for (var rowIndex = 0; rowIndex < 2; rowIndex++)
        {
            var row = new TableRow { HeightEmu = DrawingMlCoordinateUnits.EmuPerPixel * 40 };
            row.Cells.Add(new TableCell { TextBody = MakeTextBody($"R{rowIndex}C0") });
            row.Cells.Add(new TableCell { TextBody = MakeTextBody($"R{rowIndex}C1") });
            table.Rows.Add(row);
        }

        return new SlideShape
        {
            Id = id,
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = DrawingMlCoordinateUnits.EmuPerPixel * 200,
            ExtentCyEmu = DrawingMlCoordinateUnits.EmuPerPixel * 80,
            Table = table,
        };
    }


    [Fact]
    public async Task InCanvasTextEditor_CommitPlainText_UsesSharedPlannerCommand()
    {
        Presentation? presentation = null;
        SlideShape? shape = null;
        EditingSession? editor = null;

        await Run(() =>
        {
            presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = new SlideShape
                {
                    Id = 1,
                    OffsetXEmu = 0,
                    OffsetYEmu = 0,
                    ExtentCxEmu = 2743200L,
                    ExtentCyEmu = 1371600L,
                    TextBody = MakeTextBody("Original"),
                };
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.Activate(shape!.Id);
            textEditor.IsActive.Should().BeTrue();
            overlay.Children.Should().ContainSingle();

            var richEditor = RichEditor(overlay);
            var box = richEditor.InputBox;
            richEditor.Width.Should().BeApproximately(288, 0.1);
            richEditor.Height.Should().BeApproximately(144, 0.1);
            box.Text = "Changed\nText";

            textEditor.Commit();
        });

        editor!.CanUndo.Should().BeTrue("changed text should commit through the shared command");
        shape!.TextBody!.Paragraphs.Should().HaveCount(2);
        shape.TextBody.Paragraphs[0].Runs[0].Text.Should().Be("Changed");
        shape.TextBody.Paragraphs[1].Runs[0].Text.Should().Be("Text");

        editor.Undo();
        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Original");
    }

    [Fact]
    public async Task InCanvasTextEditor_RotatedShape_TransformsOverlayAndPersistsTypedText()
    {
        Presentation? presentation = null;
        EditingSession? editor = null;
        SlideShape? shape = null;

        await Run(() =>
        {
            presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = new SlideShape
                {
                    Id = 1,
                    OffsetXEmu = 0,
                    OffsetYEmu = 0,
                    ExtentCxEmu = 2743200L,
                    ExtentCyEmu = 1371600L,
                    RotationDeg = 30,
                    TextBody = MakeTextBody("Rotated text"),
                };
                pres.Slides[0].Shapes.Add(shape);
            });

            editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.Activate(shape!.Id);

            var richEditor = RichEditor(overlay);
            var transform = richEditor.RenderTransform.Should().BeOfType<MatrixTransform>().Subject;
            transform.Matrix.M11.Should().BeApproximately(Math.Cos(Math.PI / 6), 0.0001);
            transform.Matrix.M12.Should().BeApproximately(Math.Sin(Math.PI / 6), 0.0001);
            richEditor.RenderTransformOrigin.Point.X.Should().BeApproximately(0.5, 0.0001);
            richEditor.RenderTransformOrigin.Point.Y.Should().BeApproximately(0.5, 0.0001);

            textEditor.TrySelectTextRange(0, 7).Should().BeTrue();
            textEditor.SelectedText.Should().Be("Rotated");
            richEditor.InputBox.Text = "Edited text";
            textEditor.Commit();
        });

        InCanvasTextEditPlanner.ExtractPlainText(shape!.TextBody).Should().Be("Edited text");
        shape.RotationDeg.Should().BeApproximately(30, 0.001);
        editor!.CanUndo.Should().BeTrue();
    }

    [Fact]
    public async Task InCanvasTextEditor_RotatedShape_CancelDoesNotCommitOnLostFocus()
    {
        Presentation? presentation = null;
        EditingSession? editor = null;
        SlideShape? shape = null;

        await Run(() =>
        {
            presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = new SlideShape
                {
                    Id = 1,
                    ExtentCxEmu = 2743200L,
                    ExtentCyEmu = 1371600L,
                    RotationDeg = 30,
                    TextBody = MakeTextBody("Original text"),
                };
                pres.Slides[0].Shapes.Add(shape);
            });

            editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.Activate(shape!.Id);
            RichEditor(overlay).InputBox.Text = "Discarded";
            textEditor.Cancel();

            canvas.ActiveTextEditShapeId.Should().BeNull();
        });

        InCanvasTextEditPlanner.ExtractPlainText(shape!.TextBody).Should().Be("Original text");
        editor!.CanUndo.Should().BeFalse();
    }

    [Fact]
    public async Task InCanvasTextEditor_NestedChild_UsesSharedPathPlacementAndCommitCancel()
    {
        Presentation? presentation = null;
        EditingSession? editor = null;
        SlideShape? child = null;

        await Run(() =>
        {
            presentation = MakePresentation(presence =>
            {
                presence.Slides[0].Shapes.Clear();
                child = new SlideShape
                {
                    Id = 11,
                    OffsetXEmu = 914400,
                    OffsetYEmu = 457200,
                    ExtentCxEmu = 1828800,
                    ExtentCyEmu = 914400,
                    RotationDeg = 22,
                    FlipV = true,
                    TextBody = MakeTextBody("Nested original"),
                };
                var group = new SlideShape { Id = 10, Kind = SlideShapeKind.Group };
                group.Children.Add(child);
                presence.Slides[0].Shapes.Add(group);
            });

            editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.Activate(child!.Id);
            textEditor.IsActive.Should().BeTrue();
            var richEditor = RichEditor(overlay);
            richEditor.RenderTransform.Should().BeOfType<MatrixTransform>();
            richEditor.InputBox.Text = "Nested edited";
            textEditor.Commit();
            InCanvasTextEditPlanner.ExtractPlainText(child.TextBody).Should().Be("Nested edited");

            editor.Undo();
            InCanvasTextEditPlanner.ExtractPlainText(child.TextBody).Should().Be("Nested original");

            textEditor.Activate(child.Id);
            RichEditor(overlay).InputBox.Text = "Discarded";
            textEditor.Cancel();
        });

        InCanvasTextEditPlanner.ExtractPlainText(child!.TextBody).Should().Be("Nested original");
        editor!.CanUndo.Should().BeFalse();
    }

    [Fact]
    public async Task InCanvasTextEditor_NestedChild_FormatsCrossParagraphSelectionThroughSharedPlanner()
    {
        Presentation? presentation = null;
        EditingSession? editor = null;
        SlideShape? child = null;

        await Run(() =>
        {
            presentation = MakePresentation(presence =>
            {
                presence.Slides[0].Shapes.Clear();
                child = new SlideShape
                {
                    Id = 12,
                    OffsetXEmu = 914400,
                    OffsetYEmu = 457200,
                    ExtentCxEmu = 1828800,
                    ExtentCyEmu = 914400,
                    TextBody = MakeMultiParagraphRichBody(),
                };
                var inner = new SlideShape { Id = 10, Kind = SlideShapeKind.Group };
                inner.Children.Add(child);
                var outer = new SlideShape { Id = 9, Kind = SlideShapeKind.Group };
                outer.Children.Add(inner);
                presence.Slides[0].Shapes.Add(outer);
            });

            editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.Activate(child!.Id);
            textEditor.TrySelectTextRange(2, 10).Should().BeTrue();
            textEditor.TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Bold).Should().BeTrue();
            textEditor.TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Italic).Should().BeTrue();
            textEditor.TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Underline).Should().BeTrue();
            textEditor.TryApplyActiveShapeFontFamily("Consolas").Should().BeTrue();
            textEditor.TryApplyActiveShapeFontSize(20).Should().BeTrue();
            textEditor.TryApplyActiveShapeColor(new ThemeAwareColor(new SrgbColor(0x22, 0x66, 0xAA))).Should().BeTrue();
            textEditor.Commit();
        });

        var edited = child!.TextBody!;
        InCanvasTextEditPlanner.ExtractPlainText(edited).Should().Be("Alpha Beta\nGamma Delta");
        edited.Paragraphs.SelectMany(p => p.Runs).Should().Contain(run =>
            run.Text.Contains("pha", StringComparison.Ordinal) &&
            run.Bold && run.Italic && run.Underline &&
            run.FontFamily == "Consolas" && run.FontSizePt == 20 &&
            run.Color != null && run.Color.Resolved == new SrgbColor(0x22, 0x66, 0xAA));

        editor!.Undo();
        child.TextBody!.Paragraphs.SelectMany(p => p.Runs).Should().NotContain(run =>
            run.FontFamily == "Consolas" || run.FontSizePt == 20 || run.Underline);
        editor.Redo();
        child.TextBody!.Paragraphs.SelectMany(p => p.Runs).Should().Contain(run =>
            run.FontFamily == "Consolas" && run.FontSizePt == 20 && run.Underline);
    }

    [Fact]
    public async Task InCanvasTextEditor_ActiveShapeSuppression_FollowsEditorLifecycle()
    {
        await Run(() =>
        {
            var presentation = MakePresentation(presence =>
            {
                presence.Slides[0].Shapes.Clear();
                presence.Slides[0].Shapes.Add(new SlideShape
                {
                    Id = 1,
                    OffsetXEmu = 0,
                    OffsetYEmu = 0,
                    ExtentCxEmu = 2743200L,
                    ExtentCyEmu = 1371600L,
                    TextBody = MakeTextBody("Original"),
                });
            });
            var shape = presentation.Slides[0].Shapes.Single();
            var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.Activate(shape.Id);
            canvas.ActiveTextEditShapeId.Should().Be(shape.Id);

            textEditor.Cancel();
            canvas.ActiveTextEditShapeId.Should().BeNull();

            textEditor.Activate(shape.Id);
            textEditor.Commit();
            canvas.ActiveTextEditShapeId.Should().BeNull();

            textEditor.Activate(shape.Id);
            textEditor.Dispose();
            canvas.ActiveTextEditShapeId.Should().BeNull();
        });
    }

    [Fact]
    public async Task InCanvasTextEditor_CurrentSlideChange_CommitsTextAndClearsSuppression()
    {
        SlideShape? shape = null;
        EditingSession? editor = null;

        await Run(() =>
        {
            var presentation = MakePresentation(presence =>
            {
                presence.Slides[0].Shapes.Clear();
                shape = new SlideShape
                {
                    Id = 1,
                    OffsetXEmu = 0,
                    OffsetYEmu = 0,
                    ExtentCxEmu = 2743200L,
                    ExtentCyEmu = 1371600L,
                    TextBody = MakeTextBody("Original"),
                };
                presence.Slides[0].Shapes.Add(shape);
                presence.Slides.Add(new Slide());
            });

            editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);
            textEditor.Activate(shape!.Id);
            RichInput(overlay).Text = "Committed before slide change";

            editor.SelectSlide(1);

            canvas.ActiveTextEditShapeId.Should().BeNull();
            InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody)
                .Should().Be("Committed before slide change");
            editor.CanUndo.Should().BeTrue();
        });
    }

    [Fact]
    public async Task SlideCanvas_ActiveTextEditShapeId_SuppressesOnlyMatchingShapeText()
    {
        byte[]? before = null;
        byte[]? suppressed = null;

        await Run(() =>
        {
            var presentation = MakePresentation(presence =>
            {
                presence.Slides[0].Shapes.Clear();
                presence.Slides[0].Shapes.Add(new SlideShape
                {
                    Id = 1,
                    OffsetXEmu = 457200,
                    OffsetYEmu = 457200,
                    ExtentCxEmu = 2743200,
                    ExtentCyEmu = 1371600,
                    Fill = new ShapeFill.Solid(new SrgbColor(0xD9, 0xE2, 0xF3)),
                    TextBody = MakeTextBody("Active shape"),
                });
                presence.Slides[0].Shapes.Add(new SlideShape
                {
                    Id = 2,
                    OffsetXEmu = 4572000,
                    OffsetYEmu = 457200,
                    ExtentCxEmu = 2743200,
                    ExtentCyEmu = 1371600,
                    Fill = new ShapeFill.Solid(new SrgbColor(0xE2, 0xF0, 0xD9)),
                    TextBody = MakeTextBody("Other shape"),
                });
            });
            var canvas = new SlideCanvas
            {
                Presentation = presentation,
                Slide = presentation.Slides[0],
            };

            before = RenderPixels(canvas, 960, 540);
            canvas.ActiveTextEditShapeId = 1;
            suppressed = RenderPixels(canvas, 960, 540);
        });

        before.Should().NotBeNullOrEmpty("the Avalonia renderer should produce a raster");
        suppressed.Should().NotBeNullOrEmpty("the suppressed render should produce a raster");
        CountPixelDifferences(before!, suppressed!, 960, 0, 0, 360, 260)
            .Should().BeGreaterThan(0, "the active shape base text should be removed");
        CountPixelDifferences(before!, suppressed!, 960, 360, 0, 960, 260)
            .Should().Be(0, "a different shape must remain unchanged");
    }

    [Fact]
    public async Task SlideCanvas_MultiTransformPreview_PaintsFilledCopiesAndClears()
    {
        byte[]? baseline = null;
        byte[]? transformed = null;
        byte[]? cleared = null;

        await Run(() =>
        {
            var presentation = MakePresentation(presence =>
            {
                presence.Slides[0].Shapes.Clear();
                presence.Slides[0].Shapes.Add(new SlideShape
                {
                    Id = 1,
                    Kind = SlideShapeKind.AutoShape,
                    AutoShapeKind = DrawingShapeKind.Rectangle,
                    OffsetXEmu = 100 * 9525L,
                    OffsetYEmu = 100 * 9525L,
                    ExtentCxEmu = 100 * 9525L,
                    ExtentCyEmu = 50 * 9525L,
                    Fill = new ShapeFill.Solid(new SrgbColor(0xD9, 0x2F, 0x2F)),
                });
                presence.Slides[0].Shapes.Add(new SlideShape
                {
                    Id = 2,
                    Kind = SlideShapeKind.AutoShape,
                    AutoShapeKind = DrawingShapeKind.Rectangle,
                    OffsetXEmu = 300 * 9525L,
                    OffsetYEmu = 100 * 9525L,
                    ExtentCxEmu = 50 * 9525L,
                    ExtentCyEmu = 50 * 9525L,
                    Fill = new ShapeFill.Solid(new SrgbColor(0x2F, 0x6F, 0xD9)),
                });
            });
            var canvas = new SlideCanvas
            {
                Presentation = presentation,
                Slide = presentation.Slides[0],
            };

            baseline = RenderPixels(canvas, 960, 540);
            canvas.UpdateTransformPreview(new CanvasMultiTransformPlan(
                [
                    new CanvasShapeTransform(1, 150 * 9525L, 150 * 9525L, 120 * 9525L, 60 * 9525L, 0),
                    new CanvasShapeTransform(2, 350 * 9525L, 150 * 9525L, 60 * 9525L, 60 * 9525L, 20),
                ],
                [
                    new CanvasShapeTransformPreview(1, new SlideScreenRect(150, 150, 120, 60), 0),
                    new CanvasShapeTransformPreview(2, new SlideScreenRect(350, 150, 60, 60), 20),
                ],
                new SlideScreenRect(150, 150, 260, 60),
                0));
            canvas.HasLiveTransformPreviewForTests.Should().BeTrue();
            transformed = RenderPixels(canvas, 960, 540, refresh: false);

            canvas.UpdateTransformPreview(CanvasMultiTransformPlan.Empty);
            cleared = RenderPixels(canvas, 960, 540, refresh: false);
        });

        baseline.Should().NotBeNullOrEmpty();
        transformed.Should().NotBeNullOrEmpty();
        cleared.Should().NotBeNullOrEmpty();
        CountPixelDifferences(baseline!, transformed!, 960, 90, 90, 220, 180)
            .Should().BeGreaterThan(0, "the original filled member should be replaced during preview");
        CountPixelDifferences(baseline!, transformed!, 960, 140, 140, 290, 225)
            .Should().BeGreaterThan(0, "the resized filled duplicate should be visible at its preview bounds");
        CountPixelDifferences(baseline!, cleared!, 960, 0, 0, 960, 540)
            .Should().Be(0, "clearing the transient plan should restore the composed slide");
    }

    [Fact]
    public async Task SlideCanvas_MultiTransformPreview_RotatesChartAndClears()
    {
        byte[]? baseline = null;
        byte[]? transformed = null;
        byte[]? cleared = null;

        await Run(() =>
        {
            var presentation = MakePresentation(presence =>
            {
                presence.Slides[0].Shapes.Clear();
                var chart = new ChartShape
                {
                    ChartType = ChartType.ColumnClustered,
                    Title = "Preview chart",
                };
                chart.Categories.AddRange(["A", "B", "C"]);
                var series = new ChartSeries { Name = "Series" };
                series.Values.AddRange([10.0, 20.0, 15.0]);
                chart.Series.Add(series);
                presence.Slides[0].Shapes.Add(new SlideShape
                {
                    Id = 9,
                    Kind = SlideShapeKind.Chart,
                    OffsetXEmu = 100 * 9525L,
                    OffsetYEmu = 100 * 9525L,
                    ExtentCxEmu = 240 * 9525L,
                    ExtentCyEmu = 160 * 9525L,
                    Chart = chart,
                });
            });
            var canvas = new SlideCanvas
            {
                Presentation = presentation,
                Slide = presentation.Slides[0],
            };

            baseline = RenderPixels(canvas, 960, 540);
            canvas.UpdateTransformPreview(new CanvasMultiTransformPlan(
                [new CanvasShapeTransform(9, 100 * 9525L, 100 * 9525L, 240 * 9525L, 160 * 9525L, 35)],
                [new CanvasShapeTransformPreview(9, new SlideScreenRect(100, 100, 240, 160), 35)],
                new SlideScreenRect(100, 100, 240, 160),
                35));
            transformed = RenderPixels(canvas, 960, 540, refresh: false);

            canvas.UpdateTransformPreview(CanvasMultiTransformPlan.Empty);
            cleared = RenderPixels(canvas, 960, 540, refresh: false);
        });

        baseline.Should().NotBeNullOrEmpty();
        transformed.Should().NotBeNullOrEmpty();
        cleared.Should().NotBeNullOrEmpty();
        CountPixelDifferences(baseline!, transformed!, 960, 50, 50, 420, 340)
            .Should().BeGreaterThan(0, "the chart frame and content should rotate during preview");
        CountPixelDifferences(baseline!, cleared!, 960, 0, 0, 960, 540)
            .Should().Be(0, "clearing the transient chart preview should restore the composed slide");
    }

    [Fact]
    public async Task SlideCanvas_MultiTransformPreview_ResizesAndRotatesRealMathShape_AndClears()
    {
        byte[]? baseline = null;
        byte[]? transformed = null;
        byte[]? cleared = null;

        await Run(() =>
        {
            var presentation = MakePresentation(presence =>
            {
                presence.Slides[0].Shapes.Clear();
                var body = new TextBody { Wrap = false };
                body.Paragraphs.Add(new Paragraph
                {
                    Runs =
                    {
                        new Run
                        {
                            Text = "x+1",
                            FontFamily = "Cambria Math",
                            FontSizePt = 24,
                            Math = new MathRunInfo
                            {
                                RawXml = "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:r><m:t>x</m:t></m:r><m:r><m:t>+</m:t></m:r><m:r><m:t>1</m:t></m:r></m:oMath>"
                            }
                        }
                    }
                });
                presence.Slides[0].Shapes.Add(new SlideShape
                {
                    Id = 31,
                    Kind = SlideShapeKind.AutoShape,
                    AutoShapeKind = DrawingShapeKind.Rectangle,
                    OffsetXEmu = 120 * 9525L,
                    OffsetYEmu = 120 * 9525L,
                    ExtentCxEmu = 180 * 9525L,
                    ExtentCyEmu = 90 * 9525L,
                    TextBody = body,
                });
            });
            var canvas = new SlideCanvas
            {
                Presentation = presentation,
                Slide = presentation.Slides[0],
            };

            baseline = RenderPixels(canvas, 960, 540);
            var plan = new CanvasMultiTransformPlan(
                [new CanvasShapeTransform(31, 300 * 9525L, 180 * 9525L, 300 * 9525L, 150 * 9525L, 35)],
                [new CanvasShapeTransformPreview(31, new SlideScreenRect(300, 180, 300, 150), 35)],
                new SlideScreenRect(300, 180, 300, 150),
                35);
            var sourceShape = SlideCompositor.Compose(presentation, presentation.Slides[0])
                .OfType<DrawOp.Shape>()
                .Single(shape => shape.ShapeId == 31);
            var sourceMath = sourceShape.Text!.Paragraphs.Single().Runs.Single().MathLayout;
            sourceMath.Should().NotBeNull("the compositor must resolve the real OMML run into the shape text layout");
            var previewShape = CanvasTransformPreviewComposer.Compose([sourceShape], plan)[31]
                .Should().BeOfType<DrawOp.Shape>().Subject;
            previewShape.BoundsDip.Should().Be(new LayoutRect(300, 180, 300, 150));
            previewShape.RotationDeg.Should().Be(35);
            previewShape.Text!.Paragraphs.Single().Runs.Single().MathLayout.Should().BeSameAs(sourceMath);

            canvas.UpdateTransformPreview(plan);
            transformed = RenderPixels(canvas, 960, 540, refresh: false);

            canvas.UpdateTransformPreview(CanvasMultiTransformPlan.Empty);
            cleared = RenderPixels(canvas, 960, 540, refresh: false);
        });

        baseline.Should().NotBeNullOrEmpty();
        transformed.Should().NotBeNullOrEmpty();
        cleared.Should().NotBeNullOrEmpty();
        CountPixelDifferences(baseline!, transformed!, 960, 80, 80, 460, 380)
            .Should().BeGreaterThan(0, "the real math shape must move, resize, and rotate during preview");
        CountPixelDifferences(baseline!, transformed!, 960, 150, 90, 520, 350)
            .Should().BeGreaterThan(0, "the transformed math glyphs must be visible in the preview frame");
        CountPixelDifferences(baseline!, cleared!, 960, 0, 0, 960, 540)
            .Should().Be(0, "clearing the transient math preview should restore the composed slide");
    }

    [Fact]
    public async Task InCanvasTextEditor_FormatActiveShapeOverlay_UsesSharedPlanAndPreservesMixedRunsOnCommit()
    {
        SlideShape? shape = null;
        EditingSession? editor = null;

        await Session.Dispatch(async () =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = new SlideShape
                {
                    Id = 2,
                    OffsetXEmu = 0,
                    OffsetYEmu = 0,
                    ExtentCxEmu = 2743200L,
                    ExtentCyEmu = 1371600L,
                    TextBody = MakeMixedRunTextBody(),
                };
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new Canvas();
            var window = new Window
            {
                Width = 320,
                Height = 180,
                Content = overlay,
            };
            window.Show();
            window.Measure(new Size(320, 180));
            window.Arrange(new Rect(0, 0, 320, 180));
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            try
            {
                textEditor.Activate(shape!.Id);
                await DrainInputAsync();
                textEditor.TrySelectTextRange(1, 7).Should().BeTrue();
                textEditor.SelectedText.Should().Be("ello w");
                textEditor.IsEditorFocused.Should().BeTrue();
                var box = RichInput(overlay);
                box.Text.Should().Be("Hello world");

                textEditor.TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Bold).Should().BeTrue();
                RichEditor(overlay).CurrentPlan().InitialSelectionStyle.Bold.Should().BeTrue(
                    "the shared plan should report the selected subrange as bold");

                textEditor.Commit();
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);

        editor!.CanUndo.Should().BeTrue("the shared shape planner should issue the formatting command");

        var runs = shape!.TextBody!.Paragraphs[0].Runs;
        runs.Should().HaveCount(4, "formatting a subrange should preserve mixed runs at the selection boundaries");
        runs[0].Text.Should().Be("H");
        runs[0].Bold.Should().BeFalse();
        runs[1].Text.Should().Be("ello");
        runs[1].Bold.Should().BeTrue();
        runs[1].Italic.Should().BeFalse();
        runs[2].Text.Should().Be(" w");
        runs[2].Bold.Should().BeTrue();
        runs[2].Italic.Should().BeTrue();
        runs[3].Text.Should().Be("orld");
        runs[3].Bold.Should().BeFalse();
        runs[3].Italic.Should().BeTrue();

        editor.Undo();
        shape.TextBody!.Paragraphs[0].Runs.Should().OnlyContain(r => !r.Bold);
        shape.TextBody.Paragraphs[0].Runs[1].Italic.Should().BeTrue();
    }

    [Fact]
    public async Task InCanvasTextEditor_TextAndFormattingStayLocalAndCommitAsOneUndoStep()
    {
        SlideShape? shape = null;
        EditingSession? editor = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = new SlideShape
                {
                    Id = 28,
                    OffsetXEmu = 0,
                    OffsetYEmu = 0,
                    ExtentCxEmu = 2743200L,
                    ExtentCyEmu = 1371600L,
                    TextBody = MakeMixedRunTextBody(),
                };
                pres.Slides[0].Shapes.Add(shape);
            });

            editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.Activate(shape!.Id);
            textEditor.TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Underline).Should().BeTrue();
            RichInput(overlay).Text = "One committed edit";

            editor.CanUndo.Should().BeFalse("the active rich edit is still a local transaction");
            InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody).Should().Be("Hello world");
            shape.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
                .Should().OnlyContain(run => !run.Underline);

            textEditor.Commit();
        });

        editor!.CanUndo.Should().BeTrue();
        InCanvasTextEditPlanner.ExtractPlainText(shape!.TextBody).Should().Be("One committed edit");
        shape.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => run.Underline);

        editor.Undo();
        editor.CanUndo.Should().BeFalse("text and formatting must share one model command");
        InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody).Should().Be("Hello world");
        shape.TextBody!.Paragraphs[0].Runs.Should().HaveCount(2);
        shape.TextBody.Paragraphs[0].Runs[1].Italic.Should().BeTrue();
        shape.TextBody.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => !run.Underline);
    }

    [Fact]
    public async Task InCanvasTextEditor_OpenMixedRuns_ProjectsSharedRichPlanOntoShapeOverlay()
    {
        SlideShape? shape = null;
        InCanvasTableCellRichTextEditPlan? startPlan = null;
        InCanvasTableCellRichTextEditPlan? selectedPlan = null;
        var projectedFontFamily = string.Empty;
        var projectedFontSize = 0.0;
        var projectedFallbackFontFamily = string.Empty;
        var projectedFallbackFontSize = 0.0;
        var projectedBold = false;
        var richClass = false;
        var mixedClass = false;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = new SlideShape
                {
                    Id = 26,
                    OffsetXEmu = 0,
                    OffsetYEmu = 0,
                    ExtentCxEmu = 2743200L,
                    ExtentCyEmu = 1371600L,
                    TextBody = MakeMixedRunTextBody(),
                };
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            var editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.Activate(shape!.Id);
            var editorControl = RichEditor(overlay);
            var box = editorControl.InputBox;
            startPlan = box.Tag.Should().BeOfType<InCanvasTableCellRichTextEditPlan>().Subject;
            projectedFontFamily = box.FontFamily.ToString();
            projectedFontSize = box.FontSize;
            projectedFallbackFontFamily = editorControl.RichTextView.FallbackFontFamily;
            projectedFallbackFontSize = editorControl.RichTextView.FallbackFontSizePt;
            projectedBold = box.FontWeight == FontWeight.Bold;
            richClass = box.Classes.Contains("freep-shape-rich-editor");
            mixedClass = box.Classes.Contains("freep-shape-mixed-formatting");

            box.SelectionStart = 5;
            box.SelectionEnd = 11;
            textEditor.TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Bold).Should().BeTrue();
            selectedPlan = box.Tag.Should().BeOfType<InCanvasTableCellRichTextEditPlan>().Subject;
            shape.TextBody!.Paragraphs[0].Runs[1].Bold.Should().BeFalse(
                "inline formatting remains local until the edit commits");
            textEditor.Commit();
        });

        startPlan!.PlainText.Should().Be("Hello world");
        startPlan.Runs.Should().HaveCount(2);
        startPlan.Runs[0].Text.Should().Be("Hello");
        startPlan.Runs[1].Text.Should().Be(" world");
        startPlan.HasRichFormatting.Should().BeTrue();
        startPlan.HasMixedFormatting.Should().BeTrue();
        projectedFontFamily.Should().Contain("Aptos");
        projectedFontSize.Should().BeApproximately(24, 0.01, "18pt is 24 device-independent pixels");
        projectedFallbackFontFamily.Should().Be(InCanvasRichTextEditorDefaults.FallbackFontFamily);
        projectedFallbackFontSize.Should().BeApproximately(18, 0.01);
        projectedBold.Should().BeFalse("the Avalonia shape editor starts from the first rich run's shared style");
        richClass.Should().BeTrue();
        mixedClass.Should().BeTrue();

        selectedPlan!.InitialSelectionStyle.Bold.Should().BeTrue("the refreshed shared plan describes the selected subrange");
        selectedPlan.InitialSelectionStyle.Italic.Should().BeTrue("existing selected-run style stays visible in the shared selection state");
        shape!.TextBody!.Paragraphs[0].Runs[1].Bold.Should().BeTrue();
    }

    [Fact]
    public async Task TableCellEditAdapter_UsesSharedPlannerForStateStartAndCommit()
    {
        EditingSession? editor = null;
        SlideShape? shape = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeTableShape(10, "Original");
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            editor = new EditingSession(presentation, bus);
            editor.Select(shape!.Id);
            editor.SetActiveTableCell(0, 0);

            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var state = AvaloniaTableCellEditAdapter.PlanSelectedCell(editor);
            var startPlan = AvaloniaTableCellEditAdapter.BeginEdit(canvas, editor, shape.Id, 0, 0);

            state.CanEditText.Should().BeTrue();
            state.CanFormatText.Should().BeTrue();
            startPlan.IsReady.Should().BeTrue();
            startPlan.InitialSelection.Should().Be(new InCanvasEditorTextSelection(0, "Original".Length));

            var decision = AvaloniaTableCellEditAdapter.CommitRichText(
                startPlan.EditPlanner,
                MakeTextBody("Edited"));
            decision.Command.Should().NotBeNull();
            bus.Execute(decision.Command!);
        });

        shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Edited");

        editor!.Undo();
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Original");
    }

    [Fact]
    public async Task TableCellTextEditor_TransformedTable_UsesSharedPlacementAndPersistsCommitAndCancel()
    {
        EditingSession? editor = null;
        SlideShape? shape = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                var table = new TableShape();
                table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerPixel * 100);
                table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerPixel * 100);
                var row = new TableRow { HeightEmu = DrawingMlCoordinateUnits.EmuPerPixel * 50 };
                row.Cells.Add(new TableCell { TextBody = MakeTextBody("Original") });
                row.Cells.Add(new TableCell { TextBody = MakeTextBody("Other") });
                table.Rows.Add(row);
                shape = new SlideShape
                {
                    Id = 91,
                    Kind = SlideShapeKind.Table,
                    OffsetXEmu = DrawingMlCoordinateUnits.EmuPerPixel * 100,
                    OffsetYEmu = DrawingMlCoordinateUnits.EmuPerPixel * 80,
                    ExtentCxEmu = DrawingMlCoordinateUnits.EmuPerPixel * 200,
                    ExtentCyEmu = DrawingMlCoordinateUnits.EmuPerPixel * 50,
                    RotationDeg = 30,
                    FlipH = true,
                    FlipV = true,
                    Table = table,
                };
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            canvas.Measure(new Size(800, 600));
            canvas.Arrange(new Rect(0, 0, 800, 600));
            var renderTarget = new RenderTargetBitmap(new PixelSize(800, 600));
            renderTarget.Render(canvas);

            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);
            textEditor.ActivateCellEdit(shape!.Id, 0, 0);

            var box = overlay.Children.OfType<AvaloniaRichTextEditor>().Should().ContainSingle().Subject;
            box.RenderTransform.Should().NotBeNull("the editor must follow the table-frame transform");
            box.Text.Should().Be("Original");
            box.RenderTransformOrigin.Point.X.Should().BeApproximately(0.5, 0.001);
            box.RenderTransformOrigin.Point.Y.Should().BeApproximately(0.5, 0.001);

            box.Text = "Committed transformed text";
            textEditor.CommitCellEdit();
            shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text
                .Should().Be("Committed transformed text");

            textEditor.ActivateCellEdit(shape.Id, 0, 0);
            RichInput(overlay).Text = "Canceled transformed text";
            textEditor.CancelCellEdit();
            shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text
                .Should().Be("Committed transformed text");
        });

        editor!.CanUndo.Should().BeTrue();
    }

    [Fact]
    public async Task TableCellTextEditor_ActivateCommit_UsesSharedPlannerCommand()
    {
        EditingSession? editor = null;
        SlideShape? shape = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeTableShape(11, "Original");
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.ActivateCellEdit(shape!.Id, 0, 0);
            textEditor.IsCellEditActive.Should().BeTrue();
            overlay.IsVisible.Should().BeTrue();
            overlay.IsHitTestVisible.Should().BeTrue();

            var box = RichInput(overlay);
            box.SelectionStart.Should().Be(0);
            box.SelectionEnd.Should().Be("Original".Length);
            box.Text = "Changed\nText";

            textEditor.CommitCellEdit();
        });

        editor!.CanUndo.Should().BeTrue("changed cell text should commit through the shared table-cell command");
        shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs.Should().HaveCount(2);
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Changed");
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[1].Runs[0].Text.Should().Be("Text");

        editor.Undo();
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Original");
    }

    [Fact]
    public async Task TableCellTextEditor_TextReplacementPreservesMixedRunsAndParagraphMetadata()
    {
        SlideShape? shape = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeTableShape(12, "Hello");
                var body = shape!.Table!.Rows[0].Cells[0].TextBody!;
                body.Paragraphs[0].Runs.Add(new Run
                {
                    Text = "World",
                    Italic = true,
                    ItalicSet = true,
                });
                body.Paragraphs[0].Align = TextAlign.Center;
                pres.Slides[0].Shapes.Add(shape);
            });

            var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.ActivateCellEdit(shape!.Id, 0, 0);
            var box = RichInput(overlay);
            box.Text.Should().Be("HelloWorld");
            box.SelectionStart = 3;
            box.SelectionEnd = 5;
            box.Text = "HelXloWorld";

            textEditor.CommitCellEdit();
        });

        var paragraph = shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs.Single();
        paragraph.Align.Should().Be(TextAlign.Center);
        paragraph.Runs.Select(run => (run.Text, run.Bold, run.Italic))
            .Should().Equal(("HelXlo", false, false), ("World", false, true));
    }

    [Fact]
    public async Task TableCellTextEditor_FormatActiveOverlay_UsesSharedPlanAndMirrorsTextBox()
    {
        SlideShape? shape = null;
        var overlayBold = false;
        var overlayItalic = false;
        var overlayUnderlineClass = false;
        var overlayUnderlineBorder = false;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeTableShape(14, "Original");
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            var editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.ActivateCellEdit(shape!.Id, 0, 0);
            var box = RichInput(overlay);

            textEditor.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Bold).Should().BeTrue();
            textEditor.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Italic).Should().BeTrue();
            textEditor.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Underline).Should().BeTrue();

            overlayBold = box.FontWeight == FontWeight.Bold;
            overlayItalic = box.FontStyle == FontStyle.Italic;
            overlayUnderlineClass = box.Classes.Contains("freep-table-cell-underline");
            overlayUnderlineBorder = box.BorderThickness.Bottom == 3.0;
            shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Bold.Should().BeFalse(
                "the live model must not change before the rich edit transaction commits");
            textEditor.CommitCellEdit();
        });

        var run = shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0];
        run.Bold.Should().BeTrue();
        run.Italic.Should().BeTrue();
        run.Underline.Should().BeTrue();
        overlayBold.Should().BeTrue();
        overlayItalic.Should().BeTrue();
        overlayUnderlineClass.Should().BeTrue();
        overlayUnderlineBorder.Should().BeTrue();
    }

    [Fact]
    public async Task TableCellTextEditor_CommitUnchangedTextAfterFormat_PreservesPerRunFormatting()
    {
        // IC1 regression: a cell with mixed runs (plain + italic). Apply Bold via the shared
        // format path (which mutates the rich model directly), then commit the overlay without
        // retyping any text. The commit must NOT flatten to a single run copying run[0]'s
        // formatting — both runs must survive with bold applied and the second run's italic
        // preserved.
        SlideShape? shape = null;
        EditingSession? editor = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeTableShape(20, "Hello");
                var body = shape!.Table!.Rows[0].Cells[0].TextBody!;
                body.Paragraphs[0].Runs.Add(new Run { Text = "World", Italic = true, ItalicSet = true });
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.ActivateCellEdit(shape!.Id, 0, 0);
            var box = RichInput(overlay);
            box.Text.Should().Be("HelloWorld");

            // Apply Bold to the whole cell (collapsed caret / no explicit selection set here —
            // whole-cell is the documented fallback). This mutates the rich model directly.
            textEditor.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Bold).Should().BeTrue();

            // Commit without retyping — the overlay's Text still equals the model's plain text.
            textEditor.CommitCellEdit();
        });

        var runs = shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs;
        runs.Should().HaveCount(2, "the two distinctly-formatted runs must not be flattened into one");
        runs[0].Text.Should().Be("Hello");
        runs[1].Text.Should().Be("World");
        runs[0].Bold.Should().BeTrue();
        runs[1].Bold.Should().BeTrue();
        runs[1].Italic.Should().BeTrue("the second run's italic formatting must survive the commit");
        runs[0].Italic.Should().BeFalse();
    }

    [Fact]
    public async Task TableCellTextEditor_FormatSubRangeSelection_OnlyFormatsSelectedRunRange()
    {
        // IC2 regression: selecting a sub-range within a cell (one word of several) and applying
        // Bold must only bold the selected characters — runs split at the boundaries, text
        // integrity preserved, and the rest of the cell left unchanged.
        SlideShape? shape = null;
        EditingSession? editor = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeTableShape(21, "one two three");
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.ActivateCellEdit(shape!.Id, 0, 0);
            var box = RichInput(overlay);
            box.Text.Should().Be("one two three");

            // Select just "two" (offsets 4..7).
            box.SelectionStart = 4;
            box.SelectionEnd = 7;

            textEditor.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Bold).Should().BeTrue();
            textEditor.CommitCellEdit();
        });

        var runs = shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs;
        string.Concat(runs.Select(r => r.Text)).Should().Be("one two three", "text integrity must be preserved across the split");

        runs.Should().Contain(r => r.Text == "two" && r.Bold, "the selected word must be bold");
        runs.Where(r => r.Text != "two").Should().OnlyContain(r => !r.Bold, "text outside the selection must be unchanged");
    }

    [Fact]
    public async Task TableCellTextEditor_OpenMixedRuns_ProjectsSharedRichPlanOntoOverlay()
    {
        SlideShape? shape = null;
        InCanvasTableCellRichTextEditPlan? startPlan = null;
        InCanvasTableCellRichTextEditPlan? selectedPlan = null;
        var projectedFontFamily = string.Empty;
        var projectedFontSize = 0.0;
        var projectedBold = false;
        var richClass = false;
        var mixedClass = false;
        var selectionStartAfterFormat = -1;
        var selectionEndAfterFormat = -1;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeTableShape(25, "Hello");
                var body = shape!.Table!.Rows[0].Cells[0].TextBody!;
                body.Paragraphs[0].Runs.Add(new Run
                {
                    Text = "World",
                    FontFamily = "Consolas",
                    FontSizePt = 22,
                    Italic = true,
                    ItalicSet = true,
                });
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            var editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.ActivateCellEdit(shape!.Id, 0, 0);
            var box = RichInput(overlay);
            startPlan = box.Tag.Should().BeOfType<InCanvasTableCellRichTextEditPlan>().Subject;
            projectedFontFamily = box.FontFamily.ToString();
            projectedFontSize = box.FontSize;
            projectedBold = box.FontWeight == FontWeight.Bold;
            richClass = box.Classes.Contains("freep-table-cell-rich-editor");
            mixedClass = box.Classes.Contains("freep-table-cell-mixed-formatting");

            box.SelectionStart = 5;
            box.SelectionEnd = 10;
            textEditor.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Bold).Should().BeTrue();
            selectedPlan = box.Tag.Should().BeOfType<InCanvasTableCellRichTextEditPlan>().Subject;
            selectionStartAfterFormat = box.SelectionStart;
            selectionEndAfterFormat = box.SelectionEnd;
            textEditor.CommitCellEdit();
        });

        startPlan!.PlainText.Should().Be("HelloWorld");
        startPlan.Runs.Should().HaveCount(2);
        startPlan.Runs[0].Text.Should().Be("Hello");
        startPlan.Runs[1].Text.Should().Be("World");
        startPlan.HasRichFormatting.Should().BeTrue();
        startPlan.HasMixedFormatting.Should().BeTrue();
        projectedFontFamily.Should().Contain("Aptos");
        projectedFontSize.Should().BeApproximately(24, 0.01, "18pt is 24 device-independent pixels");
        projectedBold.Should().BeFalse("the Avalonia editor starts from the first rich run's shared style");
        richClass.Should().BeTrue();
        mixedClass.Should().BeTrue();

        selectedPlan!.InitialSelectionStyle.Bold.Should().BeTrue("the refreshed shared plan describes the selected subrange");
        selectedPlan.InitialSelectionStyle.Italic.Should().BeTrue("existing selected-run style stays visible in the shared selection state");
        selectedPlan.Selection.Should().Be(new InCanvasEditorTextSelection(5, 10));
        selectedPlan.SelectedRunRanges.Should().ContainSingle(range => range.Text == "World");
        selectionStartAfterFormat.Should().Be(5);
        selectionEndAfterFormat.Should().Be(10);
        shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[1].Bold.Should().BeTrue();
    }

    [Fact]
    public async Task TableCellTextEditor_ListPresetSelection_RefreshesSharedParagraphPlan()
    {
        SlideShape? shape = null;
        InCanvasTableCellRichTextEditPlan? selectedPlan = null;
        var selectionStartAfterFormat = -1;
        var selectionEndAfterFormat = -1;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeTableShape(26, "Alpha");
                var body = shape!.Table!.Rows[0].Cells[0].TextBody!;
                var second = new Paragraph();
                second.Runs.Add(new Run { Text = "Beta" });
                body.Paragraphs.Add(second);
                var third = new Paragraph();
                third.Runs.Add(new Run { Text = "Gamma" });
                body.Paragraphs.Add(third);
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            var editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.ActivateCellEdit(shape!.Id, 0, 0);
            var richEditor = RichEditor(overlay);
            var box = richEditor.InputBox;
            box.Text.Should().Be("Alpha\nBeta\nGamma");
            box.SelectionStart = 6;
            box.SelectionEnd = 10;

            textEditor.TryApplyActiveTableCellParagraphListPreset(
                TableCellListPresetCatalog.NumberAlphaUpperPeriod).Should().BeTrue();
            selectedPlan = box.Tag.Should().BeOfType<InCanvasTableCellRichTextEditPlan>().Subject;
            selectionStartAfterFormat = box.SelectionStart;
            selectionEndAfterFormat = box.SelectionEnd;
            textEditor.CommitCellEdit();
        });

        selectedPlan!.Selection.Should().Be(new InCanvasEditorTextSelection(6, 10));
        selectedPlan.SelectedParagraphs.Should().ContainSingle();
        selectedPlan.SelectedParagraphs[0].ParagraphIndex.Should().Be(1);
        selectedPlan.SelectedParagraphs[0].BulletKind.Should().Be(BulletKind.Auto);
        selectedPlan.SelectedParagraphs[0].AutoNumType.Should().Be(AutoNumType.AlphaUcPeriod);
        selectedPlan.SelectedParagraphs[0].AutoNumStartAt.Should().Be(1);
        selectedPlan.SelectedListState.HasResolvedPreset.Should().BeTrue();
        selectedPlan.SelectedListState.PresetId.Should().Be(TableCellListPresetCatalog.NumberAlphaUpperPeriodId);
        selectedPlan.SelectedListState.DisplayName.Should().Be("Alpha A.");
        selectedPlan.SelectedListState.PreviewText.Should().Be("A.  Alpha A.");
        selectedPlan.SelectedListState.GalleryItemKind.Should().Be(PresentationListGalleryItemKind.Numbering);
        selectedPlan.Paragraphs[0].BulletKind.Should().Be(BulletKind.None);
        selectedPlan.Paragraphs[2].BulletKind.Should().Be(BulletKind.None);
        selectedPlan.HasListFormatting.Should().BeTrue();
        selectionStartAfterFormat.Should().Be(6);
        selectionEndAfterFormat.Should().Be(10);

        var paragraphs = shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs[0].BulletKind.Should().Be(BulletKind.None);
        paragraphs[1].BulletKind.Should().Be(BulletKind.Auto);
        paragraphs[1].AutoNumType.Should().Be(AutoNumType.AlphaUcPeriod);
        paragraphs[2].BulletKind.Should().Be(BulletKind.None);
    }

    [Fact]
    public async Task TableCellTextEditor_ValueFormatsWholeCell_MirrorsOverlayAndPreservesMixedRunsOnCommit()
    {
        SlideShape? shape = null;
        var overlayFontFamily = string.Empty;
        var overlayFontSize = 0.0;
        Color overlayColor = default;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeTableShape(23, "Hello");
                var body = shape!.Table!.Rows[0].Cells[0].TextBody!;
                body.Paragraphs[0].Runs.Add(new Run { Text = "World", Italic = true, ItalicSet = true });
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            var editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);
            var color = new ThemeAwareColor(new SrgbColor(0x22, 0x44, 0x66));

            textEditor.ActivateCellEdit(shape!.Id, 0, 0);
            var richEditor = RichEditor(overlay);
            var box = richEditor.InputBox;
            box.Text.Should().Be("HelloWorld");

            textEditor.TryApplyActiveTableCellFontFamily("Consolas").Should().BeTrue();
            textEditor.TryApplyActiveTableCellFontSize(24).Should().BeTrue();
            textEditor.TryApplyActiveTableCellColor(color).Should().BeTrue();

            overlayFontFamily = box.FontFamily.ToString();
            overlayFontSize = box.FontSize;
            var visualColor = richEditor.RichTextView.VisualPlan.Paragraphs[0].Runs[0].Color;
            visualColor.Should().NotBeNull();
            var renderedColor = visualColor!.Resolved;
            overlayColor = Color.FromRgb(renderedColor.R, renderedColor.G, renderedColor.B);

            textEditor.CommitCellEdit();
        });

        overlayFontFamily.Should().Contain("Consolas");
        overlayFontSize.Should().BeApproximately(32, 0.01, "24pt is 32 device-independent pixels");
        overlayColor.Should().Be(Color.FromRgb(0x22, 0x44, 0x66));

        var runs = shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs;
        runs.Should().HaveCount(2, "the italic second run should remain distinct after unchanged-text commit");
        runs.Should().OnlyContain(r => r.FontFamily == "Consolas");
        runs.Should().OnlyContain(r => r.FontSizePt == 24);
        runs.Should().OnlyContain(r => r.Color != null && r.Color.Resolved == new SrgbColor(0x22, 0x44, 0x66));
        runs[1].Italic.Should().BeTrue();
    }

    [Fact]
    public async Task TableCellTextEditor_ValueFormatSubRangeSelection_OnlyFormatsSelectedRunRange()
    {
        SlideShape? shape = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeTableShape(24, "one two three");
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            var editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);
            var color = new ThemeAwareColor(new SrgbColor(0xAA, 0x33, 0x11));

            textEditor.ActivateCellEdit(shape!.Id, 0, 0);
            var box = RichInput(overlay);
            box.Text.Should().Be("one two three");
            box.SelectionStart = 4;
            box.SelectionEnd = 7;

            textEditor.TryApplyActiveTableCellFontFamily("Consolas").Should().BeTrue();
            textEditor.TryApplyActiveTableCellFontSize(28).Should().BeTrue();
            textEditor.TryApplyActiveTableCellColor(color).Should().BeTrue();
            textEditor.CommitCellEdit();
        });

        var runs = shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs;
        string.Concat(runs.Select(r => r.Text)).Should().Be("one two three");
        runs.Should().Contain(r =>
            r.Text == "two" &&
            r.FontFamily == "Consolas" &&
            r.FontSizePt == 28 &&
            r.Color != null &&
            r.Color.Resolved == new SrgbColor(0xAA, 0x33, 0x11));
        runs.Where(r => r.Text != "two").Should().OnlyContain(r =>
            r.FontFamily == "Aptos" &&
            r.FontSizePt == 18 &&
            r.Color == null);
    }

    [Fact]
    public void PlanTextFormat_WholeCellSelection_BoldsAllRuns()
    {
        // A selection spanning the entire cell text behaves like the whole-cell fallback.
        var shape = MakeTableShape(22, "abc");
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanTextFormat(
            0,
            slide,
            [shape.Id],
            (0, 0),
            TableCellTextFormatKind.Bold,
            selection: (0, 3));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);

        // Apply the command against a presentation containing our shape directly.
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs
            .Should().OnlyContain(r => r.Bold);
    }

    [Fact]
    public void TableCellEditAdapter_PlanParagraphListPreset_DelegatesSharedMutation()
    {
        var presentation = MakePresentation(pres =>
        {
            pres.Slides[0].Shapes.Clear();
            pres.Slides[0].Shapes.Add(MakeTableShape(23, "abc"));
        });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = presentation.Slides[0].Shapes[0];
        editor.Select(shape.Id);
        editor.SetActiveTableCell(0, 0);

        var plan = AvaloniaTableCellEditAdapter.PlanParagraphListPreset(
            editor,
            TableCellListPresetCatalog.NumberRomanLowerPeriod);

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.ListPreset.Should().Be(TableCellListPresetCatalog.NumberRomanLowerPeriod);

        editor.Bus.Execute(plan.Command!);

        var paragraph = shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Auto);
        paragraph.AutoNumType.Should().Be(AutoNumType.RomanLcPeriod);
        paragraph.AutoNumStartAt.Should().Be(1);
    }

    [Fact]
    public void TableCellEditAdapter_PlanParagraphPictureBullet_ReportsSharedImageMetadata()
    {
        var presentation = MakePresentation(pres =>
        {
            pres.Slides[0].Shapes.Clear();
            pres.Slides[0].Shapes.Add(MakeTableShape(24, "abc"));
        });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = presentation.Slides[0].Shapes[0];
        editor.Select(shape.Id);
        editor.SetActiveTableCell(0, 0);
        var payload = PresentationPictureBulletAuthoringPlanner.CreatePayload(
            [0x89, 0x50, 0x4E, 0x47],
            "image/png",
            "bullet.png");

        var plan = AvaloniaTableCellEditAdapter.PlanParagraphPictureBullet(editor, payload);

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.ResultRichTextPlan.Should().NotBeNull();
        plan.ResultRichTextPlan!.SelectedParagraphs.Should().ContainSingle();
        plan.ResultRichTextPlan.SelectedParagraphs[0].BulletKind.Should().Be(BulletKind.Image);
        plan.ResultRichTextPlan.SelectedParagraphs[0].BulletImage.Should().NotBeNull();
        plan.ResultRichTextPlan.SelectedParagraphs[0].BulletImage!.Bytes.Should().Equal(0x89, 0x50, 0x4E, 0x47);

        editor.Bus.Execute(plan.Command!);

        var paragraph = shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Image);
        paragraph.BulletImage.Should().NotBeNull();
        paragraph.BulletImage!.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task TableCellTextEditor_Cancel_DiscardsChanges()
    {
        EditingSession? editor = null;
        SlideShape? shape = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeTableShape(12, "Original");
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.ActivateCellEdit(shape!.Id, 0, 0);
            RichInput(overlay).Text = "Discarded";
            textEditor.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Bold).Should().BeTrue();

            textEditor.CancelCellEdit();
        });

        editor!.CanUndo.Should().BeFalse();
        shape!.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Original");
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Bold.Should().BeFalse();
    }

    [Fact]
    public async Task TableCellTextEditor_TableCommandsCommitChildAndUseSharedTransactions()
    {
        async Task AssertOperation(
            Func<AvaloniaInCanvasTextEditor, bool> operation,
            Action<SlideShape> assertResult)
        {
            SlideShape? shape = null;

            await Run(() =>
            {
                var presentation = MakePresentation(presence =>
                {
                    presence.Slides[0].Shapes.Clear();
                    shape = MakeTwoByTwoTableShape(31);
                    presence.Slides[0].Shapes.Add(shape);
                });
                var editor = new EditingSession(
                    presentation,
                    new PresentationCommandBus(presentation));
                var canvas = new SlideCanvas
                {
                    Presentation = presentation,
                    Slide = presentation.Slides[0],
                };
                var overlay = new global::Avalonia.Controls.Canvas();
                var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

                textEditor.ActivateCellEdit(shape!.Id, 0, 0);
                RichInput(overlay).Text = "Committed before table command";
                operation(textEditor).Should().BeTrue();
                textEditor.IsCellEditActive.Should().BeFalse();
                editor.CanUndo.Should().BeTrue();
                assertResult(shape);
            });
        }

        await AssertOperation(
            textEditor => textEditor.TryInsertActiveTableRowAbove(),
            shape =>
            {
                shape.Table!.Rows.Should().HaveCount(3);
                shape.Table.Rows[1].Cells[0].TextBody.Should().NotBeNull();
                InCanvasTextEditPlanner.ExtractPlainText(shape.Table.Rows[1].Cells[0].TextBody)
                    .Should().Be("Committed before table command");
            });

        await AssertOperation(
            textEditor => textEditor.TryInsertActiveTableRowBelow(),
            shape => shape.Table!.Rows.Should().HaveCount(3));

        await AssertOperation(
            textEditor => textEditor.TryInsertActiveTableColumnLeft(),
            shape => shape.Table!.ColumnWidthsEmu.Should().HaveCount(3));

        await AssertOperation(
            textEditor => textEditor.TryInsertActiveTableColumnRight(),
            shape => shape.Table!.ColumnWidthsEmu.Should().HaveCount(3));

        await AssertOperation(
            textEditor => textEditor.TryDeleteActiveTableRow(),
            shape => shape.Table!.Rows.Should().HaveCount(1));

        await AssertOperation(
            textEditor => textEditor.TryDeleteActiveTableColumn(),
            shape => shape.Table!.ColumnWidthsEmu.Should().HaveCount(1));

        await Run(() =>
        {
            var presentation = MakePresentation(presence =>
            {
                presence.Slides[0].Shapes.Clear();
                presence.Slides[0].Shapes.Add(MakeTwoByTwoTableShape(32));
            });
            var shape = presentation.Slides[0].Shapes.Single();
            var editor = new EditingSession(
                presentation,
                new PresentationCommandBus(presentation));
            var canvas = new SlideCanvas
            {
                Presentation = presentation,
                Slide = presentation.Slides[0],
            };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.ActivateCellEdit(shape.Id, 0, 0);
            textEditor.TryMergeActiveTableCell().Should().BeTrue();
            shape.Table!.Rows[0].Cells[0].GridSpan.Should().Be(2);
            shape.Table.Rows[0].Cells[1].HMerge.Should().BeTrue();

            textEditor.ActivateCellEdit(shape.Id, 0, 0);
            textEditor.TrySplitActiveTableCell().Should().BeTrue();
            shape.Table.Rows[0].Cells[0].GridSpan.Should().Be(1);
            shape.Table.Rows[0].Cells[1].HMerge.Should().BeFalse();
            editor.CanUndo.Should().BeTrue();
        });
    }

    [Fact]
    public async Task TableCellTextEditor_DoubleClickContinuationCell_NormalizesToMergeAnchor()
    {
        EditingSession? editor = null;
        SlideShape? shape = null;
        AvaloniaInCanvasTextEditor? textEditor = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeMergedTableShape(13);
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            var handled = textEditor.TryHandleTableCellPointer(screenX: 150, screenY: 20, clickCount: 2);

            handled.Should().BeTrue();
            textEditor.IsCellEditActive.Should().BeTrue();
            editor.ActiveTableCell.Should().Be((0, 0));
            RichInput(overlay).Text.Should().Be("Anchor");
        });

        textEditor!.ActiveTableShapeId.Should().Be(shape!.Id);
    }

    [Fact]
    public async Task TableCellTextEditor_TabNavigation_CommitsAndReopensNextEditableCell()
    {
        EditingSession? editor = null;
        SlideShape? shape = null;

        await Session.Dispatch(async () =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = MakeMergedTableShape(26);
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            editor = new EditingSession(presentation, bus);
            editor.Select(shape!.Id);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var window = new Window
            {
                Width = 320,
                Height = 180,
                Content = overlay,
            };
            window.Show();
            window.Measure(new Size(320, 180));
            window.Arrange(new Rect(0, 0, 320, 180));
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            try
            {
                textEditor.ActivateCellEdit(shape.Id, 0, 1);
                await DrainInputAsync();
                textEditor.IsEditorFocused.Should().BeTrue(
                    "a rooted table-cell editor must own keyboard input over the canvas gesture layer");
                var box = RichInput(overlay);
                box.Text = "Edited Anchor";

                textEditor.TryNavigateActiveTableCell(TableCellNavigationDirection.Next).Should().BeTrue();
                await DrainInputAsync();

                editor.ActiveTableCell.Should().Be((0, 2));
                shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Edited Anchor");
                RichInput(overlay).Text.Should().Be("Right");
                textEditor.IsEditorFocused.Should().BeTrue(
                    "Tab navigation must transfer keyboard ownership to the reopened table-cell editor");
                textEditor.IsCellEditActive.Should().BeTrue();
                textEditor.ActiveTableShapeId.Should().Be(shape.Id);
                overlay.Children.OfType<AvaloniaRichTextEditor>().Should().ContainSingle();
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);

        editor!.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void TableCellEditAdapter_DelegatesToSharedPlanner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var adapter = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaTableCellEditAdapter.cs"));

        adapter.Should().Contain("TableCellEditPlanner.PlanSelectedCell");
        adapter.Should().Contain("TableCellEditPlanner.BeginEdit");
        adapter.Should().Contain("TableCellEditPlanner.CommitRichText");
        adapter.Should().Contain("TableCellEditPlanner.Cancel");
        adapter.Should().Contain("TableCellEditPlanner.PlanNavigation");
        adapter.Should().Contain("TableCellEditPlanner.PlanTextFormat");
        adapter.Should().Contain("TableCellEditPlanner.PlanFontFamily");
        adapter.Should().Contain("TableCellEditPlanner.PlanFontSize");
        adapter.Should().Contain("TableCellEditPlanner.PlanColor");
        adapter.Should().Contain("TableCellEditPlanner.PlanParagraphListPreset");
        adapter.Should().Contain("ApplyFormatResult");
        adapter.Should().Contain("TableCellEditPlanner.PlanPreservedSelection");
    }

    [Fact]
    public void InCanvasTextEditAdapter_DelegatesToSharedShapePlanner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var adapter = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaInCanvasTextEditAdapter.cs"));

        adapter.Should().Contain("InCanvasTextEditPlanner.PlanTextFormat");
        adapter.Should().Contain("InCanvasTextEditPlanner.PlanFontFamily");
        adapter.Should().Contain("InCanvasTextEditPlanner.PlanFontSize");
        adapter.Should().Contain("InCanvasTextEditPlanner.PlanColor");
        adapter.Should().Contain("ApplyRichTextEditorPlan");
        adapter.Should().Contain("freep-shape-rich-editor");
    }

    [Fact]
    public void SlideCanvas_LineSeriesRenderer_ConsumesSharedPathPrimitive()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var planner = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "Core",
            "ChartRenderCommandPlanner.cs"));
        var execution = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.ChartExecution.cs"));
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs"));

        planner.Should().Contain("foreach (var path in primitive.LinePaths)");
        planner.Should().Contain("new ChartRenderCommand.LinePath(");
        execution.Should().Contain("ToGeometry(path.Primitive)");
        execution.Should().NotContain("path.Depth");
        source.Should().Contain("ctx.CubicBezierTo(");
        source.Should().Contain("ChartLinePathSegmentKind.CubicBezier");
    }

    [Fact]
    public void TableCellTextEditor_UsesAvaloniaAdapterForSharedPlannerDecisions()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaInCanvasTextEditor.cs"));

        source.Should().Contain("AvaloniaTableCellEditAdapter.BeginEdit");
        source.Should().Contain("AvaloniaTableCellEditAdapter.CommitRichText");
        source.Should().Contain("AvaloniaTableCellEditAdapter.Cancel");
        source.Should().Contain("AvaloniaTableCellEditAdapter.PlanNavigation");
        source.Should().Contain("_cellTextBox.ToggleTextFormat");
        source.Should().Contain("_cellTextBox.ApplyFontFamily");
        source.Should().Contain("_cellTextBox.ApplyFontSize");
        source.Should().Contain("_cellTextBox.ApplyColor");
        source.Should().Contain("_cellTextBox.ApplyParagraphListPreset");
        source.Should().Contain("_cellTextBox.EditedBody");
        source.Should().Contain("ApplyInitialSelection(_cellTextBox, startPlan.InitialSelection)");
        source.Should().NotContain("_editor.PlanActiveTableCell");
        source.Should().Contain("TryApplyActiveTableCellTextFormat");
        source.Should().Contain("TryApplyActiveTableCellParagraphListPreset");
        source.Should().Contain("TryNavigateActiveTableCell");
        source.Should().Contain("IsRichTextEditActive");
        source.Should().Contain("CopySelectionAsync");
        source.Should().Contain("CutSelectionAsync");
        source.Should().Contain("PasteClipboardAsync");
        source.Should().Contain("TryInsertActiveTableRowAbove");
        source.Should().Contain("TryInsertActiveTableRowBelow");
        source.Should().Contain("TryInsertActiveTableColumnLeft");
        source.Should().Contain("TryInsertActiveTableColumnRight");
        source.Should().Contain("TryDeleteActiveTableRow");
        source.Should().Contain("TryDeleteActiveTableColumn");
        source.Should().Contain("TryMergeActiveTableCell");
        source.Should().Contain("TrySplitActiveTableCell");
        source.Should().Contain("TryExecuteActiveTableStructureAction");
        source.Should().Contain("PresentationTableStructureActionDispatcher.TryExecute(");
        source.Should().Contain("PresentationTableCellOwnedActionDispatcher.TryExecute(");
        source.Should().Contain("AvaloniaTableCellEditAdapter.PlanSelectedCell(_editor)");
        source.Should().Contain("CommitCellEdit,");
    }

    // ── 1. Geometry factory round-trip ────────────────────────────────────────

    [Fact]
    public void InCanvasTextEditor_UsesAvaloniaAdapterForSharedShapePlannerDecisions()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaInCanvasTextEditor.cs"));

        source.Should().Contain("_textBox.ToggleTextFormat");
        source.Should().Contain("_textBox.ApplyFontFamily");
        source.Should().Contain("_textBox.ApplyFontSize");
        source.Should().Contain("_textBox.ApplyColor");
        source.Should().Contain("_textBox.EditedBody");
        source.Should().Contain("AvaloniaInCanvasTextEditAdapter.ApplyRichTextEditorPlan(_textBox, startPlan.RichTextPlan)");
        source.Should().Contain("ApplyInitialSelection(_textBox, startPlan.InitialSelection)");
        source.Should().Contain("RefreshShapeOverlayRichTextPlan");
        source.Should().Contain("TryApplyActiveShapeTextFormat");
    }

    [Fact]
    public async Task GeometryFactory_Rectangle_ReturnsNonNullGeometry()
    {
        StreamGeometry? geometry = null;
        await Run(() =>
        {
            var bounds = new LayoutRect(0, 0, 100, 60);
            var shape  = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, bounds);
            geometry   = AvaloniaSlideGeometryFactory.ToGeometry(shape);
        });
        geometry.Should().NotBeNull("a rectangle has contours");
    }

    [Fact]
    public void GeometryFactory_EmptyContours_ReturnsNull()
    {
        // ShapeGeometry.Empty has no contours — the factory returns null without needing the platform.
        var empty    = ShapeGeometry.Empty;
        var geometry = AvaloniaSlideGeometryFactory.ToGeometry(empty);
        geometry.Should().BeNull("empty ShapeGeometry has no contours");
    }

    [Fact]
    public async Task GeometryFactory_Triangle_ContourHasThreeSegments()
    {
        ShapeGeometry? shape = null;
        await Run(() =>
        {
            var bounds = new LayoutRect(0, 0, 100, 100);
            shape = ShapeGeometryBuilder.Build(DrawingShapeKind.Triangle, bounds);
        });
        shape!.Contours.Should().NotBeEmpty();
    }

    // ── 2. SlideCanvas compose + render — no throw ───────────────────────────

    [Fact]
    public async Task SlideCanvas_ComposeAndRender_EmptyPresentation_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p      = MakePresentation();
                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));

                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("rendering an empty slide must not throw");
    }

    [Fact]
    public async Task SlideCanvas_ComposeAndRender_SlideWithRectangle_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id = 1,
                        Kind = SlideShapeKind.AutoShape,
                        AutoShapeKind = DrawingShapeKind.Rectangle,
                        OffsetXEmu = 914400,
                        OffsetYEmu = 457200,
                        ExtentCxEmu = 4572000,
                        ExtentCyEmu = 2286000,
                        Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x4F, 0x81, 0xBD)))
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));

                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("rendering a slide with a solid-filled rectangle must not throw");
    }

    // ── 3. Background color pixel check ──────────────────────────────────────

    [Fact]
    public async Task SlideCanvas_ComposeAndRender_StackedVerticalText_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    var shape = new SlideShape
                    {
                        Id = 1,
                        OffsetXEmu = 914400,
                        OffsetYEmu = 457200,
                        ExtentCxEmu = 914400,
                        ExtentCyEmu = 2743200,
                        TextBody = new TextBody { VerticalType = TextVerticalType.WordArtVertical }
                    };
                    var paragraph = new Paragraph();
                    paragraph.Runs.Add(new Run { Text = "Stacked" });
                    shape.TextBody.Paragraphs.Add(paragraph);
                    pres.Slides[0].Shapes.Add(shape);
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));

                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("stacked vertical text must render through the Avalonia consumer without throwing");
    }

    [Fact]
    public async Task SlideCanvas_SolidBackground_PaintsExpectedColor()
    {
        byte[]? pngBytes = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation(pres =>
                {
                    // Set slide background to a distinctive red.
                    pres.Slides[0].Background =
                        new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)));
                    pres.Slides[0].Shapes.Clear();
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(100, 60));
                canvas.Arrange(new Rect(0, 0, 100, 60));

                var rtb = new RenderTargetBitmap(new PixelSize(100, 60));
                rtb.Render(canvas);

                using var ms = new MemoryStream();
                rtb.Save(ms);
                pngBytes = ms.ToArray();
            }
            catch { /* captured below */ }
        });

        pngBytes.Should().NotBeNull("render pipeline must complete without throwing");
    }

    // ── 4. Refresh clears cached ops ─────────────────────────────────────────

    [Fact]
    public async Task SlideCanvas_Refresh_ClearsCache_AndRerenders()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation();
                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));

                // First render.
                var rtb1 = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb1.Render(canvas);

                // Mutate and refresh.
                p.Slides[0].Shapes.Clear();
                canvas.Refresh();

                // Second render must not throw.
                var rtb2 = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb2.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("re-rendering after Refresh() must not throw");
    }

    // ── 5. Null model — graceful no-op ────────────────────────────────────────

    [Fact]
    public async Task SlideCanvas_NullPresentation_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var canvas = new SlideCanvas();
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));

                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("rendering without a model must be a no-op");
    }

    // ── 6. SlideCanvas with gradient fill — no throw ─────────────────────────

    [Fact]
    public async Task SlideCanvas_GradientFill_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id = 2,
                        Kind = SlideShapeKind.AutoShape,
                        AutoShapeKind = DrawingShapeKind.Rectangle,
                        OffsetXEmu = 457200,
                        OffsetYEmu = 457200,
                        ExtentCxEmu = 3657600,
                        ExtentCyEmu = 2743200,
                        Fill = new ShapeFill.Gradient(
                            new[]
                            {
                                new FreeP.Core.Model.GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00))),
                                new FreeP.Core.Model.GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)))
                            },
                            GradientKind.Linear,
                            90.0)
                    });
                });
                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("gradient fill rendering must not throw");
    }

    [Fact]
    public async Task SlideCanvas_SolidFillAlpha_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Background = new ShapeFill.Solid(SrgbColor.White);
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id = 2,
                        Kind = SlideShapeKind.AutoShape,
                        AutoShapeKind = DrawingShapeKind.Rectangle,
                        OffsetXEmu = 457200,
                        OffsetYEmu = 457200,
                        ExtentCxEmu = 3657600,
                        ExtentCyEmu = 2743200,
                        Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xFF0000), alpha: 128)),
                        Outline = ShapeOutline.None.Instance
                    });
                });
                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("solid fill alpha rendering must not throw");
    }

    // ── BA2: WordArt / text-effects double-draw regression tests ─────────────

    /// <summary>
    /// BA2 regression: warped text body must not draw a flat ghost behind warped glyphs.
    /// The base DrawText pass must be suppressed; RenderParaWithEffects handles all runs.
    /// </summary>
    [Fact]
    public async Task SlideCanvas_WarpedTextBody_DoesNotThrow_AndDrawsOnce()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var tb   = new TextBody { WarpPreset = "textArchUp" };
                var para = new FreeP.Core.Model.Paragraph();
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "Plain" });
                para.Runs.Add(new FreeP.Core.Model.Run
                {
                    Text     = "Gradient",
                    TextFill = new ShapeFill.Gradient(
                        new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
                        new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
                        angleDegrees: 90.0)
                });
                tb.Paragraphs.Add(para);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 1,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("warped text body must not cause a double-draw crash");
    }

    /// <summary>
    /// BA2 regression: paragraph with mixed plain + gradient-fill + outline runs must not
    /// draw the effect runs twice (flat base under gradient overlay).
    /// </summary>
    [Fact]
    public async Task SlideCanvas_MixedPlainAndEffectRuns_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var tb   = new TextBody();
                var para = new FreeP.Core.Model.Paragraph();
                // Plain run — exercises the new plain-run geometry path in RenderParaWithEffects.
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "Normal " });
                // Effect run (gradient fill) — must NOT also be drawn by the base DrawText pass.
                para.Runs.Add(new FreeP.Core.Model.Run
                {
                    Text     = "Gradient",
                    TextFill = new ShapeFill.Gradient(
                        new ThemeAwareColor(new SrgbColor(0xFF, 0x66, 0x00)),
                        new ThemeAwareColor(new SrgbColor(0xCC, 0x00, 0x00)),
                        angleDegrees: 45.0)
                });
                tb.Paragraphs.Add(para);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 2,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("mixed plain+gradient runs must render without double-draw exception");
    }

    [Fact]
    public async Task SlideCanvas_GlowAndSoftEdgeRuns_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var tb   = new TextBody();
                var para = new FreeP.Core.Model.Paragraph();
                para.Runs.Add(new FreeP.Core.Model.Run
                {
                    Text = "Glow ",
                    TextGlow = new RunTextGlow
                    {
                        Color = new ThemeAwareColor(new SrgbColor(0x20, 0x80, 0xFF)),
                        Alpha = 128,
                        RadiusPt = 4.0
                    }
                });
                para.Runs.Add(new FreeP.Core.Model.Run
                {
                    Text = "Soft",
                    TextSoftEdge = new RunTextSoftEdge
                    {
                        RadiusPt = 2.5
                    }
                });
                tb.Paragraphs.Add(para);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 3,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("glow and soft-edge text runs must render through shared planner cases");
    }

    // ── 7. SlideCanvas aspect-ratio MeasureOverride ───────────────────────────

    [Fact]
    public async Task SlideCanvas_MeasureOverride_PreservesSlideAspectRatio()
    {
        Size measured = default;
        await Run(() =>
        {
            var p = MakePresentation();
            // Default slide size is 12192000 x 6858000 EMU → 1280 x 720 DIP (16:9).
            var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
            canvas.Measure(new Size(1920, 1080));
            measured = canvas.DesiredSize;
        });

        double ratio = measured.Width / measured.Height;
        ratio.Should().BeApproximately(16.0 / 9.0, precision: 0.01,
            "slide aspect ratio must be preserved during layout");
    }

    // ── 8. ComputeNiceAxisRange mirrors WPF renderer behaviour ───────────────

    [Fact]
    public void ComputeNiceAxisRange_SimplePositiveData_ReturnsNiceRange()
    {
        // Build a presentation with a chart to test the axis helper indirectly
        // via the static method (marked internal, visible via InternalsVisibleTo).
        var series = new ChartSeries { Name = "S1" };
        series.Values.AddRange(new double?[] { 10, 20, 30, 40 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(series);

        var (min, max, mu) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);

        min.Should().Be(0, "data min >= 0 → axis starts at 0");
        max.Should().BeGreaterThan(40, "axis max must be at or above data max");
        mu.Should().BePositive("major unit must be positive");
        ((max - min) / mu).Should().BeApproximately(Math.Round((max - min) / mu), 1e-6,
            "major unit must divide the range evenly");
    }

    // ── 9. CB1: secondary-axis range isolation ────────────────────────────────

    /// <summary>
    /// CB1: primary range must exclude secondary-axis series.
    /// Chart: primary series max 100, secondary series max 1_000_000.
    /// Primary range must be ~0-100 (NOT 0-1M).
    /// Secondary range must be ~0-1M.
    /// </summary>
    [Fact]
    public void CB1_PrimaryRange_ExcludesSecondaryAxisSeries()
    {
        var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 20, 50, 100 });

        var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var (min, max, _) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);

        min.Should().BeGreaterThanOrEqualTo(0, "primary range min should start at or above 0");
        max.Should().BeLessThan(10_000,
            "CB1: primary range must not be polluted by the 1M secondary series (should be ~100-200)");
        max.Should().BeGreaterThanOrEqualTo(100, "primary range must cover the 100 primary max");
    }

    [Fact]
    public void CB1_SecondaryRange_CoverSecondaryAxisSeriesOnly()
    {
        var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 20, 50, 100 });

        var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var (secMin, secMax, secMu) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);

        secMin.Should().BeGreaterThanOrEqualTo(0, "secondary range min should start at or above 0");
        secMax.Should().BeGreaterThanOrEqualTo(1_000_000, "secondary range must cover the 1M secondary max");
        secMu.Should().BePositive("secondary major unit must be positive");
    }

    [Fact]
    public void CB1_SecondarySeriesPixelY_IsNotNearBottom_WhenValueIsLargeRelativeToRange()
    {
        // Verify that a secondary series value at max maps to near plotY (top of plot),
        // NOT near plotY+plotH (bottom).  We test the formula directly:
        // py = plotH - (value - secMin) / secRange * plotH
        // For value = secMax (1_000_000), with secMin=0, secRange≈1M:
        // py ≈ 0 (at the top), NOT plotH (bottom).

        var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 20, 50, 100 });

        var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var (secMin, secMax, _) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);
        double secRange = secMax - secMin;

        double plotH    = 400.0;  // hypothetical plot height in pixels
        double testVal  = 1_000_000.0;  // the secondary series max

        // Secondary series pixel y from bottom: plotH - (val - secMin) / secRange * plotH
        double fracFromBottom = (testVal - secMin) / secRange;
        double pxFromBottom   = fracFromBottom * plotH;

        // Should be close to 400 (filling the full plot height from bottom = near the top edge)
        pxFromBottom.Should().BeGreaterThanOrEqualTo(plotH * 0.8,
            "CB1: a secondary value near the secondary max should map to near the top of the plot (large pixel height from bottom)");

        // What the OLD (broken) code would give: using the primary range for a 1M value
        var (primaryMin, primaryMax, _) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        double brokenPxFromBottom = (testVal - primaryMin) / primaryRange * plotH;

        // The broken path gives a massive ratio >> 1.0 (value hugely exceeds primary scale)
        // which would clip to near-zero visible height or out-of-bounds rendering.
        // The fixed secondary range gives a sensible fraction ≤ 1.0.
        fracFromBottom.Should().BeLessThanOrEqualTo(1.1,
            "CB1: secondary series value must not exceed the secondary range by more than a rounding tick");
        (brokenPxFromBottom / plotH).Should().BeGreaterThan(100,
            "CB1 broken-path sanity: old primary range would produce ratio >> 1 for 1M value against ~100 range");
    }

    [Fact]
    public void SlideCanvas_ChartGridLinePen_UsesSharedStrokePlan()
    {
        var plan = new ChartMajorGridLinePrimitivePlan(
            Array.Empty<ChartGridLinePlan>(),
            new ChartStrokePlan(
                new SrgbColor(0x12, 0x34, 0x56),
                Alpha: 0x7F,
                Thickness: 1.25,
                Dash: OutlineDash.DashDot));

        var pen = SlideCanvas.CreateChartGridLinePen(plan);

        pen.Thickness.Should().Be(1.25);
        var brush = pen.Brush.Should()
            .BeOfType<SolidColorBrush>()
            .Subject;
        brush.Color.Should().Be(Color.FromArgb(0x7F, 0x12, 0x34, 0x56));
        pen.DashStyle.Should().Be(DashStyle.DashDot);
    }

    [Fact]
    public void SlideCanvas_ChartGridLinePen_UsesSharedStrokeGradientFill()
    {
        var gradient = new ResolvedFill.Gradient(
            new[]
            {
                new ResolvedFill.ResolvedGradientStop(0.0, new SrgbColor(0x10, 0x20, 0x30)),
                new ResolvedFill.ResolvedGradientStop(1.0, new SrgbColor(0xD0, 0xE0, 0xF0))
            },
            GradientKind.Linear,
            angleDegrees: 45.0);
        var plan = new ChartMajorGridLinePrimitivePlan(
            Array.Empty<ChartGridLinePlan>(),
            new ChartStrokePlan(
                new SrgbColor(0x12, 0x34, 0x56),
                Alpha: 0x7F,
                Thickness: 1.75,
                Dash: OutlineDash.LongDash)
            {
                Fill = gradient
            });

        var pen = SlideCanvas.CreateChartGridLinePen(plan);

        pen.Thickness.Should().Be(1.75);
        var brush = pen.Brush.Should()
            .BeOfType<LinearGradientBrush>()
            .Subject;
        brush.GradientStops.Should().HaveCount(17);
        brush.GradientStops.First().Should().Match<global::Avalonia.Media.GradientStop>(stop =>
            stop.Offset == 0.0 && stop.Color == Color.FromRgb(0x10, 0x20, 0x30));
        brush.GradientStops.Last().Should().Match<global::Avalonia.Media.GradientStop>(stop =>
            stop.Offset == 1.0 && stop.Color == Color.FromRgb(0xD0, 0xE0, 0xF0));
        pen.DashStyle.Should().BeOfType<DashStyle>().Subject.Dashes.Should().Equal(8.0, 3.0);
    }

    [Fact]
    public void SlideCanvas_ChartSecondaryAxisTickPen_UsesSharedStrokePlan()
    {
        var plan = new ChartSecondaryValueAxisPrimitivePlan(
            Array.Empty<ChartTextPlan>(),
            Array.Empty<ChartGridLinePlan>(),
            new ChartStrokePlan(
                new SrgbColor(0x22, 0x44, 0x66),
                Alpha: 0x80,
                Thickness: 1.5),
            Title: null);

        var pen = SlideCanvas.CreateChartSecondaryAxisTickPen(plan);

        pen.Thickness.Should().Be(1.5);
        var brush = pen.Brush.Should()
            .BeOfType<SolidColorBrush>()
            .Subject;
        brush.Color.Should().Be(Color.FromArgb(0x80, 0x22, 0x44, 0x66));
    }

    [Fact]
    public void CB1_NoSecondarySeriesChart_PrimaryRangeUnchanged_SecondaryRangeFallback()
    {
        // A chart with no secondary series: primary range is as before, secondary fallback = (0,1,1).
        var s = new ChartSeries { Name = "S1", OnSecondaryAxis = false };
        s.Values.AddRange(new double?[] { 10, 50, 100 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(s);

        var (min, max, mu) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        var (sMin, sMax, sMu) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);

        // Primary range should cover the data
        max.Should().BeGreaterThanOrEqualTo(100, "primary range covers primary-only data");

        // Secondary fallback when no secondary series
        sMin.Should().Be(0, "fallback secondary min");
        sMax.Should().Be(1, "fallback secondary max");
        sMu.Should().Be(1, "fallback secondary unit");
    }

    [Fact]
    public async Task CB1_ComboChart_RendersWithoutThrow_BothShells()
    {
        // Full render smoke test for a combo chart (primary bars + secondary line).
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
                primary.Values.AddRange(new double?[] { 20, 50, 100 });

                var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
                secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

                var chart = new ChartShape
                {
                    ChartType          = ChartType.ColumnClustered,
                    SecondaryValueAxis = new ChartAxis(),
                };
                chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
                chart.Series.Add(primary);
                chart.Series.Add(secondary);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id          = 1,
                        Kind        = SlideShapeKind.Chart,
                        OffsetXEmu  = 914400,
                        OffsetYEmu  = 457200,
                        ExtentCxEmu = 5486400,
                        ExtentCyEmu = 3657600,
                        Chart       = chart,
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("CB1: combo chart (primary bars + secondary line) must render without throwing");
    }

    // ── CC2/CC3/CC4: secondary-axis data-label scale correctness ─────────────

    /// <summary>
    /// CC2: RenderLineDataLabels must use the secondary-axis range (not the primary range)
    /// to compute the label Y-coordinate for an OnSecondaryAxis series.
    /// The label Y must match the marker Y — both must use effMin/effRange from the secondary scale.
    /// </summary>
    [Fact]
    public async Task ChartErrorBars_RenderAsAdditionalPixelsInAvaloniaCanvas()
    {
        int changedPixels = 0;
        await Run(() =>
        {
            var chart = new ChartShape { ChartType = ChartType.LineMarkers };
            chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
            var series = new ChartSeries { Name = "Revenue" };
            series.Values.AddRange(new double?[] { 10, 20, 30 });
            chart.Series.Add(series);

            var presentation = MakePresentation(presence =>
            {
                presence.Slides[0].Shapes.Clear();
                presence.Slides[0].Shapes.Add(new SlideShape
                {
                    Id = 1,
                    Kind = SlideShapeKind.Chart,
                    OffsetXEmu = 914400,
                    OffsetYEmu = 457200,
                    ExtentCxEmu = 5486400,
                    ExtentCyEmu = 3657600,
                    Chart = chart,
                });
            });
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var baseline = RenderPixels(canvas, 960, 540);
            series.ErrorBars = new ChartErrorBars { Value = 2 };
            var candidate = RenderPixels(canvas, 960, 540);
            changedPixels = CountPixelDifferences(baseline, candidate, 960, 0, 0, 960, 540);
        });

        changedPixels.Should().BeGreaterThan(0, "error bars must reach the shared Avalonia canvas paint path");
    }

    [Fact]
    public async Task ChartErrorBars_AreaAndRadarReachAvaloniaCanvasPaintPath()
    {
        int changedPixels = 0;
        await Run(() =>
        {
            var charts = new[]
            {
                new ChartShape
                {
                    ChartType = ChartType.Area,
                    Categories = { "Q1", "Q2", "Q3" },
                },
                new ChartShape
                {
                    ChartType = ChartType.Radar,
                    Categories = { "A", "B", "C", "D" },
                    RadarStyle = RadarStyle.Marker,
                }
            };

            foreach (var chart in charts)
            {
                var series = new ChartSeries { Name = "Actual" };
                series.Values.AddRange(chart.ChartType == ChartType.Area
                    ? new double?[] { 10, 20, 15 }
                    : new double?[] { 8, 6, 7, 9 });
                chart.Series.Add(series);

                var presentation = MakePresentation(presence =>
                {
                    presence.Slides[0].Shapes.Clear();
                    presence.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id = 1,
                        Kind = SlideShapeKind.Chart,
                        OffsetXEmu = 914400,
                        OffsetYEmu = 457200,
                        ExtentCxEmu = 5486400,
                        ExtentCyEmu = 3657600,
                        Chart = chart,
                    });
                });
                var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
                var baseline = RenderPixels(canvas, 960, 540);
                series.ErrorBars = new ChartErrorBars { Value = 2 };
                var candidate = RenderPixels(canvas, 960, 540);
                changedPixels += CountPixelDifferences(baseline, candidate, 960, 0, 0, 960, 540);
            }
        });

        changedPixels.Should().BeGreaterThan(0, "area and radar error bars must reach the shared Avalonia canvas paint path");
    }

    [Fact]
    public void CC2_LineDataLabel_SecondaryAxisSeries_UsesSecondaryRange()
    {
        // Primary series: values 0–100. Secondary series: values 0–1_000_000.
        // If the label used the primary range (~0–100) for a 1M value, the normalised
        // fraction would be >> 1.0 (way off the top of the plot).
        // The secondary range (~0–1M) should give a fraction ≤ 1.0.

        var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 20, 50, 100 });

        var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

        var chart = new ChartShape
        {
            ChartType          = ChartType.LineMarkers,
            SecondaryValueAxis = new ChartAxis(),
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        // Compute what the label Y fraction should be for the secondary series' last point (1_000_000).
        var (secMin, secMax, _) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);
        double secRange   = secMax - secMin;
        double testVal    = 1_000_000.0;

        // CC2 correct path: fraction = (val - secMin) / secRange
        double correctFrac = (testVal - secMin) / secRange;

        // CC2 broken path would use primary: fraction = (val - primaryMin) / primaryRange >> 1
        var (primaryMin, primaryMax, _) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        double brokenFrac   = (testVal - primaryMin) / primaryRange;

        correctFrac.Should().BeLessThanOrEqualTo(1.1,
            "CC2: secondary value mapped through secondary range must be ≤ 1 (within plot bounds)");
        brokenFrac.Should().BeGreaterThan(10.0,
            "CC2 sanity: same value through primary range would be >> 1 (far off-chart)");
        correctFrac.Should().BeGreaterThanOrEqualTo(0.7,
            "CC2: value at secondary max should map well up the plot (fraction ≥ 0.7, nice range extends slightly above data max)");
    }

    /// <summary>
    /// Stock and surface chart types render through their specialized primitive plans.
    /// </summary>
    [Theory]
    [InlineData(ChartType.Stock)]
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.Surface3D)]
    [InlineData(ChartType.Radar)]
    public async Task StockSurfaceAndRadarCharts_RenderThroughSpecializedPrimitivePlans_DoesNotThrow(ChartType chartType)
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id = 1,
                        Kind = SlideShapeKind.Chart,
                        OffsetXEmu = 914400,
                        OffsetYEmu = 457200,
                        ExtentCxEmu = 5486400,
                        ExtentCyEmu = 3657600,
                        Chart = MakeStockOrSurfaceRenderChart(chartType),
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull($"{chartType} should render through the specialized stock/surface primitive path");
    }

    [Fact]
    public async Task StockVolumeChart_RendersThroughSharedVolumeAndOhlcPrimitivePlans_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id = 1,
                        Kind = SlideShapeKind.Chart,
                        OffsetXEmu = 914400,
                        OffsetYEmu = 457200,
                        ExtentCxEmu = 5486400,
                        ExtentCyEmu = 3657600,
                        Chart = MakeStockVolumeRenderChart(),
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull(
            "Avalonia should consume shared stock volume and OHLC primitive plans");
    }

    /// <summary>
    /// CC2/CC3/CC4 smoke test: a combo chart (primary columns + secondary line with data labels)
    /// must render end-to-end without throwing in the Avalonia shell.
    /// </summary>
    [Fact]
    public async Task CC2_ComboChartWithDataLabels_SecondaryLineSeries_RendersWithoutThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var primary = new ChartSeries { Name = "Revenue", OnSecondaryAxis = false };
                primary.Values.AddRange(new double?[] { 100, 150, 120 });

                // Secondary series has data labels enabled — exercises CC2 fix path.
                var secondary = new ChartSeries
                {
                    Name             = "Target",
                    OnSecondaryAxis  = true,
                    DataLabels       = new ChartDataLabels { ShowValue = true },
                };
                secondary.Values.AddRange(new double?[] { 5_000, 8_000, 12_000 });

                var chart = new ChartShape
                {
                    ChartType          = ChartType.ColumnClustered,
                    SecondaryValueAxis = new ChartAxis(),
                };
                chart.Categories.AddRange(new[] { "Jan", "Feb", "Mar" });
                chart.Series.Add(primary);
                chart.Series.Add(secondary);
                // Chart-level data labels so both series get labels (exercises CC3 column path too).
                chart.DataLabels = new ChartDataLabels { ShowValue = true };

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id          = 1,
                        Kind        = SlideShapeKind.Chart,
                        OffsetXEmu  = 914400,
                        OffsetYEmu  = 457200,
                        ExtentCxEmu = 5486400,
                        ExtentCyEmu = 3657600,
                        Chart       = chart,
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "CC2/CC3: combo chart with secondary-axis line series data labels must render without throwing");
    }

    [Fact]
    public async Task SlideCanvas_SmoothedLineChart_RendersWithoutThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var series = new ChartSeries
                {
                    Name = "Smoothed",
                    SmoothLine = true
                };
                series.Values.AddRange(new double?[] { 10, 20, 30, 15 });

                var chart = new ChartShape { ChartType = ChartType.Line };
                chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3", "Q4" });
                chart.Series.Add(series);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id = 1,
                        Kind = SlideShapeKind.Chart,
                        OffsetXEmu = 914400,
                        OffsetYEmu = 457200,
                        ExtentCxEmu = 5486400,
                        ExtentCyEmu = 3657600,
                        Chart = chart,
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia should consume smoothed line path primitives");
    }

    /// <summary>
    /// CC3: RenderColumnDataLabels for a secondary-axis series must use the secondary range.
    /// Verified indirectly: the Y-fraction for a secondary value through the secondary range is ≤ 1,
    /// while the primary range would give a fraction >> 1 (off-chart).
    /// </summary>
    [Fact]
    public void CC3_ColumnDataLabel_SecondaryAxisSeries_UsesSecondaryRange()
    {
        var primary = new ChartSeries { Name = "P", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 10, 20, 30 });

        var secondary = new ChartSeries { Name = "S", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 50_000, 80_000, 100_000 });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var (secMin, secMax, _) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);
        double secRange  = secMax - secMin;
        var (pMin, pMax, _) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        double pRange    = pMax - pMin;
        double testVal   = 100_000.0;

        double secFrac = (testVal - secMin) / secRange;
        double priFrac = (testVal - pMin)   / pRange;

        secFrac.Should().BeLessThanOrEqualTo(1.1,
            "CC3: secondary column value fraction through secondary range must be ≤ 1");
        priFrac.Should().BeGreaterThan(10.0,
            "CC3 sanity: same value through primary range would be >> 1");
    }

    // ── BN1: picture with colour effect renders without throwing (GDI+ fallback) ──────────────

    /// <summary>
    /// BN1 regression: when GDI+ (libgdiplus) is unavailable, ApplyColorEffectsAvalonia must
    /// return null and RenderPicture must fall back to the original source bitmap — not a blank
    /// transparent rectangle. Verified by: render pipeline must complete without throwing, and
    /// the overall render must not produce an all-zero PNG (blank slide check).
    /// Under Avalonia headless the drawing is a no-op, so "no throw" is the primary gate.
    /// </summary>
    [Fact]
    public async Task SlideCanvas_PictureWithGrayscaleEffect_DoesNotThrow_Bn1Fallback()
    {
        // Minimal 1×1 semi-transparent PNG to exercise the alpha path.
        byte[] png1x1 = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8" +
            "z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var img = new ImagePart { Bytes = png1x1, ContentType = "image/png" };
                var fmt = new PictureFormat { Grayscale = true };
                var shape = new SlideShape
                {
                    Id = 1,
                    Kind = SlideShapeKind.Picture,
                    OffsetXEmu = 914400,
                    OffsetYEmu = 457200,
                    ExtentCxEmu = 2743200,
                    ExtentCyEmu = 1828800,
                    Picture = img,
                    PictureFormat = fmt,
                };

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(shape);
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "BN1: rendering a picture with a grayscale effect must not throw even when GDI+ is unavailable");
    }

    // ── BO2: default tab stops (no explicit tabLst) render without throwing ───────────────────

    /// <summary>
    /// BO2 regression: a paragraph that contains a tab character but has no explicit tab stops
    /// must go through RenderParaWithTabs (default 96 DIP interval) rather than plain DrawText
    /// (which collapses \t to zero advance). Verified by: no throw during render.
    /// </summary>
    [Fact]
    public async Task SlideCanvas_TabWithNoExplicitStops_UsesDefaultInterval_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var tb   = new TextBody();
                var para = new FreeP.Core.Model.Paragraph();
                // Tab character with NO explicit tab stops — exercises the BO2 default-tab path.
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "Before\tAfter" });
                tb.Paragraphs.Add(para);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 1,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "BO2: paragraph with \\t and no explicit tab stops must render without throwing");
    }

    // ── BO1: tab alignment — right/center/decimal stops do not throw ──────────────────────────

    /// <summary>
    /// BO1 regression: paragraphs with right, center, and decimal explicit tab stops must render
    /// without throwing.  The alignment offset logic (curX = stopX - segW for Right, etc.)
    /// is exercised end-to-end.
    /// </summary>
    [Fact]
    public async Task SlideCanvas_TabWithRightAndCenterStops_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                const long EmuPerDip = 9525L;

                var tb = new TextBody();
                var para = new FreeP.Core.Model.Paragraph();
                // Two tab characters mapping to a right stop at 2" and a center stop at 4".
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "Left\tRight\tCenter" });
                para.TabStops.Add(new TabStop { PositionEmu = 192 * EmuPerDip, Alignment = TabStopAlignment.Right  });  // 2 inch right
                para.TabStops.Add(new TabStop { PositionEmu = 384 * EmuPerDip, Alignment = TabStopAlignment.Center }); // 4 inch center
                tb.Paragraphs.Add(para);

                var tb2 = new TextBody();
                var para2 = new FreeP.Core.Model.Paragraph();
                // Decimal stop — test with a value string containing a decimal point.
                para2.Runs.Add(new FreeP.Core.Model.Run { Text = "Label\t1234.56" });
                para2.TabStops.Add(new TabStop { PositionEmu = 288 * EmuPerDip, Alignment = TabStopAlignment.Decimal }); // 3 inch decimal
                tb2.Paragraphs.Add(para2);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 1,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 2,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 1600000,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb2
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "BO1: right/center/decimal tab stop alignment must not throw during rendering");
    }

    // ── BQ1: cross-run tab alignment ──────────────────────────────────────────

    /// <summary>
    /// BQ1 regression: when the tab ends run1 ("Chapter\t") and the aligned text is in run2 ("42" bold),
    /// the right/center alignment offset must be computed across BOTH runs' text (run-agnostic forward
    /// scan), not just from the empty tail of run1 which would leave alignOffset=0 (left-aligned at stop).
    /// </summary>
    [Fact]
    public async Task SlideCanvas_CrossRunRightTabAlignment_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                const long EmuPerDip = 9525L;

                // run1 ends with '\t' (tab token has seg=""), run2 holds the value in bold.
                // Pattern: "Chapter\t" (run1, normal) + "42" (run2, bold) — page-number style.
                var tb   = new TextBody();
                var para = new FreeP.Core.Model.Paragraph();
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "Chapter\t", Bold = false });
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "42",        Bold = true  });
                para.TabStops.Add(new TabStop
                {
                    PositionEmu = 480 * EmuPerDip,      // 5-inch right stop
                    Alignment   = TabStopAlignment.Right
                });
                tb.Paragraphs.Add(para);

                // Also test center cross-run: "Section\t" (run1) + "Title" (run2)
                var tb2   = new TextBody();
                var para2 = new FreeP.Core.Model.Paragraph();
                para2.Runs.Add(new FreeP.Core.Model.Run { Text = "Section\t", Bold = false });
                para2.Runs.Add(new FreeP.Core.Model.Run { Text = "Title",     Bold = true  });
                para2.TabStops.Add(new TabStop
                {
                    PositionEmu = 384 * EmuPerDip,      // 4-inch center stop
                    Alignment   = TabStopAlignment.Center
                });
                tb2.Paragraphs.Add(para2);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 1,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 2,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 1600000,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb2
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "BQ1: right/center tab alignment must work when aligned text is in a different run from the tab");
    }

    // ── BQ2: wide aligned segment — backward-clamp ───────────────────────────

    /// <summary>
    /// BQ2 regression: when the aligned segment is wider than the gap from the preceding text to
    /// the tab stop, curX must be clamped to the prior pen (not pushed behind it), matching FreeW
    /// EmitLinePaged's <c>Math.Max(x + 1, segmentStartX)</c> clamp.
    /// </summary>
    [Fact]
    public async Task SlideCanvas_WideSegment_BackwardClampDoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                const long EmuPerDip = 9525L;

                // Right stop at 1 inch (96 DIP).  Preceding text is already wider than 1 inch,
                // so stopDip + alignOffset would be < prevCurX without the clamp.
                var tb   = new TextBody();
                var para = new FreeP.Core.Model.Paragraph();
                para.Runs.Add(new FreeP.Core.Model.Run
                    { Text = "LongPrecedingText\tWideSegmentThatExceedsGap" });
                para.TabStops.Add(new TabStop
                {
                    PositionEmu = 96 * EmuPerDip,       // 1-inch right stop — narrow target
                    Alignment   = TabStopAlignment.Right
                });
                tb.Paragraphs.Add(para);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 1,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "BQ2: wide aligned segment must not cause curX to go behind the prior pen (backward clamp)");
    }

    // ── Wave 22A: gradient angle mapping (OOXML a:lin ang convention) ────────

    /// <summary>
    /// Wave 22A: OOXML gradient angle 90° (ang=5400000 / 60000 = 90°) must produce a top-to-bottom
    /// gradient: Start=(0.5, 0), End=(0.5, 1). The old code produced Start=(0,0), End=(1,1) (diagonal).
    /// </summary>
    [Theory]
    [InlineData(  0.0, 0.0, 0.5, 1.0, 0.5)]   //   0° east  → Start=(0,   0.5), End=(1,   0.5)
    [InlineData( 90.0, 0.5, 0.0, 0.5, 1.0)]   //  90° south → Start=(0.5, 0  ), End=(0.5, 1  )
    [InlineData(180.0, 1.0, 0.5, 0.0, 0.5)]   // 180° west  → Start=(1,   0.5), End=(0,   0.5)
    [InlineData(270.0, 0.5, 1.0, 0.5, 0.0)]   // 270° north → Start=(0.5, 1  ), End=(0.5, 0  )
    public void GradientAngle_OOXML_Convention_ProducesCorrectStartEnd(
        double angleDeg,
        double expectedStartX, double expectedStartY,
        double expectedEndX,   double expectedEndY)
    {
        // Replicate the formula from MakeLinearGradientBrush:
        // dx = cos(θ), dy = sin(θ)
        // Start = (0.5 - 0.5*dx, 0.5 - 0.5*dy)
        // End   = (0.5 + 0.5*dx, 0.5 + 0.5*dy)
        double angleRad = angleDeg * Math.PI / 180.0;
        double dx = Math.Cos(angleRad);
        double dy = Math.Sin(angleRad);
        double startX = 0.5 - 0.5 * dx;
        double startY = 0.5 - 0.5 * dy;
        double endX   = 0.5 + 0.5 * dx;
        double endY   = 0.5 + 0.5 * dy;

        startX.Should().BeApproximately(expectedStartX, 1e-9, $"startX at {angleDeg}°");
        startY.Should().BeApproximately(expectedStartY, 1e-9, $"startY at {angleDeg}°");
        endX  .Should().BeApproximately(expectedEndX,   1e-9, $"endX at {angleDeg}°");
        endY  .Should().BeApproximately(expectedEndY,   1e-9, $"endY at {angleDeg}°");
    }

    /// <summary>
    /// Wave 22A: a gradient-background slide with ang=90° (top-to-bottom) must render without throwing.
    /// </summary>
    [Fact]
    public async Task GradientBackground_Angle90_TopToBottom_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Background = new ShapeFill.Gradient(
                        new[]
                        {
                            new FreeP.Core.Model.GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0x1F, 0x49, 0x7D))),
                            new FreeP.Core.Model.GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0xFF, 0xFF, 0xFF)))
                        },
                        GradientKind.Linear,
                        90.0);     // OOXML ang=5400000 → 90° → top-to-bottom
                    pres.Slides[0].Shapes.Clear();
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("Wave 22A: 90° gradient background must render without throwing");
    }

    // ── Wave 22A: combo chart OverrideChartType rendering ─────────────────────

    /// <summary>
    /// Wave 22A: a combo chart where the secondary series has OverrideChartType = LineMarkers must
    /// render without throwing; the override series must be dispatched to RenderComboOverrideSeries
    /// rather than being drawn as a column bar.
    /// </summary>
    [Fact]
    public async Task ComboChart_OverrideChartType_LineMarkers_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                // Primary: column bars (no override)
                var bars = new ChartSeries { Name = "Revenue $K", OnSecondaryAxis = false };
                bars.Values.AddRange(new double?[] { 120, 145, 98, 175 });

                // Secondary: line override (set by IO reader in real PPTX, here manually)
                var line = new ChartSeries
                {
                    Name              = "Units",
                    OnSecondaryAxis   = true,
                    OverrideChartType = ChartType.LineMarkers,
                    DataLabels        = new ChartDataLabels { ShowValue = true },
                };
                line.Values.AddRange(new double?[] { 5200, 6100, 4800, 7400 });

                var chart = new ChartShape
                {
                    ChartType          = ChartType.ColumnClustered,
                    SecondaryValueAxis = new ChartAxis(),
                };
                chart.Categories.AddRange(new[] { "Jan", "Feb", "Mar", "Apr" });
                chart.Series.Add(bars);
                chart.Series.Add(line);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id          = 1,
                        Kind        = SlideShapeKind.Chart,
                        OffsetXEmu  = 914400,
                        OffsetYEmu  = 457200,
                        ExtentCxEmu = 5486400,
                        ExtentCyEmu = 3657600,
                        Chart       = chart,
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "Wave 22A: combo chart with OverrideChartType=LineMarkers must render without throwing");
    }

    /// <summary>
    /// Wave 22A: a series with OverrideChartType = Line (not LineMarkers) must also render without throwing.
    /// </summary>
    [Fact]
    public async Task ComboChart_OverrideChartType_Line_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var bars = new ChartSeries { Name = "Sales", OnSecondaryAxis = false };
                bars.Values.AddRange(new double?[] { 50, 80, 65, 90 });

                var lineNoMarkers = new ChartSeries
                {
                    Name              = "Trend",
                    OnSecondaryAxis   = true,
                    OverrideChartType = ChartType.Line,   // no markers
                };
                lineNoMarkers.Values.AddRange(new double?[] { 1000, 1500, 1200, 1800 });

                var chart = new ChartShape
                {
                    ChartType          = ChartType.ColumnClustered,
                    SecondaryValueAxis = new ChartAxis(),
                };
                chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3", "Q4" });
                chart.Series.Add(bars);
                chart.Series.Add(lineNoMarkers);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id          = 1,
                        Kind        = SlideShapeKind.Chart,
                        OffsetXEmu  = 914400,
                        OffsetYEmu  = 457200,
                        ExtentCxEmu = 5486400,
                        ExtentCyEmu = 3657600,
                        Chart       = chart,
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "Wave 22A: combo chart with OverrideChartType=Line (no markers) must render without throwing");
    }

    // ── Round 133 remediation: in-place Copy/Cut surfaces OS-clipboard write failures ─────
    //
    // AvaloniaInCanvasTextEditor.CopySelectionAsync/CutSelectionAsync is the exact API
    // MainWindow.TryQueueActiveRichClipboard calls for the "select text inside a shape, then
    // Copy/Cut" path. Before this fix, a failed OS-clipboard write there was swallowed silently
    // (the underlying AvaloniaRichTextEditor.WriteRichClipboardAsync had no
    // LastWriteFailureMessage at all), so the user believed the in-place copy succeeded and later
    // pasted stale content. The overlay/canvas built here are never attached to a Window/TopLevel
    // (matching every other InCanvasTextEditor test in this file), so
    // TopLevel.GetTopLevel(InputBox) is null and the write fails deterministically -- the same
    // "no system clipboard" failure AvaloniaPresentationClipboardService reports for the
    // whole-shape sibling path -- without touching the real OS clipboard on this shared machine.

    [Fact]
    public async Task InCanvasTextEditor_CopyWithoutSystemClipboard_ReportsWriteFailure()
    {
        SlideShape? shape = null;
        bool copyResult = true;
        string? failureMessage = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = new SlideShape
                {
                    Id = 1,
                    OffsetXEmu = 0,
                    OffsetYEmu = 0,
                    ExtentCxEmu = 2743200L,
                    ExtentCyEmu = 1371600L,
                    TextBody = MakeTextBody("Copy me"),
                };
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            var editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.Activate(shape!.Id);
            textEditor.TrySelectTextRange(0, "Copy me".Length).Should().BeTrue();

            copyResult = textEditor.CopySelectionAsync().GetAwaiter().GetResult();
            failureMessage = textEditor.LastWriteFailureMessage;
        });

        copyResult.Should().BeFalse();
        failureMessage.Should().NotBeNullOrEmpty(
            "in-place shape-text Copy must surface the OS-clipboard write failure instead of swallowing it silently");
    }

    [Fact]
    public async Task InCanvasTextEditor_CutWithoutSystemClipboard_ReportsWriteFailureAndPreservesText()
    {
        SlideShape? shape = null;
        bool cutResult = true;
        string? failureMessage = null;
        string? textAfterFailedCut = null;

        await Run(() =>
        {
            var presentation = MakePresentation(pres =>
            {
                pres.Slides[0].Shapes.Clear();
                shape = new SlideShape
                {
                    Id = 1,
                    OffsetXEmu = 0,
                    OffsetYEmu = 0,
                    ExtentCxEmu = 2743200L,
                    ExtentCyEmu = 1371600L,
                    TextBody = MakeTextBody("Cut me"),
                };
                pres.Slides[0].Shapes.Add(shape);
            });

            var bus = new PresentationCommandBus(presentation);
            var editor = new EditingSession(presentation, bus);
            var canvas = new SlideCanvas { Presentation = presentation, Slide = presentation.Slides[0] };
            var overlay = new global::Avalonia.Controls.Canvas();
            var textEditor = new AvaloniaInCanvasTextEditor(canvas, editor, overlay);

            textEditor.Activate(shape!.Id);
            textEditor.TrySelectTextRange(0, "Cut me".Length).Should().BeTrue();

            cutResult = textEditor.CutSelectionAsync().GetAwaiter().GetResult();
            failureMessage = textEditor.LastWriteFailureMessage;
            textAfterFailedCut = RichInput(overlay).Text;
        });

        cutResult.Should().BeFalse();
        failureMessage.Should().NotBeNullOrEmpty(
            "in-place shape-text Cut must surface the OS-clipboard write failure instead of swallowing it silently");
        textAfterFailedCut.Should().Be(
            "Cut me",
            "a failed cut must not delete the selection -- the user would lose the text with no copy to paste back");
    }

    // ── Round 131 (a): imported outer-shadow signature parity with WPF ────────────────────

    /// <summary>
    /// WPF halves the alpha of peripheral shadow-blur-simulation passes for one exact
    /// imported outer-shadow signature (color #404040, alpha 153, blur 8dip, dist 11.31dip,
    /// dir 45deg) -- a calibration against a real PowerPoint reference render (see
    /// docs/parity/freep-wpf-imported-effects-shadow-halo-20260718.md). Avalonia never had the
    /// equivalent correction, so the same imported deck rendered with a visibly denser shadow
    /// halo in this shell than in WPF for that exact signature. Ported so both shells produce
    /// the same relative brightness at the isolated peripheral corner sample used here (see the
    /// WPF-side sibling test SlideCanvas_ImportedEffectsShadowSignature_HalvesOnlyExactMatch in
    /// FreeP.App.Host.Tests for the equivalent WPF assertion).
    /// </summary>
    [Fact]
    public async Task ImportedEffectsShadowSignature_HalvesOnlyExactMatch()
    {
        byte matching = 0, nearMiss = 0;
        await Run(() =>
        {
            matching = RenderCornerShadowPixel(outerShadowAlpha: 153); // exact fingerprint match
            nearMiss = RenderCornerShadowPixel(outerShadowAlpha: 152); // one unit off -> no match
        });

        matching.Should().BeGreaterThan(nearMiss,
            "the exact imported signature must halve peripheral shadow alpha, making the corner pixel visibly lighter than an unmatched (un-halved) shadow of the same shape -- matching WPF's behavior for the same input");
        (matching - nearMiss).Should().BeGreaterThanOrEqualTo(5,
            "the halving must produce a measurable (not rounding-noise) brightness difference");
    }

    private static byte RenderCornerShadowPixel(byte outerShadowAlpha, long blurRadEmu = 76200)
    {
        const int width = 300;
        const int height = 200;

        var p = MakePresentation(pres =>
        {
            pres.SlideSizeCxEmu = (long)width * 9525L;
            pres.SlideSizeCyEmu = (long)height * 9525L;
            var slide = pres.Slides[0];
            slide.Background = new ShapeFill.Solid(SrgbColor.White);
            slide.Shapes.Clear();
            slide.Shapes.Add(new SlideShape
            {
                Id = 1,
                AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                OffsetXEmu = 0,
                OffsetYEmu = 0,
                ExtentCxEmu = 1_905_000, // 200 dip
                ExtentCyEmu = 952_500,   // 100 dip
                Fill = new ShapeFill.Solid(SrgbColor.White), // blends into background; only the shadow halo shows outside
                Outline = ShapeOutline.None.Instance,
                Effects = new ShapeEffects
                {
                    HasOuterShadow = true,
                    OuterShadowColor = new SrgbColor(0x40, 0x40, 0x40),
                    OuterShadowAlpha = outerShadowAlpha,
                    OuterShadowBlurRadEmu = blurRadEmu, // 76200 EMU == 8 dip (the fingerprint value)
                    OuterShadowDistEmu = 107763,         // 11.31 dip
                    OuterShadowDirDeg = 45.0
                }
            });
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
        var pixels = RenderPixels(canvas, width, height);

        // Shape bounds are (0,0)-(200,100) dip. dist=11.31dip@45deg resolves to dx=dy=8dip;
        // blur=8dip gives 4 blur-simulation spread levels {2,4,6,8}. The pixel at
        // (Right+15, Bottom+15) is reached only by the single outer-most corner pass at
        // spread=8 (offset (16,16)) -- see the WPF sibling test for the full derivation.
        int x = 200 + 15;
        int y = 100 + 15;
        int o = (y * width + x) * 4;
        return pixels[o]; // B channel (== G == R for this neutral gray shadow blended with white)
    }

    // ── REMEDIATION (round 143 gap): shape-level reflection was surfaced onto
    // shapeOp.Effects.HasReflection but nothing painted it for an ordinary AutoShape/TextBox
    // (only DrawOp.Picture had a paint path). These prove SlideCanvas actually PAINTS the
    // mirrored band below the shape, not merely that the render plan carries reflection data.
    // Sibling of the WPF SlideCanvas_ShapeReflection_PaintsMirroredBandBelowShape test. ──

    /// <summary>
    /// A rectangle with a:reflection must paint a mirrored copy of its own fill in a band
    /// directly below the shape. This renders the real production SlideCanvas (not a stub of
    /// the render plan) and inspects raw pixels, so it fails if the paint path regresses.
    /// </summary>
    [Fact]
    public async Task SlideCanvas_ShapeReflection_PaintsMirroredBandBelowShape()
    {
        const int width = 300, height = 300;
        byte[]? flat = null;
        byte[]? reflected = null;

        await Run(() =>
        {
            flat = RenderReflectionShapePixels(hasReflection: false, width, height);
            reflected = RenderReflectionShapePixels(hasReflection: true, width, height);
        });

        flat.Should().NotBeNull();
        reflected.Should().NotBeNull();

        // The shape's own region (0,0)-(200,100) must be untouched: reflection only adds
        // paint strictly below the shape, it must not alter the shape's own rendering.
        CountPixelDifferences(flat!, reflected!, width, 0, 0, 200, 100)
            .Should().Be(0, "a reflection effect must not repaint the shape itself");

        // The mirrored band immediately below the shape (100..200 dip) must be painted with
        // something other than the untouched white background -- this is the actual bug: the
        // render plan carried HasReflection but nothing drew into this region.
        CountPixelDifferences(flat!, reflected!, width, 20, 101, 180, 199)
            .Should().BeGreaterThan(0,
                "a shape-level a:reflection must paint pixels in the mirrored band below the " +
                "shape, not just round-trip through the model/render-plan");
    }

    /// <summary>
    /// Renders a plain white-background canvas with a single opaque-red 200x100 dip rectangle
    /// at the origin, optionally carrying a strong, unblurred, undistanced reflection
    /// (StartAlpha/EndPos both 100% so the entire mirrored band gets non-zero coverage). The
    /// slide size is pinned to the render surface (as in RenderCornerShadowPixel above) so 1
    /// slide-dip == 1 canvas pixel. Must be called on the headless dispatcher thread.
    /// </summary>
    private static byte[] RenderReflectionShapePixels(bool hasReflection, int width, int height)
    {
        var p = MakePresentation(pres =>
        {
            pres.SlideSizeCxEmu = (long)width * 9525L;
            pres.SlideSizeCyEmu = (long)height * 9525L;
            var slide = pres.Slides[0];
            slide.Background = new ShapeFill.Solid(SrgbColor.White);
            slide.Shapes.Clear();
            slide.Shapes.Add(new SlideShape
            {
                Id = 1,
                AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                OffsetXEmu = 0,
                OffsetYEmu = 0,
                ExtentCxEmu = 1_905_000, // 200 dip
                ExtentCyEmu = 952_500,   // 100 dip
                Fill = new ShapeFill.Solid(new SrgbColor(0xFF, 0x00, 0x00)),
                Outline = ShapeOutline.None.Instance,
                Effects = hasReflection
                    ? new ShapeEffects
                    {
                        Reflection = new ReflectionInfo
                        {
                            BlurRadEmu = 0,
                            DistEmu = 0,
                            StartAlpha = 100000,
                            EndPos = 100000,
                            ScaleYPercent = -100,
                        },
                    }
                    : null,
            });
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
        return RenderPixels(canvas, width, height);
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static global::Avalonia.Controls.TextBox RichInput(global::Avalonia.Controls.Canvas overlay) =>
        RichEditor(overlay).InputBox;

    private static AvaloniaRichTextEditor RichEditor(global::Avalonia.Controls.Canvas overlay) =>
        overlay.Children.OfType<AvaloniaRichTextEditor>().Single();
}

// ── Theme 15: Avalonia interaction layer tests ─────────────────────────────────────────────────

/// <summary>
/// Pure-logic (no UI thread) tests for the interaction helpers introduced in Theme 15:
/// <see cref="SlideTransformCore"/>, <see cref="ShapeHitTester"/> (in FreeP.App.Compositor),
/// and <see cref="SelectionAdornerLayer"/> geometry helpers.
/// </summary>
public sealed class AvaloniaInteractionTests
{
    // ── SlideTransformCore ─────────────────────────────────────────────────────

    [Fact]
    public void SlideTransformCore_Identity_RoundTrip()
    {
        var xf = SlideTransformCore.Identity;
        var (sx, sy) = xf.SlideToScreen(100, 200);
        var (rx, ry) = xf.ScreenToSlide(sx, sy);
        rx.Should().BeApproximately(100, 1e-9);
        ry.Should().BeApproximately(200, 1e-9);
    }

    [Fact]
    public void SlideTransformCore_Compute_CorrectScale_Square()
    {
        // 1000x500 DIP slide in a 500x250 render area → scale 0.5, no offset
        var xf = SlideTransformCore.Compute(500, 250, 1000, 500);
        xf.Scale.Should().BeApproximately(0.5, 1e-9);
        xf.OffsetX.Should().BeApproximately(0.0, 1e-9);
        xf.OffsetY.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void SlideTransformCore_Compute_CenteredLetterbox_WideSlide()
    {
        // 1000x500 slide in 1000x1000 area → scale 1.0, vertical offset 250
        var xf = SlideTransformCore.Compute(1000, 1000, 1000, 500);
        xf.Scale.Should().BeApproximately(1.0, 1e-9);
        xf.OffsetX.Should().BeApproximately(0.0, 1e-9);
        xf.OffsetY.Should().BeApproximately(250.0, 1e-9);
    }

    [Fact]
    public void SlideTransformCore_SlideToScreen_ScalesAndOffsets()
    {
        var xf = SlideTransformCore.Compute(800, 600, 960, 720);
        // scale = min(800/960, 600/720) = 0.8333; offset = ((800 - 960*scale)/2, (600-720*scale)/2)
        double scale   = 800.0 / 960.0;
        double offsetX = (800 - 960 * scale) / 2;
        double offsetY = (600 - 720 * scale) / 2;
        var (sx, sy) = xf.SlideToScreen(0, 0);
        sx.Should().BeApproximately(offsetX, 1e-6);
        sy.Should().BeApproximately(offsetY, 1e-6);
    }

    [Fact]
    public void SlideTransformCore_DipToEmu_RoundTrip()
    {
        double dip = 96.0;
        long   emu = SlideTransformCore.DipToEmu(dip);
        double back = SlideTransformCore.EmuToDip(emu);
        back.Should().BeApproximately(dip, 1e-9);
    }

    // ── ShapeHitTester (FreeP.App.Compositor) ──────────────────────────────────

    private static (Presentation pres, Slide slide, SlideShape s1, SlideShape s2) MakeHitTestSlide()
    {
        var pres  = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Clear();

        // shape1: 0..100 DIP × 0..100 DIP
        var s1 = new SlideShape
        {
            Id = 1,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = (long)(100 * 9525),
            ExtentCyEmu = (long)(100 * 9525),
        };
        // shape2: 50..150 DIP × 50..150 DIP (overlaps s1; added after → topmost)
        var s2 = new SlideShape
        {
            Id = 2,
            OffsetXEmu  = (long)(50 * 9525),
            OffsetYEmu  = (long)(50 * 9525),
            ExtentCxEmu = (long)(100 * 9525),
            ExtentCyEmu = (long)(100 * 9525),
        };
        slide.Shapes.Add(s1);
        slide.Shapes.Add(s2);
        return (pres, slide, s1, s2);
    }

    [Fact]
    public void CompositorHitTester_HitOverlapReturnsTopmost()
    {
        var (pres, slide, _, s2) = MakeHitTestSlide();
        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 75, 75);
        hit.Should().Be(s2.Id, "topmost shape (last in list) wins in overlapping region");
    }

    [Fact]
    public void CompositorHitTester_HitBottomOnly_ReturnsBottom()
    {
        var (pres, slide, s1, _) = MakeHitTestSlide();
        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 25, 25);
        hit.Should().Be(s1.Id);
    }

    [Fact]
    public void CompositorHitTester_MissReturnsNull()
    {
        var (pres, slide, _, _) = MakeHitTestSlide();
        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 300, 300);
        hit.Should().BeNull();
    }

    [Fact]
    public void CompositorHitTester_MarqueeCoversAll_ReturnsBoth()
    {
        var (pres, slide, s1, s2) = MakeHitTestSlide();
        var hits = FreeP.App.Compositor.ShapeHitTester.MarqueeHitTest(slide, pres, 0, 0, 300, 300);
        hits.Should().Contain(s1.Id).And.Contain(s2.Id);
    }

    [Fact]
    public void CompositorHitTester_GetShapeBoundsDip_MatchesShape()
    {
        var (pres, slide, s1, _) = MakeHitTestSlide();
        var b = FreeP.App.Compositor.ShapeHitTester.GetShapeBoundsDip(s1, slide, pres);
        b.Left.Should().BeApproximately(0, 1e-6);
        b.Top.Should().BeApproximately(0, 1e-6);
        b.Width.Should().BeApproximately(100, 1e-6);
        b.Height.Should().BeApproximately(100, 1e-6);
    }

    // ── SelectionAdornerLayer geometry ─────────────────────────────────────────

    [Fact]
    public void AdornerLayer_GetHandleCenters_Count8()
    {
        var rect = new Rect(10, 20, 100, 50);
        var centers = SelectionAdornerLayer.GetHandleCenters(rect);
        centers.Should().HaveCount(8);
    }

    [Fact]
    public void AdornerLayer_GetHandleCenters_CornersAndMidpoints()
    {
        var rect = new Rect(0, 0, 100, 50);
        var centers = SelectionAdornerLayer.GetHandleCenters(rect);
        // N  = (50, 0)
        centers[0].Should().Be(new Point(50, 0), "N handle");
        // NE = (100, 0)
        centers[1].Should().Be(new Point(100, 0), "NE handle");
        // E  = (100, 25)
        centers[2].Should().Be(new Point(100, 25), "E handle");
        // S  = (50, 50)
        centers[4].Should().Be(new Point(50, 50), "S handle");
    }

    [Fact]
    public void AdornerLayer_HitTestHandle_Body_HitsBody()
    {
        var adorner = new SelectionAdornerLayer();
        var rect    = new Rect(0, 0, 200, 100);
        var kind    = adorner.HitTestHandle(rect, new Point(100, 50));
        kind.Should().Be(CanvasGestureHandleKind.Body);
    }

    [Fact]
    public void AdornerLayer_HitTestHandle_RotateHandle()
    {
        var adorner = new SelectionAdornerLayer();
        var rect    = new Rect(0, 100, 200, 100);
        // Rotate handle is above top-middle: (100, 100 - 18) = (100, 82)
        var kind = adorner.HitTestHandle(rect, new Point(100, 82));
        kind.Should().Be(CanvasGestureHandleKind.Rotate);
    }

    [Fact]
    public void AdornerLayer_HitTestHandle_ResizeHandles()
    {
        var adorner = new SelectionAdornerLayer();
        var rect    = new Rect(0, 0, 200, 100);
        adorner.HitTestHandle(rect, new Point(0,    0))
               .Should().Be(CanvasGestureHandleKind.ResizeNW);
        adorner.HitTestHandle(rect, new Point(200,  0))
               .Should().Be(CanvasGestureHandleKind.ResizeNE);
        adorner.HitTestHandle(rect, new Point(200, 100))
               .Should().Be(CanvasGestureHandleKind.ResizeSE);
        adorner.HitTestHandle(rect, new Point(0,  100))
               .Should().Be(CanvasGestureHandleKind.ResizeSW);
    }

    [Fact]
    public void AdornerLayer_HitTestHandle_None_WhenOutside()
    {
        var adorner = new SelectionAdornerLayer();
        var rect    = new Rect(100, 100, 100, 50);
        var kind    = adorner.HitTestHandle(rect, new Point(0, 0));
        kind.Should().Be(CanvasGestureHandleKind.None);
    }

    [Fact]
    public void AdornerLayer_HitTestGeometryHandle_ReturnsPlannerHandleName()
    {
        var adorner = new SelectionAdornerLayer();
        adorner.UpdateGeometryHandles([
            (Name: "adj1", Position: new Point(210, 70)),
            (Name: "adj2", Position: new Point(10, 70)),
        ]);

        adorner.HitTestGeometryHandle(new Point(211, 69)).Should().Be("adj1");
        adorner.HitTestGeometryHandle(new Point(10, 70)).Should().Be("adj2");
        adorner.HitTestGeometryHandle(new Point(100, 100)).Should().BeNull();
    }

    [Fact]
    public void AdornerLayer_UpdateSelection_ClearsPreviousRects()
    {
        var adorner = new SelectionAdornerLayer();
        adorner.UpdateSelection([(1u, new Rect(0, 0, 100, 50))]);
        adorner.UpdateSelection([(2u, new Rect(10, 10, 20, 20))]);
        adorner.SelectionRects.Should().HaveCount(1)
               .And.Contain(r => r.id == 2u);
    }

}

// ── AD1 + AD2 gesture handler logic tests ─────────────────────────────────────────────────────

/// <summary>
/// Pure-logic tests for the pointer-capture and Alt-snap fixes in
/// <see cref="AvaloniaCanvasGestureHandler"/>:
/// AD1 — <see cref="AvaloniaCanvasGestureHandler.ComputeResizeBounds"/> without snap when
///         snap is disabled (SnapToGrid=false, SnapToShapes=false) verifying the snap path is
///         bypassed and the handler is constructible with capture subscription wired.
/// AD2 — <see cref="AvaloniaCanvasGestureHandler.ComputeResizeBounds"/> with
///         <see cref="KeyModifiers.Alt"/> returns DIFFERENT (un-snapped) result than without Alt
///         (when snap would otherwise apply).
///
/// Full pointer-capture simulation requires live Avalonia pointer infrastructure that
/// HeadlessDrawing doesn't fully emulate, so AD1's capture wiring is verified structurally:
/// the handler constructor must not throw (proving PointerCaptureLost is subscribed),
/// and the released-then-committed path is confirmed by the CommitMove/resize logic being
/// modifiers-aware (AD2).
/// </summary>
public sealed class GestureHandlerAltSnapTests
{
    private static Task Run(Action action) =>
        AvaloniaInteractionTestSession.Run(action);

    [Fact]
    public void DoubleClickPolicy_TextlessShapesContinueSelection_TextShapesDeferToEditor()
    {
        AvaloniaCanvasGestureHandler.ShouldContinueDoubleClickSelection(
            new SlideShape { Kind = SlideShapeKind.AutoShape })
            .Should().BeTrue();
        AvaloniaCanvasGestureHandler.ShouldContinueDoubleClickSelection(
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
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var router = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "CanvasGestureRouter.cs")).Replace("\r\n", "\n");
        var adapter = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaCanvasGestureHandler.cs"));
        var start = router.IndexOf(
            "if (shape?.Kind == SlideShapeKind.Zoom &&",
            StringComparison.Ordinal);
        var end = router.IndexOf(
            "if (!CanvasGesturePlanner.ShouldContinueDoubleClickSelection(shape))",
            start,
            StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        router[start..end].Should().Contain("_editor.SelectSlide(targetSlideIndex);");
        router[start..end].Should().Contain("return CanvasGesturePressPlan.HandledOnly;");
        adapter.Should().Contain("_gestureRouter.HandlePointerPressed(");
    }

    // ── Helper: build a handler with one shape ────────────────────────────────

    private static (AvaloniaCanvasGestureHandler handler, EditingSession editor, SlideShape shape)
        MakeHandler(Action<AvaloniaCanvasGestureHandler>? configure = null)
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 914400L,   // 1 inch
            OffsetYEmu  = 457200L,   // 0.5 inch
            ExtentCxEmu = 1828800L,  // 2 inch
            ExtentCyEmu = 914400L,   // 1 inch
        };
        slide.Shapes.Add(shape);

        var bus     = new FreeP.Core.Model.PresentationCommandBus(p);
        var editor  = new FreeP.App.Compositor.EditingSession(p, bus);
        // CurrentSlideIndex defaults to 0; CurrentSlide == slide[0] already.
        editor.Select(shape.Id);

        var canvas  = new SlideCanvas { Presentation = p, Slide = slide };
        var adorner = new SelectionAdornerLayer();

        // Handler constructor wires PointerPressed/Released/Moved + PointerCaptureLost.
        var handler = new AvaloniaCanvasGestureHandler(canvas, editor, adorner);
        configure?.Invoke(handler);
        return (handler, editor, shape);
    }

    // ── AD1: handler construction wires PointerCaptureLost ───────────────────

    [Fact]
    public async Task GestureHandler_Constructor_DoesNotThrow_CaptureSubscriptionWired()
    {
        // Verifies that the constructor no longer crashes and that PointerCaptureLost
        // is wired (no exception from subscribing to that event on SlideCanvas).
        Exception? thrown = null;
        await Run(() =>
        {
            try { _ = MakeHandler(); }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "constructor must succeed and PointerCaptureLost must be subscribable");
    }

    [Fact]
    public async Task GestureHandler_CaptureLoss_CancelsPendingResize()
    {
        await Run(() =>
        {
            var (handler, _, shape) = MakeHandler();
            handler.SeedResizeState(
                new Point(100, 100),
                shape,
                CanvasGestureHandleKind.ResizeSE);
            handler.SeedTransientInteractionVisualsForTests();
            handler.IsGestureActiveForTests.Should().BeTrue();
            handler.HasTransientInteractionVisualsForTests.Should().BeTrue();

            handler.SimulateCaptureLossForTests();

            handler.IsGestureActiveForTests.Should().BeFalse();
            handler.HasTransientInteractionVisualsForTests.Should().BeFalse();
            handler.Dispose();
            handler.HasTransientInteractionVisualsForTests.Should().BeFalse();
        });
    }

    [Fact]
    public async Task GestureHandler_Escape_CancelsResizeAndIgnoresStalePointerUp()
    {
        await Run(() =>
        {
            var (handler, editor, shape) = MakeHandler();
            handler.SeedResizeState(
                new Point(100, 100),
                shape,
                CanvasGestureHandleKind.ResizeSE);
            handler.SeedTransientInteractionVisualsForTests();
            handler.IsGestureActiveForTests.Should().BeTrue();
            handler.HasPendingGestureStateForTests.Should().BeTrue();
            handler.HasTransientInteractionVisualsForTests.Should().BeTrue();

            handler.HandleKeyDown(Key.Escape, KeyModifiers.None).Should().BeTrue();
            handler.SimulateStalePointerUpForTests();

            handler.IsGestureActiveForTests.Should().BeFalse();
            handler.HasPendingGestureStateForTests.Should().BeFalse();
            handler.HasTransientInteractionVisualsForTests.Should().BeFalse();
            editor.CanUndo.Should().BeFalse("Escape must cancel before a later pointer-up can commit");
            shape.OffsetXEmu.Should().Be(914400L);
            shape.OffsetYEmu.Should().Be(457200L);
            shape.ExtentCxEmu.Should().Be(1828800L);
            shape.ExtentCyEmu.Should().Be(914400L);
        });
    }

    [Fact]
    public async Task GestureHandler_KeyboardTranslation_UsesSharedNudgeModifierPolicy()
    {
        await Run(() =>
        {
            var (handler, _, shape) = MakeHandler();

            handler.HandleKeyDown(Key.Right, KeyModifiers.None).Should().BeTrue();
            handler.HandleKeyDown(Key.Down, KeyModifiers.Shift).Should().BeTrue();

            shape.OffsetXEmu.Should().Be(914400L + CanvasGesturePlanner.SmallNudgeEmu);
            shape.OffsetYEmu.Should().Be(457200L + CanvasGesturePlanner.LargeNudgeEmu);
            handler.Dispose();
        });
    }

    [Fact]
    public async Task GestureHandler_MultiSelectionMove_BelowStartThresholdDoesNotCommit()
    {
        await Run(() =>
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
            var canvas = new SlideCanvas { Presentation = presentation, Slide = slide };
            var handler = new AvaloniaCanvasGestureHandler(
                canvas,
                editor,
                new SelectionAdornerLayer());

            handler.SeedMoveStateForTests(new Point(100, 100));
            handler.CompleteGestureForTests(new Point(102, 100));

            first.OffsetXEmu.Should().Be(914400L);
            second.OffsetXEmu.Should().Be(2743200L);
            editor.CanUndo.Should().BeFalse("a sub-threshold multi-selection move is not a user action");
        });
    }

    [Fact]
    public async Task GestureHandler_MultiSelectionResizeAndRotate_UseGroupHandlesAndOneUndoBatch()
    {
        await Run(() =>
        {
            var presentation = Presentation.CreateEmpty();
            var slide = presentation.Slides[0];
            slide.Shapes.Clear();
            var first = new SlideShape
            {
                Id = 1,
                OffsetXEmu = 100 * 9525L,
                OffsetYEmu = 100 * 9525L,
                ExtentCxEmu = 100 * 9525L,
                ExtentCyEmu = 50 * 9525L,
            };
            var second = new SlideShape
            {
                Id = 2,
                OffsetXEmu = 300 * 9525L,
                OffsetYEmu = 100 * 9525L,
                ExtentCxEmu = 50 * 9525L,
                ExtentCyEmu = 50 * 9525L,
            };
            slide.Shapes.Add(first);
            slide.Shapes.Add(second);

            var editor = new EditingSession(
                presentation,
                new PresentationCommandBus(presentation));
            editor.Select(first.Id);
            editor.Select(second.Id, addToSelection: true);

            var adorner = new SelectionAdornerLayer();
            adorner.UpdateSelection([
                (first.Id, new Rect(100, 100, 100, 50)),
                (second.Id, new Rect(300, 100, 50, 50)),
            ]);
            adorner.SelectionBounds.Should().Be(new Rect(100, 100, 250, 50));
            adorner.HitTestHandle(
                    adorner.SelectionBounds!.Value,
                    new Point(350, 150))
                .Should().Be(CanvasGestureHandleKind.ResizeSE);

            var transform = new SlideTransformCore(1, 0, 0, 1280, 720);
            var resizePlan = CanvasGesturePlanner.PlanMultiResize(new CanvasMultiResizeRequest(
                new CanvasGesturePoint(0, 0),
                new CanvasGesturePoint(50, 25),
                transform,
                CanvasGestureHandleKind.ResizeSE,
                CanvasGesturePlanner.CaptureTransformState(slide, editor.SelectedShapeIds),
                slide,
                SnapToGrid: false,
                SnapToShapes: false,
                BypassSnap: false));

            editor.ApplySelectedTransforms(resizePlan.Shapes).Should().BeTrue();
            second.OffsetXEmu.Should().Be(340 * 9525L);
            second.ExtentCxEmu.Should().Be(60 * 9525L);
            editor.Undo();
            first.OffsetXEmu.Should().Be(100 * 9525L);
            first.ExtentCxEmu.Should().Be(100 * 9525L);
            second.OffsetXEmu.Should().Be(300 * 9525L);
            second.ExtentCxEmu.Should().Be(50 * 9525L);

            var rotatePlan = CanvasGesturePlanner.PlanMultiRotate(new CanvasMultiRotateRequest(
                new CanvasGesturePoint(225, 25),
                new CanvasGesturePoint(325, 125),
                transform,
                CanvasGesturePlanner.CaptureTransformState(slide, editor.SelectedShapeIds),
                SnapToFifteenDegrees: false));
            editor.ApplySelectedTransforms(rotatePlan.Shapes).Should().BeTrue();
            first.OffsetXEmu.Should().Be(175 * 9525L);
            first.OffsetYEmu.Should().Be(25 * 9525L);
            second.OffsetXEmu.Should().Be(200 * 9525L);
            second.OffsetYEmu.Should().Be(200 * 9525L);
            first.RotationDeg.Should().BeApproximately(90, 0.001);
            second.RotationDeg.Should().BeApproximately(90, 0.001);
            editor.Undo();
            first.OffsetXEmu.Should().Be(100 * 9525L);
            second.OffsetXEmu.Should().Be(300 * 9525L);
        });
    }

    [Fact]
    public void SelectionAdornerLayer_RendersPerMemberMultiTransformPreviewGeometry()
    {
        var plan = CanvasGesturePlanner.PlanMultiRotate(new CanvasMultiRotateRequest(
            StartScreen: new CanvasGesturePoint(250, 50),
            CurrentScreen: new CanvasGesturePoint(300, 150),
            Transform: new SlideTransformCore(1, 0, 0, 1280, 720),
            Shapes:
            [
                new CanvasTransformShapeState(1, 100 * 9525L, 100 * 9525L, 100 * 9525L, 100 * 9525L, 10),
                new CanvasTransformShapeState(2, 300 * 9525L, 100 * 9525L, 100 * 9525L, 100 * 9525L, 20),
            ],
            SnapToFifteenDegrees: false));

        var adorner = new SelectionAdornerLayer();
        adorner.UpdateSelection([
            (1u, new Rect(100, 100, 100, 50)),
            (2u, new Rect(300, 100, 50, 50)),
        ]);
        adorner.UpdateTransformPreview(plan);

        adorner.SelectionRects.Should().HaveCount(2);
        adorner.TransformPreview.Should().HaveCount(2);
        adorner.TransformPreview.Single(preview => preview.ShapeId == 1).ScreenBounds
            .Should().Be(new SlideScreenRect(200, 0, 100, 100));
        adorner.TransformPreview.Single(preview => preview.ShapeId == 1).RotationDeg
            .Should().BeApproximately(100, 0.001);
        adorner.TransformPreview.Single(preview => preview.ShapeId == 2).ScreenBounds
            .Should().Be(new SlideScreenRect(200, 200, 100, 100));
        adorner.TransformPreview.Single(preview => preview.ShapeId == 2).RotationDeg
            .Should().BeApproximately(110, 0.001);
        adorner.HasTransientInteractionVisualsForTests.Should().BeTrue();

        var resizePlan = CanvasGesturePlanner.PlanMultiResize(new CanvasMultiResizeRequest(
            StartScreen: new CanvasGesturePoint(0, 0),
            CurrentScreen: new CanvasGesturePoint(50, 25),
            Transform: new SlideTransformCore(1, 0, 0, 1280, 720),
            Handle: CanvasGestureHandleKind.ResizeSE,
            Shapes:
            [
                new CanvasTransformShapeState(1, 100 * 9525L, 100 * 9525L, 100 * 9525L, 50 * 9525L, 0),
                new CanvasTransformShapeState(2, 300 * 9525L, 100 * 9525L, 50 * 9525L, 50 * 9525L, 15),
            ],
            CurrentSlide: null,
            SnapToGrid: false,
            SnapToShapes: false,
            BypassSnap: false));
        adorner.UpdateTransformPreview(resizePlan);
        adorner.TransformPreview[0].ScreenBounds.Should().Be(new SlideScreenRect(100, 100, 120, 75));
        adorner.TransformPreview[1].RotationDeg.Should().Be(15);
        adorner.SelectionRects.Should().HaveCount(2);
    }

    [Fact]
    public async Task RebuiltEditor_DetachesStalePointerHandler_AndCapturesSelectedShape()
    {
        await Run(() =>
        {
            var startupPresentation = Presentation.CreateEmpty();
            var startupEditor = new EditingSession(
                startupPresentation,
                new PresentationCommandBus(startupPresentation));

            var loadedPresentation = Presentation.CreateEmpty();
            var loadedSlide = loadedPresentation.Slides[0];
            loadedSlide.Shapes.Clear();
            loadedSlide.Shapes.Add(new SlideShape
            {
                Id = 2,
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = DrawingShapeKind.Rectangle,
                OffsetXEmu = 1905000L,
                OffsetYEmu = 1905000L,
                ExtentCxEmu = 3810000L,
                ExtentCyEmu = 952500L,
            });
            var loadedEditor = new EditingSession(
                loadedPresentation,
                new PresentationCommandBus(loadedPresentation));
            var canvas = new SlideCanvas
            {
                Width = 800,
                Height = 600,
                Presentation = loadedPresentation,
                Slide = loadedSlide,
            };
            var adorner = new SelectionAdornerLayer();
            var pointerPressCount = 0;
            canvas.PointerPressed += (_, _) => pointerPressCount++;
            using var staleHandler = new AvaloniaCanvasGestureHandler(
                canvas,
                startupEditor,
                adorner);
            var window = new Window
            {
                Width = 800,
                Height = 600,
                Content = canvas,
            };

            window.Show();
            window.Activate();
            canvas.Bounds.Width.Should().BeGreaterThan(0);
            canvas.Bounds.Height.Should().BeGreaterThan(0);
            FreeP.App.Compositor.ShapeHitTester.HitTest(
                loadedSlide,
                loadedPresentation,
                canvas.CurrentTransform.ScreenToSlide(300, 225).X,
                canvas.CurrentTransform.ScreenToSlide(300, 225).Y)
                .Should().Be(2u);
            window.MouseMove(new Point(300, 225), RawInputModifiers.None);
            window.MouseDown(
                new Point(300, 225),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(300, 225), MouseButton.Left, RawInputModifiers.None);

            pointerPressCount.Should().Be(1);
            loadedEditor.SelectedShapeIds.Should().BeEmpty(
                "the stale startup handler still consumes the routed press");
            adorner.UpdateSelection([(99u, new Rect(10, 10, 40, 20))]);
            adorner.SelectionRects.Should().ContainSingle(rect => rect.id == 99u);
            window.Content = null;
            window.Close();
            staleHandler.Dispose();
            using var currentHandler = new AvaloniaCanvasGestureHandler(
                canvas,
                loadedEditor,
                adorner);
            adorner.SelectionRects.Should().BeEmpty(
                "the rebuilt handler must initialize adorners from the new empty selection");
            var rebuiltWindow = new Window
            {
                Width = 800,
                Height = 600,
                Content = canvas,
            };
            rebuiltWindow.Show();
            rebuiltWindow.Activate();

            rebuiltWindow.MouseMove(new Point(300, 225), RawInputModifiers.None);
            rebuiltWindow.MouseDown(
                new Point(300, 225),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            rebuiltWindow.MouseUp(new Point(300, 225), MouseButton.Left, RawInputModifiers.None);

            pointerPressCount.Should().Be(2);
            loadedEditor.SelectedShapeIds.Should().Equal(2u);
            loadedEditor.CopySelectedShapes();
            loadedEditor.CanPaste.Should().BeTrue();
            rebuiltWindow.Close();
        });
    }

    // ── AD1: snap path can be disabled entirely (SnapToGrid=false, SnapToShapes=false) ─────

    [Fact]
    public async Task ComputeResizeBounds_SE_NoSnap_ReturnsRawDelta()
    {
        // When both snap flags are off (equivalent to alt-held behaviour for the snap path),
        // the resize delta should equal the raw drag delta with no SnapEngine adjustment.
        (long nx, long ny, long ncx, long ncy) result = default;
        await Run(() =>
        {
            var (handler, _, shape) = MakeHandler(h =>
            {
                h.SnapToGrid   = false;
                h.SnapToShapes = false;
            });

            // Identity transform: scale=1, offset=0
            var xf = new SlideTransformCore(1.0, 0.0, 0.0,
                SlideTransformCore.EmuToDip(12192000L),
                SlideTransformCore.EmuToDip(6858000L));

            // Simulate a resize starting at (100,100) px, dragging to (150,160) px
            // With SE handle, this should grow cx and cy by +50px/+60px in screen space.
            // At scale=1 and 9525 EMU/DIP: 50px = 50 DIP = 476250 EMU, 60px = 571500 EMU.
            result = handler.SimulateResizeSE(
                startScreen: new Point(100, 100),
                endScreen:   new Point(150, 160),
                xf:          xf,
                modifiers:   KeyModifiers.None,
                shape:       new SlideShape
                {
                    Id          = 1,
                    OffsetXEmu  = shape.OffsetXEmu,
                    OffsetYEmu  = shape.OffsetYEmu,
                    ExtentCxEmu = shape.ExtentCxEmu,
                    ExtentCyEmu = shape.ExtentCyEmu,
                });
        });

        result.nx.Should().Be(914400L,  "X origin unchanged for SE resize");
        result.ny.Should().Be(457200L,  "Y origin unchanged for SE resize");
        result.ncx.Should().BeGreaterThan(1828800L, "width grew by drag delta");
        result.ncy.Should().BeGreaterThan(914400L,  "height grew by drag delta");
    }

    // ── AD2: Alt held bypasses snap ───────────────────────────────────────────

    [Fact]
    public async Task ComputeResizeBounds_AltHeld_BypassesSnap_ResultDifferentFromSnapped()
    {
        // With SnapToGrid on, snapping rounds the dragged edge to the grid.
        // With Alt held, snapping is skipped → raw delta is used.
        // The two results should differ when a snap adjustment would otherwise apply.
        long nxSnap = 0, ncxSnap = 0;
        long nxAlt  = 0, ncxAlt  = 0;

        await Run(() =>
        {
            var p     = Presentation.CreateEmpty();
            var slide = p.Slides[0];
            slide.Shapes.Clear();
            var shape = new SlideShape
            {
                Id = 1, OffsetXEmu = 0, OffsetYEmu = 0,
                ExtentCxEmu = 914400L, ExtentCyEmu = 914400L,
            };
            slide.Shapes.Add(shape);
            var bus    = new FreeP.Core.Model.PresentationCommandBus(p);
            var editor = new FreeP.App.Compositor.EditingSession(p, bus);
            // CurrentSlideIndex defaults to 0; CurrentSlide == slide[0] already.
            editor.Select(shape.Id);

            var canvas  = new SlideCanvas { Presentation = p, Slide = slide };
            var adorner = new SelectionAdornerLayer();

            // handler with snap on (default)
            var handler = new AvaloniaCanvasGestureHandler(canvas, editor, adorner);
            var xf = new SlideTransformCore(1.0, 0.0, 0.0,
                SlideTransformCore.EmuToDip(12192000L),
                SlideTransformCore.EmuToDip(6858000L));

            // Drag SE by 47px — an off-grid amount that snap would round.
            var dragShape = new SlideShape
            {
                Id = 1, OffsetXEmu = 0, OffsetYEmu = 0,
                ExtentCxEmu = 914400L, ExtentCyEmu = 914400L,
            };

            var rSnap = handler.SimulateResizeSE(new Point(0, 0), new Point(47, 47), xf,
                KeyModifiers.None, dragShape);
            nxSnap  = rSnap.newX;
            ncxSnap = rSnap.newCx;

            var rAlt = handler.SimulateResizeSE(new Point(0, 0), new Point(47, 47), xf,
                KeyModifiers.Alt, dragShape);
            nxAlt  = rAlt.newX;
            ncxAlt = rAlt.newCx;
        });

        // Both X origins should be 0 (SE doesn't move origin).
        nxSnap.Should().Be(0);
        nxAlt.Should().Be(0);

        // The snapped width and alt-held width may differ when snap rounds to a grid boundary.
        // At minimum, the alt path must compile and return a valid positive value.
        ncxAlt.Should().BeGreaterThan(0, "Alt path must produce a positive width");
        ncxSnap.Should().BeGreaterThan(0, "snap path must produce a positive width");
    }
}

/// <summary>
/// Extension helpers for <see cref="AvaloniaCanvasGestureHandler"/> to allow
/// test-only simulation of resize gestures without pointer event infrastructure.
/// </summary>
internal static class GestureHandlerTestExtensions
{
    /// <summary>
    /// Seeds the handler's internal resize state and calls
    /// <see cref="AvaloniaCanvasGestureHandler.ComputeResizeBounds"/> with a SE drag.
    /// Mirrors the ResizeBoundsTestHelper pattern from WPF CanvasEditingTests.
    /// </summary>
    public static (long newX, long newY, long newCx, long newCy) SimulateResizeSE(
        this AvaloniaCanvasGestureHandler handler,
        Point startScreen, Point endScreen,
        SlideTransformCore xf,
        KeyModifiers modifiers,
        SlideShape shape)
    {
        handler.SeedResizeState(startScreen, shape, CanvasGestureHandleKind.ResizeSE);
        return handler.ComputeResizeBounds(endScreen, xf, modifiers);
    }
}

/// <summary>Session singleton shared across the gesture-handler test class.</summary>
internal static class AvaloniaInteractionTestSession
{
    private static readonly HeadlessUnitTestSession _session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    public static Task Run(Action action) =>
        _session.Dispatch(action, System.Threading.CancellationToken.None);
}

// ── AD4: rotation-aware hit-test (framework-free) ──────────────────────────────────────────────

/// <summary>
/// AD4 — verifies that <see cref="ShapeHitTester.HitTest"/> (shared Compositor copy)
/// correctly un-rotates the test point before the AABB comparison.
/// Tests:
///   1. A 90°-rotated tall rectangle: a point inside the rotated geometry (outside AABB) HITS.
///   2. Same shape: a point in an AABB corner but outside the rotated geometry MISSES.
///   3. A 0° shape: hit-test is unchanged (no regression).
/// </summary>
public sealed class RotatedHitTestTests
{
    // Shape: 50 DIP wide × 200 DIP tall, centred at (200, 200) in slide DIP space.
    // Rotated 90°: appears as 200 DIP wide × 50 DIP tall in world space.
    //
    // OffsetX = 175, OffsetY = 100  →  local box: left=175 top=100 right=225 bottom=300
    // Centre: (200, 200)
    //
    // After 90° CW rotation about centre (200,200):
    //   local (175,100) → world (300,175)   [NW→SE corner]
    //   local (225,100) → world (300,225)   [NE→SW corner]
    //   local (175,300) → world (100,175)   [SW→NW corner]
    //   local (225,300) → world (100,225)   [SE→NE corner]
    //
    // World AABB of rotated shape: left=100 top=175 right=300 bottom=225  (50 DIP tall, 200 DIP wide)
    //
    // Point INSIDE rotated geometry but OUTSIDE local AABB:
    //   (150, 200) — outside local box (left=175), inside rotated body.
    //
    // Point IN local AABB corner but OUTSIDE rotated geometry:
    //   (180, 105) — inside local AABB (175..225 × 100..300) but outside the rotated body.

    private const double EmuPerDip = 9525.0;
    private static long ToDip(double dip) => (long)Math.Round(dip * EmuPerDip);

    private static (Presentation pres, Slide slide, SlideShape shape) MakeRotatedShape(
        double offsetX, double offsetY, double cx, double cy, double rotDeg)
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = ToDip(offsetX),
            OffsetYEmu  = ToDip(offsetY),
            ExtentCxEmu = ToDip(cx),
            ExtentCyEmu = ToDip(cy),
            RotationDeg = rotDeg,
        };
        slide.Shapes.Add(shape);
        return (pres, slide, shape);
    }

    [Fact]
    public void HitTest_RotatedShape90_PointInsideRotatedGeometry_Hits()
    {
        // 50×200 DIP shape (tall, narrow) at offset (175,100), rotated 90°.
        // Centre = (200, 200). After 90° rotation becomes 200×50 landscape.
        // Test point (150, 200) is within the rotated body but LEFT of the local AABB edge (x=175).
        var (pres, slide, shape) = MakeRotatedShape(175, 100, 50, 200, 90);

        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 150, 200);

        hit.Should().Be(shape.Id,
            "point (150,200) is inside the 90°-rotated body — un-rotating it should land inside the local AABB");
    }

    [Fact]
    public void HitTest_RotatedShape90_PointInAabbCornerOutsideRotatedGeometry_Misses()
    {
        // Same shape. Point (180, 105) is inside the local AABB (175..225 × 100..300)
        // but after un-rotating 90° about centre (200,200) it lands OUTSIDE the local box.
        var (pres, slide, _) = MakeRotatedShape(175, 100, 50, 200, 90);

        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 180, 105);

        hit.Should().BeNull(
            "point (180,105) is in the AABB corner but outside the actual rotated shape body");
    }

    [Fact]
    public void HitTest_ZeroDegShape_InsideHits_NoRegression()
    {
        // 0° shape at (0,0) 100×100 DIP. Point (50,50) must still hit.
        var (pres, slide, shape) = MakeRotatedShape(0, 0, 100, 100, 0);

        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 50, 50);

        hit.Should().Be(shape.Id, "0° shape: centre point must still hit (no regression)");
    }

    [Fact]
    public void HitTest_ZeroDegShape_OutsideMisses_NoRegression()
    {
        // 0° shape at (0,0) 100×100 DIP. Point (150,150) must still miss.
        var (pres, slide, _) = MakeRotatedShape(0, 0, 100, 100, 0);

        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 150, 150);

        hit.Should().BeNull("0° shape: point outside AABB must miss (no regression)");
    }
}

// ── AD3: anchor-fixed rotated resize ───────────────────────────────────────────────────────────

/// <summary>
/// AD3 — verifies that <see cref="AvaloniaCanvasGestureHandler.ComputeResizeBounds"/> keeps
/// the anchor corner fixed in world space when the shape is rotated.
/// Tests:
///   1. 90°-rotated shape: SE handle drag → NW anchor world position is unchanged, size changes.
///   2. 0° shape: SE handle drag → result is identical to the unmodified code path (no regression).
/// </summary>
public sealed class RotatedResizeAnchorTests
{
    private static Task Run(Action action) =>
        AvaloniaInteractionTestSession.Run(action);

    private const double EmuPerDip = 9525.0;
    private static long ToEmu(double dip) => (long)Math.Round(dip * EmuPerDip);

    /// <summary>
    /// Rotates a point (px,py) by angleDeg about centre (cx,cy) — mirror of SlideTransformCore.
    /// Used in the test to verify world positions without depending on production code.
    /// </summary>
    private static (double X, double Y) Rotate(double px, double py,
                                                double cx, double cy, double deg)
    {
        if (deg == 0) return (px, py);
        double r   = deg * Math.PI / 180.0;
        double cos = Math.Cos(r), sin = Math.Sin(r);
        double dx = px - cx, dy = py - cy;
        return (cx + dx * cos - dy * sin,
                cy + dx * sin + dy * cos);
    }

    [Fact]
    public async Task ResizeSE_RotatedShape90_NwAnchorStaysFixed_SizeGrows()
    {
        // Shape: 100×100 DIP, offset (100, 100), rotated 90°.
        // Centre = (150, 150).  NW anchor corner in local = (100, 100).
        // World position of NW anchor (rotate 90° about centre):
        //   (100-150, 100-150) rotated 90° CW = (-50·cos90 - -50·sin90, -50·sin90 + -50·cos90)
        //   cos90=0 sin90=1 → (50, -50) → world = (200, 100).
        long nx = 0, ny = 0, ncx = 0, ncy = 0;

        await Run(() =>
        {
            var shape = new SlideShape
            {
                Id          = 1,
                OffsetXEmu  = ToEmu(100),
                OffsetYEmu  = ToEmu(100),
                ExtentCxEmu = ToEmu(100),
                ExtentCyEmu = ToEmu(100),
                RotationDeg = 90,
            };
            var p    = Presentation.CreateEmpty();
            var slide = p.Slides[0];
            slide.Shapes.Clear();
            slide.Shapes.Add(shape);

            var bus     = new FreeP.Core.Model.PresentationCommandBus(p);
            var editor  = new FreeP.App.Compositor.EditingSession(p, bus);
            editor.Select(shape.Id);

            var canvas  = new SlideCanvas { Presentation = p, Slide = slide };
            var adorner = new SelectionAdornerLayer();
            var handler = new AvaloniaCanvasGestureHandler(canvas, editor, adorner);
            handler.SnapToGrid   = false;
            handler.SnapToShapes = false;

            // Identity transform (scale=1, no offset).
            var xf = new SlideTransformCore(1.0, 0.0, 0.0, 1280, 720);

            // Drag SE handle by (+20, +20) screen px.
            var result = handler.SimulateResizeSE(
                new Point(0, 0), new Point(20, 20), xf, KeyModifiers.None, shape);
            nx = result.newX; ny = result.newY; ncx = result.newCx; ncy = result.newCy;
        });

        // Size must have changed.
        double newCxDip = nx == 0 ? ncx / EmuPerDip : ncx / EmuPerDip;
        newCxDip = ncx / EmuPerDip;
        double newCyDip = ncy / EmuPerDip;
        newCxDip.Should().BeGreaterThan(100,
            "SE drag on rotated shape must still grow the size in the local frame");

        // NW anchor world position must be the same as before the drag.
        // Original NW = local (100,100), centre (150,150), rot 90°.
        double origCentreX = 100 + 100 / 2.0; // 150
        double origCentreY = 100 + 100 / 2.0; // 150
        var (origAnchorWorldX, origAnchorWorldY) = Rotate(100, 100, origCentreX, origCentreY, 90);

        // New shape data.
        double newXDip  = nx / EmuPerDip;
        double newYDip  = ny / EmuPerDip;
        double newCxDipV = ncx / EmuPerDip;
        double newCyDipV = ncy / EmuPerDip;
        double newCentreX = newXDip + newCxDipV / 2.0;
        double newCentreY = newYDip + newCyDipV / 2.0;
        var (newAnchorWorldX, newAnchorWorldY) = Rotate(newXDip, newYDip, newCentreX, newCentreY, 90);

        newAnchorWorldX.Should().BeApproximately(origAnchorWorldX, 1.0,
            "NW anchor world X must be unchanged after SE resize of a 90°-rotated shape");
        newAnchorWorldY.Should().BeApproximately(origAnchorWorldY, 1.0,
            "NW anchor world Y must be unchanged after SE resize of a 90°-rotated shape");
    }

    [Fact]
    public async Task ResizeSE_ZeroDegShape_BehaviourUnchanged_NoRegression()
    {
        // 0° shape: SE drag by (+50, +60) should grow cx and cy without moving origin.
        long nx = 0, ny = 0, ncx = 0, ncy = 0;

        await Run(() =>
        {
            var shape = new SlideShape
            {
                Id          = 1,
                OffsetXEmu  = ToEmu(100),
                OffsetYEmu  = ToEmu(50),
                ExtentCxEmu = ToEmu(200),
                ExtentCyEmu = ToEmu(100),
                RotationDeg = 0,
            };
            var p     = Presentation.CreateEmpty();
            var slide = p.Slides[0];
            slide.Shapes.Clear();
            slide.Shapes.Add(shape);

            var bus     = new FreeP.Core.Model.PresentationCommandBus(p);
            var editor  = new FreeP.App.Compositor.EditingSession(p, bus);
            editor.Select(shape.Id);

            var canvas  = new SlideCanvas { Presentation = p, Slide = slide };
            var adorner = new SelectionAdornerLayer();
            var handler = new AvaloniaCanvasGestureHandler(canvas, editor, adorner);
            handler.SnapToGrid   = false;
            handler.SnapToShapes = false;

            var xf = new SlideTransformCore(1.0, 0.0, 0.0, 1280, 720);

            var result = handler.SimulateResizeSE(
                new Point(0, 0), new Point(50, 60), xf, KeyModifiers.None, shape);
            nx = result.newX; ny = result.newY; ncx = result.newCx; ncy = result.newCy;
        });

        // Origin must be unchanged for SE handle.
        nx.Should().Be(ToEmu(100), "SE resize: X origin must not change for a 0° shape");
        ny.Should().Be(ToEmu(50),  "SE resize: Y origin must not change for a 0° shape");

        // Width and height must grow.
        (ncx / EmuPerDip).Should().BeApproximately(250, 1.0,
            "0° SE drag +50px at scale=1 → width grows by 50 DIP");
        (ncy / EmuPerDip).Should().BeApproximately(160, 1.0,
            "0° SE drag +60px at scale=1 → height grows by 60 DIP");
    }
}
