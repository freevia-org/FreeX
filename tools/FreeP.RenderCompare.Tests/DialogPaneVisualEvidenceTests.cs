using FreeP.App.Compositor;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeP.RenderCompare.Tests;

public sealed class DialogPaneVisualEvidenceTests
{
    [Fact]
    public void BuildSummary_counts_complete_real_pair_contract()
    {
        var summary = DialogPaneVisualEvidence.BuildSummary(Manifest("wpf"), Manifest("avalonia"));

        summary.ScenarioCount.Should().Be(28);
        summary.RouteCount.Should().Be(19);
        summary.PairedCaptureCount.Should().Be(28);
        summary.PassCount.Should().Be(28);
        summary.MismatchCount.Should().Be(0);
        summary.LimitationCount.Should().Be(0);
    }

    [Fact]
    public void Canonical_comments_pane_evidence_is_a_fresh_same_authority_pass()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var summaryPath = Path.Combine(
            root,
            "docs",
            "parity",
            "freep-dialog-pane-visual-evidence",
            "summary.json");
        using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
        var comparison = document.RootElement
            .GetProperty("comparisons")
            .EnumerateArray()
            .Single(item => item.GetProperty("scenarioId").GetString() == "review.comments-pane.seeded");

        comparison.GetProperty("classification").GetString().Should().Be("pass");
        comparison.GetProperty("dimensionsMatch").GetBoolean().Should().BeTrue();
        comparison.GetProperty("focusMatches").GetBoolean().Should().BeTrue();
        comparison.GetProperty("buttonOrderMatches").GetBoolean().Should().BeTrue();
        comparison.GetProperty("enabledStateMatches").GetBoolean().Should().BeTrue();

        var target = comparison.GetProperty("pixelMetrics");
        target.GetProperty("normalizedWidth").GetInt32().Should().Be(1100);
        target.GetProperty("normalizedHeight").GetInt32().Should().Be(100);
        target.GetProperty("changedPixelRatio").GetDouble().Should().BeLessThan(0.20);
        target.GetProperty("meanChannelDelta").GetDouble().Should().BeLessThan(18.0);
        target.GetProperty("thresholdPassed").GetBoolean().Should().BeTrue();

