using System.Text;
using System.Text.RegularExpressions;

namespace FreeX.App.Host;

internal static partial class PseudoLocalization
{
    public static string Expand(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
            return string.Empty;

        var builder = new StringBuilder(value.Length * 2 + 4);
        builder.Append("[[");

        var position = 0;
        foreach (Match match in CompositeFormatPlaceholderPattern().Matches(value))
        {
            AppendExpandedLiteral(value.AsSpan(position, match.Index - position), builder);
            builder.Append(match.Value);
            position = match.Index + match.Length;
        }

        AppendExpandedLiteral(value.AsSpan(position), builder);
        builder.Append("]]");
        return builder.ToString();
    }

    private static void AppendExpandedLiteral(ReadOnlySpan<char> value, StringBuilder builder)
    {
        foreach (var character in value)
        {
            builder.Append(character);
            if (IsAsciiLetter(character))
                builder.Append(character);
        }
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    [GeneratedRegex(@"\{[^{}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex CompositeFormatPlaceholderPattern();
}
