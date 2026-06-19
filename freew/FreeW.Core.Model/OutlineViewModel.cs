namespace FreeW.Core.Model;

/// <summary>
/// One displayed line in Word's Outline view: a body block (heading or body paragraph) shown at its
/// outline depth. Pure data, produced by <see cref="OutlineViewModel"/>.
/// </summary>
/// <param name="BlockIndex">Index of the block in <see cref="TextDocument.Blocks"/> (document order).</param>
/// <param name="Level">
/// Outline depth used to indent the row: a heading's own level (Title = 0, HeadingN = N), or — for a
/// body paragraph — the level of the heading it sits under (body before the first heading uses level 0).
/// </param>
/// <param name="Text">The block's plain text (possibly trimmed to its first line by the view model).</param>
/// <param name="IsHeading">True when the block is a heading/title paragraph; false for body text/tables.</param>
public readonly record struct OutlineRow(int BlockIndex, int Level, string Text, bool IsHeading);

/// <summary>
/// Pure, WPF-free computation of the rows shown by FreeW's Outline view (View &gt; Outline). It walks the
/// document in order, emitting one <see cref="OutlineRow"/> per block, each carrying the depth at which the
/// outline indents it — a heading at its own level, a body paragraph at its owning heading's level. The
/// <c>showLevel</c> filter mirrors Word's "Show Level" box: only headings at or above the chosen level are
/// kept (and body text is hidden unless every level is shown). Lives in the model project so the structure
/// is unit-testable without any UI, and reuses <see cref="DocumentOutline.TryGetLevel"/> for classification.
/// </summary>
public static class OutlineViewModel
{
    /// <summary>The sentinel <paramref name="showLevel"/> that shows all headings and body text.</summary>
    public const int ShowAllLevels = int.MaxValue;

    /// <summary>The shallowest selectable "Show Level" (Heading 1 only).</summary>
    public const int MinShowLevel = 1;

    /// <summary>The deepest selectable "Show Level" (Heading 9), matching Word's outline depth cap.</summary>
    public const int MaxShowLevel = 9;

    /// <summary>
    /// Build the rows displayed by the Outline view for <paramref name="document"/>.
    /// </summary>
    /// <param name="document">The document whose blocks are laid out as an outline.</param>
    /// <param name="showLevel">
    /// The deepest heading level to display (1..9), or <see cref="ShowAllLevels"/> for "All Levels". When a
    /// finite level is given, deeper headings and all body text are omitted (Word's "Show Level N").
    /// </param>
    /// <param name="firstLineOnly">
    /// When true, each row's text is trimmed to its first line only (Word's "Show First Line Only"). Whole
    /// text is shown otherwise.
    /// </param>
    public static IReadOnlyList<OutlineRow> Build(TextDocument document, int showLevel = ShowAllLevels, bool firstLineOnly = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        var rows = new List<OutlineRow>();
        var currentHeadingLevel = 0; // depth applied to body text before the first heading
        var blocks = document.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            string text;
            bool isHeading;
            int level;

            if (blocks[i] is Paragraph paragraph && DocumentOutline.TryGetLevel(paragraph.StyleId, out var headingLevel))
            {
                isHeading = true;
                level = headingLevel;
                currentHeadingLevel = headingLevel;
                text = paragraph.PlainText;

                // "Show Level N" hides headings deeper than N (Title = 0 is always shown).
                if (showLevel != ShowAllLevels && headingLevel > showLevel)
                    continue;
            }
            else
            {
                // Body paragraph or table: indented under its owning heading. Hidden unless all levels show.
                if (showLevel != ShowAllLevels)
                    continue;

                isHeading = false;
                level = currentHeadingLevel;
                text = BlockText(blocks[i]);
            }

            if (firstLineOnly)
                text = FirstLine(text);

            rows.Add(new OutlineRow(i, level, text, isHeading));
        }

        return rows;
    }

    // The block's plain text for display (paragraph text, or flattened table cell text).
    private static string BlockText(Block block) => block switch
    {
        Paragraph paragraph => paragraph.PlainText,
        Table table => string.Join(" ", table.Rows.SelectMany(row => row.Cells).Select(cell => cell.PlainText)),
        _ => string.Empty
    };

    // The first line of a block's text (Word's "Show First Line Only"), trimmed of a trailing CR.
    private static string FirstLine(string text)
    {
        var newline = text.IndexOf('\n');
        if (newline < 0)
            return text;
        return text[..newline].TrimEnd('\r');
    }
}
