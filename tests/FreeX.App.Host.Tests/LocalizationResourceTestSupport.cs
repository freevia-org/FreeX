using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreeX.App.Host.Tests;

internal static partial class LocalizationResourceTestSupport
{
    public static string ResourceDirectory =>
        DialogSourceTestSupport.FindHostSourceDirectory("Resources", "Strings.resx");

    public static Dictionary<string, string> ReadResxValues(string fileName)
    {
        var path = Path.Combine(ResourceDirectory, fileName);
        return XDocument.Load(path)
            .Descendants("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    public static HashSet<string> CompositePlaceholderTokens(string value) =>
        CompositeFormatPlaceholderPattern().Matches(value)
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);

    public static int AccessKeyCount(string value) =>
        AccessKeyPattern().Matches(value).Count;

    public static int CountAsciiLettersOutsideCompositePlaceholders(string value) =>
        CompositeFormatPlaceholderPattern()
            .Replace(value, string.Empty)
            .Count(IsAsciiLetter);

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    [GeneratedRegex(@"\{[^{}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex CompositeFormatPlaceholderPattern();

    [GeneratedRegex(@"(?<!_)_(?!_)", RegexOptions.CultureInvariant)]
    private static partial Regex AccessKeyPattern();
}
