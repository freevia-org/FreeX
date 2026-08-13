namespace FreeP.RenderCompare.Tests;

public sealed class SlidePaneThumbnailEvidenceTests
{
    [Fact]
    public void CreatePlan_UsesSlidePaneThumbnailEvidenceRoutes()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-slide-pane-thumb-plan-");
        var root = temporaryDirectory.Path;
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
        using var temporaryDirectory = new TestTemporaryDirectory("freep-slide-pane-thumb-files-");
        var root = temporaryDirectory.Path;
        var plan = SlidePaneThumbnailEvidence.CreatePlan(Path.Combine(root, "deck.pptx"), root);

        CreatePlaceholderPng(plan.WpfDirectory, "slide-01.png");
        CreatePlaceholderPng(plan.WpfDirectory, "slide-02.png");
        CreatePlaceholderPng(plan.AvaloniaDirectory, "slide-01.png");
        CreatePlaceholderPng(plan.PowerPointDirectory, "slide-02.png");

        var fileSets = SlidePaneThumbnailEvidence.CollectFileSets(plan);

        fileSets.Should().Equal(
            new SlidePaneThumbnailEvidenceFileSet("slide-01", HasWpf: true, HasAvalonia: true, HasPowerPoint: false),
            new SlidePaneThumbnailEvidenceFileSet("slide-02", HasWpf: true, HasAvalonia: false, HasPowerPoint: true));
    }

    [Theory]
    [InlineData(false, 0, 0, 1)]
    [InlineData(true, 0, 0, 0)]
    [InlineData(true, 2, 0, 2)]
    [InlineData(true, 0, 2, 2)]
    public void GetExitCode_AllowsMissingPowerPointComOnlyWhenRequested(
        bool allowMissingPowerPoint,
        int wpfExitCode,
        int avaloniaExitCode,
        int expected)
    {
        var powerPoint = PowerPointExportResult.Failed(PowerPointExportFailureKind.ComUnavailable, 0, 0);

        SlidePaneThumbnailEvidence.GetExitCode(wpfExitCode, avaloniaExitCode, powerPoint, allowMissingPowerPoint)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void GetExitCode_DoesNotAllowPowerPointExportFailures()
    {
        var powerPoint = PowerPointExportResult.Failed(PowerPointExportFailureKind.ExportFailed, 0, 0);

        SlidePaneThumbnailEvidence.GetExitCode(
                wpfExitCode: 0,
                avaloniaExitCode: 0,
                powerPoint: powerPoint,
                allowMissingPowerPoint: true)
            .Should()
            .Be(1);
    }

    private static void CreatePlaceholderPng(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), "placeholder");
    }
}
