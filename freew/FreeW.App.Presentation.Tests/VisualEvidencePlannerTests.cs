using System.Text.Json;
using System.Security.Cryptography;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class VisualEvidencePlannerTests
{
    [Fact]
    public void ScenarioCatalog_IncludesF2PageCompositionContracts()
    {
        var scenarios = FreeWVisualEvidencePlanner.Scenarios;

        scenarios.Select(s => s.ScenarioId).Should().Contain([
            "f2-hf-basic",
            "f2-hf-firstpage",
            "f2-hf-oddeven",
            "f2-footnotes",
            "f2-endnotes",
            "f2-section-landscape",
            "f2-tracked-changes",
            "f2-comments",
            "page-composition-print-layout",
            "page-composition-web-layout",
            "page-composition-draft",
            "page-composition-floating-image"]);

        var sectionScenario = FreeWVisualEvidencePlanner.ResolveScenario("f2-section-landscape.docx");
        sectionScenario.ExpectedFeatureTags.Should().Contain(["f2", "section-geometry", "portrait-landscape"]);
        sectionScenario.Composition.ExpectsSectionGeometryChange.Should().BeTrue();

        var floatingScenario = FreeWVisualEvidencePlanner.ResolveScenario("page-composition-floating-image");
        floatingScenario.Composition.ExpectsFloatingObjects.Should().BeTrue();
    }

    [Fact]
    public void BuildPageExpectation_UsesSharedGeometryAndExpectedOutputName()
    {
        var page = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 72,
            MarginBottomPt = 72
        };

        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-hf-basic",
            page,
            pageNumber: 2,
            pageCount: 3,
            outputName: "actual.png",
            headerSlotName: "header",
            footerSlotName: "footer");

        expectation.ExpectedOutputName.Should().Be("f2-hf-basic_p2.png");
        expectation.LayoutKind.Should().Be(nameof(DocumentViewLayoutKind.PrintLayout));
        expectation.HeaderSlotName.Should().Be("header");
        expectation.FooterSlotName.Should().Be("footer");
        expectation.Composition.ExpectsHeadersFooters.Should().BeTrue();
        expectation.Geometry.PageWidthDip.Should().BeApproximately(816, 0.01);
        expectation.Geometry.ContentWidthDip.Should().BeApproximately(624, 0.01);
        expectation.Geometry.TextAreaHeightDip.Should().BeApproximately(864, 0.01);
    }

    [Fact]
    public void ComputePixelStats_AndTrustGuard_RejectBlankAllBackgroundCapture()
    {
        var blank = new byte[20 * 20 * 4];
        for (var i = 0; i < blank.Length; i += 4)
        {
            blank[i + 0] = 255;
            blank[i + 1] = 255;
            blank[i + 2] = 255;
            blank[i + 3] = 255;
        }

        var stats = FreeWVisualEvidencePlanner.ComputePixelStats(
            blank,
            width: 20,
            height: 20,
            stride: 20 * 4,
            FreeWVisualEvidencePixelFormat.Bgra32);
        var row = BuildRow(stats, byteLength: 1_024);

        row.Trust.Passed.Should().BeFalse();
        row.Trust.Failures.Should().Contain(f => f.Contains("distinct sampled colors", StringComparison.Ordinal));
        row.Trust.Failures.Should().Contain(f => f.Contains("non-background pixel ratio", StringComparison.Ordinal));
        row.Trust.Failures.Should().Contain(f => f.Contains("dominant color ratio", StringComparison.Ordinal));
        Action act = () => FreeWVisualEvidencePlanner.EnsureTrusted(row);
        act.Should().Throw<InvalidOperationException>().WithMessage("*blank.png*failed trust checks*");
    }

    [Fact]
    public void BuildManifest_SerializesStableSchemaAndTrustedRows()
    {
        var pixels = new byte[20 * 20 * 4];
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                var offset = (y * 20 + x) * 4;
                if (x is >= 2 and <= 17 && y is >= 8 and <= 12)
                {
                    pixels[offset + 0] = (byte)(x % 3 == 0 ? 32 : 0);
                    pixels[offset + 1] = (byte)(y % 2 == 0 ? 32 : 0);
                    pixels[offset + 2] = (byte)(x % 5 == 0 ? 160 : 0);
                }
                else
                {
                    pixels[offset + 0] = 255;
                    pixels[offset + 1] = 255;
                    pixels[offset + 2] = 255;
                }
                pixels[offset + 3] = 255;
            }
        }

        var stats = FreeWVisualEvidencePlanner.ComputePixelStats(
            pixels,
            width: 20,
            height: 20,
            stride: 20 * 4,
            FreeWVisualEvidencePixelFormat.Bgra32);
        var row = BuildRow(stats, byteLength: 2_048, outputName: "f2-hf-basic_p1.png");

        row.Trust.Passed.Should().BeTrue();

        var manifest = FreeWVisualEvidencePlanner.BuildManifest(
            [row],
            new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        var json = FreeWVisualEvidencePlanner.ToJson(manifest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("schemaId").GetString().Should().Be("freew.visual-evidence.v1");
        root.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        root.GetProperty("product").GetString().Should().Be("FreeW");
        root.GetProperty("scenarios").GetArrayLength().Should().Be(1);
        root.GetProperty("evidence")[0].GetProperty("scenarioId").GetString().Should().Be("f2-hf-basic");
        root.GetProperty("evidence")[0].GetProperty("trust").GetProperty("passed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_RelativizesOutputsAndComputesHashes()
    {
        var root = CreateTempRoot();
        try
        {
            var expected = FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios;
            var rows = expected
                .SelectMany(e => Enumerable.Range(1, e.MinimumExpectedOutputs)
                    .Select(page => BuildFileBackedRow(
                        root,
                        e.HostId,
                        e.ScenarioId,
                        page,
                        e.MinimumExpectedOutputs)))
                .ToList();
            var wpfDir = Path.Combine(root, "wpf");
            var avaloniaDir = Path.Combine(root, "avalonia");
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                rows.Where(r => r.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId).ToList(),
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            FreeWVisualEvidencePlanner.WriteManifest(
                avaloniaDir,
                rows.Where(r => r.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId).ToList(),
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [
                    Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName),
                    Path.Combine(avaloniaDir, FreeWVisualEvidencePlanner.ManifestFileName)
                ],
                root);

            summary.Trust.Passed.Should().BeTrue();
            summary.Sources.Should().HaveCount(2);
            summary.Scenarios.Should().OnlyContain(s => s.Trust.Passed);
            summary.Evidence.Should().HaveCount(expected.Sum(e => e.MinimumExpectedOutputs));
            summary.Evidence.Should().OnlyContain(e => !Path.IsPathRooted(e.OutputPath));
            summary.Evidence.Should().OnlyContain(e => e.OutputPath.Contains('/', StringComparison.Ordinal));
            summary.Evidence.Should().OnlyContain(e => e.Sha256.Length == 64);

            var first = summary.Evidence.Single(e =>
                e.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId
                && e.ScenarioId == "f2-hf-basic"
                && e.PageNumber == 1);
            first.OutputPath.Should().Be("wpf/f2-hf-basic_p1.png");
            first.ByteLength.Should().Be(2_048);
            first.Sha256.Should().Be(ComputeSha256(Path.Combine(root, first.OutputPath.Replace('/', Path.DirectorySeparatorChar))));

            var json = FreeWVisualEvidenceManifestNormalizer.ToJson(summary);
            json.Should().NotContain(Path.GetFileName(root));
            json.Should().Contain("f2-hf-basic");

            var markdown = FreeWVisualEvidenceManifestNormalizer.ToMarkdown(summary);
            markdown.Should().Contain("Scenario Coverage");
            markdown.Should().Contain("avalonia-page-layout-shot");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildNormalizedSummaryFromFiles_ReportsMissingExpectedScenario()
    {
        var root = CreateTempRoot();
        try
        {
            var wpfDir = Path.Combine(root, "wpf");
            var row = BuildFileBackedRow(
                root,
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "f2-hf-basic",
                pageNumber: 1,
                pageCount: 1);
            FreeWVisualEvidencePlanner.WriteManifest(
                wpfDir,
                [row],
                new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

            var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
                [Path.Combine(wpfDir, FreeWVisualEvidencePlanner.ManifestFileName)],
                root,
                [
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-hf-basic",
                        1),
                    new FreeWVisualEvidenceExpectedScenario(
                        FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                        "f2-comments",
                        1)
                ]);

            summary.Trust.Passed.Should().BeFalse();
            summary.Scenarios.Single(s => s.ScenarioId == "f2-comments").Trust.Passed.Should().BeFalse();
            summary.Trust.Failures.Should().Contain(f =>
                f.Contains("f2-comments", StringComparison.Ordinal)
                && f.Contains("expected at least 1", StringComparison.Ordinal));
            Action act = () => FreeWVisualEvidenceManifestNormalizer.EnsureSummaryTrusted(summary);
            act.Should().Throw<InvalidOperationException>().WithMessage("*f2-comments*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FreeWVisualEvidenceRow BuildRow(
        FreeWVisualPixelStats stats,
        long byteLength,
        string outputName = "blank.png")
    {
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            "f2-hf-basic",
            new PageSettings(),
            pageNumber: 1,
            pageCount: 1,
            outputName: outputName);
        var capture = new FreeWVisualEvidenceCapture(
            ScenarioId: "f2-hf-basic",
            HostId: "test-host",
            OutputName: outputName,
            OutputPath: outputName,
            PixelWidth: stats.Width,
            PixelHeight: stats.Height,
            ByteLength: byteLength,
            PixelStats: stats,
            PageExpectation: expectation,
            HostMetadata: new Dictionary<string, string> { ["renderer"] = "test" });

        return FreeWVisualEvidencePlanner.BuildEvidenceRow(capture);
    }

    private static FreeWVisualEvidenceRow BuildFileBackedRow(
        string root,
        string hostId,
        string scenarioId,
        int pageNumber,
        int pageCount)
    {
        var scenario = FreeWVisualEvidencePlanner.ResolveScenario(scenarioId);
        var outputName = FreeWVisualEvidencePlanner.ExpectedOutputName(scenarioId, pageNumber);
        var outputDir = Path.Combine(
            root,
            hostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId ? "wpf" : "avalonia");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, outputName);
        var bytes = Enumerable.Range(0, 2_048).Select(i => (byte)(i % 251)).ToArray();
        File.WriteAllBytes(outputPath, bytes);

        var stats = BuildTrustedStats();
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            scenarioId,
            new PageSettings(),
            pageNumber,
            pageCount,
            outputName,
            scenario.LayoutKind);
        var capture = new FreeWVisualEvidenceCapture(
            ScenarioId: scenarioId,
            HostId: hostId,
            OutputName: outputName,
            OutputPath: outputPath,
            PixelWidth: stats.Width,
            PixelHeight: stats.Height,
            ByteLength: bytes.LongLength,
            PixelStats: stats,
            PageExpectation: expectation,
            HostMetadata: new Dictionary<string, string> { ["renderer"] = hostId });

        return FreeWVisualEvidencePlanner.BuildEvidenceRow(capture);
    }

    private static FreeWVisualPixelStats BuildTrustedStats()
    {
        var pixels = new byte[20 * 20 * 4];
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                var offset = (y * 20 + x) * 4;
                if (x is >= 2 and <= 17 && y is >= 8 and <= 12)
                {
                    pixels[offset + 0] = (byte)(x % 3 == 0 ? 32 : 0);
                    pixels[offset + 1] = (byte)(y % 2 == 0 ? 32 : 0);
                    pixels[offset + 2] = (byte)(x % 5 == 0 ? 160 : 0);
                }
                else
                {
                    pixels[offset + 0] = 255;
                    pixels[offset + 1] = 255;
                    pixels[offset + 2] = 255;
                }
                pixels[offset + 3] = 255;
            }
        }

        return FreeWVisualEvidencePlanner.ComputePixelStats(
            pixels,
            width: 20,
            height: 20,
            stride: 20 * 4,
            FreeWVisualEvidencePixelFormat.Bgra32);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FreeWVisualEvidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
