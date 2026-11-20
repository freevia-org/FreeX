using System.Globalization;
using System.Text;

namespace Free.Shared.AppServices;

/// <summary>
/// The default author initials the Office apps derive from a user name: the first letter of each
/// name word, upper-cased ("John Smith" gives "JS", "John Quincy Adams" gives "JQA"). The user can
/// still override the result in the app's options. A "letter" is a whole text element, so a name
/// that starts outside the BMP never yields half of a surrogate pair (a lone surrogate stored in an
/// OOXML <c>initials</c> attribute aborts the whole save).
/// </summary>
public static class AuthorInitials
{
    /// <summary>
    /// Derives the default initials for <paramref name="name"/>; empty when the name has no words.
    /// </summary>
    public static string Derive(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var word in name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            builder.Append(FirstTextElement(word).ToUpperInvariant());
        return builder.ToString();
    }

    /// <summary>
    /// The leading text element of <paramref name="text"/> (never half of a surrogate pair);
    /// empty for null or empty input.
    /// </summary>
    public static string FirstTextElement(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text[..StringInfo.GetNextTextElementLength(text)];
    }
}
