using System.IO;
using System.Text.RegularExpressions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R131: the Avalonia shell's Gradient Fill "invalid color" message referenced
/// <c>UiText.Get("FormatCells_InvalidColor")</c> — a resx key that does not exist anywhere in the
/// FreeX.App.Localization or shared catalogs — so the user saw the raw <c>[[FormatCells_InvalidColor]]</c>
/// sentinel instead of a message (fixed to reuse the WPF host's sibling key
/// <c>ShapeGradient_InvalidRgbColorMessage</c>). This is the durable version of that fix: every
/// literal <c>UiText.Get/Format/GetNeutral("Key")</c> call site under FreeX.App.Avalonia must
/// resolve against the neutral resource catalog, so a future typo/rename fails the build instead
/// of surfacing a raw key at runtime. Mirrors FreeX.App.Host.Tests.LocalizationUsageTests
/// (WPF host has carried the equivalent contract test since before this defect was found; the
/// Avalonia shell had no such gate, which is how this key went unnoticed).
/// </summary>
public sealed class LocalizationKeyIntegrityTests
{
    private static readonly Regex UiTextKeyRegex = new(
        @"UiText\.(?:Get|Format|GetNeutral)\(\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void AppSourceLocalizationKeys_AllExistInNeutralResources()
    {
        var sourceRoot = FindAvaloniaSourceDirectory();
        var resourceKeys = UiText.GetNeutralResourceKeys();

        var used = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(FindLocalizationKeyUses)
            .ToArray();

        var missing = used
            .Where(use => !resourceKeys.Contains(use.Key))
            .Select(use => $"{use.Key} (referenced from {use.File})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        used.Should().NotBeEmpty();
        missing.Should().BeEmpty(
            because: "every literal UiText.Get/Format/GetNeutral key must exist in the neutral resource " +
                     $"catalog so it never surfaces as a raw [[key]] sentinel; missing: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Sibling no-regression: the specific Gradient Fill "invalid color" call site (and its true
    /// FormatCells siblings for Fill/Pattern invalid-color messages elsewhere in the shell) must
    /// keep resolving to real, non-sentinel, non-empty text — proving the fix did not merely swap
    /// one broken key for another and did not disturb the neighboring dialogs.
    /// </summary>
    [Fact]
    public void InvalidColorMessages_ResolveToRealNonSentinelText()
    {
        var dialogSource = File.ReadAllText(Path.Combine(
            FindAvaloniaSourceDirectory(), "MainWindow.DrawingFormatDialogs.cs"));

        dialogSource.Should().Contain(
            "ShowEditIssue(UiText.Get(\"ShapeGradient_InvalidRgbColorMessage\"))",
            because: "the Gradient Fill dialog's invalid-color path must use the real shared key, " +
                     "matching the WPF host's ShapeGradientDialog.cs sibling");
        dialogSource.Should().NotContain(
            "FormatCells_InvalidColor",
            because: "the nonexistent key must not be reintroduced");

        foreach (var key in new[]
                 {
                     "ShapeGradient_InvalidRgbColorMessage",
                     "FormatCells_InvalidFillColorMessage",
                     "FormatCells_InvalidPatternColorMessage",
                 })
        {
            var text = UiText.GetNeutral(key);
            text.Should().NotBeNullOrWhiteSpace(because: $"'{key}' must resolve to real text");
            text.Should().NotStartWith("[[", because: $"'{key}' must not be a missing-key sentinel");
        }
    }

    private static IEnumerable<(string Key, string File)> FindLocalizationKeyUses(string path)
    {
        // Skip XML doc-comment lines: UiText.cs's own "Keeps call sites short
        // (<c>UiText.Get("Key")</c>)" summary is a documentation example, not a real call, and
        // "Key" is not (and should not become) a resource key.
        var codeOnly = string.Join(
            '\n',
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        foreach (Match match in UiTextKeyRegex.Matches(codeOnly))
            yield return (match.Groups["key"].Value, Path.GetFileName(path));
    }

    private static string FindAvaloniaSourceDirectory() =>
        Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
            "src",
            "FreeX.App.Avalonia");
}
