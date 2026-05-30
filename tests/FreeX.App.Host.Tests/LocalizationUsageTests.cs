using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class LocalizationUsageTests
{
    private static readonly Regex UiTextKeyRegex = new(
        @"UiText\.(?:Get|Format)\(\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex XamlLocKeyRegex = new(
        @"\{local:Loc\s+Key=(?<quote>['""]?)(?<key>[A-Za-z0-9_]+)\k<quote>\}",
        RegexOptions.Compiled);

    [Fact]
    public void AppSourceLocalizationKeys_AllExistInNeutralResources()
    {
        var sourceRoot = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "UiText.cs"))!;
        var resourceKeys = UiText.GetNeutralResourceKeys();

        var usedKeys = Directory
            .EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(FindLocalizationKeys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        usedKeys.Should().NotBeEmpty();
        usedKeys.Should().OnlyContain(key => resourceKeys.Contains(key));
    }

    private static IEnumerable<string> FindLocalizationKeys(string path)
    {
        var source = File.ReadAllText(path);
        foreach (Match match in UiTextKeyRegex.Matches(source))
            yield return match.Groups["key"].Value;

        foreach (Match match in XamlLocKeyRegex.Matches(source))
            yield return match.Groups["key"].Value;
    }
}
