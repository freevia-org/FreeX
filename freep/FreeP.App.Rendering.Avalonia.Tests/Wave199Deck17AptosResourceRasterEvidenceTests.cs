using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Media;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;
using SkiaSharp;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class Wave199Deck17AptosResourceRasterEvidenceTests
{
    private const string EvidenceDirectory =
        "docs/parity/evidence/freep-wave199-deck17-aptos-resource-raster-20260829";

    [Fact]
    public void Diagnostic_RecomputesEveryRetainedPixelGateAndRecordsUnsupportedBoundaries()
    {
        using var metricsDocument = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        using var referencesDocument = JsonDocument.Parse(File.ReadAllText(EvidenceFile("references.json")));
        using var controlsDocument = JsonDocument.Parse(File.ReadAllText(EvidenceFile("broader-controls.json")));
        var root = metricsDocument.RootElement;

        root.GetProperty("schema").GetString()
            .Should().Be("freep.parity.wave199.deck17-aptos-resource-raster.v2");
        root.GetProperty("status").GetString()
            .Should().Be("diagnostic-rejected-no-production-change");

        var provenance = root.GetProperty("sourceProvenance");
        provenance.GetProperty("sourceRevision").GetString()
            .Should().Be("4760be18736bf14affc66746b450ad093e54a6bf");
        provenance.GetProperty("generationLinkage").GetString()
            .Should().Be("not-independently-proven");

        var semantics = root.GetProperty("comparisonSemantics");
        semantics.GetProperty("implementation").GetString()
            .Should().Be("tools/FreeP.RenderCompare/ImageDiff.cs");
        semantics.GetProperty("recordedPrecisionDecimals").GetInt32().Should().Be(4);

        var production = root.GetProperty("productionRoute");
        production.GetProperty("aptosFallback").GetString().Should().Be("Arial");
        production.GetProperty("fixedSizeAptosBodyFontScale").GetDouble().Should().Be(0.93);
        production.GetProperty("textRenderingMode").GetString().Should().Be("Antialias");
        production.GetProperty("textHintingMode").GetString().Should().Be("Light");
        production.GetProperty("sourceChanged").GetBoolean().Should().BeFalse();

        SlideCanvas.ResolvePowerPointFontFamily("Aptos").Should().Be("Arial");
        SlideCanvas.ResolvePowerPointFontFamily("Aptos Display").Should().Be("Arial");
        SlideCanvas.FixedSizeAptosBodyFontScale.Should().Be(0.930);
        SlideCanvas.ResolveFixedSizeAptosBodyTextHintingMode(CreateLayout(8))
            .Should().Be(TextHintingMode.Light);

        var references = referencesDocument.RootElement;
        references.GetProperty("schema").GetString()
            .Should().Be("freep.parity.wave199.deck17-references.v1");
        var corpus = references.GetProperty("corpus");
        AssertFileHash(
            WorkspaceFile(corpus.GetProperty("path").GetString()!),
            corpus.GetProperty("sha256").GetString());

        var referencePaths = references.GetProperty("images")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("id").GetString()!,
                item => WorkspaceFile(item.GetProperty("path").GetString()!),
                StringComparer.Ordinal);
        referencePaths.Should().HaveCount(6);
        foreach (var reference in references.GetProperty("images").EnumerateArray())
        {
            AssertFileHash(
                WorkspaceFile(reference.GetProperty("path").GetString()!),
                reference.GetProperty("sha256").GetString());
        }

        var imageCache = new Dictionary<string, ImagePixels>(StringComparer.OrdinalIgnoreCase);
        var accepted = root.GetProperty("acceptedBaseline");
        AssertAcceptedMetrics(accepted.GetProperty("slide01"), "01", referencePaths, imageCache);
        AssertAcceptedMetrics(accepted.GetProperty("slide02"), "02", referencePaths, imageCache);

        var candidates = root.GetProperty("candidateMeasurements").EnumerateArray().ToArray();
        candidates.Select(candidate => candidate.GetProperty("id").GetString()).Should().Equal(
            "global-calibri",
            "global-carlito",
            "global-liberation-sans",
            "fixed-body-liberation-sans",
            "fixed-body-plus-shape-title-liberation-sans",
            "fixed-body-liberation-sans-scale-0.950");

        foreach (var candidate in candidates)
        {
            candidate.GetProperty("status").GetString().Should().Be("pixel-gate-rejected");
            candidate.GetProperty("artifactIdentity").GetString()
                .Should().Be("historical-label-not-independently-proven");

            AssertCandidateMetrics(candidate, "01", referencePaths, imageCache);
            AssertCandidateMetrics(candidate, "02", referencePaths, imageCache);
            AssertRejectionGate(candidate, accepted);
        }

        var apiProbe = root.GetProperty("apiProbe");
        apiProbe.GetProperty("status").GetString()
            .Should().Be("independently-auditable-unsupported-api");
        Enum.GetNames<TextHintingMode>().Should().NotContain("Full");

        var observations = root.GetProperty("unverifiedObservations").EnumerateArray().ToArray();
        observations.Should().ContainSingle();
        observations[0].GetProperty("id").GetString().Should().Be("native-aptos-resource");
        observations[0].GetProperty("status").GetString()
            .Should().Be("not-independently-auditable");

        AssertBroaderControlBoundary(root.GetProperty("broaderCorpusControl"), controlsDocument.RootElement);
        root.GetProperty("decision").GetProperty("outcome").GetString()
            .Should().Be("reject-retained-pixel-artifacts-preserve-production-Arial-route");
    }

    [Fact]
    public void EvidenceImages_MatchRecordedHashes()
    {
        using var images = JsonDocument.Parse(File.ReadAllText(EvidenceFile("images.json")));
        using var metrics = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        var trackedImages = metrics.RootElement.GetProperty("imageIntegrity")
            .GetProperty("trackedImages");

        images.RootElement.EnumerateObject().Should().HaveCount(12);
        foreach (var image in images.RootElement.EnumerateObject())
        {
            var imagePath = EvidenceFile(image.Name);
            AssertFileHash(imagePath, image.Value.GetString());
            trackedImages.GetProperty(image.Name).GetString().Should().Be(image.Value.GetString());
        }
    }

    private static void AssertAcceptedMetrics(
        JsonElement recorded,
        string slide,
        IReadOnlyDictionary<string, string> references,
        IDictionary<string, ImagePixels> imageCache)
    {
        var office = references[$"office-slide-{slide}"];
        var avalonia = references[$"accepted-avalonia-slide-{slide}"];
        var wpf = references[$"wpf-slide-{slide}"];

        AssertRecordedMetric(recorded, "avaloniaOffice", avalonia, office, imageCache);
        AssertRecordedMetric(recorded, "wpfOffice", wpf, office, imageCache);
        AssertRecordedMetric(recorded, "wpfAvalonia", wpf, avalonia, imageCache);
    }

    private static void AssertCandidateMetrics(
        JsonElement candidate,
        string slide,
        IReadOnlyDictionary<string, string> references,
        IDictionary<string, ImagePixels> imageCache)
    {
        var metricKey = $"slide{slide}";
        var imageName = candidate.GetProperty("images").GetProperty(metricKey).GetString();
        imageName.Should().NotBeNullOrWhiteSpace();
        var candidatePath = EvidenceFile(imageName!);

        AssertRecordedMetric(
            candidate.GetProperty(metricKey),
            "avaloniaOffice",
            candidatePath,
            references[$"office-slide-{slide}"],
            imageCache);
        AssertRecordedMetric(
            candidate.GetProperty(metricKey),
            "wpfAvalonia",
            candidatePath,
            references[$"wpf-slide-{slide}"],
            imageCache);
    }

    private static void AssertRecordedMetric(
        JsonElement recorded,
        string property,
        string pathA,
        string pathB,
        IDictionary<string, ImagePixels> imageCache)
    {
        var actual = Compare(
            LoadImage(pathA, imageCache),
            LoadImage(pathB, imageCache)).MeanChannelDiffPercent;
        Math.Round(actual, 4).Should().Be(
            recorded.GetProperty(property).GetDouble(),
            $"{property} must be recomputable from {pathA} and {pathB}");
    }

    private static void AssertRejectionGate(JsonElement candidate, JsonElement accepted)
    {
        var slide01 = candidate.GetProperty("slide01");
        var slide02 = candidate.GetProperty("slide02");
        var acceptedSlide01 = accepted.GetProperty("slide01");
        var acceptedSlide02 = accepted.GetProperty("slide02");

        switch (candidate.GetProperty("rejectionGate").GetString())
        {
            case "slide02-both-regress":
                slide02.GetProperty("avaloniaOffice").GetDouble().Should()
                    .BeGreaterThan(acceptedSlide02.GetProperty("avaloniaOffice").GetDouble());
                slide02.GetProperty("wpfAvalonia").GetDouble().Should()
                    .BeGreaterThan(acceptedSlide02.GetProperty("wpfAvalonia").GetDouble());
                break;
            case "slide02-office-regresses":
                slide02.GetProperty("avaloniaOffice").GetDouble().Should()
                    .BeGreaterThan(acceptedSlide02.GetProperty("avaloniaOffice").GetDouble());
                break;
            case "slide01-wpf-avalonia-regresses":
                slide01.GetProperty("wpfAvalonia").GetDouble().Should()
                    .BeGreaterThan(acceptedSlide01.GetProperty("wpfAvalonia").GetDouble());
                break;
            default:
                throw new InvalidDataException(
                    $"Unknown rejection gate: {candidate.GetProperty("rejectionGate").GetString()}");
        }
    }

    private static void AssertBroaderControlBoundary(JsonElement recorded, JsonElement inventory)
    {
        recorded.GetProperty("status").GetString().Should().Be("not-independently-auditable");
        recorded.GetProperty("sourceInventory").GetString().Should().Be("broader-controls.json");
        recorded.GetProperty("retainedCandidateRenderCount").GetInt32().Should().Be(0);
        recorded.GetProperty("recomputableComparisonCount").GetInt32().Should().Be(0);
        recorded.TryGetProperty("averageBefore", out _).Should().BeFalse();
        recorded.TryGetProperty("averageAfter", out _).Should().BeFalse();
        recorded.TryGetProperty("perSlide", out _).Should().BeFalse();

        inventory.GetProperty("schema").GetString()
            .Should().Be("freep.parity.wave199.broader-control-inventory.v1");
        inventory.GetProperty("retainedCandidateRenderCount").GetInt32().Should().Be(0);
        var controls = inventory.GetProperty("controls").EnumerateArray().ToArray();
        controls.Should().HaveCount(recorded.GetProperty("officeReferenceCount").GetInt32());
        controls.Select(control =>
                $"{control.GetProperty("deck").GetString()}/{control.GetProperty("slide").GetString()}")
            .Should().OnlyHaveUniqueItems();

        foreach (var control in controls)
        {
            AssertFileHash(
                WorkspaceFile(control.GetProperty("officeReference").GetString()!),
                control.GetProperty("sha256").GetString());
        }
    }

    private static PixelDiff Compare(ImagePixels first, ImagePixels second)
    {
        var width = Math.Max(first.Width, second.Width);
        var height = Math.Max(first.Height, second.Height);
        long totalDiff = 0;
        var maxDiff = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var a = first.PixelOrWhite(x, y);
                var b = second.PixelOrWhite(x, y);
                var aAlpha = a.Alpha / 255.0;
                var bAlpha = b.Alpha / 255.0;
                var dR = (int)Math.Abs(
                    a.Red * aAlpha + 255.0 * (1.0 - aAlpha)
                    - (b.Red * bAlpha + 255.0 * (1.0 - bAlpha)));
                var dG = (int)Math.Abs(
                    a.Green * aAlpha + 255.0 * (1.0 - aAlpha)
                    - (b.Green * bAlpha + 255.0 * (1.0 - bAlpha)));
                var dB = (int)Math.Abs(
                    a.Blue * aAlpha + 255.0 * (1.0 - aAlpha)
                    - (b.Blue * bAlpha + 255.0 * (1.0 - bAlpha)));
                totalDiff += dR + dG + dB;
                maxDiff = Math.Max(maxDiff, Math.Max(dR, Math.Max(dG, dB)));
            }
        }

        var pixelCount = (long)width * height;
        var maxPossible = pixelCount * 3.0 * 255.0;
        return new PixelDiff(
            maxPossible > 0 ? totalDiff / maxPossible * 100.0 : 0.0,
            maxDiff);
    }

    private static ImagePixels LoadImage(
        string path,
        IDictionary<string, ImagePixels> imageCache)
    {
        if (imageCache.TryGetValue(path, out var cached))
            return cached;

        using var bitmap = SKBitmap.Decode(path)
            ?? throw new InvalidDataException($"Could not decode evidence image: {path}");
        var pixels = new SKColor[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
                pixels[y * bitmap.Width + x] = bitmap.GetPixel(x, y);
        }

        var loaded = new ImagePixels(bitmap.Width, bitmap.Height, pixels);
        imageCache.Add(path, loaded);
        return loaded;
    }

    private static void AssertFileHash(string filePath, string? expectedHash)
    {
        File.Exists(filePath).Should().BeTrue($"the durable evidence artifact must exist: {filePath}");
        using var stream = File.OpenRead(filePath);
        Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant().Should().Be(expectedHash);
    }

    private static ResolvedTextLayout CreateLayout(int paragraphCount) => new()
    {
        AutoFitKind = TextAutoFitKind.None,
        ColumnCount = 1,
        Paragraphs = Enumerable.Range(0, paragraphCount)
            .Select(_ => new ResolvedParagraph
            {
                Runs =
                [
                    new ResolvedRun
                    {
                        Text = "Office body",
                        FontFamily = "Aptos",
                        FontSizePt = 18.0,
                        Color = SrgbColor.Black
                    }
                ],
                BulletKind = BulletKind.None
            })
            .ToArray()
    };

    private static string EvidenceFile(string fileName) =>
        TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            EvidenceDirectory.Split('/').Append(fileName).ToArray());

    private static string WorkspaceFile(string relativePath) =>
        TestWorkspaceFileLocator.FindFromWorkspaceRoot(relativePath.Split('/'));

    private sealed record ImagePixels(int Width, int Height, SKColor[] Pixels)
    {
        public SKColor PixelOrWhite(int x, int y) =>
            x < Width && y < Height ? Pixels[y * Width + x] : SKColors.White;
    }

    private readonly record struct PixelDiff(double MeanChannelDiffPercent, int MaxChannelDiff);
}
