using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SourceTodoDocumentationTests
{
    // Round 172: the mojibake scans above can only ever report what MojibakeRegex recognises, so a
    // gap in the pattern reads exactly like a clean tree -- which is how box-drawing and arrow
    // mojibake survived in the repo for as long as it did. These two theories are the tripwire on the
    // tripwire: the first fails if the pattern stops detecting a real mis-decode, the second fails if
    // it starts flagging ordinary localized text (the FreeX/FreeW resx files are full of both Slovak
    // caron letters and French non-breaking spaces, and an over-broad pattern makes the whole guard
    // useless by crying wolf). Inputs are built from char codes so this file stays free of the very
    // byte sequences it is asserting about.
    [Theory]
    // "°" (U+00B0, UTF-8 C2 B0) mis-decoded: a 2-byte lead plus one continuation.
    [InlineData("rotated 90\u00C2\u00B0")]
    // "—" (U+2014, UTF-8 E2 80 94): a 3-byte lead plus two continuations.
    [InlineData("capture \u00E2\u20AC\u201D 2026-08-16")]
    // "─" (U+2500, UTF-8 E2 94 80) -- the box-drawing banner case the old pattern missed, because
    // U+20AC was reachable only through a lead set that did not admit this shape.
    [InlineData("// \u00E2\u201D\u20AC\u00E2\u201D\u20AC Helpers")]
    // "→" (U+2192, UTF-8 E2 86 92): the arrow case, missed because U+2020 was not in the class.
    [InlineData("Negative indices \u00E2\u2020\u2019 #VALUE!")]
    // "Ａ" (U+FF21, UTF-8 EF BC A1): fullwidth test data, missed for the same reason.
    [InlineData("new TextValue(\u00EF\u00BC\u00A1)")]
    // "«"/"»" (U+00AB/U+00BB): the mail-merge delimiters.
    [InlineData("placeholder \u00C2\u00ABValue\u00C2\u00BB")]
    public void MojibakePattern_DetectsRealMisDecodes(string line) =>
        MojibakeRegex().IsMatch(line).Should().BeTrue();

    [Theory]
    // Slovak: U+00E1 then U+017E. Both are continuation-class characters, but U+00E1 is a 3-byte
    // lead carrying only one of them, so arity rules it out.
    [InlineData("<value>Ukážka štýlu {0}</value>")]
    // French: U+00E9 followed by a non-breaking space, the same shape one continuation short.
    [InlineData("<value>L'aperçu avant impression a échoué : {0}</value>")]
    // Correctly encoded text made of characters that are themselves in the continuation class.
    [InlineData("// ─── Helpers ───")]
    [InlineData("rotated 90°, then → inherit — as documented")]
    [InlineData("placeholder «Value»")]
    public void MojibakePattern_LeavesCorrectlyEncodedTextAlone(string line) =>
        MojibakeRegex().IsMatch(line).Should().BeFalse();

    // Round 172: this guard used to scan only "src", so every sibling app and every test/tool tree
    // was unwatched -- and that is exactly where the mojibake had accumulated (box-drawing banners in
    // FreeP/FreeW, arrows in FreeX.Core.Formula, guillemet merge-field delimiters in FreeW's mail
    // merge tests, an em-dash in a parity report's runtime output). Scanning one tree while the same
    // text lives in six is a guard that reports green by not looking.
    private static readonly string[] SourceScanDirectories =
        ["src", "shared", "tools", "tests", "freew", "freep"];

    [Fact]
    public void SourceText_DoesNotContainMojibake()
    {
        var repoDirectory = WorkspaceFileLocator.FindWorkspaceRoot();
        var invalidLines = SourceScanDirectories
            .Select(directory => Path.Combine(repoDirectory, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            .Where(IsTrackedSourceFile)
            .Where(path => SourceTextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .SelectMany(path => FindMojibake(repoDirectory, path))
            .ToList();

        invalidLines.Should().BeEmpty("source text shown to users and maintainers should not contain garbled UTF-8/Windows-1252 mojibake");
    }

    [Fact]
    public void DocumentationText_DoesNotContainMojibake()
    {
        var repoDirectory = WorkspaceFileLocator.FindWorkspaceRoot();
        var docsDirectory = Path.Combine(repoDirectory, "docs");
        var invalidLines = Directory
            .EnumerateFiles(docsDirectory, "*.md", SearchOption.AllDirectories)
            // Round 172: the repo-root markdown (README, AGENTS, SECURITY, THIRD_PARTY_*) is the most
            // widely read documentation there is and was outside every mojibake scan.
            .Concat(Directory.EnumerateFiles(repoDirectory, "*.md", SearchOption.TopDirectoryOnly))
            .SelectMany(path => FindMojibake(repoDirectory, path))
            .ToList();

        invalidLines.Should().BeEmpty("build, release, and parity documentation should stay readable after branch handoffs");
    }

    [Fact]
    public void SourceDeferredWorkMarkers_LinkToTrackingDocumentation()
    {
        var repoDirectory = WorkspaceFileLocator.FindWorkspaceRoot();
        var sourceDirectory = Path.Combine(repoDirectory, "src");
        var invalidMarkers = Directory
            .EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(IsTrackedSourceFile)
            .SelectMany(path => FindInvalidMarkers(repoDirectory, path))
            .ToList();

        invalidMarkers.Should().BeEmpty(
            "source TODO/FIXME/HACK/XXX markers must use '// TODO(owner): note (ref: docs/file.md#anchor)' or an exact documented legacy mapping so deferred work remains traceable");
    }

    private static bool IsTrackedSourceFile(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase) &&
            !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindInvalidMarkers(string repoDirectory, string path)
    {
        var relativePath = Path.GetRelativePath(repoDirectory, path).Replace('\\', '/');
        var lines = File.ReadLines(path).Select((line, index) => new { Line = line, Number = index + 1 });

        foreach (var line in lines)
        {
            if (!DeferredWorkMarkerRegex().IsMatch(line.Line))
                continue;

            if (DocumentedDeferredWorkMarkerRegex().IsMatch(line.Line) ||
                IsDocumentedLegacyMarker(repoDirectory, relativePath, line.Line))
            {
                continue;
            }

            yield return $"{relativePath}:{line.Number}: {line.Line.Trim()}";
        }
    }

    private static bool IsDocumentedLegacyMarker(string repoDirectory, string relativePath, string line)
    {
        var marker = LegacyDeferredWorkMarkers.SingleOrDefault(candidate =>
            candidate.SourcePath == relativePath &&
            line.Contains(candidate.Marker, StringComparison.Ordinal));
        if (marker is null)
            return false;

        var documentPath = Path.Combine(
            repoDirectory,
            marker.DocumentPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(documentPath) &&
            File.ReadAllText(documentPath).Contains(marker.Heading, StringComparison.Ordinal);
    }

    private static IEnumerable<string> FindMojibake(string repoDirectory, string path)
    {
        var relativePath = Path.GetRelativePath(repoDirectory, path).Replace('\\', '/');
        var lines = File.ReadLines(path).Select((line, index) => new { Line = line, Number = index + 1 });

        foreach (var line in lines)
        {
            if (MojibakeRegex().IsMatch(line.Line))
                yield return $"{relativePath}:{line.Number}: {line.Line.Trim()}";
        }
    }

    private static readonly string[] SourceTextExtensions = [".cs", ".xaml", ".props", ".targets", ".resx"];

    private static readonly LegacyDeferredWorkMarker[] LegacyDeferredWorkMarkers =
    [
        new(
            "src/FreeX.Core.Formula/FormulaRewriter.cs",
            "// TODO(H28 3-D sheet-span refs, partially addressed — F1 defined-names-span-rowcol-shift):",
            "docs/planning/formula-deferred-work.md",
            "## H28 3-D Sheet-Span Structural Rewrites")
    ];

    [GeneratedRegex(@"//\s*(TODO|FIXME|HACK|XXX)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeferredWorkMarkerRegex();

    [GeneratedRegex(@"//\s*(TODO|FIXME|HACK|XXX)\([A-Za-z0-9_.-]+\):\s+\S.+\s+\(ref:\s+docs/[A-Za-z0-9_./-]+\.md#[^)]+\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentedDeferredWorkMarkerRegex();

    // Mojibake is UTF-8 bytes decoded as Windows-1252: a UTF-8 LEAD byte followed by CONTINUATION
    // bytes, each seen through CP1252. Round 172 rewrote this pattern after both halves proved wrong
    // in opposite directions.
    //
    // The continuation class was a partial hand-list of CP1252's mappings for 0x80-0xBF and omitted,
    // among others, U+2022 (0x95) and U+2020 (0x86) -- which is exactly why box-drawing banners
    // ("═", lead E2 then 0x95) and arrows ("→", lead E2 then 0x86) sat mis-decoded in the tree
    // unreported. It is now CP1252's COMPLETE mapping of 0x80-0xBF, leaving no byte to hide behind.
    //
    // Widening the lead to the whole UTF-8 lead range then over-fired on ordinary localized text,
    // because a continuation char is also a perfectly normal letter: Slovak "Ukážka" is U+00E1 U+017E
    // and French "échoué : " ends U+00E9 U+00A0, both structurally identical to a 2-byte mis-decode.
    // What separates them is ARITY -- real mojibake carries exactly as many continuations as its lead
    // byte announces, and the trailing lookahead forbids one more. "Ukážka" fails because U+00E1 is a
    // 3-byte lead with only one continuation after it; a mis-decoded em-dash (U+00E2 U+20AC U+201D --
    // named rather than written out, or this comment would trip the very scan below) matches because
    // U+00E2 is a 3-byte lead with exactly two. Anchoring on arity keeps the class complete without
    // false alarms.
    [GeneratedRegex(
        "[\\uFFFD]"
        + "|[\\u00C2-\\u00DF]" + MojibakeContinuation + "(?!" + MojibakeContinuation + ")"
        + "|[\\u00E0-\\u00EF]" + MojibakeContinuation + "{2}(?!" + MojibakeContinuation + ")"
        + "|[\\u00F0-\\u00F4]" + MojibakeContinuation + "{3}(?!" + MojibakeContinuation + ")",
        RegexOptions.CultureInvariant)]
    private static partial Regex MojibakeRegex();

    /// <summary>CP1252's complete mapping of the UTF-8 continuation-byte range 0x80-0xBF.</summary>
    private const string MojibakeContinuation =
        "[\\u0081\\u008D\\u008F\\u0090\\u009D\\u00A0-\\u00BF\\u0152\\u0153\\u0160\\u0161\\u0178"
        + "\\u017D\\u017E\\u0192\\u02C6\\u02DC\\u2013\\u2014\\u2018-\\u201A\\u201C-\\u201E"
        + "\\u2020-\\u2022\\u2026\\u2030\\u2039\\u203A\\u20AC\\u2122]";

    private sealed record LegacyDeferredWorkMarker(
        string SourcePath,
        string Marker,
        string DocumentPath,
        string Heading);
}
