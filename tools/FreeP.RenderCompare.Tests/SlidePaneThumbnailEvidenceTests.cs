namespace FreeP.RenderCompare.Tests;

public sealed class SlidePaneThumbnailEvidenceTests
{
    [Fact]
    public void CreatePlan_UsesSlidePaneThumbnailEvidenceRoutes()
    {
        var root = Path.Combine(Path.GetTempPath(), "freep-slide-pane-thumb-plan-" + Guid.NewGuid().ToString("N"));
        var deck = Path.Combine(root, "deck.pptx");

        var plan = SlidePaneThumbnailEvidence.CreatePlan(deck, root);

        plan.DeckPath.Should().Be(Path.GetFullPath(deck));
        plan.OutputDirectory.Should().Be(Path.GetFullPath(root));
        plan.RenderWidth.Should().Be(320);
        plan.RenderHeight.Should().Be(180);
        plan.PaneThumbnailWidth.Should().BeApproximately(150.0, 0.0001);
        plan.PaneThumbnailHeight.Should().BeApproximately(84.375, 0.0001);
        plan.WpfDirectory.Should().Be(Path.Combine(Path.GetFullPath(root), "wpf-slide-pane-thumbnails"));
        plan.AvaloniaDirectory.Should().Be(Path.Combine(Path.GetFullPath(root), "avalonia-slide-pane-thumbnails"));
        plan.PowerPointDirectory.Should().Be(Path.Combine(Path.GetFullPath(root), "powerpoint-slide-pane-thumbnails"));
        plan.DiffDirectory.Should().Be(Path.Combine(Path.GetFullPath(root), "slide-pane-thumbnail-diffs"));
        plan.RequiresPowerPointBaseline.Should().BeTrue();
    }

    [Fact]
    public void CollectFileSets_ReportsAvailableThumbnailArtifactsAcrossRenderers()
    {
        var root = Path.Combine(Path.GetTempPath(), "freep-slide-pane-thumb-files-" + Guid.NewGuid().ToString("N"));
        var plan = SlidePaneThumbnailEvidence.CreatePlan(Path.Combine(root, "deck.pptx"), root);

        try
        {
            CreatePlaceholderPng(plan.WpfDirectory, "slide-01.png");
            CreatePlaceholderPng(plan.WpfDirectory, "slide-02.png");
            CreatePlaceholderPng(plan.AvaloniaDirectory, "slide-01.png");
            CreatePlaceholderPng(plan.PowerPointDirectory, "slide-02.png");

            var fileSets = SlidePaneThumbnailEvidence.CollectFileSets(plan);

            fileSets.Should().Equal(
                new SlidePaneThumbnailEvidenceFileSet("slide-01", HasWpf: true, HasAvalonia: true, HasPowerPoint: false),
                new SlidePaneThumbnailEvidenceFileSet("slide-02", HasWpf: true, HasAvalonia: false, HasPowerPoint: true));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void CreatePlaceholderPng(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), "placeholder");
    }
}
