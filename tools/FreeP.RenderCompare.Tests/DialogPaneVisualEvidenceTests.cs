using FreeP.App.Compositor;

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
        var root = Path.Combine(Path.GetTempPath(), "freep-dialog-pane-report-" + Guid.NewGuid().ToString("N"));
        try
        {
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
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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
