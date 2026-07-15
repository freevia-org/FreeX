using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// App-neutral mechanics for localization/resx hygiene tests: read the <c>name → value</c>
/// pairs out of a <c>.resx</c>, and measure composite-format placeholders / access keys /
/// translatable letters in a string. The *which app / which resx directory* concern stays
/// with the caller (pass an absolute path); these measurements are language- and
/// app-independent so a sister app does not reinvent them.
/// </summary>
internal static partial class ResxResourceTestSupport
{
    /// <summary>Reads every <c>&lt;data name="…"&gt;&lt;value&gt;</c> pair from the resx at <paramref name="path"/>.</summary>
    public static Dictionary<string, string> ReadResxValues(string path) =>
        XDocument.Load(path)
            .Descendants("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    /// <summary>Reads the resx named <paramref name="fileName"/> inside <paramref name="resourceDirectory"/>.</summary>
    public static Dictionary<string, string> ReadResxValues(string resourceDirectory, string fileName) =>
        ReadResxValues(Path.Combine(resourceDirectory, fileName));

    public static IReadOnlySet<string> FindSatelliteCultures(
        string baseDirectory,
        string satelliteAssemblyName) =>
        Directory.EnumerateDirectories(baseDirectory)
            .Where(directory => File.Exists(Path.Combine(directory, satelliteAssemblyName)))
            .Select(directory => Path.GetFileName(directory)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The set of <c>{…}</c> composite-format placeholder tokens present in <paramref name="value"/>.</summary>
    public static HashSet<string> CompositePlaceholderTokens(string value) =>
        CompositeFormatPlaceholderPattern().Matches(value)
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The number of WPF/WinForms access-key markers (a single, non-doubled underscore) in <paramref name="value"/>.</summary>
    public static int AccessKeyCount(string value) =>
        AccessKeyPattern().Matches(value).Count;

    /// <summary>The count of ASCII letters in <paramref name="value"/> ignoring any inside composite-format placeholders.</summary>
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
