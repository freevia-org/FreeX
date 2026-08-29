using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave199RibbonFontFamilyEvidenceTests
{
    private static readonly Regex LocalEvidenceReferencePattern = new(
        @"(?<![A-Za-z0-9._/\\-])(?<path>(?:(?:\.\.|[A-Za-z0-9._-]+)[/\\])*[A-Za-z0-9._-]+\.(?:html|json|png|txt))(?![A-Za-z0-9._/\\-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        var traversalReferences = ExtractLocalEvidenceReferences("../outside.json ..\\outside.txt");
        traversalReferences.Should().Equal("../outside.json", "..\\outside.txt");
        foreach (var traversalReference in traversalReferences)
        {
            var resolveTraversal = () => ResolveWithinEvidenceRoot(root, traversalReference);
            resolveTraversal.Should().Throw<InvalidDataException>()
                .WithMessage($"*{traversalReference}*");
        }

        var mixedLineEndings = System.Text.Encoding.UTF8.GetBytes("alpha\r\nbeta\rgamma\n");
        var canonicalLineEndings = NormalizeCanonicalTextBytes(mixedLineEndings);
        System.Text.Encoding.UTF8.GetString(canonicalLineEndings)
            .Should().Be("alpha\nbeta\ngamma\n");
        canonicalLineEndings.Should().NotContain((byte)'\r');

        using var reportDocument = System.Text.Json.JsonDocument.Parse(report);
        var evidenceDirectory = reportDocument.RootElement
            .GetProperty("physicalX11")
            .GetProperty("evidenceDirectory")
            .GetString();
        evidenceDirectory.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(ResolveWithinEvidenceRoot(root, evidenceDirectory!)).Should().BeTrue();

        foreach (var sourceName in new[]
                 {
                     "interaction-validation.json",
                     "x11-input-results.json",
                     "interaction-validation.html",
                 })
        {
            AssertEveryLocalEvidenceReferenceExists(root, sourceName);
        }

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
            return NormalizeCanonicalTextBytes(bytes);
        }

        return bytes;
    }

    private static byte[] NormalizeCanonicalTextBytes(byte[] bytes)
    {
        var text = System.Text.Encoding.UTF8.GetString(bytes)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        return System.Text.Encoding.UTF8.GetBytes(text);
    }

    private static void AssertEveryLocalEvidenceReferenceExists(string root, string sourceName)
    {
        var source = File.ReadAllText(Path.Combine(root, sourceName));
        var references = ExtractLocalEvidenceReferences(source);

        references.Should().NotBeEmpty($"{sourceName} must retain its local evidence references");
        foreach (var reference in references)
        {
            var target = ResolveWithinEvidenceRoot(root, reference);
            File.Exists(target).Should().BeTrue($"{sourceName} references promoted evidence {reference}");
        }
    }

    private static string[] ExtractLocalEvidenceReferences(string source)
    {
        return LocalEvidenceReferencePattern.Matches(source)
            .Select(match => match.Groups["path"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveWithinEvidenceRoot(string root, string relativePath)
    {
        var rootPath = Path.GetFullPath(root);
        var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(
            rootPath,
            relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)));

        if (!target.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
            && !target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Local evidence reference {relativePath} escapes the promoted bundle.");
        }

        return target;
    }
}
