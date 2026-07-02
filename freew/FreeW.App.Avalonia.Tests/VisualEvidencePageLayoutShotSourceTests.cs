using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class VisualEvidencePageLayoutShotSourceTests
{
    [Fact]
    public void PageLayoutShot_EmitsSharedVisualEvidenceManifestAndTrustChecks()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.PageLayoutShot", "Program.cs"));
        var project = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.PageLayoutShot", "FreeW.PageLayoutShot.csproj"));

        source.Should().Contain("FreeWVisualEvidencePlanner.WriteManifest(outDir, evidence)");
        source.Should().Contain("AddAvaloniaEvidence(");
        source.Should().Contain("FreeWVisualEvidencePlanner.BuildEvidenceRow(");
        source.Should().Contain("FreeWVisualEvidencePlanner.EnsureTrusted(row)");
        source.Should().Contain("ComputePngPixelStats(");
        source.Should().Contain("page-composition-print-layout");
        source.Should().Contain("page-composition-columns");
        source.Should().Contain("page-composition-border-watermark");
        source.Should().Contain("page-composition-floating-image");
        source.Should().Contain("table-layout-complex");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument");
        source.Should().Contain("drawing-objects-complex");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildDrawingObjectsCompositionDocument");
        source.Should().Contain("chart-smartart-complex");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildChartSmartArtCompositionDocument");
        source.Should().Contain("backstage-print-preview-fidelity");
        source.Should().Contain("backstage-pdf-export-fidelity");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"backstage-print-preview-fidelity\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"backstage-pdf-export-fidelity\", 2)");
        source.Should().Contain("viewportOffsetY: 1100");
        source.Should().Contain("pageNumber: pageNumber");
        source.Should().Contain("pageCount: pageCount");
        source.Should().Contain("refusing placeholder fallback for backstage renderer evidence");
        source.Should().Contain("freew_columns_layout.png");
        source.Should().Contain("freew_border_watermark.png");
        source.Should().Contain("FreeWVisualEvidencePlanner.BuildSectionOwnerId");
        source.Should().Contain("hostId: \"avalonia-page-layout-shot\"");
        project.Should().Contain("FreeW.App.Presentation");
    }

    private static string RepositoryFile(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
