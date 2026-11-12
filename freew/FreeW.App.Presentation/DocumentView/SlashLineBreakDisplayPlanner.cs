namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Word wraps Latin text only at spaces and hyphens: "header/footer" stays one word at a line end.
/// WPF follows the Unicode line-breaking algorithm, which allows a break after a solidus before a letter,
/// so a WPF line fits "header/" where Word moves the whole word down (text in table cells and narrow
/// newspaper columns then wraps visibly differently). A renderer that never reads its text back (print,
/// preview, fidelity capture) can insert an invisible WORD JOINER after such a solidus to suppress that
/// break opportunity. The joiner is display-only: <see cref="Strip"/> removes exactly what
/// <see cref="Join"/> inserted.
/// </summary>
public static class SlashLineBreakDisplayPlanner
{
    public const char WordJoiner = '⁠';

    public static string Join(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var slash = text.IndexOf('/');
        if (slash < 0)
            return text;

        var builder = new System.Text.StringBuilder(text.Length + 4);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            builder.Append(character);
            if (character == '/'
                && index > 0
                && !char.IsWhiteSpace(text[index - 1])
                && index + 1 < text.Length
                && char.IsLetter(text[index + 1]))
            {
                builder.Append(WordJoiner);
            }
        }

        return builder.ToString();
    }

    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains("/" + WordJoiner, StringComparison.Ordinal)
            ? text.Replace("/" + WordJoiner, "/", StringComparison.Ordinal)
            : text;
    }
}
