using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class VisualEvidenceFidelityRenderSourceTests
{
    [Fact]
    public void FidelityRender_EmitsSharedVisualEvidenceManifestAndTrustChecks()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));
        var project = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "FreeW.FidelityRender.csproj"));

        source.Should().Contain("FreeWVisualEvidencePlanner.WriteManifest(outDir, evidence)");
        source.Should().Contain("FreeWVisualEvidencePlanner.BuildPageExpectation(");
        source.Should().Contain("FreeWVisualEvidencePlanner.EnsureTrusted(row)");
        source.Should().Contain("ComputeWpfPixelStats(");
        source.Should().Contain("HostId: \"wpf-fidelity-render\"");
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
