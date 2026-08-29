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
        var attributes = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(".gitattributes");

        report.Should().Contain("\"status\":  \"failed\"");
        report.Should().Contain("automatic-focus-after-combo=false");
        postcondition.Should().Contain("automatic-focus-status=failed");
        postcondition.Should().Contain("automatic-focus-clipboard=Wave198 Font Family Target");
        postcondition.Should().Contain("worksheet-focus-after-reselect=true");
        postcondition.Should().Contain("save-clean=false");
        postcondition.Should().Contain("font-name=Calibri");
        attributes.Should().Contain("*.json text eol=lf");
        attributes.Should().Contain("*.txt text eol=lf");

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
                SHA256.HashData(ReadCanonicalEvidenceBytes(Path.Combine(root, name))))
                .ToLowerInvariant();
            actualHash.Should().Be(expectedHash, $"the promoted evidence hash must match {name}");
        }
    }

    private static byte[] ReadCanonicalEvidenceBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
        {
            // Evidence text is tracked with eol=lf. Hash the Git/blob representation so a Windows
            // checkout with CRLF conversion and a Linux checkout with LF bytes verify identically.
            var text = System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n");
            return System.Text.Encoding.UTF8.GetBytes(text);
        }

        return bytes;
    }
}