        var shell = comparison.GetProperty("shellContextPixelMetrics");
        shell.GetProperty("normalizedWidth").GetInt32().Should().Be(1280);
        shell.GetProperty("normalizedHeight").GetInt32().Should().Be(760);
        shell.GetProperty("thresholdPassed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void BuildSummary_preserves_visual_mismatch_without_semantic_parity_claim()
    {
        var avalonia = Manifest("avalonia");
        avalonia = avalonia with
        {
            Captures = avalonia.Captures.Select(capture => capture.ScenarioId == "design.slide-size.initial"
                ? capture with { LogicalHeight = capture.LogicalHeight + 20 }
                : capture).ToArray(),
        };

        var summary = DialogPaneVisualEvidence.BuildSummary(Manifest("wpf"), avalonia);

        summary.MismatchCount.Should().Be(1);
        summary.Comparisons.Single(comparison => comparison.ScenarioId == "design.slide-size.initial")
            .Details.Should().Contain(detail => detail.Contains("dimensions differ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WriteReports_emits_json_markdown_html_and_paired_paths()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-dialog-pane-report-");
        var root = temporaryDirectory.Path;
        var summary = DialogPaneVisualEvidence.BuildSummary(Manifest("wpf"), Manifest("avalonia"));

        DialogPaneVisualEvidence.WriteReports(root, summary);

        File.ReadAllText(Path.Combine(root, "summary.json")).Should().Contain("\"pairedCaptureCount\": 28");
        File.ReadAllText(Path.Combine(root, "report.md")).Should()
            .Contain("[WPF](wpf/design.slide-size.initial.png)")
            .And.Contain("Semantic route coverage is not treated as visual parity");
        File.ReadAllText(Path.Combine(root, "report.html")).Should()
            .Contain("src=\"avalonia/design.slide-size.initial.png\"")
            .And.Contain("Paired 28");
    }

    [Fact]
    public void CompareNormalized_reports_scaled_pixel_delta_and_alpha_over_white()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-dialog-pane-pixels-");
        var root = temporaryDirectory.Path;
        var transparent = Path.Combine(root, "transparent.png");
        var white = Path.Combine(root, "white.png");
        var black = Path.Combine(root, "black.png");
        WriteSolidPng(transparent, 2, 2, 0, 0, 0, 0);
        WriteSolidPng(white, 4, 4, 255, 255, 255, 255);
        WriteSolidPng(black, 4, 4, 0, 0, 0, 255);

        var composited = ImageDiff.CompareNormalized(transparent, white, 8, 8);
        var changed = ImageDiff.CompareNormalized(white, black, 8, 8, Path.Combine(root, "diff.png"));

        composited.ChangedPixelRatio.Should().Be(0);
        composited.MeanChannelDelta.Should().Be(0);
        changed.WidthA.Should().Be(4);
        changed.WidthB.Should().Be(4);
        changed.NormalizedWidth.Should().Be(8);
        changed.ChangedPixelRatio.Should().Be(1);
        changed.MeanChannelDelta.Should().Be(255);
        changed.MaxChannelDelta.Should().Be(255);
        changed.BackgroundHandling.Should().Contain("alpha-composited-over-white");
        File.Exists(Path.Combine(root, "diff.png")).Should().BeTrue();
    }

    [Fact]
    public void BuildSummary_uses_target_crop_for_acceptance_and_shell_context_for_information()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-dialog-pane-targets-");
        var root = temporaryDirectory.Path;
        Directory.CreateDirectory(Path.Combine(root, "wpf"));
        Directory.CreateDirectory(Path.Combine(root, "avalonia"));
        foreach (var host in new[] { "wpf", "avalonia" })
        {
            WriteSolidPng(Path.Combine(root, host, "context.png"), 8, 8, 240, 240, 240, 255);
            WriteSolidPng(Path.Combine(root, host, "target.png"), 8, 8, 255, 255, 255, 255);
        }

        var summary = DialogPaneVisualEvidence.BuildSummary(
            PixelManifest("wpf"),
            PixelManifest("avalonia"),
            evidenceRoot: root);

        summary.PassCount.Should().Be(28);
        summary.Comparisons.Should().OnlyContain(comparison =>
            comparison.PixelMetrics != null && comparison.PixelMetrics.ThresholdPassed);
        summary.Comparisons.Single(comparison => comparison.ScenarioId == "review.comments-pane.seeded")
            .ShellContextPixelMetrics.Should().NotBeNull();
        summary.Comparisons.Single(comparison => comparison.ScenarioId == "design.slide-size.initial")
            .ShellContextPixelMetrics.Should().BeNull();
    }

    private static DialogPaneVisualEvidenceHostManifest Manifest(string host) => new(
        1,
        host,
        "test",
        96,
        1280,
        760,
        "2026-07-20T00:00:00Z",
        DialogPaneVisualEvidenceCatalog.All.Select(scenario => Capture(host, scenario)).ToArray(),
        []);

    private static DialogPaneVisualEvidenceHostManifest PixelManifest(string host) => new(
        1,
        host,
        "test",
        96,
        1280,
        760,
        "2026-07-20T00:00:00Z",
        DialogPaneVisualEvidenceCatalog.All.Select(scenario => PixelCapture(host, scenario)).ToArray(),
        []);

    private static DialogPaneVisualEvidenceCapture PixelCapture(string host, DialogPaneVisualEvidenceScenario scenario)
    {
        var targetPath = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
            ? $"{host}/context.png"
            : $"{host}/target.png";
        return Capture(host, scenario) with
        {
            ImagePath = $"{host}/context.png",
            LogicalWidth = 8,
            LogicalHeight = 8,
            PixelWidth = 8,
            PixelHeight = 8,
            PixelComparisonImagePath = targetPath,
            PixelComparisonLogicalWidth = 8,
            PixelComparisonLogicalHeight = 8,
        };
    }

    private static void WriteSolidPng(
        string path,
        int width,
        int height,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = green;
            pixels[offset + 2] = red;
            pixels[offset + 3] = alpha;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static DialogPaneVisualEvidenceCapture Capture(string host, DialogPaneVisualEvidenceScenario scenario) => new(
        scenario.Id,
        scenario.RouteId,
        scenario.StateId,
        host,
        "complete",
        $"{host}/{scenario.Id}.png",
        scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog ? 440 : 1280,
        scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog ? 320 : 760,
        scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog ? 440 : 1280,
        scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog ? 320 : 760,
        96,
        96,
        50,
        "textbox",
        "Input",
        [new("ok", "OK", true, true, false), new("cancel", "Cancel", true, false, true)],
        [new("textbox", "Input", true)],
        [new("fixture", true, "Prepared")],
        []);
}
