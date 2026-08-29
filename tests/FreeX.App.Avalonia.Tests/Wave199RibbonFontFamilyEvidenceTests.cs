using System.Security.Cryptography;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave199RibbonFontFamilyEvidenceTests
{
    [Fact]
    public void RejectedPhysicalRun_IsDurableAndHashComplete()
    {
        var root = Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
            "docs", "parity", "freex-wave199-ribbon-font-family", "evidence");
        var report = File.ReadAllText(Path.Combine(root, "interaction-validation.json"));
        var postcondition = File.ReadAllText(Path.Combine(root, "ribbon-home-font-family-combo-postcondition.txt"));
        var hashes = File.ReadAllText(Path.Combine(root, "SHA256SUMS.txt"));

        report.Should().Contain("\"status\":  \"failed\"");
        report.Should().Contain("automatic-focus-after-combo=false");
        postcondition.Should().Contain("automatic-focus-status=failed");
        postcondition.Should().Contain("automatic-focus-clipboard=Wave198 Font Family Target");
        postcondition.Should().Contain("worksheet-focus-after-reselect=true");
        postcondition.Should().Contain("save-clean=false");
        postcondition.Should().Contain("font-name=Calibri");

        var recordedHashes = hashes.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split("  ", 2, StringSplitOptions.None))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
        var promotedFiles = Directory.EnumerateFiles(root)
            .Select(Path.GetFileName)
            .Where(name => name != "SHA256SUMS.txt")
            .Order(StringComparer.Ordinal)
            .ToArray();

        recordedHashes.Keys.Order(StringComparer.Ordinal).Should().Equal(promotedFiles);
        foreach (var (name, expectedHash) in recordedHashes)
        {
            var actualHash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(root, name))))
                .ToLowerInvariant();
            actualHash.Should().Be(expectedHash, $"the promoted evidence hash must match {name}");
        }
    }
}
