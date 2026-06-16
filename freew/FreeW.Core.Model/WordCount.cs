namespace FreeW.Core.Model;

/// <summary>
/// Aggregate counts for a document: words, characters (with and without spaces), and paragraphs.
/// Pure data, produced by <see cref="WordCount"/>.
/// </summary>
public readonly record struct DocumentStats(int Words, int CharactersWithSpaces, int CharactersWithoutSpaces, int Paragraphs)
{
    /// <summary>An all-zero stats value (an empty document).</summary>
    public static readonly DocumentStats Empty = new(0, 0, 0, 0);
}

/// <summary>
/// Pure, WPF-free counting of words, characters, and paragraphs over plain text and over a
/// <see cref="TextDocument"/>. Lives in the model project so it is unit-testable without WPF.
///
/// Boundary definitions:
/// <list type="bullet">
/// <item>Words: maximal runs of non-whitespace characters. Text is split on Unicode whitespace
///   (<see cref="char.IsWhiteSpace(char)"/>) and empty segments are ignored, so leading/trailing
///   and repeated whitespace never inflate the count. A purely whitespace string counts as 0 words.</item>
/// <item>Characters: counted as UTF-16 code units of the text. <c>includeSpaces: true</c> counts
///   every character; <c>includeSpaces: false</c> excludes all Unicode whitespace.</item>
/// <item>Paragraphs: every body <see cref="Paragraph"/> plus every paragraph inside a table cell is
///   counted (table-cell paragraphs are included). Empty paragraphs still count as one paragraph,
///   matching how a word processor reports paragraph count.</item>
/// </list>
/// </summary>
public static class WordCount
{
    /// <summary>Counts words: maximal runs of non-whitespace characters. Empty/whitespace text is 0.</summary>
    public static int Words(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var count = 0;
        var inWord = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Counts characters in <paramref name="text"/>. When <paramref name="includeSpaces"/> is false,
    /// Unicode whitespace characters are excluded.
    /// </summary>
    public static int Characters(string? text, bool includeSpaces)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (includeSpaces)
            return text.Length;

        var count = 0;
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch))
                count++;
        }
        return count;
    }

    /// <summary>Computes word/character/paragraph stats for a whole document.</summary>
    public static DocumentStats Of(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var words = 0;
        var charsWithSpaces = 0;
        var charsWithoutSpaces = 0;
        var paragraphs = 0;

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    CountParagraph(paragraph, ref words, ref charsWithSpaces, ref charsWithoutSpaces, ref paragraphs);
                    break;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var cellParagraph in cell.Paragraphs)
                                CountParagraph(cellParagraph, ref words, ref charsWithSpaces, ref charsWithoutSpaces, ref paragraphs);
                        }
                    }
                    break;
            }
        }

        return new DocumentStats(words, charsWithSpaces, charsWithoutSpaces, paragraphs);
    }

    private static void CountParagraph(Paragraph paragraph, ref int words, ref int charsWithSpaces, ref int charsWithoutSpaces, ref int paragraphs)
    {
        var text = paragraph.PlainText;
        words += Words(text);
        charsWithSpaces += Characters(text, includeSpaces: true);
        charsWithoutSpaces += Characters(text, includeSpaces: false);
        paragraphs++;
    }
}
