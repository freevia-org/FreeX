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
        // r520: SourceHashes was REMOVED deliberately (see "Stop parity evidence from pinning hashes
        // of constantly-edited sources"): pinning a hash of every covered source made this artifact
        // go stale on any unrelated edit. The assertion that it is non-empty was left behind and
        // threw KeyNotFoundException, so it is inverted here to pin the new contract instead of
        // simply deleted -- reintroducing the property should fail this test, not pass silently.
        root.TryGetProperty("SourceHashes", out _).Should().BeFalse();

        var script = File.ReadAllText(RepositoryFile("tools", "Generate-FreeWDesignDialogParityEvidence.ps1"));
        script.Should().Contain("[switch]$Check");
        // r520: the generator no longer hashes covered sources AT ALL -- neither the normalised
        // helper nor Get-FileHash -- so asserting it still calls one was the second assertion left
        // behind by the same change. Both are replaced by the contract that actually holds now:
        // no source hashing of any kind, which is what stops this artifact going stale on an
        // unrelated edit.
        script.Should().NotContain("Get-ToolNormalizedTextSha256 -Path $resolved");
        script.Should().NotContain("Get-FileHash -LiteralPath $resolved");
        // r520: the freshness gate survives, but its wording changed with the hashing removal. The
        // point of this assertion is that -Check still REFUSES stale evidence rather than quietly
        // regenerating it, so it now pins the two conditions the script actually rejects on.
        script.Should().Contain("Recorded GeneratedInputs no longer match");
        script.Should().Contain("Unexpected evidence schema");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
