namespace FreeP.App.Compositor.Tests;

public sealed class PresentationPdfPlacementDedupSourceTests
{
    [Fact]
    public void HandoutAndNotesExporters_DelegatePlacementAndMetadataToCanonicalHelpers()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var handout = Read(root, "freep", "FreeP.App.Presentation", "PresentationHandoutPdfExporter.cs");
        var notes = Read(root, "freep", "FreeP.App.Presentation", "PresentationNotesPagePdfExporter.cs");

        foreach (var source in new[] { handout, notes })
        {
            source.Should().Contain("PdfContentPagePlacement.MapOps(")
                .And.Contain("PresentationPdfScenePlanner.BuildDocumentProperties(presentation)")
                .And.NotContain("MapSlideOps(")
                .And.NotContain("NullIfBlank(")
                .And.NotContain("switch (op)");
        }
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
