using System.IO;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

internal static partial class XamlLocalizationTestHelper
{
    public static string ReadLocalizedXaml(string xamlFileName)
    {
        var path = WorkspaceFileLocator.Find("src", "FreeX.App.Host", xamlFileName);
        return ResolveLocMarkup(File.ReadAllText(path));
    }

    public static XDocument LoadLocalizedXaml(string xamlFileName) =>
        XDocument.Parse(ReadLocalizedXaml(xamlFileName), LoadOptions.PreserveWhitespace);

    private static string ResolveLocMarkup(string xaml) =>
        LocMarkupPattern().Replace(
            xaml,
            match => SecurityElement.Escape(UiText.Get(match.Groups["key"].Value)) ?? string.Empty);

    [GeneratedRegex(@"\{local:Loc\s+Key=(?<key>[A-Za-z0-9_]+)\}")]
    private static partial Regex LocMarkupPattern();
}
