using System.IO;
using System.Text.Json;

namespace FreeW.App.Presentation.Tests;

public sealed class DesignDialogEvidenceTests
{
    [Fact]
    public void GeneratedDesignEvidence_ListsAllOwnedRoutesAndFreshnessContract()
    {
        var jsonPath = RepositoryFile("docs", "parity", "freew-design-dialog-parity-20260720.json");
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = document.RootElement;

        root.GetProperty("Schema").GetString().Should().Be("freew.design-dialog-parity.v1");
        root.GetProperty("RouteCounts").GetProperty("Total").GetInt32().Should().Be(11);
        root.GetProperty("RouteCounts").GetProperty("RemainingOwnedRoutes").GetInt32().Should().Be(0);
        root.GetProperty("Routes").GetArrayLength().Should().Be(11);
        root.GetProperty("SourceHashes").EnumerateObject().Should().NotBeEmpty();

        var script = File.ReadAllText(RepositoryFile("tools", "Generate-FreeWDesignDialogParityEvidence.ps1"));
        script.Should().Contain("[switch]$Check");
        script.Should().Contain("Get-FileHash -LiteralPath $resolved -Algorithm SHA256");
        script.Should().Contain("Stale evidence");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
