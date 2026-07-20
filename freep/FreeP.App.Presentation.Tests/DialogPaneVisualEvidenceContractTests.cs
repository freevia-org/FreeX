using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class DialogPaneVisualEvidenceContractTests
{
    [Fact]
    public void Catalog_covers_every_app_owned_inventory_route_and_requested_state()
    {
        DialogPaneVisualEvidenceCatalog.All.Should().HaveCount(28);
        DialogPaneVisualEvidenceCatalog.All.Select(scenario => scenario.Id).Should().OnlyHaveUniqueItems();
        DialogPaneVisualEvidenceCatalog.All.Select(scenario => scenario.RouteId).Distinct().Should().HaveCount(19);
        DialogPaneVisualEvidenceCatalog.All.Count(scenario => scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog)
            .Should().Be(15);
        DialogPaneVisualEvidenceCatalog.All.Count(scenario => scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Pane)
            .Should().Be(11);
        DialogPaneVisualEvidenceCatalog.All.Count(scenario => scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.ChoiceOverlay)
            .Should().Be(2);

        DialogPaneVisualEvidenceCatalog.All
            .Where(scenario => scenario.RouteId is "insert.hyperlink" or "chart.edit-data" or "slideshow.custom-shows")
            .GroupBy(scenario => scenario.RouteId)
            .Should().AllSatisfy(group => group.Select(scenario => scenario.StateId)
                .Should().Equal("initial", "validation", "populated"));
    }

    [Fact]
    public void Comparer_reports_pass_for_equivalent_nonblank_pair()
    {
        var scenario = DialogPaneVisualEvidenceCatalog.Get("design.slide-size.initial");
        var wpf = Capture("wpf", nonBackgroundPixels: 20);
        var avalonia = Capture("avalonia", nonBackgroundPixels: 20);

        var comparison = DialogPaneVisualEvidenceComparer.Compare(scenario, wpf, avalonia);

        comparison.Classification.Should().Be(DialogPaneVisualEvidenceClassification.Pass);
        comparison.Details.Should().BeEmpty();
    }

    [Fact]
    public void Comparer_reports_visual_mismatch_without_turning_it_into_a_semantic_claim()
    {
        var scenario = DialogPaneVisualEvidenceCatalog.Get("design.slide-size.initial");
        var wpf = Capture("wpf", nonBackgroundPixels: 20);
        var avalonia = Capture("avalonia", nonBackgroundPixels: 0) with
        {
            LogicalHeight = 310,
            Buttons = [new("cancel", "Cancel", true, false, true), new("ok", "OK", true, true, false)],
        };

        var comparison = DialogPaneVisualEvidenceComparer.Compare(scenario, wpf, avalonia);

        comparison.Classification.Should().Be(DialogPaneVisualEvidenceClassification.Mismatch);
        comparison.DimensionsMatch.Should().BeFalse();
        comparison.ButtonOrderMatches.Should().BeFalse();
        comparison.AvaloniaNonblank.Should().BeFalse();
    }

    [Fact]
    public void Comparer_reports_missing_capture_as_limitation()
    {
        var scenario = DialogPaneVisualEvidenceCatalog.Get("review.comments-pane.seeded");

        var comparison = DialogPaneVisualEvidenceComparer.Compare(scenario, Capture("wpf", 20), null);

        comparison.Classification.Should().Be(DialogPaneVisualEvidenceClassification.Limitation);
        comparison.Details.Should().ContainSingle().Which.Should().Contain("Avalonia capture is missing");
    }

    private static DialogPaneVisualEvidenceCapture Capture(string host, long nonBackgroundPixels) =>
        new(
            "design.slide-size.initial",
            "design.slide-size",
            "initial",
            host,
            "complete",
            $"captures/{host}/design.slide-size.initial.png",
            380,
            260,
            380,
            260,
            96,
            96,
            nonBackgroundPixels,
            "textbox",
            "Width",
            [new("ok", "OK", true, true, false), new("cancel", "Cancel", true, false, true)],
            [new("textbox", "Width", true)],
            [new("state-prepared", true, "State prepared.")],
            []);
}
