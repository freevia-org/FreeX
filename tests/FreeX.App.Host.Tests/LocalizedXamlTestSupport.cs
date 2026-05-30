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
        var expected = WebUtility.HtmlDecode(expectedValue);
        var values = FindAttributeValues(xaml, attributeName)
            .Select(ResolveLocalizedValue)
            .ToArray();

        values.Should().Contain(expected, $"the XAML should declare {attributeName} with neutral value {expected}");
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

    public static string ExtractElementByInvariantCommandName(
        this string xaml,
        string elementName,
        string commandName,
        string? requiredSubstring = null)
    {
        var escapedCommandName = EscapeAttribute(WebUtility.HtmlDecode(commandName));
        var needle = $"local:RibbonMetadata.CommandName=\"{escapedCommandName}\"";
        var searchIndex = 0;

        while (true)
        {
            var commandIndex = xaml.IndexOf(needle, searchIndex, StringComparison.Ordinal);
            commandIndex.Should().BeGreaterThanOrEqualTo(0, $"the {commandName} {elementName} should be present");

            var start = xaml.LastIndexOf($"<{elementName}", commandIndex, StringComparison.Ordinal);
            if (start >= 0)
            {
                var startTagEnd = xaml.IndexOf(">", start, StringComparison.Ordinal);
                if (startTagEnd > commandIndex)
                {
                    var element = ExtractElement(xaml, elementName, start, startTagEnd);
                    if (requiredSubstring is null || element.Contains(requiredSubstring, StringComparison.Ordinal))
                        return element;
                }
            }

            searchIndex = commandIndex + needle.Length;
        }
    }

    public static string ExtractElementByLocalizedAttributeValue(
        this string xaml,
        string elementName,
        string attributeName,
        string expectedValue,
        string? requiredSubstring = null)
    {
        var searchIndex = 0;
        while (true)
        {
            var start = xaml.IndexOf($"<{elementName}", searchIndex, StringComparison.Ordinal);
            start.Should().BeGreaterThanOrEqualTo(0, $"the {expectedValue} {elementName} should be present");

            var startTagEnd = xaml.IndexOf(">", start, StringComparison.Ordinal);
            startTagEnd.Should().BeGreaterThan(start, $"the {elementName} element should have a closing bracket");
            var startTag = xaml[start..(startTagEnd + 1)];
            var matches = FindAttributeValueMatches(startTag, attributeName);
            if (matches.Count > 0 &&
                ResolveLocalizedValue(matches[0].Groups["value"].Value) == WebUtility.HtmlDecode(expectedValue))
            {
                var element = ExtractElement(xaml, elementName, start, startTagEnd);
                if (requiredSubstring is null || element.Contains(requiredSubstring, StringComparison.Ordinal))
                    return element;
            }

            searchIndex = startTagEnd + 1;
        }
    }

    private static string FindAttributeValue(string xaml, string attributeName)
    {
        var matches = FindAttributeValueMatches(xaml, attributeName);

        matches.Should().NotBeEmpty($"the XAML fragment should declare {attributeName}");
        return matches[0].Groups["value"].Value;
    }

    private static IEnumerable<string> FindAttributeValues(string xaml, string attributeName)
    {
        var matches = FindAttributeValueMatches(xaml, attributeName).ToArray();
        matches.Should().NotBeEmpty($"the XAML fragment should declare {attributeName}");
        return matches.Select(match => match.Groups["value"].Value);
    }

    private static MatchCollection FindAttributeValueMatches(string xaml, string attributeName) =>
        Regex.Matches(
            xaml,
            $@"(?<![\w\.:]){Regex.Escape(attributeName)}=""(?<value>[^""]*)""",
            RegexOptions.CultureInvariant);

    private static string ExtractElement(string xaml, string elementName, int start, int startTagEnd)
    {
        if (startTagEnd > start && xaml[startTagEnd - 1] == '/')
            return xaml[start..(startTagEnd + 1)];

        var closing = xaml.IndexOf($"</{elementName}>", startTagEnd, StringComparison.Ordinal);
        return closing >= 0
            ? xaml[start..(closing + elementName.Length + 3)]
            : xaml[start..(startTagEnd + 1)];
    }
}
