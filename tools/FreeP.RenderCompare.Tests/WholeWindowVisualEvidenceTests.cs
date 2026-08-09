using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeP.RenderCompare.Tests;

public sealed class WholeWindowVisualEvidenceTests
{
    [Theory]
    [InlineData("view.gridlines-guides", "view.clean-canvas")]
    [InlineData("view.clean-canvas", "view.gridlines-guides")]
    public void View_show_state_duplicates_are_classified_as_missing_raster_effect(
        string scenarioId,
        string peerId)
    {
        WholeWindowVisualEvidence.IsViewShowStatePair(scenarioId, peerId).Should().BeTrue();
        WholeWindowVisualEvidence.IsViewShowStatePair(scenarioId, "ribbon.home").Should().BeFalse();
    }

    [Fact]
    public void Pixel_content_gate_rejects_black_transparent_and_uniform_captures()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-whole-window-content-");
        var root = temporaryDirectory.Path;
        var black = Path.Combine(root, "black.png");
        var transparent = Path.Combine(root, "transparent.png");
        var uniform = Path.Combine(root, "uniform.png");
        WriteSolidPng(black, 128, 76, 0, 0, 0, 255);
        WriteSolidPng(transparent, 128, 76, 0, 0, 0, 0);
        WriteSolidPng(uniform, 128, 76, 210, 210, 210, 255);

