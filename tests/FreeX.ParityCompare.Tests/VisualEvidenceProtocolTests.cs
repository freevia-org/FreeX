using Free.Shared.AppServices;
using FluentAssertions;

namespace FreeX.ParityCompare.Tests;

public sealed class VisualEvidenceProtocolTests
{
    [Fact]
    public void Argument_parser_preserves_order_values_filtering_and_diagnostics()
    {
        var specs = new[]
        {
            new VisualEvidenceArgumentSpec(
                "output",
                "--capture",
                "capture missing",
                "capture blank",
                "capture duplicate"),
            new VisualEvidenceArgumentSpec(
                "scenario",
                "--surface",
                "surface missing",
                "surface blank",
                "surface duplicate"),
        };

        var parsed = VisualEvidenceArgumentParser.Parse(
            ["book.pptx", "--CAPTURE", "out", "--surface", "dialog.one", "tail"],
            specs,
            StringComparison.OrdinalIgnoreCase);

        parsed.Error.Should().BeNull();
        parsed.Value("output").Should().Be("out");
        parsed.Value("scenario").Should().Be("dialog.one");
        parsed.RemainingArguments.Should().Equal("book.pptx", "tail");

        VisualEvidenceArgumentParser.Parse(
                ["--capture", "one", "--capture", "two"],
                specs)
            .Error.Should().Be("capture duplicate");
        VisualEvidenceArgumentParser.Parse(["--surface"], specs)
            .Error.Should().Be("surface missing");
        VisualEvidenceArgumentParser.ReadFirst(
                ["--capture=output folder"],
                "--capture",
                allowEqualsSyntax: true)
            .Should().Be(new VisualEvidenceArgumentValue(true, "output folder"));
    }

    [Fact]
    public void Path_policy_normalizes_declared_paths_and_rejects_root_escape()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("visual-evidence-protocol-");
        var artifact = VisualEvidencePathPolicy.ResolveContainedPath(
            temporaryDirectory.Path,
            "wpf/images/capture.png");

        VisualEvidencePathPolicy.IsContained(temporaryDirectory.Path, artifact).Should().BeTrue();
        VisualEvidencePathPolicy.NormalizeRelativePath(temporaryDirectory.Path, artifact)
            .Should().Be("wpf/images/capture.png");

        Action escape = () => VisualEvidencePathPolicy.ResolveContainedPath(
            temporaryDirectory.Path,
            "../outside.png");
        escape.Should().Throw<InvalidDataException>()
            .WithMessage("*escapes the run root*");
    }

    [Fact]
    public void Text_policy_is_portable_and_preserves_existing_semantic_tokens()
    {
        VisualEvidenceTextPolicy.ToSafeArtifactName("review/comments:pane")
            .Should().Be("review-comments-pane");
        VisualEvidenceTextPolicy.ToAsciiSafeArtifactName("Dialog / 1:State")
            .Should().Be("Dialog---1-State");
        VisualEvidenceTextPolicy.ToAlphaNumericSafeArtifactName("grid/\u0394 1")
            .Should().Be("grid_\u0394_1");
        VisualEvidenceTextPolicy.ToLowerSafeArtifactName("  Save As  ")
            .Should().Be("save-as");
        VisualEvidenceTextPolicy.NormalizeLabel("  _Apply:  ", "ignored")
            .Should().Be("Apply");
        VisualEvidenceTextPolicy.NormalizeLabel(" ", " _Apply to All: ")
            .Should().Be("Apply to All");
        VisualEvidenceTextPolicy.SemanticActionId("+ Add slide")
            .Should().Be("add-add-slide");
    }

    [Fact]
    public void Manifest_hash_progress_and_metadata_policies_are_deterministic()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("visual-evidence-protocol-");
        var manifestPath = Path.Combine(temporaryDirectory.Path, "manifest.json");
        var options = VisualEvidenceManifestIO.CreateJsonOptions(
            propertyNameCaseInsensitive: true,
            stringEnums: false);
        var manifest = new ManifestStub("FreeX", ["dialog.one", "dialog.two"]);

        VisualEvidenceManifestIO.Write(manifestPath, manifest, options);

        VisualEvidenceManifestIO.Read<ManifestStub>(manifestPath, options)
            .Should().BeEquivalentTo(manifest);
        VisualEvidenceHash.Sha256File(manifestPath)
            .Should().Be(VisualEvidenceHash.Sha256Bytes(File.ReadAllBytes(manifestPath)));
        VisualEvidenceHash.Sha256Text("abc")
            .Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");

        var progressPath = Path.Combine(temporaryDirectory.Path, "capture-progress.log");
        VisualEvidenceProgressLog.Reset(progressPath);
        VisualEvidenceProgressLog.Append(progressPath, new VisualEvidenceProgressRecord("start dialog.one"));
        VisualEvidenceProgressLog.Append(progressPath, new VisualEvidenceProgressRecord("complete dialog.one"));
        File.ReadAllLines(progressPath).Should().Equal("start dialog.one", "complete dialog.one");

        VisualEvidenceNormalization.OrderMetadata(
                new Dictionary<string, string>
                {
                    ["zeta"] = "last",
                    ["Alpha"] = "first",
                },
                StringComparer.OrdinalIgnoreCase)
            .Keys.Should().Equal("Alpha", "zeta");
    }

    private sealed record ManifestStub(string Product, IReadOnlyList<string> Surfaces);
}
