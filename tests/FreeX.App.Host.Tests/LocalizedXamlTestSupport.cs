using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

internal static class LocalizedXamlTestSupport
{
    private const string LocPrefix = "{local:Loc Key=";

    public static void ShouldContainInvariantCommandName(this string xaml, string commandName) =>
        xaml.Should().Contain($"local:RibbonMetadata.CommandName=\"{EscapeAttribute(WebUtility.HtmlDecode(commandName))}\"");

    public static void ShouldContainLocalizedAttribute(this string xaml, string attributeName, string expectedValue)
    {
        var rawValue = FindAttributeValue(xaml, attributeName);
        ResolveLocalizedValue(rawValue).Should().Be(WebUtility.HtmlDecode(expectedValue));
    }

    public static string? ResolveLocalizedValue(string? value)
    {
        if (value is null)
            return null;

        var decoded = WebUtility.HtmlDecode(value);
        if (!decoded.StartsWith(LocPrefix, StringComparison.Ordinal) ||
            !decoded.EndsWith("}", StringComparison.Ordinal))
        {
            return decoded;
        }

        var key = decoded[LocPrefix.Length..^1];
        return UiText.Get(key);
    }

    public static string EscapeAttribute(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string FindAttributeValue(string xaml, string attributeName)
    {
        var match = Regex.Match(
            xaml,
            $@"(?<![\w\.:]){Regex.Escape(attributeName)}=""(?<value>[^""]*)""",
            RegexOptions.CultureInvariant);

        match.Success.Should().BeTrue($"the XAML fragment should declare {attributeName}");
        return match.Groups["value"].Value;
    }
}