        ImageDiff.ValidateContent(black).IsValid.Should().BeFalse();
        ImageDiff.ValidateContent(black).Failures.Should().Contain(reason => reason.Contains("black", StringComparison.Ordinal));
        ImageDiff.ValidateContent(transparent).IsValid.Should().BeFalse();
        ImageDiff.ValidateContent(transparent).Failures.Should().Contain(reason => reason.Contains("transparent", StringComparison.Ordinal));
        ImageDiff.ValidateContent(uniform).IsValid.Should().BeFalse();
        ImageDiff.ValidateContent(uniform).Failures.Should().Contain(reason => reason.Contains("variation", StringComparison.Ordinal));
    }

    [Fact]
    public void Pixel_content_gate_accepts_structured_ui_capture()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-whole-window-ui-");
        var root = temporaryDirectory.Path;
        var path = Path.Combine(root, "ui.png");
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, 128, 76));
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(31, 64, 103)), null, new System.Windows.Rect(0, 0, 128, 8));
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(242, 242, 242)), null, new System.Windows.Rect(0, 8, 128, 18));
            context.DrawRectangle(Brushes.LightGray, null, new System.Windows.Rect(0, 26, 24, 46));
            context.DrawRectangle(Brushes.SteelBlue, null, new System.Windows.Rect(30, 34, 72, 28));
            context.DrawLine(new Pen(Brushes.Black, 1), new System.Windows.Point(0, 72), new System.Windows.Point(128, 72));
        }
        var bitmap = new RenderTargetBitmap(128, 76, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        WritePng(path, bitmap);

        var validation = ImageDiff.ValidateContent(path);

        validation.IsValid.Should().BeTrue(string.Join(", ", validation.Failures));
        validation.LuminanceStandardDeviation.Should().BeGreaterThan(3);
        validation.EdgePixelRatio.Should().BeGreaterThan(0.0005);
    }

    [Fact]
    public void Titlebar_raster_gate_requires_shared_freep_accent_in_declared_bounds()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-whole-window-titlebar-");
        var root = temporaryDirectory.Path;
        var visible = Path.Combine(root, "visible.png");
        var occluded = Path.Combine(root, "occluded.png");
        var pixels = Enumerable.Repeat((byte)255, 128 * 76 * 4).ToArray();
        for (var offset = 0; offset < pixels.Length; offset += 4)
            pixels[offset + 3] = 255;
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 128; x++)
            {
                var offset = (y * 128 + x) * 4;
                pixels[offset] = 42;
                pixels[offset + 1] = 71;
                pixels[offset + 2] = 183;
            }
        }
        WritePng(visible, BitmapSource.Create(128, 76, 96, 96, PixelFormats.Bgra32, null, pixels, 128 * 4));
        WriteSolidPng(occluded, 128, 76, 255, 255, 255, 255);
        var bounds = new FreeP.App.Compositor.WholeWindowVisualEvidenceBounds(0, 0, 128, 10);

        ImageDiff.ValidateFreePTitleBarRegion(visible, bounds).IsValid.Should().BeTrue();
        ImageDiff.ValidateFreePTitleBarRegion(occluded, bounds).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rich_editor_selection_crop_contract_rejects_missing_evidence()
    {
        var bounds = new FreeP.App.Compositor.WholeWindowVisualEvidenceBounds(0, 0, 40, 20);
        using var temporaryDirectory = new TestTemporaryDirectory("freep-selection-crop-");
        var destination = Path.Combine(temporaryDirectory.Path, "crop.png");

        ImageDiff.TryWriteCrop("missing-selection-capture.png", bounds, destination).Should().BeFalse();
        File.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public void Rich_editor_selection_state_defaults_to_missing_evidence()
    {
        FreeP.App.Compositor.WholeWindowVisualEvidenceRichEditorState.Empty.Active.Should().BeFalse();
        FreeP.App.Compositor.WholeWindowVisualEvidenceRichEditorState.Empty.Bounds.IsVisible.Should().BeFalse();
        FreeP.App.Compositor.WholeWindowVisualEvidenceRichEditorState.Empty.SelectedText.Should().BeEmpty();
    }

    [Fact]
    public void Rich_editor_selection_chrome_matches_native_wpf_contract()
    {
        FreeP.App.Compositor.InCanvasRichTextSelectionVisualContract.BackgroundRed.Should().Be(0x00);
        FreeP.App.Compositor.InCanvasRichTextSelectionVisualContract.BackgroundGreen.Should().Be(0x78);
        FreeP.App.Compositor.InCanvasRichTextSelectionVisualContract.BackgroundBlue.Should().Be(0xD7);
        FreeP.App.Compositor.InCanvasRichTextSelectionVisualContract.ForegroundRed.Should().Be(0xFF);
        FreeP.App.Compositor.InCanvasRichTextSelectionVisualContract.ForegroundGreen.Should().Be(0xFF);
        FreeP.App.Compositor.InCanvasRichTextSelectionVisualContract.ForegroundBlue.Should().Be(0xFF);
    }

    [Theory]
    [InlineData(35, 10, 40, 10, 35)]
    [InlineData(-4, 50, 40, 0, 40)]
    public void Rich_editor_selection_range_is_clamped_and_ordered(
        int start,
        int end,
        int textLength,
        int expectedStart,
        int expectedEnd)
    {
        var range = FreeP.App.Compositor.InCanvasRichTextSelectionVisualContract.NormalizeRange(
            start,
            end,
            textLength);

        range.Start.Should().Be(expectedStart);
        range.End.Should().Be(expectedEnd);
    }

    [Fact]
    public void Markdown_detail_rows_match_the_nine_column_report_header()
    {
        var comparison = new WholeWindowVisualEvidenceComparison(
            "editor.rich-text-selection",
            FreeP.App.Compositor.WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay,
            FreeP.App.Compositor.DialogPaneVisualEvidenceClassification.Pass,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            ["detail row"],
            null);
        var host = new FreeP.App.Compositor.WholeWindowVisualEvidenceHostManifest(
            1,
            "wpf",
            "test",
            96,
            1280,
            760,
            "test",
            [],
            []);
        var summary = new WholeWindowVisualEvidenceSummary(
            1,
            "test",
            1,
            0,
            1,
            0,
            0,
            0,
            0,
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            host,
            host with { Host = "avalonia" },
            [comparison],
            [],
            []);

        var markdown = WholeWindowVisualEvidence.BuildMarkdown(summary);
        var header = markdown.Split('\n').Single(line => line.StartsWith("| Scenario |", StringComparison.Ordinal));
        var detail = markdown.Split('\n').Single(line => line.Contains("detail row", StringComparison.Ordinal));

        header.Split('|').Length.Should().Be(11);
        detail.Split('|').Length.Should().Be(11);
    }

    private static void WriteSolidPng(string path, int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = green;
            pixels[offset + 2] = red;
            pixels[offset + 3] = alpha;
        }
        WritePng(path, BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4));
    }

    private static void WritePng(string path, BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
