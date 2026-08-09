using FluentAssertions;
using System.IO;
using System.Text.RegularExpressions;

internal static class LocalizationKeyIntegrityTestSupport
{
    private static readonly Regex UiTextKeyRegex = new(
        @"UiText\.(?:Get|Format|GetNeutral)\(\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    public static void AssertAllLiteralUiTextKeysExist(
        string solutionFileName,
        IReadOnlySet<string> neutralResourceKeys,
        bool requireLiteralUses,
        params string[] sourceDirectoryParts)
    {
        var sourceRoot = FindSourceDirectory(solutionFileName, sourceDirectoryParts);
        var used = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(FindLocalizationKeyUses)
            .ToArray();

        var missing = used
            .Where(use => !neutralResourceKeys.Contains(use.Key))
            .Select(use => $"{use.Key} (referenced from {use.File})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        if (requireLiteralUses)
            used.Should().NotBeEmpty();

        missing.Should().BeEmpty(
            because: "every literal UiText.Get/Format/GetNeutral key must exist in the neutral resource " +
                     $"catalog so it never surfaces as a raw [[key]] sentinel; missing: {string.Join(", ", missing)}");
    }

    public static void AssertKeysResolveToRealNonSentinelText(
        Func<string, string> getNeutralText,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            var text = getNeutralText(key);
            text.Should().NotBeNullOrWhiteSpace(because: $"'{key}' must resolve to real text");
            text.Should().NotStartWith("[[", because: $"'{key}' must not be a missing-key sentinel");
        }
    }

    public static string FindSourceDirectory(
        string solutionFileName,
        params string[] sourceDirectoryParts)
    {
        var repositoryRoot = TestWorkspaceFileLocator
            .FindDirectoryContainingFileFromBaseDirectory(solutionFileName);
        return sourceDirectoryParts.Aggregate(
            repositoryRoot,
            static (path, part) => Path.Combine(path, part));
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ||
        path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(string Key, string File)> FindLocalizationKeyUses(string path)
    {
        // UiText wrapper summaries include illustrative UiText.Get("Key") calls that are not
        // runtime resource references and must not make "Key" part of the neutral catalog.
        var codeOnly = string.Join(
            '\n',
            File.ReadLines(path)
                .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        foreach (Match match in UiTextKeyRegex.Matches(codeOnly))
            yield return (match.Groups["key"].Value, Path.GetFileName(path));
    }
}
