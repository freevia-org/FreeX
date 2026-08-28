using System.Security.Cryptography;
using System.Text.Json;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class Wave196Deck17LightHintingEvidenceTests
{
    private const string EvidenceDirectory =
        "docs/parity/evidence/freep-wave196-deck17-light-hinting-20260829";

    [Fact]
    public void Metrics_PinTheAcceptedCorrectionAndUnchangedControl()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        var root = document.RootElement;

        root.GetProperty("schema").GetString()
            .Should().Be("freep.parity.wave196.deck17-light-hinting.v1");
        root.GetProperty("target").GetProperty("corpusSha256").GetString()
            .Should().Be("f4fc0c9e3d048cac3e0c7fe3d929029238448ff05281be542df105a46c6c88ea");

        var correction = root.GetProperty("acceptedCorrection");
        correction.GetProperty("textHintingModeBefore").GetString().Should().Be("None");
        correction.GetProperty("textHintingModeAfter").GetString().Should().Be("Light");
        correction.GetProperty("fontScale").GetDouble().Should().Be(0.93);

        var measurements = root.GetProperty("measurements");
        var control = measurements.GetProperty("slide01Control");
        control.GetProperty("after").GetProperty("avaloniaOffice").GetDouble()
            .Should().Be(control.GetProperty("before").GetProperty("avaloniaOffice").GetDouble());
        control.GetProperty("beforeAfterAvalonia").GetDouble().Should().Be(0.0);
        control.GetProperty("beforeAfterAvaloniaMaxChannel").GetInt32().Should().Be(0);

        var target = measurements.GetProperty("slide02Target");
        target.GetProperty("after").GetProperty("avaloniaOffice").GetDouble()
            .Should().BeLessThan(target.GetProperty("before").GetProperty("avaloniaOffice").GetDouble());
        target.GetProperty("after").GetProperty("wpfAvalonia").GetDouble()
            .Should().BeLessThan(target.GetProperty("before").GetProperty("wpfAvalonia").GetDouble());
        target.GetProperty("after").GetProperty("avaloniaOffice").GetDouble().Should().Be(2.4820);
        target.GetProperty("after").GetProperty("wpfAvalonia").GetDouble().Should().Be(2.8755);

        root.GetProperty("refutedHypotheses").GetArrayLength().Should().Be(7);
    }

    [Fact]
    public void EvidenceImages_MatchTheirRecordedHashes()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(EvidenceFile("images.json")));

        foreach (var image in document.RootElement.EnumerateObject())
        {
            using var stream = File.OpenRead(EvidenceFile(image.Name));
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
                .Should().Be(image.Value.GetString());
        }
    }

    private static string EvidenceFile(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return Path.Combine(root, EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar), fileName);
    }
}
