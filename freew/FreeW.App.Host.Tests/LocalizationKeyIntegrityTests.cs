using System.IO;
using System.Text.RegularExpressions;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R131 family sweep: FreeX.App.Avalonia's Gradient Fill dialog referenced a nonexistent resx key
/// (<c>UiText.Get("FormatCells_InvalidColor")</c>), surfacing the raw <c>[[key]]</c> sentinel to the
/// user instead of a message. This durable contract test guards the FreeW WPF host the same way:
/// every literal <c>UiText.Get/Format/GetNeutral("Key")</c> call site under FreeW.App.Host must
/// resolve against the neutral resource catalog (app-owned or shared).
/// Mirrors FreeX.App.Host.Tests.LocalizationUsageTests.
/// </summary>
public sealed class LocalizationKeyIntegrityTests
{
    private static readonly Regex UiTextKeyRegex = new(
        @"UiText\.(?:Get|Format|GetNeutral)\(\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void AppSourceLocalizationKeys_AllExistInNeutralResources()
    {
        var sourceRoot = FindHostSourceDirectory();
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
    /// Sibling no-regression: a representative set of real, currently-used keys must keep
    /// resolving to real, non-sentinel text, proving the sweep above is not vacuously green.
    /// </summary>
    [Fact]
    public void RepresentativeCommonKeys_ResolveToRealNonSentinelText()
    {
        foreach (var key in new[] { "Common_Ok", "Common_Cancel" })
        {
            var text = UiText.GetNeutral(key);
            text.Should().NotBeNullOrWhiteSpace(because: $"'{key}' must resolve to real text");
            text.Should().NotStartWith("[[", because: $"'{key}' must not be a missing-key sentinel");
        }
    }

    private static IEnumerable<(string Key, string File)> FindLocalizationKeyUses(string path)
    {
        var codeOnly = string.Join(
            '\n',
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        foreach (Match match in UiTextKeyRegex.Matches(codeOnly))
            yield return (match.Groups["key"].Value, Path.GetFileName(path));
    }

    private static string FindHostSourceDirectory() =>
        Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Host");
}
