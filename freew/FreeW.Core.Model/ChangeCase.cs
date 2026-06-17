using System.Text;

namespace FreeW.Core.Model;

/// <summary>The five "Change Case" transforms offered by the Home &gt; Font command.</summary>
public enum CaseKind
{
    /// <summary>Every letter upper-cased (e.g. "Hello World" → "HELLO WORLD").</summary>
    Upper,

    /// <summary>Every letter lower-cased (e.g. "Hello World" → "hello world").</summary>
    Lower,

    /// <summary>First letter of each sentence upper, the rest lower (e.g. "hi. bye" → "Hi. Bye").</summary>
    Sentence,

    /// <summary>First letter of each word upper, the rest lower (e.g. "hello world" → "Hello World").</summary>
    Capitalize,

    /// <summary>Invert the case of every letter (e.g. "Hello" → "hELLO").</summary>
    Toggle,
}

/// <summary>
/// Pure, deterministic "Change Case" transforms over a string, culture-invariant so results are stable
/// across locales. Nothing here touches the document model — callers transform the selection's text and
/// feed the result back through the editor. The transforms:
/// <list type="bullet">
/// <item><see cref="CaseKind.Upper"/> / <see cref="CaseKind.Lower"/> — every letter upper/lower.</item>
/// <item><see cref="CaseKind.Sentence"/> — the first letter of the string and the first letter following
/// each sentence terminator (<c>.</c>, <c>!</c>, <c>?</c>) plus whitespace is upper-cased; everything else
/// is lower-cased.</item>
/// <item><see cref="CaseKind.Capitalize"/> — the first letter of every whitespace-delimited word is
/// upper-cased; every other letter is lower-cased (title-ish casing).</item>
/// <item><see cref="CaseKind.Toggle"/> — each letter's case is inverted; non-letters are untouched.</item>
/// </list>
/// Empty/whitespace input is returned unchanged.
/// </summary>
public static class ChangeCase
{
    /// <summary>
    /// Return <paramref name="text"/> transformed by <paramref name="kind"/>. A null argument throws; an
    /// empty string returns empty. The transform is culture-invariant and deterministic.
    /// </summary>
    public static string Apply(string text, CaseKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return text;

        return kind switch
        {
            CaseKind.Upper => text.ToUpperInvariant(),
            CaseKind.Lower => text.ToLowerInvariant(),
            CaseKind.Sentence => ToSentenceCase(text),
            CaseKind.Capitalize => ToCapitalizeCase(text),
            CaseKind.Toggle => ToToggleCase(text),
            _ => text,
        };
    }

    // First letter of the string and the first letter after each sentence terminator (. ! ?) is upper;
    // every other letter is lower. "atSentenceStart" tracks whether the next letter begins a sentence:
    // it starts true (the string's first letter), is re-armed by a terminator, and is cleared once a
    // letter has been emitted. Whitespace and other punctuation between a terminator and the next letter
    // do not reset the flag, so ". " or ".\n" or even ".)" still capitalises the following letter.
    private static string ToSentenceCase(string text)
    {
        var builder = new StringBuilder(text.Length);
        var atSentenceStart = true;
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                builder.Append(atSentenceStart ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                atSentenceStart = false;
            }
            else
            {
                builder.Append(ch);
                if (ch is '.' or '!' or '?')
                    atSentenceStart = true;
            }
        }
        return builder.ToString();
    }

    // First letter of every whitespace-delimited word is upper; every other letter is lower. "atWordStart"
    // is set whenever whitespace is seen (and at the start) and cleared once the word's first letter is
    // emitted, so leading punctuation on a word (e.g. "(hello") still capitalises the first letter.
    private static string ToCapitalizeCase(string text)
    {
        var builder = new StringBuilder(text.Length);
        var atWordStart = true;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
                atWordStart = true;
            }
            else if (char.IsLetter(ch))
            {
                builder.Append(atWordStart ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                atWordStart = false;
            }
            else
            {
                builder.Append(ch); // punctuation/digits keep the word open but are not themselves cased
            }
        }
        return builder.ToString();
    }

    // Invert the case of each letter; non-letters pass through unchanged.
    private static string ToToggleCase(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsUpper(ch))
                builder.Append(char.ToLowerInvariant(ch));
            else if (char.IsLower(ch))
                builder.Append(char.ToUpperInvariant(ch));
            else
                builder.Append(ch);
        }
        return builder.ToString();
    }
}
