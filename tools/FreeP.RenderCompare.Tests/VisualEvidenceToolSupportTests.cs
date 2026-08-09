using System.Text.Json;

namespace FreeP.RenderCompare.Tests;

public sealed class VisualEvidenceToolSupportTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Sha256_and_manifest_reader_share_deterministic_file_handling()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-visual-evidence-tool-support-");
        var manifestPath = Path.Combine(temporaryDirectory.Path, "manifest.json");
        File.WriteAllText(manifestPath, "{\"name\":\"paired\"}");

        VisualEvidenceToolSupport.Sha256(manifestPath)
            .Should().Be("c22e607ca0f823f50c42a6653d1e62f635885473a4b24b4281479ad970aefc8a");
        VisualEvidenceToolSupport.ReadManifest<ManifestStub>(
                manifestPath,
                JsonOptions,
                "missing",
                "invalid")
            .Should().Be(new ManifestStub("paired"));
    }

    [Fact]
    public void Manifest_reader_preserves_missing_and_null_manifest_diagnostics()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-visual-evidence-tool-support-");
        var missingPath = Path.Combine(temporaryDirectory.Path, "missing.json");

        Action readMissing = () => VisualEvidenceToolSupport.ReadManifest<ManifestStub>(
            missingPath,
            JsonOptions,
            "manifest missing",
            "manifest invalid");

        readMissing.Should().Throw<FileNotFoundException>()
            .WithMessage("manifest missing");

        var nullPath = Path.Combine(temporaryDirectory.Path, "null.json");
        File.WriteAllText(nullPath, "null");
        Action readNull = () => VisualEvidenceToolSupport.ReadManifest<ManifestStub>(
            nullPath,
            JsonOptions,
            "manifest missing",
            "manifest invalid");

        readNull.Should().Throw<InvalidDataException>()
            .WithMessage("manifest invalid");
    }

    private sealed record ManifestStub(string Name);
}
