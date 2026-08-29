using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Media;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class Wave199Deck17AptosResourceRasterEvidenceTests
{
    private const string EvidenceDirectory =
        "docs/parity/evidence/freep-wave199-deck17-aptos-resource-raster-20260829";

    [Fact]
    public void Diagnostic_RejectsCandidatesAndPreservesProductionRoute()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        var root = document.RootElement;

        root.GetProperty("schema").GetString()
            .Should().Be("freep.parity.wave199.deck17-aptos-resource-raster.v1");
        root.GetProperty("status").GetString()
            .Should().Be("diagnostic-rejected-no-production-change");

        var provenance = root.GetProperty("sourceProvenance");
        provenance.GetProperty("sourceRevision").GetString()
            .Should().Be("4760be18736bf14affc66746b450ad093e54a6bf");
        provenance.GetProperty("generationLinkage").GetString()
            .Should().Be("not-independently-proven");

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

        var baseline = root.GetProperty("acceptedBaseline");
        var slide01 = baseline.GetProperty("slide01");
        slide01.GetProperty("avaloniaOffice").GetDouble().Should().Be(0.8339);
        slide01.GetProperty("wpfAvalonia").GetDouble().Should().Be(0.8439);
        var slide02 = baseline.GetProperty("slide02");
        slide02.GetProperty("avaloniaOffice").GetDouble().Should().Be(2.482);
        slide02.GetProperty("wpfAvalonia").GetDouble().Should().Be(2.8755);

        var body = Candidate("fixed-body-liberation-sans");
        body.GetProperty("slide02").GetProperty("avaloniaOffice").GetDouble()
            .Should().BeGreaterThan(slide02.GetProperty("avaloniaOffice").GetDouble());

        var combined = Candidate("fixed-body-plus-shape-title-liberation-sans");
        combined.GetProperty("slide02").GetProperty("avaloniaOffice").GetDouble()
            .Should().BeLessThan(slide02.GetProperty("avaloniaOffice").GetDouble());
        combined.GetProperty("slide01").GetProperty("wpfAvalonia").GetDouble()
            .Should().BeGreaterThan(slide01.GetProperty("wpfAvalonia").GetDouble());

        var controls = root.GetProperty("broaderCorpusControl");
        controls.GetProperty("controlCount").GetInt32().Should().Be(18);
        controls.GetProperty("averageAfter").GetDouble()
            .Should().BeGreaterThan(controls.GetProperty("averageBefore").GetDouble());
        controls.GetProperty("worsenedStates").GetInt32().Should().Be(15);
    }

    [Fact]
    public void EvidenceImages_MatchRecordedHashes()
    {
        using var images = JsonDocument.Parse(File.ReadAllText(EvidenceFile("images.json")));
        using var metrics = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        var trackedImages = metrics.RootElement.GetProperty("imageIntegrity")
            .GetProperty("trackedImages");

        foreach (var image in images.RootElement.EnumerateObject())
        {
            var imagePath = EvidenceFile(image.Name);
            File.Exists(imagePath).Should().BeTrue($"the evidence image must exist: {image.Name}");
            using var stream = File.OpenRead(imagePath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            actualHash.Should().Be(image.Value.GetString(), $"images.json must match {image.Name}");
            trackedImages.GetProperty(image.Name).GetString().Should().Be(actualHash);
        }
    }

    private static JsonElement Candidate(string id)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        return document.RootElement.GetProperty("candidateMeasurements")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("id").GetString() == id)
            .Clone();
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

    private static string EvidenceFile(string fileName)
    {
        return TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            EvidenceDirectory.Split('/').Append(fileName).ToArray());
    }
}
